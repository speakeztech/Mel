module Mel.UI.Program

open System
open System.Runtime.InteropServices
open Avalonia

// Windows API to allocate console for GUI app
module WinConsole =
    [<DllImport("kernel32.dll")>]
    extern bool AllocConsole()
    
    [<DllImport("kernel32.dll")>]
    extern bool AttachConsole(int processId)
    
    [<DllImport("kernel32.dll")>]
    extern bool FreeConsole()

[<EntryPoint>]
let main args =
    // Check for debug flag
    let debugMode = args |> Array.exists (fun arg -> 
        arg = "--debug" || arg = "-d" || arg = "/debug")
    
    // Allocate console for Windows GUI app if in debug mode
    if debugMode && Environment.OSVersion.Platform = PlatformID.Win32NT then
        if not (WinConsole.AttachConsole(-1)) then // Try to attach to parent console
            WinConsole.AllocConsole() |> ignore // Create new console if no parent
        
        // Redirect standard streams to console
        Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput()) |> fun sw -> sw.AutoFlush <- true; sw)
        Console.SetError(new System.IO.StreamWriter(Console.OpenStandardError()) |> fun sw -> sw.AutoFlush <- true; sw)
        Console.SetIn(new System.IO.StreamReader(Console.OpenStandardInput()))
    
    Console.OutputEncoding <- System.Text.Encoding.UTF8
    
    if debugMode then
        Console.WriteLine("========================================")
        Console.WriteLine("   SpeakEZ Voice Transcription - DEBUG")
        Console.WriteLine("========================================")
        Console.WriteLine("Working directory: " + Environment.CurrentDirectory)
        let argsStr = String.Join(", ", args)
        Console.WriteLine("Arguments: " + argsStr)
        Console.WriteLine("")
    
    try
        if debugMode then Console.WriteLine("Building Avalonia app...")
        
        // Store debug mode globally
        Environment.SetEnvironmentVariable("SPEAKEZ_DEBUG", if debugMode then "1" else "0")
        
        let result = 
            AppBuilder
                .Configure<SpeakEZApp>()
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args)
        
        if debugMode then Console.WriteLine("App exited with code: " + result.ToString())
        result
    with
    | ex ->
        Console.Error.WriteLine("Fatal error: " + ex.Message)
        Console.Error.WriteLine(ex.StackTrace)
        1