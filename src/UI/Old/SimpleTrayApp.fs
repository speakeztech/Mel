namespace Mel.UI

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent

type SimpleTrayApp() =
    inherit Application()
    
    let mutable trayIcon: TrayIcon option = None
    
    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.RequestedThemeVariant <- Avalonia.Styling.ThemeVariant.Dark
    
    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            // Create system tray icon
            let tray = new TrayIcon()
            
            // Set icon
            try
                let iconPaths = [
                    "../../img/SpeakEZcolorIcon.ico"
                    "img/SpeakEZcolorIcon.ico"
                    @"D:\repos\Mel\img\SpeakEZcolorIcon.ico"
                ]
                
                let existingPath = iconPaths |> List.tryFind System.IO.File.Exists
                
                match existingPath with
                | Some path ->
                    use stream = System.IO.File.OpenRead(path)
                    tray.Icon <- WindowIcon(stream)
                    printfn "Loaded SpeakEZ icon from: %s" path
                | None ->
                    printfn "Warning: SpeakEZ icon not found"
            with
            | ex -> printfn "Failed to load icon: %s" ex.Message
            
            // Set tooltip
            tray.ToolTipText <- "SpeakEZ Voice Transcription - Right-click for options"
            
            // Create context menu
            let menu = NativeMenu()
            
            let toggleItem = NativeMenuItem("Start/Stop Recording (F9)")
            toggleItem.Click.Add(fun _ -> 
                printfn "Toggle recording clicked"
            )
            menu.Add(toggleItem)
            
            menu.Add(NativeMenuItemSeparator())
            
            let settingsItem = NativeMenuItem("Settings...")
            settingsItem.Click.Add(fun _ -> 
                printfn "Settings clicked"
            )
            menu.Add(settingsItem)
            
            let transcriptItem = NativeMenuItem("Show Transcript")
            transcriptItem.Click.Add(fun _ -> 
                printfn "Show transcript clicked"
            )
            menu.Add(transcriptItem)
            
            menu.Add(NativeMenuItemSeparator())
            
            let aboutItem = NativeMenuItem("About SpeakEZ")
            aboutItem.Click.Add(fun _ -> 
                printfn "SpeakEZ Voice Transcription"
                printfn "Powered by Whisper.NET"
            )
            menu.Add(aboutItem)
            
            menu.Add(NativeMenuItemSeparator())
            
            let exitItem = NativeMenuItem("Exit")
            exitItem.Click.Add(fun _ -> 
                desktop.Shutdown()
            )
            menu.Add(exitItem)
            
            tray.Menu <- menu
            
            // Double-click to toggle recording
            tray.Clicked.Add(fun _ -> 
                printfn "Tray icon double-clicked - toggle recording"
            )
            
            // Show the tray icon
            tray.IsVisible <- true
            trayIcon <- Some tray
            
            // Keep app running in background
            desktop.ShutdownMode <- ShutdownMode.OnExplicitShutdown
            
            printfn "SpeakEZ system tray initialized"
            
        | _ -> ()
        
        base.OnFrameworkInitializationCompleted()