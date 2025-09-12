module Mel.Core.Service.Host

open System
open Mel.Core.Service

// Placeholder service host for future implementation
// The Vosk streaming version is currently implemented directly in the UI app

type TranscriptionServiceHost(config: ServiceConfig) =
    interface ITranscriptionService with
        member _.StartRecording() = ()
        member _.StopRecording() = ()
        member _.GetStatus() = ServiceStatus.Idle
        member _.OnTranscription = Event<TranscriptionEvent>().Publish
        member _.OnStatusChanged = Event<ServiceStatus>().Publish
        member _.Dispose() = ()