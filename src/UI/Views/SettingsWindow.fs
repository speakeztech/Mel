module Mel.UI.SettingsWindow

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading

type SettingsWindow() as this =
    inherit Window()
    
    let mutable levelProgressBar: ProgressBar option = None
    let mutable levelLabel: TextBlock option = None
    let mutable logTextBox: TextBox option = None
    let mutable deviceCombo: ComboBox option = None
    let mutable modelCombo: ComboBox option = None
    let mutable statusLabel: TextBlock option = None
    let mutable transcriptionLabel: TextBlock option = None
    let mutable speechIndicator: Border option = None
    
    do
        this.Title <- "SpeakEZ Settings"
        this.Width <- 700.0
        this.Height <- 560.0
        this.WindowStartupLocation <- WindowStartupLocation.CenterScreen
        this.CanResize <- true
        
        // Main panel
        let mainPanel = StackPanel()
        mainPanel.Margin <- Thickness(10.0)
        mainPanel.Spacing <- 10.0
        
        // Title
        let title = TextBlock()
        title.Text <- "SpeakEZ Voice Transcription Settings"
        title.FontSize <- 18.0
        title.FontWeight <- FontWeight.Bold
        mainPanel.Children.Add(title)
        
        // Audio section
        let audioSection = Border()
        audioSection.Background <- SolidColorBrush(Color.FromRgb(40uy, 40uy, 40uy))
        audioSection.CornerRadius <- CornerRadius(5.0)
        audioSection.Padding <- Thickness(10.0)
        
        let audioPanel = StackPanel()
        audioPanel.Spacing <- 10.0
        
        let audioTitle = TextBlock()
        audioTitle.Text <- "Audio Input"
        audioTitle.FontWeight <- FontWeight.SemiBold
        audioPanel.Children.Add(audioTitle)
        
        // Device selection
        let devicePanel = DockPanel()
        devicePanel.LastChildFill <- true
        
        let deviceLabel = TextBlock()
        deviceLabel.Text <- "Device: "
        deviceLabel.VerticalAlignment <- VerticalAlignment.Center
        deviceLabel.Width <- 80.0
        DockPanel.SetDock(deviceLabel, Dock.Left)
        devicePanel.Children.Add(deviceLabel)
        
        let combo = ComboBox()
        combo.HorizontalAlignment <- HorizontalAlignment.Stretch
        deviceCombo <- Some combo
        devicePanel.Children.Add(combo)
        
        audioPanel.Children.Add(devicePanel)
        
        // Audio level meter with progress bar
        let levelPanel = StackPanel()
        levelPanel.Spacing <- 5.0
        
        let levelTextPanel = DockPanel()
        let levelText = TextBlock()
        levelText.Text <- "Level: "
        DockPanel.SetDock(levelText, Dock.Left)
        levelTextPanel.Children.Add(levelText)
        
        let level = TextBlock()
        level.Text <- "-60.0 dB"
        level.HorizontalAlignment <- HorizontalAlignment.Right
        levelLabel <- Some level
        levelTextPanel.Children.Add(level)
        
        levelPanel.Children.Add(levelTextPanel)
        
        // Progress bar for visual level
        let progressBar = ProgressBar()
        progressBar.Height <- 20.0
        progressBar.Minimum <- 0.0
        progressBar.Maximum <- 100.0
        progressBar.Value <- 0.0
        progressBar.Foreground <- SolidColorBrush(Colors.LimeGreen)
        progressBar.Background <- SolidColorBrush(Color.FromRgb(20uy, 20uy, 20uy))
        levelProgressBar <- Some progressBar
        levelPanel.Children.Add(progressBar)
        
        audioPanel.Children.Add(levelPanel)
        
        audioSection.Child <- audioPanel
        mainPanel.Children.Add(audioSection)
        
        // Model section
        let modelSection = Border()
        modelSection.Background <- SolidColorBrush(Color.FromRgb(40uy, 40uy, 40uy))
        modelSection.CornerRadius <- CornerRadius(5.0)
        modelSection.Padding <- Thickness(10.0)
        
        let modelPanel = StackPanel()
        modelPanel.Spacing <- 10.0
        
        let modelTitle = TextBlock()
        modelTitle.Text <- "Whisper Model"
        modelTitle.FontWeight <- FontWeight.SemiBold
        modelPanel.Children.Add(modelTitle)
        
        // Model selection
        let modelSelectPanel = DockPanel()
        modelSelectPanel.LastChildFill <- true
        
        let modelLabel = TextBlock()
        modelLabel.Text <- "Model: "
        modelLabel.VerticalAlignment <- VerticalAlignment.Center
        modelLabel.Width <- 80.0
        DockPanel.SetDock(modelLabel, Dock.Left)
        modelSelectPanel.Children.Add(modelLabel)
        
        let modelComboBox = ComboBox()
        modelComboBox.HorizontalAlignment <- HorizontalAlignment.Stretch
        // Add available Vosk models
        let models = [
            "Small English (40 MB)"
            "Large English (1.8 GB)"
        ]
        for model in models do
            modelComboBox.Items.Add(model) |> ignore
        modelComboBox.SelectedIndex <- 0 // Default to Small for low latency
        modelCombo <- Some modelComboBox
        modelSelectPanel.Children.Add(modelComboBox)
        
        modelPanel.Children.Add(modelSelectPanel)
        
        modelSection.Child <- modelPanel
        mainPanel.Children.Add(modelSection)
        
        // Status section
        let statusSection = Border()
        statusSection.Background <- SolidColorBrush(Color.FromRgb(40uy, 40uy, 40uy))
        statusSection.CornerRadius <- CornerRadius(5.0)
        statusSection.Padding <- Thickness(10.0)
        
        let statusPanel = StackPanel()
        statusPanel.Spacing <- 5.0
        
        let statusTitle = TextBlock()
        statusTitle.Text <- "Status"
        statusTitle.FontWeight <- FontWeight.SemiBold
        statusPanel.Children.Add(statusTitle)
        
        let instructionText = TextBlock()
        instructionText.Text <- "Hold F9 to record (push-to-talk)"
        instructionText.Foreground <- SolidColorBrush(Colors.LightGreen)
        statusPanel.Children.Add(instructionText)
        
        let statusText = TextBlock()
        statusText.Text <- "Ready"
        statusText.Foreground <- SolidColorBrush(Colors.White)
        statusLabel <- Some statusText
        statusPanel.Children.Add(statusText)
        
        // Speech detection indicator
        let speechPanel = DockPanel()
        speechPanel.Margin <- Thickness(0.0, 5.0, 0.0, 0.0)
        
        let speechLabel = TextBlock()
        speechLabel.Text <- "Speech: "
        DockPanel.SetDock(speechLabel, Dock.Left)
        speechPanel.Children.Add(speechLabel)
        
        let indicator = Border()
        indicator.Width <- 20.0
        indicator.Height <- 20.0
        indicator.CornerRadius <- CornerRadius(10.0)
        indicator.Background <- SolidColorBrush(Colors.Gray)
        indicator.Margin <- Thickness(5.0, 0.0, 0.0, 0.0)
        DockPanel.SetDock(indicator, Dock.Left)
        speechIndicator <- Some indicator
        speechPanel.Children.Add(indicator)
        
        let speechStatus = TextBlock()
        speechStatus.Text <- "Not detecting"
        speechStatus.Margin <- Thickness(5.0, 0.0, 0.0, 0.0)
        speechPanel.Children.Add(speechStatus)
        
        statusPanel.Children.Add(speechPanel)
        
        statusSection.Child <- statusPanel
        mainPanel.Children.Add(statusSection)
        
        // Transcription section
        let transcriptionSection = Border()
        transcriptionSection.Background <- SolidColorBrush(Color.FromRgb(40uy, 40uy, 40uy))
        transcriptionSection.CornerRadius <- CornerRadius(5.0)
        transcriptionSection.Padding <- Thickness(10.0)
        
        let transcriptionPanel = StackPanel()
        transcriptionPanel.Spacing <- 5.0
        
        let transcriptionTitle = TextBlock()
        transcriptionTitle.Text <- "Last Transcription"
        transcriptionTitle.FontWeight <- FontWeight.SemiBold
        transcriptionPanel.Children.Add(transcriptionTitle)
        
        let transcriptionText = TextBlock()
        transcriptionText.Text <- "(No transcription yet)"
        transcriptionText.Foreground <- SolidColorBrush(Colors.LightGray)
        transcriptionText.TextWrapping <- TextWrapping.Wrap
        transcriptionText.FontStyle <- FontStyle.Italic
        transcriptionLabel <- Some transcriptionText
        transcriptionPanel.Children.Add(transcriptionText)
        
        transcriptionSection.Child <- transcriptionPanel
        mainPanel.Children.Add(transcriptionSection)
        
        // Log section
        let logSection = Border()
        logSection.Background <- SolidColorBrush(Color.FromRgb(40uy, 40uy, 40uy))
        logSection.CornerRadius <- CornerRadius(5.0)
        logSection.Padding <- Thickness(10.0)
        
        let logPanel = DockPanel()
        
        let logTitle = TextBlock()
        logTitle.Text <- "Activity Log"
        logTitle.FontWeight <- FontWeight.SemiBold
        logTitle.Margin <- Thickness(0.0, 0.0, 0.0, 5.0)
        DockPanel.SetDock(logTitle, Dock.Top)
        logPanel.Children.Add(logTitle)
        
        // ScrollViewer for the log
        let scrollViewer = ScrollViewer()
        scrollViewer.VerticalScrollBarVisibility <- Primitives.ScrollBarVisibility.Auto
        scrollViewer.Height <- 120.0
        
        let logBox = TextBox()
        logBox.IsReadOnly <- true
        logBox.AcceptsReturn <- true
        logBox.FontFamily <- FontFamily("Consolas, Courier New")
        logBox.FontSize <- 10.0
        logBox.BorderThickness <- Thickness(0.0)
        logBox.Background <- Brushes.Transparent
        logTextBox <- Some logBox
        
        scrollViewer.Content <- logBox
        logPanel.Children.Add(scrollViewer)
        
        logSection.Child <- logPanel
        mainPanel.Children.Add(logSection)
        
        this.Content <- mainPanel
    
    member this.UpdateLevel(dbLevel: float32) =
        Dispatcher.UIThread.Post(fun () ->
            match levelLabel, levelProgressBar with
            | Some label, Some bar ->
                // Update text
                let dbText = 
                    if System.Single.IsNaN(dbLevel) || System.Single.IsInfinity(dbLevel) then
                        "-∞ dB"
                    else
                        sprintf "%.1f dB" dbLevel
                label.Text <- dbText
                
                // Update progress bar (map -60 to 0 dB to 0% to 100%)
                let percentage = 
                    if dbLevel <= -60.0f then 0.0
                    elif dbLevel >= 0.0f then 100.0
                    else float(dbLevel + 60.0f) * 100.0 / 60.0
                
                bar.Value <- percentage
                
                // Color based on level
                bar.Foreground <- 
                    if dbLevel > -10.0f then SolidColorBrush(Colors.Red)
                    elif dbLevel > -20.0f then SolidColorBrush(Colors.Yellow)
                    else SolidColorBrush(Colors.LimeGreen)
            | _ -> ()
        )
    
    member this.AddLog(message: string) =
        match logTextBox with
        | Some box ->
            Dispatcher.UIThread.Post(fun () ->
                box.Text <- box.Text + message + Environment.NewLine
                // Auto-scroll to bottom
                box.CaretIndex <- box.Text.Length
            )
        | None -> ()
    
    member this.SetDevices(devices: (string * string) list) =
        match deviceCombo with
        | Some combo ->
            Dispatcher.UIThread.Post(fun () ->
                combo.Items.Clear()
                for (name, _) in devices do
                    combo.Items.Add(name) |> ignore
                if devices.Length > 0 then
                    combo.SelectedIndex <- 0
            )
        | None -> ()
    
    member this.SetStatus(status: string, isRecording: bool) =
        match statusLabel with
        | Some label ->
            Dispatcher.UIThread.Post(fun () ->
                label.Text <- status
                label.Foreground <- 
                    if isRecording then SolidColorBrush(Colors.Red)
                    else SolidColorBrush(Colors.White)
            )
        | None -> ()
    
    member this.GetSelectedModel() =
        match modelCombo with
        | Some combo -> 
            match combo.SelectedIndex with
            | 0 -> "small-en"
            | 1 -> "en"
            | _ -> "small-en"
        | None -> "small-en"
    
    member this.SetSpeechDetected(detected: bool) =
        match speechIndicator with
        | Some indicator ->
            Dispatcher.UIThread.Post(fun () ->
                if detected then
                    indicator.Background <- SolidColorBrush(Colors.LimeGreen)
                    // Pulse animation effect
                    indicator.Width <- 25.0
                    indicator.Height <- 25.0
                    async {
                        do! Async.Sleep(100)
                        Dispatcher.UIThread.Post(fun () ->
                            indicator.Width <- 20.0
                            indicator.Height <- 20.0
                        )
                    } |> Async.Start
                else
                    indicator.Background <- SolidColorBrush(Colors.Gray)
            )
        | None -> ()
    
    member this.SetTranscription(text: string) =
        match transcriptionLabel with
        | Some label ->
            Dispatcher.UIThread.Post(fun () ->
                label.Text <- text
                label.Foreground <- SolidColorBrush(Colors.White)
                label.FontStyle <- FontStyle.Normal
            )
        | None -> ()