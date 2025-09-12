module Mel.UI.CompositionRoot

open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Mel.Core.Service
open Mel.Core.Transcription
open Mel.UI.ViewModels
open Mel.UI.Tray.TrayIconService
open Whisper.net.Ggml

type CompositionRoot() =
    let services = ServiceCollection()
    
    do
        services
            .AddSingleton<ServiceConfig>(fun _ ->
                {
                    WhisperConfig = {
                        ModelPath = "./models/ggml-base.en.bin"
                        ModelType = GgmlType.BaseEn
                        Language = "en"
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
            )
            .AddSingleton<MainViewModel>()
            .AddTransient<SettingsViewModel>()
            .AddTransient<TranscriptViewModel>()
            .AddSingleton<TrayIconService>()
            .AddLogging(fun builder ->
                builder
                    .SetMinimumLevel(LogLevel.Information)
                    .AddConsole()
                |> ignore
            )
        |> ignore
    
    let serviceProvider = services.BuildServiceProvider()
    
    member _.GetService<'T>() = serviceProvider.GetRequiredService<'T>()
    
    member _.GetMainViewModel() = serviceProvider.GetRequiredService<MainViewModel>()
    
    member _.GetTrayService() = serviceProvider.GetRequiredService<TrayIconService>()