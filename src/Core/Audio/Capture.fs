module Mel.Core.Audio.Capture

open System
open System.Threading
open System.Threading.Channels
open NAudio.Wave
open Mel.Core.Audio

type WasapiCapture(deviceIndex: int, ?bufferSize: int) =
    let bufferSize = defaultArg bufferSize 4096
    let sampleRate = 16000
    let channels = 1
    let audioChannel = Channel.CreateUnbounded<AudioFrame>()
    
    let mutable waveIn: WaveInEvent option = None
    let mutable isCapturing = false
    
    let audioFormat = {
        SampleRate = sampleRate
        Channels = channels
        BitsPerSample = 32
    }
    
    let convertBytesToFloat32 (buffer: byte[]) (bytesRecorded: int) =
        // Convert 16-bit PCM to float32
        let sampleCount = bytesRecorded / 2  // 2 bytes per 16-bit sample
        let samples = Array.zeroCreate<float32> sampleCount
        
        for i in 0 .. sampleCount - 1 do
            let sample16 = BitConverter.ToInt16(buffer, i * 2)
            samples.[i] <- float32 sample16 / 32768.0f  // Normalize to -1.0 to 1.0
        
        samples
    
    let onDataAvailable (args: WaveInEventArgs) =
        if args.BytesRecorded > 0 then
            let samples = convertBytesToFloat32 args.Buffer args.BytesRecorded
            let frame = {
                Samples = samples
                SampleRate = sampleRate
                Channels = channels
                Timestamp = DateTime.UtcNow
            }
            audioChannel.Writer.TryWrite(frame) |> ignore
    
    interface IAudioCapture with
        member _.StartCapture() =
            if not isCapturing then
                let wave = new WaveInEvent()
                wave.DeviceNumber <- deviceIndex
                wave.WaveFormat <- new WaveFormat(sampleRate, 16, channels)  // Use 16-bit for better compatibility
                wave.BufferMilliseconds <- (bufferSize * 1000) / (sampleRate * channels * 4)
                wave.DataAvailable.Add(onDataAvailable)
                wave.StartRecording()
                waveIn <- Some wave
                isCapturing <- true
        
        member _.StopCapture() =
            match waveIn with
            | Some wave ->
                wave.StopRecording()
                wave.Dispose()
                waveIn <- None
                isCapturing <- false
            | None -> ()
        
        member _.CaptureFrameAsync(ct) =
            async {
                let! frame = audioChannel.Reader.ReadAsync(ct).AsTask() |> Async.AwaitTask
                return Some frame
            }
        
        member _.IsCapturing = isCapturing
        
        member _.AudioFormat = audioFormat
        
        member this.Dispose() =
            (this :> IAudioCapture).StopCapture()
            audioChannel.Writer.Complete()

type SimulatedCapture(sampleRate: int, frameSize: int) =
    let mutable isCapturing = false
    let random = Random()
    
    let audioFormat = {
        SampleRate = sampleRate
        Channels = 1
        BitsPerSample = 32
    }
    
    let generateSilence size =
        Array.init size (fun _ -> 
            (random.NextSingle() - 0.5f) * 0.001f
        )
    
    let generateSpeech size =
        Array.init size (fun i ->
            let t = float32 i / float32 sampleRate
            let freq = 440.0f + (random.NextSingle() * 100.0f)
            let amplitude = 0.1f + (random.NextSingle() * 0.05f)
            amplitude * sin(2.0f * float32 Math.PI * freq * t) +
            (random.NextSingle() - 0.5f) * 0.01f
        )
    
    interface IAudioCapture with
        member _.StartCapture() =
            isCapturing <- true
        
        member _.StopCapture() =
            isCapturing <- false
        
        member _.CaptureFrameAsync(ct) =
            async {
                if isCapturing && not ct.IsCancellationRequested then
                    do! Async.Sleep(frameSize * 1000 / sampleRate)
                    
                    let isSpeech = random.Next(100) < 30
                    let samples = 
                        if isSpeech then generateSpeech frameSize
                        else generateSilence frameSize
                    
                    let frame = {
                        Samples = samples
                        SampleRate = sampleRate
                        Channels = 1
                        Timestamp = DateTime.UtcNow
                    }
                    return Some frame
                else
                    return None
            }
        
        member _.IsCapturing = isCapturing
        
        member _.AudioFormat = audioFormat
        
        member _.Dispose() =
            isCapturing <- false