namespace Mel.UI

open System
open System.Threading
open System.Runtime.InteropServices
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open NAudio.Wave
open NAudio.CoreAudioApi
open Mel.Core.Audio
open Mel.Core.Audio.VAD
open Mel.Core.Audio.Capture
open Mel.Core.Transcription.VoskTypes
open Mel.Core.Transcription.Vosk
open Mel.Core.Service

// Windows API for sending keystrokes and hotkeys
module WinApi =
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type MSG =
        val mutable hwnd: IntPtr
        val mutable message: uint32
        val mutable wParam: IntPtr
        val mutable lParam: IntPtr
        val mutable time: uint32
        val mutable pt: POINT
    
    and [<Struct>] POINT =
        val mutable x: int
        val mutable y: int
    
    [<DllImport("user32.dll")>]
    extern bool RegisterHotKey(IntPtr hWnd, int id, uint32 fsModifiers, uint32 vk)
    
    [<DllImport("user32.dll")>]
    extern bool UnregisterHotKey(IntPtr hWnd, int id)
    
    [<DllImport("user32.dll")>]
    extern void keybd_event(byte bVk, byte bScan, uint32 dwFlags, UIntPtr dwExtraInfo)
    
    [<DllImport("user32.dll", CharSet = CharSet.Auto)>]
    extern IntPtr SendMessage(IntPtr hWnd, uint32 Msg, IntPtr wParam, IntPtr lParam)
    
    [<DllImport("user32.dll")>]
    extern IntPtr GetForegroundWindow()
    
    [<DllImport("user32.dll")>]
    extern bool SetForegroundWindow(IntPtr hWnd)
    
    [<DllImport("user32.dll", SetLastError = true)>]
    extern IntPtr CreateWindowEx(uint32 dwExStyle, string lpClassName, string lpWindowName, uint32 dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam)
    
    [<DllImport("user32.dll")>]
    extern bool PeekMessage(MSG* lpMsg, IntPtr hWnd, uint32 wMsgFilterMin, uint32 wMsgFilterMax, uint32 wRemoveMsg)
    
    let PM_REMOVE = 0x0001u
    let HWND_MESSAGE = IntPtr(-3)
    
    [<DllImport("user32.dll")>]
    extern IntPtr DefWindowProc(IntPtr hWnd, uint32 uMsg, IntPtr wParam, IntPtr lParam)
    
    [<DllImport("user32.dll")>]
    extern bool GetMessage(MSG* lpMsg, IntPtr hWnd, uint32 wMsgFilterMin, uint32 wMsgFilterMax)
    
    [<DllImport("user32.dll")>]
    extern bool TranslateMessage(MSG* lpMsg)
    
    [<DllImport("user32.dll")>]
    extern IntPtr DispatchMessage(MSG* lpMsg)
    
    let WM_HOTKEY = 0x0312u
    let WM_KEYDOWN = 0x0100u
    let WM_KEYUP = 0x0101u
    let MOD_NOREPEAT = 0x4000u
    let VK_F9 = 0x78u
    
    [<DllImport("user32.dll")>]
    extern int16 GetAsyncKeyState(int vKey)
    

