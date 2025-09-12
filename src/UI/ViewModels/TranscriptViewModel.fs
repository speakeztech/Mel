namespace Mel.UI.ViewModels

open System
open System.Collections.ObjectModel
open Mel.UI.Models
open Mel.UI.Models.AppModel
open Elmish

type TranscriptViewModel(store: IStore<Model, Msg>) =
    inherit BaseViewModel()
    
    let mutable transcriptionHistory = ObservableCollection<TranscriptionEntry>()
    let mutable selectedTranscription: TranscriptionEntry option = None
    
    do
        store.Model.TranscriptionHistory
        |> List.iter transcriptionHistory.Add
    
    member _.TranscriptionHistory = transcriptionHistory
    
    member _.SelectedTranscription
        with get() = selectedTranscription
        and set(value) = base.SetProperty(&selectedTranscription, value) |> ignore
    
    member _.ClearHistory() =
        transcriptionHistory.Clear()
        store.Dispatch ClearHistory
    
    member _.ExportTranscript() =
        store.Dispatch ExportTranscript
    
    member _.CopyToClipboard() =
        match selectedTranscription with
        | Some entry -> store.Dispatch (CopyToClipboard entry.Text)
        | None -> ()
    
    member _.TotalTranscriptions = transcriptionHistory.Count
    
    member _.TotalDuration =
        transcriptionHistory
        |> Seq.sumBy (fun t -> t.Duration.TotalSeconds)
        |> TimeSpan.FromSeconds