module Mel.UI.Tray.TrayIconService

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Platform
open Mel.UI.ViewModels

type TrayIconService(mainVm: MainViewModel) =
    let trayIcon = TrayIcon()
    
    let createContextMenu() =
        let menu = NativeMenu()
        
        let toggleItem = NativeMenuItem("Toggle Recording (F9)")
        toggleItem.Click.Add(fun _ -> mainVm.ToggleRecording())
        menu.Add(toggleItem)
        
        menu.Add(NativeMenuItemSeparator())
        
        let settingsItem = NativeMenuItem("Settings...")
        settingsItem.Click.Add(fun _ -> mainVm.ShowSettings())
        menu.Add(settingsItem)
        
        let transcriptItem = NativeMenuItem("Show Transcript")
        transcriptItem.Click.Add(fun _ -> mainVm.ShowTranscript())
        menu.Add(transcriptItem)
        
        menu.Add(NativeMenuItemSeparator())
        
        let exitItem = NativeMenuItem("Exit")
        exitItem.Click.Add(fun _ -> 
            match Application.Current.ApplicationLifetime with
            | :? Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime as desktop ->
                desktop.Shutdown()
            | _ -> ()
        )
        menu.Add(exitItem)
        
        menu
    
    member _.Initialize() =
        trayIcon.Menu <- createContextMenu()
        trayIcon.ToolTipText <- "SpeakEZ Voice Transcription"
        
        try
            let iconPath = 
                if System.IO.File.Exists("../../img/SpeakEZcolorIcon.ico") then
                    "../../img/SpeakEZcolorIcon.ico"
                elif System.IO.File.Exists("img/SpeakEZcolorIcon.ico") then
                    "img/SpeakEZcolorIcon.ico"
                elif System.IO.File.Exists("D:\\repos\\Mel\\img\\SpeakEZcolorIcon.ico") then
                    "D:\\repos\\Mel\\img\\SpeakEZcolorIcon.ico"
                else
                    null
            
            if iconPath <> null then
                use stream = System.IO.File.OpenRead(iconPath)
                trayIcon.Icon <- WindowIcon(stream)
                printfn "Loaded tray icon from: %s" iconPath
        with
        | ex -> printfn "Failed to load tray icon: %s" ex.Message
        
        trayIcon.Clicked.Add(fun _ -> 
            mainVm.ShowSettings()
        )
        
        trayIcon.IsVisible <- true
    
    member _.UpdateIcon(iconPath: string) =
        try
            if System.IO.File.Exists(iconPath) then
                use stream = System.IO.File.OpenRead(iconPath)
                trayIcon.Icon <- WindowIcon(stream)
        with
        | ex -> printfn "Failed to update tray icon: %s" ex.Message
    
    member _.Dispose() =
        trayIcon.IsVisible <- false
        trayIcon.Dispose()