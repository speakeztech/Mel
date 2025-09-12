module Mel.UI.Program

open System
open Avalonia

[<EntryPoint>]
let main args =
    try
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args)
    with
    | ex ->
        Console.Error.WriteLine($"Fatal error: {ex.Message}")
        Console.Error.WriteLine(ex.StackTrace)
        1