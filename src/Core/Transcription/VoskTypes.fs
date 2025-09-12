module Mel.Core.Transcription.VoskTypes

open System

// Vosk configuration
type VoskConfig = {
    ModelPath: string
    SampleRate: float32
    MaxAlternatives: int
    Words: bool  // Include word-level timing
    PartialWords: bool  // Include partial word results
}

// Streaming result types
type VoskPartialResult = {
    Partial: string
    Timestamp: DateTime
}

type VoskFinalResult = {
    Text: string
    Confidence: float32 option
    Words: VoskWord list option
    Timestamp: DateTime
}

and VoskWord = {
    Word: string
    Start: float32
    End: float32
    Confidence: float32
}

// Vosk transcriber interface for streaming
type IVoskTranscriber =
    abstract member StartStream: unit -> unit
    abstract member ProcessAudio: samples: float32[] -> VoskPartialResult option
    abstract member FinishStream: unit -> VoskFinalResult option
    abstract member Reset: unit -> unit
    abstract member IsModelLoaded: bool
    inherit IDisposable

// Model info
type VoskModelInfo = {
    Name: string
    Language: string
    Size: int64
    Url: string option
}