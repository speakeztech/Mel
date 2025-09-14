module Mel.Core.Transcription.Whisper

open System
open System.IO
open WhisperFS
open Mel.Core.Transcription

// Global initialization state - shared across all instances
let mutable private globalInitialized = false
let private initLock = obj()

let private ensureGlobalInitialized() =
    async {
        if not globalInitialized then
            return lock initLock (fun () ->
                if not globalInitialized then
                    // Initialize synchronously within the lock to ensure it only happens once
                    let initResult = WhisperFS.initialize() |> Async.RunSynchronously
                    match initResult with
                    | Ok () ->
                        globalInitialized <- true
                        Ok ()
                    | Error err ->
                        Error (sprintf "Failed to initialize WhisperFS: %s" err.Message)
                else
                    Ok ()
            )
        else
            return Ok ()
    }

type WhisperTranscriber(config: WhisperConfig) =

    let whisperConfig =
        { WhisperFS.WhisperConfig.defaultConfig with
            ModelPath = config.ModelPath
            ModelType = config.ModelType
            Language = config.Language
            ThreadCount = config.ThreadCount
            MaxLen = config.MaxSegmentLength
            Translate = config.EnableTranslate
        }

    let mutable clientOption: IWhisperClient option = None

    // Initialize client eagerly at construction time
    let initializeClient() =
        async {
            let! initResult = ensureGlobalInitialized()
            match initResult with
            | Ok () ->
                // createClient is now async and returns Result
                let! clientResult = WhisperFS.createClient whisperConfig
                match clientResult with
                | Ok client ->
                    clientOption <- Some client
                    printfn "WhisperFS client initialized successfully at startup"
                    return Ok client
                | Error err ->
                    let msg = sprintf "Failed to create WhisperFS client: %s" err.Message
                    printfn "WARNING: %s" msg
                    return Error msg
            | Error msg ->
                printfn "WARNING: %s" msg
                return Error msg
        }

    // Start initialization immediately
    let initTask = initializeClient() |> Async.StartAsTask

    let getOrCreateClient() =
        async {
            match clientOption with
            | Some client -> return Ok client
            | None ->
                // Wait for initialization if still running
                let! result = Async.AwaitTask initTask
                return result
        }

    let mapSegment (segment: WhisperFS.Segment) : TranscriptionSegment =
        {
            Start = float segment.StartTime
            End = float segment.EndTime
            Text = segment.Text
            Confidence =
                segment.Tokens
                |> List.tryHead
                |> Option.map (fun t -> t.Probability)
        }

    interface IWhisperTranscriber with
        member _.TranscribeAsync(samples, sampleRate) =
            async {
                let startTime = DateTime.UtcNow

                let! clientResult = getOrCreateClient()
                match clientResult with
                | Error msg ->
                    return failwith msg
                | Ok client ->
                    let! result = client.ProcessAsync(samples)

                    match result with
                    | Ok transcription ->
                        let processingTime = DateTime.UtcNow - startTime
                        let audioDuration = TimeSpan.FromSeconds(float samples.Length / float sampleRate)

                        return {
                            FullText = transcription.FullText
                            Segments = transcription.Segments |> List.map mapSegment
                            Duration = audioDuration
                            ProcessingTime = processingTime
                            Timestamp = startTime
                        }
                    | Error err ->
                        return failwithf "Transcription failed: %s" err.Message
            }

        member _.TranscribeFileAsync(audioPath) =
            async {
                let startTime = DateTime.UtcNow

                let! clientResult = getOrCreateClient()
                match clientResult with
                | Error msg ->
                    return failwith msg
                | Ok client ->
                    let! result = client.ProcessFileAsync(audioPath)

                    match result with
                    | Ok transcription ->
                        let processingTime = DateTime.UtcNow - startTime

                        return {
                            FullText = transcription.FullText
                            Segments = transcription.Segments |> List.map mapSegment
                            Duration = transcription.Duration
                            ProcessingTime = processingTime
                            Timestamp = startTime
                        }
                    | Error err ->
                        return failwithf "Transcription failed: %s" err.Message
            }

        member _.IsModelLoaded = File.Exists(config.ModelPath)

        member _.Dispose() =
            match clientOption with
            | Some client -> client.Dispose()
            | None -> ()

let downloadModel (modelType: ModelType) (targetPath: string) =
    async {
        printfn "Downloading model %A to %s" modelType targetPath

        // Create directory if it doesn't exist
        let directory = Path.GetDirectoryName(targetPath)
        if not (Directory.Exists(directory)) then
            Directory.CreateDirectory(directory) |> ignore

        // Use WhisperFS's model downloader
        let! result = Runtime.Models.downloadModelAsync modelType
        match result with
        | Ok modelPath ->
            // Copy to target path if different
            if modelPath <> targetPath then
                File.Copy(modelPath, targetPath, true)
            printfn "Model downloaded successfully to %s" targetPath
        | Error err ->
            failwithf "Failed to download model: %s" err.Message
    }

let getModelInfo (modelType: ModelType) =
    {
        ModelType = modelType
        Size = modelType.GetModelSize()
        Url = None
        IsQuantized = false
    }