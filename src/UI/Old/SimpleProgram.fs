module Mel.UI.SimpleProgram

open System
open Mel.Core.Service.Host

[<EntryPoint>]
let main args =
    try
        printfn "Mel Voice Transcription Service"
        printfn "================================"
        printfn "Starting service..."
        
        let host = MelServiceHost.CreateHost(args)
        host.Run()
        0
    with
    | ex ->
        Console.Error.WriteLine($"Fatal error: {ex.Message}")
        Console.Error.WriteLine(ex.StackTrace)
        1