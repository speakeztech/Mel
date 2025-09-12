namespace Mel.UI.ViewModels

open System
open System.Reactive.Linq
open System.Windows.Input
open Elmish
open FSharp.Control.Reactive
open Mel.UI.Models
open Mel.UI.Models.AppModel
open Mel.UI.Models.AppUpdate

type MainViewModel() =
    inherit BaseViewModel()
    
    let mutable store = Program.mkProgram init update (fun _ _ -> ()) |> Program.mkStore
    
    let mutable isRecording = false
    let mutable isTranscribing = false
    let mutable statusText = "Ready"
    let mutable statusIcon = "Assets/Icons/idle.png"
    let mutable currentTranscription = ""
    
    let updateFromModel (model: Model) =
        isRecording <- model.Status = Recording
        isTranscribing <- model.Status = Transcribing
        
        statusText <- 
            match model.Status with
            | Idle -> "Ready"
            | Recording -> "Recording..."
            | Transcribing -> "Transcribing..."
            | Error msg -> $"Error: {msg}"
        
        statusIcon <-
            match model.Status with
            | Idle -> "Assets/Icons/idle.png"
            | Recording -> "Assets/Icons/recording.png"
            | Transcribing -> "Assets/Icons/processing.png"
            | Error _ -> "Assets/Icons/error.png"
        
        currentTranscription <- model.CurrentTranscription |> Option.defaultValue ""
    
    do
        store.Model
        |> Observable.subscribe updateFromModel
        |> ignore
    
    member _.IsRecording 
        with get() = isRecording
        and set(value) = base.SetProperty(&isRecording, value) |> ignore
    
    member _.IsTranscribing
        with get() = isTranscribing
        and set(value) = base.SetProperty(&isTranscribing, value) |> ignore
    
    member _.StatusText
        with get() = statusText
        and set(value) = base.SetProperty(&statusText, value) |> ignore
    
    member _.StatusIcon
        with get() = statusIcon
        and set(value) = base.SetProperty(&statusIcon, value) |> ignore
    
    member _.CurrentTranscription
        with get() = currentTranscription
        and set(value) = base.SetProperty(&currentTranscription, value) |> ignore
    
    member _.TranscriptionHistory = store.Model.TranscriptionHistory
    
    member _.ToggleRecording() =
        let currentStatus = store.Model.Status
        match currentStatus with
        | Recording -> store.Dispatch StopRecording
        | _ -> store.Dispatch StartRecording
    
    member _.ShowSettings() =
        store.Dispatch (ShowView SettingsView)
    
    member _.ShowTranscript() =
        store.Dispatch (ShowView TranscriptView)
    
    member _.MinimizeToTray() =
        store.Dispatch MinimizeToTray
    
    member _.Store = store