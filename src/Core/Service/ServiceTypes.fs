namespace Mel.Core.Service

open System
open Mel.Core.Transcription.VoskTypes

type ServiceStatus =
    | Idle
    | Recording
    | Transcribing
    | Error of string

type ServiceConfig = {
    VoskConfig: VoskConfig
    VADConfig: Mel.Core.Audio.VADConfig
    AudioDeviceIndex: int
    BufferSize: int
    EnableAutoStart: bool
    SaveTranscripts: bool
    TranscriptPath: string option
}

type TranscriptionEvent = {
    Id: Guid
    Text: string
    Timestamp: DateTime
    Duration: TimeSpan
    AudioLength: float
}

type ITranscriptionService =
    abstract member StartRecording: unit -> unit
    abstract member StopRecording: unit -> unit
    abstract member GetStatus: unit -> ServiceStatus
    abstract member OnTranscription: IEvent<TranscriptionEvent>
    abstract member OnStatusChanged: IEvent<ServiceStatus>
    inherit IDisposable