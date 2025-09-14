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
open Mel.Core.Transcription
open Mel.Core.Transcription.Whisper
open Mel.Core.Service
// Only import specific WhisperFS types we need
// to avoid conflicts with existing Mel types

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
    let mutable whisperTranscriber: IWhisperTranscriber option = None
    let mutable recordingThread: Thread option = None
    let mutable audioBuffer = ResizeArray<float32>()
    let mutable statusMenuItem: NativeMenuItem option = None
    let mutable levelMenuItem: NativeMenuItem option = None
    let mutable deviceMenuItem: NativeMenuItem option = None

    // Transcription mode state
    let mutable transcriptionMode = Mel.UI.SettingsWindow.TranscriptionMode.ASR // Default to ASR
    let mutable streamingClient: WhisperFS.IWhisperClient option = None
    let mutable streamingBuffer = ResizeArray<float32>()
    let mutable lastTranscribedText = ""
    let mutable isProcessingAudio = false  // Flag to control whether we process captured audio
    let mutable captureThread: Thread option = None  // Keep capture thread reference

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

    let processStreamingTranscription() =
        // Process audio in streaming mode with sliding windows
        async {
            try
                // For streaming, use the existing transcriber
                match whisperTranscriber with
                | Some transcriber ->
                    // Process buffered audio in larger chunks for better context
                    let chunkSize = 32000 // 2 seconds at 16kHz for better context
                    if streamingBuffer.Count >= chunkSize then
                        let chunk = streamingBuffer.GetRange(0, chunkSize).ToArray()

                        // Check if chunk has sufficient energy (not silence)
                        let rms = calculateRMS chunk
                        let dbLevel = if rms <= -60.0f then rms else 20.0f * log10(max 0.0001f rms)

                        // Only process if audio is above silence threshold (more permissive)
                        if dbLevel > -45.0f then // Lower threshold to catch softer speech
                            streamingBuffer.RemoveRange(0, chunkSize)

                            // Transcribe chunk
                            let! result = transcriber.TranscribeAsync(chunk, 16000)
                            let newText = result.FullText.Trim()

                            // Filter out common Whisper hallucinations
                            let hallucinations = [
                                "thank you"; "thanks"; "thank you."; "thanks.";
                                "you"; "thank you for watching"; "bye"; "goodbye";
                                "please subscribe"; "see you"; "music";
                                "[music]"; "[applause]"; "foreign"
                            ]

                            let isHallucination =
                                hallucinations
                                |> List.exists (fun h -> newText.ToLowerInvariant().Trim() = h)

                            // Only type if we have meaningful text and it's not a hallucination
                            if newText.Length > 0 && not isHallucination then
                                Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                                    typeText (newText + " ")
                                )

                                // Update settings window
                                match settingsWindow with
                                | Some window -> window.SetTranscription(newText)
                                | None -> ()
                        else
                            // Remove silent chunk to prevent buffer overflow
                            streamingBuffer.RemoveRange(0, min chunkSize streamingBuffer.Count)
                | None ->
                    log "No transcriber available for streaming"
            with ex ->
                log $"Streaming processing error: {ex.Message}"
        }
    
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

        // Only process audio for transcription if F9 is held (isProcessingAudio = true)
        if isProcessingAudio then
            // Check transcription mode
            let mode =
                match settingsWindow with
                | Some window -> window.GetTranscriptionMode()
                | None -> transcriptionMode

            // Handle audio based on mode
            if mode = Mel.UI.SettingsWindow.TranscriptionMode.ASR then
                // ASR streaming mode - add to streaming buffer and process
                streamingBuffer.AddRange(frame.Samples)

                // Process streaming chunks periodically with larger chunks
                if streamingBuffer.Count >= 32000 then // 2 seconds of audio for better context
                    processStreamingTranscription() |> Async.Start

                // Prevent buffer from growing too large (keep max 10 seconds)
                if streamingBuffer.Count > 160000 then // 10 seconds at 16kHz
                    let excess = streamingBuffer.Count - 160000
                    streamingBuffer.RemoveRange(0, excess)
                    log "Trimmed excess streaming buffer"
            else
                // PTT batch mode - use VAD processing
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
                        log $"🔇 Speech ended after {duration.TotalSeconds:F1}s - transcribing..."

                        // Update settings window
                        match settingsWindow with
                        | Some window -> window.SetSpeechDetected(false)
                        | None -> ()

                        if audioBuffer.Count > 8000 then // At least 0.5 seconds
                            let samples = audioBuffer.ToArray()
                            audioBuffer.Clear()

                            // Transcribe asynchronously
                            async {
                                try
                                    match whisperTranscriber with
                                    | Some transcriber ->
                                        log $"Sending {samples.Length} samples to Whisper..."
                                        let! result = transcriber.TranscribeAsync(samples, 16000)
                                        if not (String.IsNullOrWhiteSpace(result.FullText)) then
                                            log $"✅ Transcribed: '{result.FullText}'"

                                            // Update settings window
                                            match settingsWindow with
                                            | Some window -> window.SetTranscription(result.FullText)
                                            | None -> ()

                                            // Type the text at cursor on UI thread with trailing space
                                            Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                                                typeText (result.FullText + " ")
                                            )
                                        else
                                            ()  // Silent on empty result
                                    | None ->
                                        log "❌ Transcriber not initialized"
                                with
                                | ex ->
                                    log $"❌ Transcription error: {ex.Message}"
                                    if ex.InnerException <> null then
                                        log $"Inner exception: {ex.InnerException.Message}"
                            } |> Async.Start
                | None -> ()
    
    let startContinuousCapture() =
        // Start audio capture that runs continuously in the background
        if audioCapture.IsNone then
            try
                let capture = new WasapiCapture(selectedDeviceIndex, 8192)
                audioCapture <- Some (capture :> IAudioCapture)
                log "Initialized continuous audio capture"
            with ex ->
                log $"Failed to initialize continuous audio capture: {ex.Message}"
                ()

        match audioCapture with
        | Some capture ->
            try
                capture.StartCapture()
                log "Started continuous audio capture"

                // Start processing thread
                let thread = Thread(fun () ->
                    let mutable running = true

                    while running do
                        try
                            let frameTask = capture.CaptureFrameAsync(CancellationToken.None)
                            match Async.RunSynchronously (async { return! frameTask }) with
                            | Some frame ->
                                // Process on UI thread for menu updates
                                Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                                    processAudioFrame frame
                                )
                            | None ->
                                Thread.Sleep(10)
                        with
                        | ex ->
                            log $"Audio processing error: {ex.Message}"
                            Thread.Sleep(100)
                )
                thread.IsBackground <- true
                thread.Start()
                captureThread <- Some thread
            with ex ->
                log $"Failed to start continuous capture: {ex.Message}"
        | None ->
            log "No audio capture device available"

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
            // Recording started - just set flags, audio capture is already running
            isRecording <- true
            isProcessingAudio <- true  // Enable audio processing
            updateMenuStatus()

            // Check transcription mode
            let mode =
                match settingsWindow with
                | Some window -> window.GetTranscriptionMode()
                | None -> transcriptionMode

            if mode = Mel.UI.SettingsWindow.TranscriptionMode.ASR then
                // ASR mode - clear streaming buffers for new session
                streamingBuffer.Clear()
                lastTranscribedText <- ""
                log $"Starting ASR streaming mode (buffer cleared, size: {streamingBuffer.Count})"
            else
                // PTT mode - clear audio buffer for new recording
                audioBuffer.Clear()
                log "Starting PTT batch mode"
    
    let stopRecording() =
        if isRecording then
            // Stopping recording - just disable processing, keep capture running
            isRecording <- false
            isProcessingAudio <- false  // Stop processing audio for transcription
            updateMenuStatus()

            // Check transcription mode
            let mode =
                match settingsWindow with
                | Some window -> window.GetTranscriptionMode()
                | None -> transcriptionMode

            if mode = Mel.UI.SettingsWindow.TranscriptionMode.ASR then
                // ASR mode - process remaining streaming buffer
                log $"Stopping ASR streaming, processing remaining buffer ({streamingBuffer.Count} samples)..."

                // Process any remaining audio in the streaming buffer
                async {
                    // Force process any remaining audio, even if smaller than normal chunk size
                    if streamingBuffer.Count >= 8000 then // At least 0.5 seconds
                        // Process whatever is left
                        let chunk = streamingBuffer.ToArray()
                        streamingBuffer.Clear()

                        match whisperTranscriber with
                        | Some transcriber ->
                            let! result = transcriber.TranscribeAsync(chunk, 16000)
                            let newText = result.FullText.Trim()

                            // Filter hallucinations
                            let hallucinations = [
                                "thank you"; "thanks"; "thank you."; "thanks.";
                                "you"; "thank you for watching"; "bye"; "goodbye";
                                "please subscribe"; "see you"; "music";
                                "[music]"; "[applause]"; "foreign"
                            ]

                            let isHallucination =
                                hallucinations
                                |> List.exists (fun h -> newText.ToLowerInvariant().Trim() = h)

                            if newText.Length > 0 && not isHallucination then
                                Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                                    typeText (newText + " ")
                                )
                        | None -> ()
                    else
                        // Too short to process, just clear
                        streamingBuffer.Clear()

                    log "ASR streaming buffer processed and cleared"
                } |> Async.Start
            else
                // PTT mode - transcribe the buffered audio
                if audioBuffer.Count > 8000 then // At least 0.5 seconds
                    let samples = audioBuffer.ToArray()

                    // Transcribe asynchronously
                    async {
                        try
                            match whisperTranscriber with
                            | Some transcriber ->
                                log $"Transcribing {samples.Length} samples..."
                                let! result = transcriber.TranscribeAsync(samples, 16000)
                                if not (String.IsNullOrWhiteSpace(result.FullText)) then
                                    let finalText = result.FullText.Trim()
                                    log $"✅ Transcribed: '{finalText}'"

                                    // Update settings window
                                    match settingsWindow with
                                    | Some window -> window.SetTranscription(finalText)
                                    | None -> ()

                                    // Type the text at cursor with trailing space
                                    Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                                        typeText (finalText + " ")
                                    )
                            | None ->
                                log "❌ Transcriber not initialized"
                        with
                        | ex ->
                            log $"❌ Transcription error: {ex.Message}"
                            if ex.InnerException <> null then
                                log $"Inner exception: {ex.InnerException.Message}"
                    } |> Async.Start

            // Clear speech indicator
            match settingsWindow with
            | Some window -> window.SetSpeechDetected(false)
            | None -> ()

            // Reset VAD state for PTT mode
            match vad with
            | Some vadProcessor -> vadProcessor.Reset()
            | None -> ()

            // Clear batch buffer for PTT mode
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
                    
                    // Initialize Whisper (this might download model)
                    log "Initializing Whisper transcriber..."
                    
                    // Use Base model by default
                    let modelType = WhisperFS.ModelType.Base
                    let modelName = "base"
                    
                    let config = {
                        ModelPath = $"./models/ggml-{modelName}.bin"
                        ModelType = modelType
                        Language = Some "en"
                        UseGpu = true
                        ThreadCount = 4
                        MaxSegmentLength = 30
                        EnableTranslate = false
                    }
                    
                    try
                        log $"Using Whisper model: {modelName} at {config.ModelPath}"

                        // Check if model exists
                        if not (System.IO.File.Exists(config.ModelPath)) then
                            log $"Model file not found, will download during initialization"

                        log "Initializing WhisperFS and downloading native libraries..."
                        whisperTranscriber <- Some (new WhisperTranscriber(config) :> IWhisperTranscriber)

                        // Give WhisperFS time to download and initialize native libraries
                        // This happens in the background but we'll give it a moment
                        do! Async.Sleep(100)

                        log "Whisper transcriber created, native library initialization in progress"
                    with ex ->
                        log $"WARNING: Whisper initialization failed: {ex.Message}"
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

                    // Start continuous audio capture
                    startContinuousCapture()

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