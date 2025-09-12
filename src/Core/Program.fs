module Mel.Core.Program

open System
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Mel.Core.Service.Host

[<EntryPoint>]
let main args =
    try
        let host = MelServiceHost.CreateHost(args)
        host.Run()
        0
    with
    | ex ->
        Console.Error.WriteLine($"Fatal error: {ex.Message}")
        Console.Error.WriteLine(ex.StackTrace)
        1