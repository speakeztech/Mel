module Mel.UI.TrayProgram

open System
open Avalonia

[<EntryPoint>]
let main args =
    try
        printfn "Starting SpeakEZ Voice Transcription..."
        printfn "======================================="
        printfn "The app will run in the system tray."
        printfn "Right-click the SpeakEZ icon for options."
        printfn ""
        
        AppBuilder
            .Configure<SimpleTrayApp>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args)
    with
    | ex ->
        Console.Error.WriteLine($"Fatal error: {ex.Message}")
        Console.Error.WriteLine(ex.StackTrace)
        1