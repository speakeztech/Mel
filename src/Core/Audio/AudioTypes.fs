namespace Mel.Core.Audio

open System
open System.Threading

[<Struct>]
type AudioFrame = {
    Samples: float32[]
    SampleRate: int
    Channels: int
    Timestamp: DateTime
}

type AudioFormat = {
    SampleRate: int
    Channels: int
    BitsPerSample: int
}

type IAudioCapture =
    abstract member StartCapture: unit -> unit
    abstract member StopCapture: unit -> unit
    abstract member CaptureFrameAsync: CancellationToken -> Async<AudioFrame option>
    abstract member IsCapturing: bool
    abstract member AudioFormat: AudioFormat
    inherit IDisposable

type VADConfig = {
    EnergyThreshold: float32
    MinSpeechDuration: float32
    MaxSilenceDuration: float32
    SampleRate: int
    FrameSize: int
}

type VADState =
    | Idle
    | Speaking of startTime: DateTime
    | SilenceAfterSpeech of speechStart: DateTime * silenceStart: DateTime

type VADResult =
    | NoChange
    | SpeechStarted
    | SpeechEnded of duration: TimeSpan
    | SpeechContinuing