module Mel.Core.Service.Host

open System
open System.Threading
open System.Threading.Tasks
open System.Threading.Channels
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Mel.Core.Audio
open Mel.Core.Audio.VAD
open Mel.Core.Audio.Capture
open Mel.Core.Transcription
open Mel.Core.Transcription.Whisper
open Mel.Core.Service

type MelBackgroundService(
    config: ServiceConfig,
    logger: ILogger<MelBackgroundService>) =
    
    inherit BackgroundService()
    
    let transcriptionEvent = Event<TranscriptionEvent>()
    let statusChangedEvent = Event<ServiceStatus>()
    
    let mutable status = ServiceStatus.Idle
    let audioBuffer = ResizeArray<float32>()
    let transcriptionQueue = Channel.CreateUnbounded<TranscriptionRequest>()
    
    let whisperTranscriber = 
        new WhisperTranscriber(config.WhisperConfig) :> IWhisperTranscriber
    
    let vad = new VoiceActivityDetector(config.VADConfig)
    
    let audioCapture = 
        new WasapiCapture(config.AudioDeviceIndex, config.BufferSize) :> IAudioCapture
    
    let setStatus newStatus =
        if status <> newStatus then
            status <- newStatus
            statusChangedEvent.Trigger(newStatus)
            logger.LogInformation("Service status changed to: {Status}", newStatus)
    
    let processAudioPipeline (ct: CancellationToken) =
        async {
            try
                audioCapture.StartCapture()
                setStatus ServiceStatus.Recording
                logger.LogInformation("Audio capture started")
                
                while not ct.IsCancellationRequested do
                    let! frameOpt = audioCapture.CaptureFrameAsync(ct)
                    
                    match frameOpt with
                    | Some frame ->
                        let vadResults = vad.ProcessFrame(frame)
                        
                        match vadResults with
                        | SpeechStarted ->
                            logger.LogDebug("Speech detected, starting buffer")
                            audioBuffer.Clear()
                            audioBuffer.AddRange(frame.Samples)
                            
                        | SpeechContinuing when audioBuffer.Count > 0 ->
                            audioBuffer.AddRange(frame.Samples)
                            
                        | SpeechEnded duration ->
                            logger.LogInformation("Speech ended after {Duration}s", duration.TotalSeconds)
                            
                            if audioBuffer.Count > config.VADConfig.SampleRate / 2 then
                                let request = {
                                    Audio = audioBuffer.ToArray()
                                    SampleRate = frame.SampleRate
                                    Timestamp = DateTime.UtcNow
                                    RequestId = Guid.NewGuid()
                                }
                                
                                do! transcriptionQueue.Writer.WriteAsync(request, ct).AsTask() |> Async.AwaitTask
                                logger.LogDebug("Queued transcription request {RequestId}", request.RequestId)
                            
                            audioBuffer.Clear()
                            
                        | _ -> ()
                    | None -> ()
                    
            with
            | :? OperationCanceledException -> 
                logger.LogInformation("Audio pipeline cancelled")
            | ex -> 
                logger.LogError(ex, "Error in audio pipeline")
                setStatus (ServiceStatus.Error ex.Message)
        }
    
    let processTranscriptionPipeline (ct: CancellationToken) =
        async {
            try
                while not ct.IsCancellationRequested do
                    let! request = transcriptionQueue.Reader.ReadAsync(ct).AsTask() |> Async.AwaitTask
                    
                    logger.LogInformation("Processing transcription request {RequestId}", request.RequestId)
                    setStatus ServiceStatus.Transcribing
                    
                    let startTime = DateTime.UtcNow
                    
                    try
                        let! result = whisperTranscriber.TranscribeAsync(request.Audio, request.SampleRate)
                        
                        let processingTime = DateTime.UtcNow - startTime
                        
                        if not (String.IsNullOrWhiteSpace(result.FullText)) then
                            let event = {
                                Id = request.RequestId
                                Text = result.FullText
                                Timestamp = request.Timestamp
                                Duration = processingTime
                                AudioLength = float request.Audio.Length / float request.SampleRate
                            }
                            
                            transcriptionEvent.Trigger(event)
                            
                            logger.LogInformation(
                                "Transcription completed in {Duration}ms: {Text}", 
                                processingTime.TotalMilliseconds,
                                result.FullText)
                            
                            if config.SaveTranscripts && config.TranscriptPath.IsSome then
                                let path = config.TranscriptPath.Value
                                let timestamp = request.Timestamp.ToString("yyyyMMdd_HHmmss")
                                let filename = System.IO.Path.Combine(path, $"{timestamp}.txt")
                                do! System.IO.File.WriteAllTextAsync(filename, result.FullText) |> Async.AwaitTask
                        
                        setStatus ServiceStatus.Recording
                        
                    with
                    | ex ->
                        logger.LogError(ex, "Transcription failed for request {RequestId}", request.RequestId)
                        setStatus ServiceStatus.Recording
                        
            with
            | :? OperationCanceledException -> 
                logger.LogInformation("Transcription pipeline cancelled")
            | ex -> 
                logger.LogError(ex, "Error in transcription pipeline")
                setStatus (ServiceStatus.Error ex.Message)
        }
    
    override _.ExecuteAsync(ct: CancellationToken) =
        task {
            logger.LogInformation("Mel service starting...")
            logger.LogInformation("Model: {Model}, GPU: {UseGpu}, Threads: {Threads}", 
                config.WhisperConfig.ModelType, 
                config.WhisperConfig.UseGpu,
                config.WhisperConfig.ThreadCount)
            
            let audioPipeline = processAudioPipeline ct |> Async.StartAsTask
            let transcriptionPipeline = processTranscriptionPipeline ct |> Async.StartAsTask
            
            let! _ = Task.WhenAll([| audioPipeline; transcriptionPipeline |])
            
            logger.LogInformation("Mel service stopped")
        }
    
    member _.TranscriptionEvent = transcriptionEvent.Publish
    member _.StatusChangedEvent = statusChangedEvent.Publish
    member _.Status = status
    
    interface ITranscriptionService with
        member _.StartRecording() =
            if status = ServiceStatus.Idle then
                audioCapture.StartCapture()
                setStatus ServiceStatus.Recording
        
        member _.StopRecording() =
            if status = ServiceStatus.Recording then
                audioCapture.StopCapture()
                setStatus ServiceStatus.Idle
        
        member _.GetStatus() = status
        
        member _.OnTranscription = transcriptionEvent.Publish
        
        member _.OnStatusChanged = statusChangedEvent.Publish
        
        member _.Dispose() =
            audioCapture.Dispose()
            whisperTranscriber.Dispose()

type MelServiceHost() =
    static member CreateHost(args: string[]) =
        Host.CreateDefaultBuilder(args)
            .ConfigureServices(fun (services: IServiceCollection) ->
                let config = {
                    WhisperConfig = {
                        ModelPath = "./models/ggml-base.en.bin"
                        ModelType = WhisperFS.ModelType.BaseEn
                        Language = Some "en"
                        UseGpu = true
                        ThreadCount = 8
                        MaxSegmentLength = 30
                        EnableTranslate = false
                    }
                    VADConfig = {
                        EnergyThreshold = 0.01f
                        MinSpeechDuration = 0.5f
                        MaxSilenceDuration = 1.0f
                        SampleRate = 16000
                        FrameSize = 512
                    }
                    AudioDeviceIndex = 0
                    BufferSize = 4096
                    EnableAutoStart = true
                    SaveTranscripts = false
                    TranscriptPath = None
                }
                
                services.AddSingleton<ServiceConfig>(config) |> ignore
                services.AddHostedService<MelBackgroundService>() |> ignore
                services.AddWindowsService(fun options ->
                    options.ServiceName <- "Mel Voice Transcription"
                ) |> ignore
            )
            .Build()