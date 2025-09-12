namespace Mel.Core.Transcription

open System
open Whisper.net.Ggml

type WhisperConfig = {
    ModelPath: string
    ModelType: GgmlType
    Language: string
    UseGpu: bool
    ThreadCount: int
    MaxSegmentLength: int
    EnableTranslate: bool
}

type TranscriptionSegment = {
    Start: float
    End: float
    Text: string
    Confidence: float option
}

type TranscriptionResult = {
    FullText: string
    Segments: TranscriptionSegment list
    Duration: TimeSpan
    ProcessingTime: TimeSpan
    Timestamp: DateTime
}

type TranscriptionRequest = {
    Audio: float32[]
    SampleRate: int
    Timestamp: DateTime
    RequestId: Guid
}

type IWhisperTranscriber =
    abstract member TranscribeAsync: float32[] * int -> Async<TranscriptionResult>
    abstract member TranscribeFileAsync: string -> Async<TranscriptionResult>
    abstract member IsModelLoaded: bool
    inherit IDisposable

type ModelDownloadInfo = {
    ModelType: GgmlType
    Size: int64
    Url: string option
    IsQuantized: bool
}

type ModelDownloadProgress = {
    ModelType: GgmlType
    BytesDownloaded: int64
    TotalBytes: int64
    PercentComplete: float
}