module Mel.Core.Transcription.Whisper

open System
open System.IO
open System.Threading.Tasks
open Whisper.net
open Whisper.net.Ggml
open Mel.Core.Transcription

type WhisperTranscriber(config: WhisperConfig) =
    
    let ensureModel() =
        task {
            if not (File.Exists(config.ModelPath)) then
                printfn "Downloading Whisper GGML model %A..." config.ModelType
                use! modelStream = WhisperGgmlDownloader.GetGgmlModelAsync(config.ModelType)
                use fileWriter = File.OpenWrite(config.ModelPath)
                do! modelStream.CopyToAsync(fileWriter)
                printfn "Model downloaded: %s" config.ModelPath
        }
    
    let whisperFactory = 
        let initTask = task {
            do! ensureModel()
            let builder = WhisperFactory.FromPath(config.ModelPath)
            return builder
        }
        initTask |> Async.AwaitTask |> Async.RunSynchronously
    
    let createProcessor() =
        let builder = whisperFactory.CreateBuilder()
                        .WithLanguage(config.Language)
                        .WithThreads(config.ThreadCount)
                        .WithMaxSegmentLength(config.MaxSegmentLength)
        
        let builder' = if config.EnableTranslate then builder.WithTranslate() else builder
        builder'.Build()
    
    let createWavFile (samples: float32[]) (sampleRate: int) (filePath: string) =
        use fs = new FileStream(filePath, FileMode.Create)
        use writer = new BinaryWriter(fs)
        
        writer.Write("RIFF"B)
        writer.Write(36 + samples.Length * 2)
        writer.Write("WAVE"B)
        writer.Write("fmt "B)
        writer.Write(16)
        writer.Write(1s)
        writer.Write(1s)
        writer.Write(sampleRate)
        writer.Write(sampleRate * 2)
        writer.Write(2s)
        writer.Write(16s)
        writer.Write("data"B)
        writer.Write(samples.Length * 2)
        
        for sample in samples do
            let pcm = int16 (Math.Max(-32768.0f, Math.Min(32767.0f, sample * 32767.0f)))
            writer.Write(pcm)
    
    interface IWhisperTranscriber with
        member _.TranscribeAsync(samples, sampleRate) =
            async {
                let startTime = DateTime.UtcNow
                let tempFile = Path.GetTempFileName() + ".wav"
                
                try
                    createWavFile samples sampleRate tempFile
                    
                    use processor = createProcessor()
                    use fileStream = File.OpenRead(tempFile)
                    
                    let segments = ResizeArray<TranscriptionSegment>()
                    let texts = ResizeArray<string>()
                    
                    let asyncEnum = processor.ProcessAsync(fileStream).GetAsyncEnumerator()
                    let mutable hasMore = true
                    while hasMore do
                        let! moveNext = asyncEnum.MoveNextAsync().AsTask() |> Async.AwaitTask
                        if moveNext then
                            let segment = asyncEnum.Current
                            segments.Add({
                                Start = segment.Start.TotalSeconds
                                End = segment.End.TotalSeconds
                                Text = segment.Text
                                Confidence = None
                            })
                            texts.Add(segment.Text)
                        else
                            hasMore <- false
                    
                    let processingTime = DateTime.UtcNow - startTime
                    let audioDuration = TimeSpan.FromSeconds(float samples.Length / float sampleRate)
                    
                    return {
                        FullText = String.Join(" ", texts)
                        Segments = segments |> List.ofSeq
                        Duration = audioDuration
                        ProcessingTime = processingTime
                        Timestamp = startTime
                    }
                finally
                    if File.Exists(tempFile) then
                        File.Delete(tempFile)
            }
        
        member _.TranscribeFileAsync(audioPath) =
            async {
                let startTime = DateTime.UtcNow
                use processor = createProcessor()
                use fileStream = File.OpenRead(audioPath)
                
                let segments = ResizeArray<TranscriptionSegment>()
                let texts = ResizeArray<string>()
                
                let asyncEnum = processor.ProcessAsync(fileStream).GetAsyncEnumerator()
                let mutable hasMore = true
                while hasMore do
                    let! moveNext = asyncEnum.MoveNextAsync().AsTask() |> Async.AwaitTask
                    if moveNext then
                        let segment = asyncEnum.Current
                        segments.Add({
                            Start = segment.Start.TotalSeconds
                            End = segment.End.TotalSeconds
                            Text = segment.Text
                            Confidence = None
                        })
                        texts.Add(segment.Text)
                    else
                        hasMore <- false
                
                let processingTime = DateTime.UtcNow - startTime
                
                return {
                    FullText = String.Join(" ", texts)
                    Segments = segments |> List.ofSeq
                    Duration = TimeSpan.Zero
                    ProcessingTime = processingTime
                    Timestamp = startTime
                }
            }
        
        member _.IsModelLoaded = File.Exists(config.ModelPath)
        
        member _.Dispose() =
            whisperFactory.Dispose()

let downloadModel (modelType: GgmlType) (targetPath: string) =
    task {
        printfn "Downloading model %A to %s" modelType targetPath
        use! stream = WhisperGgmlDownloader.GetGgmlModelAsync(modelType)
        use fileStream = File.Create(targetPath)
        do! stream.CopyToAsync(fileStream)
        printfn "Model download complete: %s" targetPath
    }

let getModelInfo (modelType: GgmlType) =
    let (size, isQuantized) =
        match modelType with
        | GgmlType.Tiny -> 39L * 1024L * 1024L, false
        | GgmlType.TinyEn -> 39L * 1024L * 1024L, false
        | GgmlType.Base -> 142L * 1024L * 1024L, false
        | GgmlType.BaseEn -> 142L * 1024L * 1024L, false
        | GgmlType.Small -> 466L * 1024L * 1024L, false
        | GgmlType.SmallEn -> 466L * 1024L * 1024L, false
        | GgmlType.Medium -> 1500L * 1024L * 1024L, false
        | GgmlType.MediumEn -> 1500L * 1024L * 1024L, false
        | GgmlType.LargeV1 -> 3000L * 1024L * 1024L, false
        | GgmlType.LargeV2 -> 3000L * 1024L * 1024L, false
        | GgmlType.LargeV3 -> 3000L * 1024L * 1024L, false
        | _ -> 0L, false
    
    {
        ModelType = modelType
        Size = size
        Url = None
        IsQuantized = isQuantized
    }