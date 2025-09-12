module Mel.Core.Audio.VAD

open System
open Mel.Core.Audio

type VoiceActivityDetector(config: VADConfig) =
    let mutable smoothedEnergy = 0.0f
    let mutable state = Idle
    let alpha = 0.95f
    
    let calculateRMS (samples: float32[]) =
        let mutable sum = 0.0f
        for sample in samples do
            sum <- sum + (sample * sample)
        sqrt (sum / float32 samples.Length)
    
    member _.ProcessFrame(frame: AudioFrame): VADResult =
        let energy = calculateRMS frame.Samples
        smoothedEnergy <- alpha * smoothedEnergy + (1.0f - alpha) * energy
        
        let voiceDetected = smoothedEnergy > config.EnergyThreshold
        let now = DateTime.UtcNow
        
        match state, voiceDetected with
        | Idle, true ->
            state <- Speaking now
            SpeechStarted
            
        | Speaking startTime, false ->
            state <- SilenceAfterSpeech(startTime, now)
            NoChange
            
        | Speaking _, true ->
            SpeechContinuing
            
        | SilenceAfterSpeech(speechStart, silenceStart), true ->
            state <- Speaking speechStart
            SpeechContinuing
            
        | SilenceAfterSpeech(speechStart, silenceStart), false ->
            let silenceDuration = (now - silenceStart).TotalSeconds |> float32
            if silenceDuration > config.MaxSilenceDuration then
                let speechDuration = silenceStart - speechStart
                state <- Idle
                SpeechEnded speechDuration
            else
                NoChange
                
        | Idle, false ->
            NoChange
    
    member _.Reset() =
        smoothedEnergy <- 0.0f
        state <- Idle
    
    member _.State = state
    member _.CurrentEnergy = smoothedEnergy