type SpeakEZApp() =
    inherit Application()
    
    let mutable trayIcon: TrayIcon option = None
    let mutable isRecording = false
    let mutable audioCapture: IAudioCapture option = None
    let mutable selectedDeviceIndex = 0
    let mutable vad: VoiceActivityDetector option = None
    let mutable voskTranscriber: IVoskTranscriber option = None
    let mutable recordingThread: Thread option = None
    let mutable audioBuffer = ResizeArray<float32>()
    let mutable statusMenuItem: NativeMenuItem option = None
    let mutable levelMenuItem: NativeMenuItem option = None
    let mutable deviceMenuItem: NativeMenuItem option = None
    
    // Simple push-to-talk state
    // audioBuffer already defined above for VAD processing
    
    let mutable logHistory = ResizeArray<string>()
    let logEvent = Event<string>()
    let debugMode = Environment.GetEnvironmentVariable("SPEAKEZ_DEBUG") = "1"
    let mutable hotkeyWindow: IntPtr = IntPtr.Zero
    let mutable hotkeyThread: Thread option = None
    let HOTKEY_ID = 1
    let mutable settingsWindow: Mel.UI.SettingsWindow.SettingsWindow option = None
    let mutable monitoringThread: Thread option = None
    let mutable isMonitoring = false
    
    let log msg =
        let timestamp = DateTime.Now.ToString("HH:mm:ss.fff")
        let logMsg = sprintf "[%s] %s" timestamp msg
        logHistory.Add(logMsg)
        if logHistory.Count > 100 then logHistory.RemoveAt(0) // Keep last 100 lines
        if debugMode then Console.WriteLine(logMsg)
        logEvent.Trigger(logMsg)
        // Update settings window if open
        match settingsWindow with
        | Some window -> window.AddLog(logMsg)
        | None -> ()
    
    let getAudioDevices() =
        try
            // Use WaveIn to get devices (matches what we're using for capture)
            let deviceCount = NAudio.Wave.WaveInEvent.DeviceCount
            [ for i in 0 .. deviceCount - 1 do
                let capabilities = NAudio.Wave.WaveInEvent.GetCapabilities(i)
                yield (capabilities.ProductName, i.ToString()) ]
        with
        | ex -> 
            log $"Error enumerating audio devices: {ex.Message}"
            []
    
    let typeText (text: string) =
        // Type the text at current cursor position using InputSimulator
        try
            let simulator = WindowsInput.InputSimulator()
            simulator.Keyboard.TextEntry(text) |> ignore
            log $"Typed text: {text}"
        with
        | ex -> log $"Error typing text: {ex.Message}"
    
    let calculateRMS (samples: float32[]) =
        if samples = null || samples.Length = 0 then 
            -60.0f // Return very low dB for empty/null samples
        else
            let sum = samples |> Array.sumBy (fun s -> s * s)
            let rms = sqrt(sum / float32 samples.Length)
            if rms = 0.0f || System.Single.IsNaN(rms) then -60.0f
            else rms
    
    // Removed parallel transcription - using simple push-to-talk instead
    
    let processAudioFrame (frame: AudioFrame) =
        // Debug logging for frame info
        if frame.Samples = null then
            log "Warning: Received null audio samples"
        elif frame.Samples.Length = 0 then
            log "Warning: Received empty audio frame"
        
        let rms = calculateRMS frame.Samples
        let dbLevel = 
            if rms <= -60.0f then rms
            else 20.0f * log10(max 0.0001f rms)
        
        // Update level display
        let bars = 
            if System.Single.IsNaN(dbLevel) || System.Single.IsInfinity(dbLevel) then
                "❌ERROR❌"
            elif dbLevel > -10.0f then "████████"
            elif dbLevel > -20.0f then "██████░░"
            elif dbLevel > -30.0f then "████░░░░"
            elif dbLevel > -40.0f then "██░░░░░░"
            else "░░░░░░░░"
        
        match levelMenuItem with
        | Some item -> 
            item.Header <- $"Level: {bars} ({dbLevel:F1} dB)"
        | None -> ()
        
        // Update settings window if open
        match settingsWindow with
        | Some window -> window.UpdateLevel(dbLevel)
        | None -> ()
        
        // Stream audio to Vosk if recording
        if isRecording then
            match voskTranscriber with
            | Some transcriber ->
                // Process audio in real-time
                match transcriber.ProcessAudio(frame.Samples) with
                | Some partial ->
                    // Update UI with partial transcription immediately
                    match settingsWindow with
                    | Some window -> 
                        window.SetTranscription($"[Live] {partial.Partial}")
                    | None -> ()
                    
                    // Type out new text incrementally
                    if partial.Partial.Length > 0 then
                        log $"📝 Streaming: '{partial.Partial}'"
                        // In a real implementation, we'd track what's already typed
                        // For now, we'll update the display only
                | None -> ()
            | None -> ()
        
        // Process with VAD
        match vad with
        | Some vadProcessor ->
            let result = vadProcessor.ProcessFrame(frame)
            match result with
            | SpeechStarted ->
                log "🎤 Speech detected"
                // Don't clear buffer - we want to keep any audio that led to speech detection
                // audioBuffer.Clear()  // REMOVED - this was dropping initial audio
                audioBuffer.AddRange(frame.Samples)
                // Update settings window
                match settingsWindow with
                | Some window -> window.SetSpeechDetected(true)
                | None -> ()
                
            | SpeechContinuing ->
                audioBuffer.AddRange(frame.Samples)
                // Silent during continuous speech
                
            | NoChange ->
                // Keep a rolling buffer even when no speech detected
                // This ensures we capture the beginning of speech
                audioBuffer.AddRange(frame.Samples)
                // Keep only last 1 second of audio (16000 samples)
                if audioBuffer.Count > 16000 then
                    let excess = audioBuffer.Count - 16000
                    audioBuffer.RemoveRange(0, excess)
                
            | SpeechEnded duration ->
                log $"🔇 Speech ended after {duration.TotalSeconds:F1}s"
                
                // Update settings window
                match settingsWindow with
                | Some window -> window.SetSpeechDetected(false)
                | None -> ()
                
                // With Vosk streaming, transcription happens in real-time
                // No need to process here - it's already been streamed
                audioBuffer.Clear()
        | None -> ()
    
    let rec updateMenuStatus() =
        match statusMenuItem with
        | Some item -> 
            if isRecording then
                item.Header <- "🔴 Recording... (Press F9 to stop)"
            else
                item.Header <- "⚪ Ready (Press F9 to start)"
        | None -> ()
    
    and startRecording() =
        if not isRecording then
            // Recording started
            isRecording <- true
            updateMenuStatus()
            
            // Clear audio buffer for new recording
            audioBuffer.Clear()
            
            // Start Vosk streaming session
            match voskTranscriber with
            | Some transcriber ->
                transcriber.StartStream()
                log "Started Vosk streaming session"
            | None -> ()
            
            // Initialize audio capture if needed (on main thread first)
            if audioCapture.IsNone then
                try
                    // Use selected device with larger buffer for virtual devices
                    let capture = new WasapiCapture(selectedDeviceIndex, 8192) // Increased buffer size
                    audioCapture <- Some (capture :> IAudioCapture)
                with ex ->
                    log $"Failed to initialize audio capture: {ex.Message}"
            
            // Start capture
            match audioCapture with
            | Some capture ->
                try
                    capture.StartCapture()
                    
                    // Start processing thread
                    let thread = Thread(fun () ->
                        let mutable running = true
                        let mutable frameCount = 0
                        let mutable emptyFrameCount = 0
                        
                        while running && isRecording do
                            try
                                let frameTask = capture.CaptureFrameAsync(CancellationToken.None)
                                match Async.RunSynchronously (async { return! frameTask }) with
                                | Some frame -> 
                                    frameCount <- frameCount + 1
                                    
                                    // Check frame validity
                                    if frame.Samples = null || frame.Samples.Length = 0 then
                                        emptyFrameCount <- emptyFrameCount + 1
                                        if emptyFrameCount % 10 = 0 then
                                            log $"Received {emptyFrameCount} empty frames"
                                    else
                                        if frameCount % 100 = 0 then
                                            log $"Processed {frameCount} audio frames ({frame.Samples.Length} samples each)"
                                        
                                        // Process on UI thread for menu updates
                                        Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                                            processAudioFrame frame
                                        )
                                | None -> 
                                    Thread.Sleep(10)
                            with
                            | ex -> 
                                log $"Audio processing error: {ex.Message}"
                                log $"Stack trace: {ex.StackTrace}"
                                running <- false
                    )
                    thread.IsBackground <- true
                    thread.Start()
                    recordingThread <- Some thread
                with ex ->
                    log $"Failed to start capture: {ex.Message}"
            | None -> 
                log "No audio capture device available"
    
    let stopRecording() =
        if isRecording then
            // Stopping recording
            isRecording <- false
            updateMenuStatus()
            
            // Finish Vosk stream and get final result
            match voskTranscriber with
            | Some transcriber ->
                match transcriber.FinishStream() with
                | Some final ->
                    if not (String.IsNullOrWhiteSpace(final.Text)) then
                        let finalText = final.Text.Trim()
                        log $"✅ Final transcription: '{finalText}'"
                        
                        // Update settings window
                        match settingsWindow with
                        | Some window -> window.SetTranscription(finalText)
                        | None -> ()
                        
                        // Type the final text with trailing space
                        Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                            typeText (finalText + " ")
                        )
                | None ->
                    log "No final transcription available"
                
                // Reset for next session
                transcriber.Reset()
            | None -> 
                log "❌ Transcriber not initialized"
            
            // Clear speech indicator
            match settingsWindow with
            | Some window -> window.SetSpeechDetected(false)
            | None -> ()
            
            // Stop capture
            match audioCapture with
            | Some capture -> 
                capture.StopCapture()
                (capture :> IDisposable).Dispose()
                audioCapture <- None  // Clear so it's reinitialized next time
            | None -> ()
            
            // Stop processing thread
            match recordingThread with
            | Some thread -> 
                thread.Join(1000) |> ignore
                recordingThread <- None
            | None -> ()
            
            // Reset VAD state
            match vad with
            | Some vadProcessor -> vadProcessor.Reset()
            | None -> ()
            
            audioBuffer.Clear()
    
    let toggleRecording() =
        if isRecording then stopRecording()
        else startRecording()
    
    override this.Initialize() =
        try
            log "App.Initialize() starting..."
            this.Styles.Add(FluentTheme())
            this.RequestedThemeVariant <- Avalonia.Styling.ThemeVariant.Dark
            log "Theme initialized"
        with ex ->
            log $"Error initializing theme: {ex.Message}"
    
    override this.OnFrameworkInitializationCompleted() =
        log "OnFrameworkInitializationCompleted() starting..."
        
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            log "Desktop lifetime detected"
            
            // Defer heavy initialization
            async {
                try
                    log "Starting deferred initialization..."
                    
                    // Initialize Vosk for streaming transcription
                    log "Initializing Vosk transcriber..."
                    
                    // Use small English model for low latency
                    let modelName = "vosk-model-small-en-us-0.15"
                    
                    let config = {
                        ModelPath = $"./models/{modelName}"
                        SampleRate = 16000.0f
                        MaxAlternatives = 0
                        Words = false  // Don't need word-level timing for now
                        PartialWords = false
                    }
                    
                    try
                        log $"Using Vosk model: {modelName}"
                        
                        // Check if model directory exists
                        if not (System.IO.Directory.Exists(config.ModelPath)) then
                            log $"Model directory not found at {config.ModelPath}"
                            log "Please download from: https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip"
                            log "And extract to ./models/ directory"
                        else
                            voskTranscriber <- Some (new VoskTranscriber(config) :> IVoskTranscriber)
                            log "Vosk initialized successfully for streaming"
                    with ex ->
                        log $"WARNING: Vosk initialization failed: {ex.Message}"
                        log "Transcription will not be available"
                    
                    // Initialize VAD
                    log "Initializing VAD..."
                    let vadConfig = {
                        EnergyThreshold = 0.005f  // Lower threshold for better sensitivity
                        MinSpeechDuration = 0.2f  // Shorter minimum for responsiveness
                        MaxSilenceDuration = 1.0f  // Slightly longer silence tolerance
                        SampleRate = 16000
                        FrameSize = 512
                    }
                    vad <- Some (new VoiceActivityDetector(vadConfig))
                    log "VAD initialized with improved sensitivity"
                    
                with ex ->
                    log $"Error in deferred initialization: {ex.Message}"
            } |> Async.Start
            
            // Create system tray icon immediately
            log "Creating system tray icon..."
            let tray = new TrayIcon()
            
            // Set icon
            try
                let iconPath = @"D:\repos\Mel\img\SpeakEZcolorIcon.ico"
                if System.IO.File.Exists(iconPath) then
                    use stream = System.IO.File.OpenRead(iconPath)
                    tray.Icon <- WindowIcon(stream)
                    log $"Loaded icon: {iconPath}"
                else
                    log $"Icon not found at: {iconPath}"
            with ex -> log $"Icon error: {ex.Message}"
            
            tray.ToolTipText <- "SpeakEZ - F9 to toggle recording"
            
            // Create context menu
            let menu = NativeMenu()
            
            // Status indicator
            let status = NativeMenuItem("⚪ Ready (Press F9 to start)")
            statusMenuItem <- Some status
            menu.Add(status)
            
            // Audio level meter
            let level = NativeMenuItem("Level: ░░░░░░░░")
            levelMenuItem <- Some level
            menu.Add(level)
            
            menu.Add(NativeMenuItemSeparator())
            
            // Push-to-talk info
            let pushToTalkItem = NativeMenuItem("Push-to-Talk: Hold F9")
            pushToTalkItem.IsEnabled <- false
            menu.Add(pushToTalkItem)
            
            // Manual toggle recording
            let toggleItem = NativeMenuItem("Manual Toggle Recording")
            toggleItem.Click.Add(fun _ -> toggleRecording())
            menu.Add(toggleItem)
            
            menu.Add(NativeMenuItemSeparator())
            
            // Audio devices submenu
            let devicesMenu = NativeMenu()
            let devices = getAudioDevices()
            let mutable deviceMenuItems = []
            
            for (name, id) in devices do
                let deviceIndex = Int32.Parse(id)
                let deviceItem = NativeMenuItem(name)
                deviceMenuItems <- (deviceIndex, deviceItem) :: deviceMenuItems
                deviceItem.Click.Add(fun _ -> 
                    log $"Selected device: {name} (Index: {deviceIndex})"
                    selectedDeviceIndex <- deviceIndex
                    
                    // Stop current recording if active
                    if isRecording then
                        stopRecording()
                    
                    // Clear current audio capture to force reinitialization with new device
                    match audioCapture with
                    | Some capture -> 
                        (capture :> IDisposable).Dispose()
                        audioCapture <- None
                        log "Disposed previous audio capture"
                    | None -> ()
                    
                    // Update checkmarks
                    for (devIdx, item) in deviceMenuItems do
                        if devIdx = deviceIndex then
                            item.Header <- "✓ " + name
                        else
                            let cleanName = item.Header.Replace("✓ ", "")
                            item.Header <- cleanName
                    
                    log $"Audio device changed to: {name} (index {deviceIndex})"
                )
                devicesMenu.Add(deviceItem)
            
            // Select first device by default
            if devices.Length > 0 then
                let (firstName, firstId) = devices.[0]
                selectedDeviceIndex <- Int32.Parse(firstId)
                match deviceMenuItems |> List.tryFind (fun (idx, _) -> idx = selectedDeviceIndex) with
                | Some (_, item) -> item.Header <- "✓ " + firstName
                | None -> ()
            
            let devicesItem = NativeMenuItem("Audio Devices")
            devicesItem.Menu <- devicesMenu
            deviceMenuItem <- Some devicesItem
            menu.Add(devicesItem)
            
            menu.Add(NativeMenuItemSeparator())
            
            // Settings
            let settingsItem = NativeMenuItem("Settings...")
            settingsItem.Click.Add(fun _ -> 
                match settingsWindow with
                | Some window when window.IsVisible ->
                    window.Activate()
                | _ ->
                    let window = Mel.UI.SettingsWindow.SettingsWindow()
                    window.SetDevices(devices)
                    settingsWindow <- Some window
                    window.Closed.Add(fun _ -> 
                        settingsWindow <- None
                        // Stop monitoring when settings window closes
                        isMonitoring <- false
                    )
                    window.Show()
                    
                    // Start audio monitoring for live levels
                    if not isMonitoring then
                        isMonitoring <- true
                        let monitorThread = Thread(fun () ->
                            // Initialize monitoring capture
                            try
                                let monitorCapture = new WasapiCapture(selectedDeviceIndex, 2048)
                                (monitorCapture :> IAudioCapture).StartCapture()
                                // Audio monitoring started silently
                                
                                while isMonitoring do
                                    try
                                        let frameTask = (monitorCapture :> IAudioCapture).CaptureFrameAsync(CancellationToken.None)
                                        match Async.RunSynchronously (async { return! frameTask }) with
                                        | Some frame when frame.Samples <> null && frame.Samples.Length > 0 -> 
                                            // Calculate RMS and update display
                                            let rms = calculateRMS frame.Samples
                                            let dbLevel = 
                                                if rms <= -60.0f then rms
                                                else 20.0f * log10(max 0.0001f rms)
                                            
                                            // Update settings window
                                            match settingsWindow with
                                            | Some window -> 
                                                Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                                                    window.UpdateLevel(dbLevel)
                                                )
                                            | None -> ()
                                        | _ -> 
                                            Thread.Sleep(10)
                                    with
                                    | ex -> 
                                        log $"Monitoring error: {ex.Message}"
                                        Thread.Sleep(100)
                                
                                (monitorCapture :> IAudioCapture).StopCapture()
                                (monitorCapture :> IDisposable).Dispose()
                                log "Stopped audio monitoring"
                            with ex ->
                                log $"Failed to start monitoring: {ex.Message}"
                        )
                        monitorThread.IsBackground <- true
                        monitorThread.Start()
                        monitoringThread <- Some monitorThread
            )
            menu.Add(settingsItem)
            
            // Show log
            let logItem = NativeMenuItem("Show Log")
            logItem.Click.Add(fun _ -> 
                log "=== Recent Activity ==="
                if logHistory.Count > 0 then
                    // Show last 20 log entries
                    let startIdx = max 0 (logHistory.Count - 20)
                    for i in startIdx .. logHistory.Count - 1 do
                        Console.WriteLine(logHistory.[i])
                else
                    log "No activity logged yet"
                log "=== End of Log ==="
            )
            menu.Add(logItem)
            
            menu.Add(NativeMenuItemSeparator())
            
            // Exit
            let exitItem = NativeMenuItem("Exit")
            exitItem.Click.Add(fun _ -> 
                stopRecording()
                desktop.Shutdown()
            )
            menu.Add(exitItem)
            
            tray.Menu <- menu
            tray.IsVisible <- true
            trayIcon <- Some tray
            
            // Set up push-to-talk F9 monitoring
            let setupHotkey() =
                let thread = Thread(fun () ->
                    try
                        // F9 monitoring started silently
                        let mutable wasPressed = false
                        let mutable running = true
                        
                        while running do
                            let keyState = WinApi.GetAsyncKeyState(int WinApi.VK_F9)
                            let isPressed = (keyState &&& int16 0x8000) <> 0s
                            
                            if isPressed && not wasPressed then
                                // Key just pressed down
                                Avalonia.Threading.Dispatcher.UIThread.Post(fun () -> 
                                    if not isRecording then
                                        startRecording()
                                )
                                wasPressed <- true
                            elif not isPressed && wasPressed then
                                // Key just released
                                Avalonia.Threading.Dispatcher.UIThread.Post(fun () -> 
                                    if isRecording then
                                        stopRecording()
                                )
                                wasPressed <- false
                            
                            Thread.Sleep(10) // Poll every 10ms
                    with ex ->
                        log $"❌ Hotkey monitoring error: {ex.Message}"
                )
                thread.IsBackground <- true
                thread.Start()
                hotkeyThread <- Some thread
            
            // Start hotkey thread
            setupHotkey()
            
            desktop.ShutdownMode <- ShutdownMode.OnExplicitShutdown
            
            log "==================================="
            log "SpeakEZ Voice Transcription Ready"
            log "==================================="
            log "Hold F9 to speak"
            log ""
            
        | _ -> ()
        
        base.OnFrameworkInitializationCompleted()