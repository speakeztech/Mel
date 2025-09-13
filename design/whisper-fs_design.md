# Whisper-FS: A Streaming-First F# Wrapper for Whisper.cpp

## Executive Summary

Whisper-FS is a proposed F# library that provides comprehensive bindings to whisper.cpp, offering both full feature parity with Whisper.NET for push-to-talk (PTT) scenarios and advanced streaming capabilities for real-time transcription. This "one-stop shopping" library consolidates all transcription needs, eliminating the need for multiple dependencies. This design document captures lessons learned from implementing both Whisper.NET (batch processing) and Vosk (unstable streaming) integrations, and proposes a unified solution.

## Motivation and Lessons Learned

### The Whisper.NET Experience

Our initial implementation used Whisper.NET, which provided excellent accuracy but suffered from fundamental limitations:

1. **Batch-Only Processing**: Whisper.NET only exposes `ProcessAsync`, requiring complete audio segments
2. **No Streaming Support**: Users had to wait until button release to see any text
3. **Lack of Intermediate Results**: No access to partial transcriptions or token-level callbacks
4. **Memory Inefficiency**: Had to buffer entire audio segments in memory

Example of the limitation we faced:
```fsharp
// Current Whisper.NET approach - all or nothing
let processWhisper (audioBuffer: float32[]) =
    async {
        // Must wait for complete audio
        let! result = processor.ProcessAsync(audioBuffer)
        return result.Text  // Only get final result
    }
```

### The Vosk Experiment

We then tried Vosk for true streaming, which revealed different problems:

1. **Unstable Partial Results**: Text would completely change mid-stream ("heard" → "hood")
2. **Aggressive Corrections**: Massive backspacing and rewriting that confused users
3. **Poor Grammar**: Lack of punctuation and capitalization
4. **Inconsistent Confidence**: No reliable way to know when text was stable

Example of Vosk's instability:
```
Stream: "this is a test to see if the" 
Stream: "cursor"  // Complete change!
Stream: "jump is still"
Stream: "present"
Final: "this is a test to see if the cursor jump is still present"
```

### Key Insights

1. **Streaming !== Real-time Token Output**: True streaming ASR (like Android voice typing) uses models specifically designed for stable incremental output
2. **Whisper's Strength**: Whisper excels at accuracy because it uses full context - this is also why it's not naturally streaming
3. **The Chunking Compromise**: Processing overlapping chunks with Whisper can provide periodic updates while maintaining accuracy
4. **Context is Critical**: Maintaining context between chunks is essential for coherent transcription
5. **Feature Parity Essential**: Must support all existing Whisper.NET features while adding streaming capabilities

## Whisper-FS Design Philosophy

### Core Principles

1. **Complete Feature Parity**: Support ALL Whisper.NET features for seamless migration
2. **Streaming-First Architecture**: Every API designed with streaming as the primary use case
3. **F# Idiomatic**: Leverage F#'s strengths - discriminated unions, async workflows, observables
4. **Zero-Copy Where Possible**: Direct memory management for audio buffers
5. **Flexible Confidence Models**: Let applications decide when text is "stable enough" to display
6. **Unified API**: Single library for both PTT and streaming scenarios

### Architecture Overview

```fsharp
namespace WhisperFS

open System
open System.IO

/// Model types matching Whisper.NET's GgmlType
type ModelType =
    | Tiny | TinyEn
    | Base | BaseEn
    | Small | SmallEn
    | Medium | MediumEn
    | LargeV1 | LargeV2 | LargeV3
    | Custom of path:string

    member this.GetModelSize() =
        match this with
        | Tiny | TinyEn -> 39L * 1024L * 1024L
        | Base | BaseEn -> 142L * 1024L * 1024L
        | Small | SmallEn -> 466L * 1024L * 1024L
        | Medium | MediumEn -> 1500L * 1024L * 1024L
        | LargeV1 | LargeV2 | LargeV3 -> 3000L * 1024L * 1024L
        | Custom _ -> 0L

/// Complete configuration matching and extending Whisper.NET
type WhisperConfig = {
    // Core Whisper.NET compatible options
    ModelPath: string
    ModelType: ModelType
    Language: string option  // None for auto-detect
    ThreadCount: int
    UseGpu: bool
    EnableTranslate: bool
    MaxSegmentLength: int

    // Advanced options from whisper.cpp
    Temperature: float32
    TemperatureInc: float32
    BeamSize: int
    BestOf: int
    MaxTokensPerSegment: int
    AudioContext: int
    NoContext: bool
    SingleSegment: bool
    PrintSpecialTokens: bool
    PrintProgress: bool
    PrintTimestamps: bool
    TokenTimestamps: bool
    ThresholdPt: float32
    ThresholdPtSum: float32
    MaxLen: int
    SplitOnWord: bool
    MaxTokens: int
    SpeedUp: bool
    DebugMode: bool
    AudioCtx: int
    InitialPrompt: string option
    SuppressBlank: bool
    SuppressNonSpeechTokens: bool
    MaxInitialTs: float32
    LengthPenalty: float32

    // Streaming-specific options
    StreamingMode: bool
    ChunkSizeMs: int
    OverlapMs: int
    MinConfidence: float32
    MaxContext: int
    StabilityThreshold: float32
}

/// Model management matching Whisper.NET
module ModelManagement =

    /// Download model (equivalent to WhisperGgmlDownloader)
    type IModelDownloader =
        abstract member DownloadModelAsync: modelType:ModelType -> Async<string>
        abstract member GetModelPath: modelType:ModelType -> string
        abstract member IsModelDownloaded: modelType:ModelType -> bool
        abstract member GetDownloadProgress: unit -> float

    /// Model factory (equivalent to WhisperFactory)
    type IWhisperFactory =
        inherit IDisposable
        abstract member CreateProcessor: config:WhisperConfig -> IWhisperProcessor
        abstract member CreateStream: config:WhisperConfig -> IWhisperStream
        abstract member FromPath: modelPath:string -> IWhisperFactory
        abstract member FromBuffer: buffer:byte[] -> IWhisperFactory

namespace WhisperFS

open System
open System.Runtime.InteropServices

/// Core streaming types
type TranscriptionEvent =
    | PartialTranscription of text:string * tokens:Token list * confidence:float32
    | FinalTranscription of text:string * tokens:Token list * segments:Segment list
    | ContextUpdate of contextData:byte[]
    | ProcessingError of error:string

and Token = {
    Text: string
    Timestamp: float32
    Probability: float32
    IsSpecial: bool
}

and Segment = {
    Text: string
    StartTime: float32
    EndTime: float32
    Tokens: Token list
}

/// Streaming configuration
type StreamConfig = {
    /// Size of audio chunks in milliseconds
    ChunkSizeMs: int
    /// Overlap between chunks in milliseconds  
    OverlapMs: int
    /// Minimum confidence to emit partial results
    MinConfidence: float32
    /// Maximum context to maintain (in tokens)
    MaxContext: int
    /// Enable token-level timestamps
    TokenTimestamps: bool
    /// Language hint (empty for auto-detect)
    Language: string
    /// Prompt to guide transcription style
    InitialPrompt: string
}

/// Batch processor interface (Whisper.NET compatible)
type IWhisperProcessor =
    inherit IDisposable

    /// Process audio file (Whisper.NET compatible)
    abstract member ProcessAsync: audioPath:string -> Async<TranscriptionResult>

    /// Process audio stream (Whisper.NET compatible)
    abstract member ProcessAsync: audioStream:Stream -> IAsyncEnumerable<Segment>

    /// Process audio buffer
    abstract member ProcessAsync: samples:float32[] -> Async<TranscriptionResult>

    /// Process with callbacks for segments
    abstract member ProcessAsync: samples:float32[] * onSegment:(Segment -> unit) -> Async<TranscriptionResult>

    /// Change language at runtime
    abstract member SetLanguage: language:string -> unit

    /// Detect language from audio
    abstract member DetectLanguageAsync: samples:float32[] -> Async<string * float32>

/// Main streaming interface (new capability)
type IWhisperStream =
    inherit IDisposable

    /// Process a chunk of audio samples
    abstract member ProcessChunk: samples:float32[] -> Async<TranscriptionEvent>

    /// Get current context for continuation
    abstract member GetContext: unit -> byte[]

    /// Reset the stream state
    abstract member Reset: unit -> unit

    /// Observable stream of transcription events
    abstract member Events: IObservable<TranscriptionEvent>

    /// Switch between streaming and batch mode
    abstract member SetStreamingMode: enabled:bool -> unit

    /// Get current configuration
    abstract member GetConfig: unit -> WhisperConfig

/// Unified result type (Whisper.NET compatible)
and TranscriptionResult = {
    FullText: string
    Segments: Segment list
    Duration: TimeSpan
    ProcessingTime: TimeSpan
    Timestamp: DateTime
    Language: string option
    LanguageConfidence: float32 option
    Tokens: Token list option  // Extended from Whisper.NET
}
```

## Implementation Details

### Fluent Builder Pattern (Whisper.NET Compatible)

```fsharp
/// Builder pattern matching Whisper.NET's API
type WhisperBuilder(factory: IWhisperFactory) =
    let mutable config = {
        ModelPath = ""
        ModelType = Base
        Language = None
        ThreadCount = Environment.ProcessorCount
        UseGpu = false
        EnableTranslate = false
        MaxSegmentLength = 0
        Temperature = 0.0f
        TemperatureInc = 0.2f
        BeamSize = 5
        BestOf = 5
        MaxTokensPerSegment = 0
        AudioContext = 0
        NoContext = false
        SingleSegment = false
        PrintSpecialTokens = false
        PrintProgress = false
        PrintTimestamps = false
        TokenTimestamps = false
        ThresholdPt = 0.01f
        ThresholdPtSum = 0.01f
        MaxLen = 0
        SplitOnWord = false
        MaxTokens = 0
        SpeedUp = false
        DebugMode = false
        AudioCtx = 0
        InitialPrompt = None
        SuppressBlank = true
        SuppressNonSpeechTokens = true
        MaxInitialTs = 1.0f
        LengthPenalty = -1.0f
        StreamingMode = false
        ChunkSizeMs = 1000
        OverlapMs = 200
        MinConfidence = 0.5f
        MaxContext = 512
        StabilityThreshold = 0.7f
    }

    member _.WithLanguage(lang: string) =
        config <- { config with Language = Some lang }
        this

    member _.WithLanguageDetection() =
        config <- { config with Language = None }
        this

    member _.WithThreads(threads: int) =
        config <- { config with ThreadCount = threads }
        this

    member _.WithGpu() =
        config <- { config with UseGpu = true }
        this

    member _.WithTranslate() =
        config <- { config with EnableTranslate = true }
        this

    member _.WithMaxSegmentLength(length: int) =
        config <- { config with MaxSegmentLength = length }
        this

    member _.WithPrompt(prompt: string) =
        config <- { config with InitialPrompt = Some prompt }
        this

    member _.WithTokenTimestamps() =
        config <- { config with TokenTimestamps = true }
        this

    member _.WithBeamSearch(size: int) =
        config <- { config with BeamSize = size }
        this

    member _.WithTemperature(temp: float32) =
        config <- { config with Temperature = temp }
        this

    member _.WithStreaming(chunkMs: int, overlapMs: int) =
        config <- { config with
                        StreamingMode = true
                        ChunkSizeMs = chunkMs
                        OverlapMs = overlapMs }
        this

    member _.Build() : IWhisperProcessor =
        factory.CreateProcessor(config)

    member _.BuildStream() : IWhisperStream =
        factory.CreateStream({ config with StreamingMode = true })
```

### P/Invoke Bindings to whisper.cpp

```fsharp
module WhisperNative =
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_init_from_file(string path)
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_init_from_buffer(IntPtr buffer, int buffer_size)
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern void whisper_free(IntPtr ctx)
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full(
        IntPtr ctx,
        WhisperFullParams parameters,
        float32[] samples,
        int n_samples)
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_n_segments(IntPtr ctx)
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_full_get_segment_text(IntPtr ctx, int i_segment)
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t0(IntPtr ctx, int i_segment)
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t1(IntPtr ctx, int i_segment)
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_n_tokens(IntPtr ctx, int i_segment)
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_full_get_token_text(IntPtr ctx, int i_segment, int i_token)
    
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern float whisper_full_get_token_p(IntPtr ctx, int i_segment, int i_token)

    // Language detection
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_lang_auto_detect(
        IntPtr ctx,
        int offset_ms,
        int n_threads,
        float[] lang_probs)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_lang_str(int lang_id)

    // Model info
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_vocab(IntPtr ctx)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_audio_ctx(IntPtr ctx)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_audio_state(IntPtr ctx)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_type(IntPtr ctx)

    /// Streaming-specific parameters
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type WhisperFullParams =
        val mutable strategy: int
        val mutable n_threads: int
        val mutable n_max_text_ctx: int
        val mutable offset_ms: int
        val mutable duration_ms: int
        val mutable translate: bool
        val mutable no_context: bool
        val mutable single_segment: bool
        val mutable print_special: bool
        val mutable print_progress: bool
        val mutable print_realtime: bool
        val mutable print_timestamps: bool
        val mutable token_timestamps: bool
        val mutable thold_pt: float32
        val mutable thold_ptsum: float32
        val mutable max_len: int
        val mutable split_on_word: bool
        val mutable max_tokens: int
        val mutable audio_ctx: int  // Critical for streaming!
        val mutable prompt_tokens: IntPtr
        val mutable prompt_n_tokens: int
        val mutable language: IntPtr
        val mutable suppress_blank: bool
        val mutable suppress_non_speech_tokens: bool
        val mutable temperature: float32
        val mutable max_initial_ts: float32
        val mutable length_penalty: float32
```

### Batch Processing Implementation (Whisper.NET Compatible)

```fsharp
/// Implementation of IWhisperProcessor for PTT scenarios
type WhisperProcessor(ctx: IntPtr, config: WhisperConfig) =

    let processBuffer (samples: float32[]) =
        async {
            let startTime = DateTime.UtcNow

            // Setup parameters matching Whisper.NET behavior
            let mutable parameters = WhisperNative.WhisperFullParams()
            parameters.strategy <- 0 // GREEDY
            parameters.n_threads <- config.ThreadCount
            parameters.translate <- config.EnableTranslate
            parameters.no_context <- config.NoContext
            parameters.single_segment <- config.SingleSegment
            parameters.print_special <- config.PrintSpecialTokens
            parameters.print_progress <- config.PrintProgress
            parameters.print_timestamps <- config.PrintTimestamps
            parameters.token_timestamps <- config.TokenTimestamps
            parameters.thold_pt <- config.ThresholdPt
            parameters.thold_ptsum <- config.ThresholdPtSum
            parameters.max_len <- config.MaxLen
            parameters.split_on_word <- config.SplitOnWord
            parameters.max_tokens <- config.MaxTokens
            parameters.suppress_blank <- config.SuppressBlank
            parameters.suppress_non_speech_tokens <- config.SuppressNonSpeechTokens
            parameters.temperature <- config.Temperature
            parameters.max_initial_ts <- config.MaxInitialTs
            parameters.length_penalty <- config.LengthPenalty

            // Set language if specified
            match config.Language with
            | Some lang ->
                parameters.language <- Marshal.StringToHGlobalAnsi(lang)
            | None -> ()

            // Set initial prompt if provided
            match config.InitialPrompt with
            | Some prompt ->
                let promptBytes = System.Text.Encoding.UTF8.GetBytes(prompt)
                parameters.prompt_tokens <- Marshal.AllocHGlobal(promptBytes.Length)
                Marshal.Copy(promptBytes, 0, parameters.prompt_tokens, promptBytes.Length)
                parameters.prompt_n_tokens <- promptBytes.Length
            | None -> ()

            // Process audio
            let result = WhisperNative.whisper_full(ctx, parameters, samples, samples.Length)

            if result = 0 then
                // Extract segments and build result
                let segmentCount = WhisperNative.whisper_full_n_segments(ctx)
                let segments = [
                    for i in 0 .. segmentCount - 1 do
                        let textPtr = WhisperNative.whisper_full_get_segment_text(ctx, i)
                        let text = Marshal.PtrToStringAnsi(textPtr)
                        let t0 = WhisperNative.whisper_full_get_segment_t0(ctx, i)
                        let t1 = WhisperNative.whisper_full_get_segment_t1(ctx, i)

                        // Get tokens if requested
                        let tokens =
                            if config.TokenTimestamps then
                                let tokenCount = WhisperNative.whisper_full_n_tokens(ctx, i)
                                [
                                    for j in 0 .. tokenCount - 1 do
                                        let tokenPtr = WhisperNative.whisper_full_get_token_text(ctx, i, j)
                                        let tokenText = Marshal.PtrToStringAnsi(tokenPtr)
                                        let prob = WhisperNative.whisper_full_get_token_p(ctx, i, j)
                                        yield {
                                            Text = tokenText
                                            Timestamp = float32 t0 / 100.0f
                                            Probability = prob
                                            IsSpecial = tokenText.StartsWith("<|") && tokenText.EndsWith("|>")
                                        }
                                ]
                            else
                                []

                        yield {
                            Text = text
                            StartTime = float32 t0 / 100.0f
                            EndTime = float32 t1 / 100.0f
                            Tokens = tokens
                        }
                ]

                let fullText = segments |> List.map (fun s -> s.Text) |> String.concat " "
                let processingTime = DateTime.UtcNow - startTime

                return {
                    FullText = fullText
                    Segments = segments
                    Duration = TimeSpan.FromSeconds(float (samples.Length / 16000))
                    ProcessingTime = processingTime
                    Timestamp = startTime
                    Language = config.Language
                    LanguageConfidence = None
                    Tokens = if config.TokenTimestamps then Some (segments |> List.collect (fun s -> s.Tokens)) else None
                }
            else
                return failwith $"Whisper processing failed with code {result}"
        }

    interface IWhisperProcessor with
        member _.ProcessAsync(audioPath: string) =
            async {
                use stream = File.OpenRead(audioPath)
                let buffer = Array.zeroCreate<byte> (int stream.Length)
                let! _ = stream.AsyncRead(buffer, 0, buffer.Length)
                // Convert to float32 samples (assuming WAV format)
                let samples = convertToFloat32 buffer
                return! processBuffer samples
            }

        member _.ProcessAsync(audioStream: Stream) =
            // Return async enumerable for segment-by-segment processing
            AsyncSeq.unfoldAsync (fun state -> async {
                // Implementation for streaming segments
                return None
            }) ()

        member _.ProcessAsync(samples: float32[]) =
            processBuffer samples

        member _.ProcessAsync(samples: float32[], onSegment: Segment -> unit) =
            async {
                let! result = processBuffer samples
                result.Segments |> List.iter onSegment
                return result
            }

        member _.SetLanguage(language: string) =
            // Update config for next processing
            ()

        member _.DetectLanguageAsync(samples: float32[]) =
            async {
                let langProbs = Array.zeroCreate<float> 100
                let langId = WhisperNative.whisper_lang_auto_detect(ctx, 0, config.ThreadCount, langProbs)
                let langPtr = WhisperNative.whisper_lang_str(langId)
                let lang = Marshal.PtrToStringAnsi(langPtr)
                return (lang, langProbs.[langId])
            }

        member _.Dispose() =
            WhisperNative.whisper_free(ctx)
```

### Core Streaming Implementation

```fsharp
type WhisperStream(modelPath: string, config: WhisperConfig) =
    let ctx = WhisperNative.whisper_init_from_file(modelPath)
    let events = Event<TranscriptionEvent>()
    let mutable contextBuffer = Array.empty<byte>
    let mutable previousText = ""
    let mutable audioContext = Array.empty<float32>
    
    // Maintain sliding window of audio for context
    let audioRingBuffer = ResizeArray<float32>()
    let maxAudioContext = config.ChunkSizeMs * 16 // samples per ms
    
    /// Process chunk with overlap and context
    let processChunkInternal (samples: float32[]) = async {
        try
            // Add to ring buffer
            audioRingBuffer.AddRange(samples)
            
            // Keep only recent context
            if audioRingBuffer.Count > maxAudioContext then
                audioRingBuffer.RemoveRange(0, audioRingBuffer.Count - maxAudioContext)
            
            // Prepare parameters for streaming
            let mutable parameters = WhisperNative.WhisperFullParams()
            parameters.strategy <- 0 // WHISPER_SAMPLING_GREEDY
            parameters.n_threads <- 4
            parameters.audio_ctx <- min 1500 (audioRingBuffer.Count / 2) // Adaptive context
            parameters.single_segment <- false
            parameters.token_timestamps <- config.TokenTimestamps
            parameters.suppress_blank <- true
            parameters.suppress_non_speech_tokens <- true
            parameters.temperature <- 0.0f
            parameters.language <- Marshal.StringToHGlobalAnsi(config.Language)
            
            // Process with whisper
            let audioArray = audioRingBuffer.ToArray()
            let result = WhisperNative.whisper_full(ctx, parameters, audioArray, audioArray.Length)
            
            if result = 0 then
                // Extract segments and tokens
                let segmentCount = WhisperNative.whisper_full_n_segments(ctx)
                
                if segmentCount > 0 then
                    // Get the latest segment
                    let segIdx = segmentCount - 1
                    let textPtr = WhisperNative.whisper_full_get_segment_text(ctx, segIdx)
                    let text = Marshal.PtrToStringAnsi(textPtr)
                    
                    // Get tokens for confidence calculation
                    let tokenCount = WhisperNative.whisper_full_n_tokens(ctx, segIdx)
                    let tokens = [
                        for t in 0 .. tokenCount - 1 do
                            let tokenPtr = WhisperNative.whisper_full_get_token_text(ctx, segIdx, t)
                            let tokenText = Marshal.PtrToStringAnsi(tokenPtr)
                            let prob = WhisperNative.whisper_full_get_token_p(ctx, segIdx, t)
                            yield {
                                Text = tokenText
                                Timestamp = 0.0f // Calculate from position
                                Probability = prob
                                IsSpecial = tokenText.StartsWith("<|") && tokenText.EndsWith("|>")
                            }
                    ]
                    
                    // Calculate average confidence
                    let confidence = 
                        tokens 
                        |> List.filter (fun t -> not t.IsSpecial)
                        |> List.averageBy (fun t -> t.Probability)
                    
                    // Determine if this is stable enough to emit
                    if confidence >= config.MinConfidence then
                        // Check for text extension vs correction
                        if text.StartsWith(previousText) then
                            // Text is extending - emit partial
                            events.Trigger(PartialTranscription(text, tokens, confidence))
                        else
                            // Text changed - might be a correction
                            // Only emit if significantly different and confident
                            let similarity = calculateSimilarity previousText text
                            if similarity < 0.7f && confidence > config.MinConfidence * 1.2f then
                                events.Trigger(PartialTranscription(text, tokens, confidence))
                        
                        previousText <- text
                    
                return PartialTranscription(text, tokens, confidence)
            else
                return ProcessingError($"Whisper processing failed with code {result}")
                
        with ex ->
            return ProcessingError(ex.Message)
    }
    
    /// Calculate text similarity for correction detection
    let calculateSimilarity (text1: string) (text2: string) =
        let len1 = text1.Length
        let len2 = text2.Length
        let maxLen = max len1 len2
        if maxLen = 0 then 1.0f
        else
            let minLen = min len1 len2
            let commonLen = 
                [0 .. minLen - 1]
                |> List.filter (fun i -> text1.[i] = text2.[i])
                |> List.length
            float32 commonLen / float32 maxLen
    
    interface IWhisperStream with
        member _.ProcessChunk(samples) = processChunkInternal samples
        
        member _.GetContext() = contextBuffer
        
        member _.Reset() =
            audioRingBuffer.Clear()
            previousText <- ""
            contextBuffer <- Array.empty
            
        member _.Events = events.Publish :> IObservable<_>
        
        member _.Dispose() =
            WhisperNative.whisper_free(ctx)
```

### Functional Stream Processing Patterns

```fsharp
module StreamProcessing =
    open System
    open System.Reactive.Linq
    open FSharp.Control.Reactive
    
    /// Create a streaming transcription pipeline
    let createTranscriptionPipeline (audioSource: IObservable<float32[]>) (config: StreamConfig) =
        
        // Initialize whisper stream
        let whisperStream = new WhisperStream("models/ggml-base.en.bin", config)
        
        // Create processing pipeline
        audioSource
        // Buffer audio into chunks
        |> Observable.bufferTimeSpan (TimeSpan.FromMilliseconds(float config.ChunkSizeMs))
        |> Observable.map (Array.concat)
        
        // Process through whisper
        |> Observable.selectAsync (fun chunk -> whisperStream.ProcessChunk(chunk))
        
        // Filter based on confidence
        |> Observable.choose (function
            | PartialTranscription(text, _, conf) when conf >= config.MinConfidence -> 
                Some text
            | _ -> None)
        
        // Debounce rapid changes
        |> Observable.throttle (TimeSpan.FromMilliseconds 200.0)
    
    /// Smart text stabilization
    let stabilizeText (events: IObservable<TranscriptionEvent>) =
        events
        |> Observable.scan (fun (lastText, lastConf) event ->
            match event with
            | PartialTranscription(text, _, conf) ->
                // Only update if more confident or extending
                if conf > lastConf || text.StartsWith(lastText) then
                    (text, conf)
                else
                    (lastText, lastConf)
            | FinalTranscription(text, _, _) ->
                (text, 1.0f) // Final is always accepted
            | _ -> (lastText, lastConf)
        ) ("", 0.0f)
        |> Observable.map fst
        |> Observable.distinctUntilChanged
    
    /// Incremental typing with correction support
    type TypedTextState = {
        Committed: string  // Text already typed
        Pending: string     // Text waiting to be typed
        LastUpdate: DateTime
    }
    
    let createTypingPipeline (transcriptions: IObservable<string>) =
        transcriptions
        |> Observable.scan (fun state text ->
            // Check if we need to correct
            if text.StartsWith(state.Committed) then
                // Extension - just add pending
                { state with 
                    Pending = text.Substring(state.Committed.Length) 
                    LastUpdate = DateTime.UtcNow }
            else
                // Correction needed - mark for backspace
                let commonPrefix = 
                    Seq.zip state.Committed text
                    |> Seq.takeWhile (fun (a, b) -> a = b)
                    |> Seq.length
                
                { Committed = text.Substring(0, commonPrefix)
                  Pending = text.Substring(commonPrefix)
                  LastUpdate = DateTime.UtcNow }
        ) { Committed = ""; Pending = ""; LastUpdate = DateTime.UtcNow }
        
        // Emit typing commands
        |> Observable.map (fun state ->
            if state.Pending.Length > 0 then
                Some (TypeText state.Pending)
            else
                None)
        |> Observable.choose id
```

### Integration with Existing Codebase

Replacing current Whisper.NET usage:

#### Before (Whisper.NET):
```fsharp
// Old batch approach
let processRecording (audioBuffer: ResizeArray<float32>) =
    async {
        let samples = audioBuffer.ToArray()
        let! result = whisperProcessor.ProcessAsync(samples)
        typeText result.Text
    }
```

#### After (Whisper-FS):
```fsharp
// New streaming approach
let processRecording (audioCapture: IAudioCapture) =
    // Create audio observable
    let audioStream = 
        audioCapture.AudioFrameAvailable
        |> Observable.map (fun frame -> frame.Samples)
    
    // Create transcription pipeline
    let transcriptions = 
        StreamProcessing.createTranscriptionPipeline audioStream streamConfig
        |> StreamProcessing.stabilizeText
    
    // Subscribe to type text incrementally
    transcriptions
    |> Observable.subscribe (fun text ->
        // Show in UI immediately
        updateUITranscription text
        
        // Type only when stable
        if isStableEnough text then
            typeText text)
```

### Advanced Features

#### 1. Voice Activity Detection Integration

```fsharp
type VADIntegratedStream(whisperStream: IWhisperStream, vadThreshold: float32) =
    
    let processWithVAD (samples: float32[]) =
        async {
            let energy = calculateEnergy samples
            
            if energy > vadThreshold then
                // Speech detected - process normally
                return! whisperStream.ProcessChunk(samples)
            else
                // Silence - might be end of sentence
                return PartialTranscription("", [], 0.0f)
        }
    
    member _.Process = processWithVAD
```

#### 2. Multi-language Support with Detection

```fsharp
let detectLanguageAndTranscribe (samples: float32[]) =
    async {
        // First pass: detect language
        let! detection = whisperStream.ProcessChunk(samples)
        
        match detection with
        | PartialTranscription(_, tokens, _) ->
            // Analyze tokens for language hints
            let language = detectLanguageFromTokens tokens
            
            // Second pass: transcribe with detected language
            whisperStream.SetLanguage(language)
            return! whisperStream.ProcessChunk(samples)
        | other -> return other
    }
```

#### 3. Punctuation and Formatting

```fsharp
module TextFormatting =
    
    /// Add punctuation based on prosody and pauses
    let addPunctuation (segments: Segment list) =
        segments
        |> List.map (fun segment ->
            let pauseAfter = 
                match segments |> List.tryFind (fun s -> s.StartTime > segment.EndTime) with
                | Some next -> next.StartTime - segment.EndTime
                | None -> 0.0f
            
            let punctuation =
                if pauseAfter > 1.0f then ". "
                elif pauseAfter > 0.5f then ", "
                else " "
            
            { segment with Text = segment.Text + punctuation })
    
    /// Apply capitalization rules
    let applyCapitalization (text: string) =
        let sentences = text.Split([|'. '; '? '; '! '|], StringSplitOptions.None)
        sentences
        |> Array.map (fun s -> 
            if s.Length > 0 then
                Char.ToUpper(s.[0]).ToString() + s.Substring(1)
            else s)
        |> String.concat ". "
```

## Performance Considerations

### Memory Management

```fsharp
module MemoryOptimization =
    open System.Buffers
    
    /// Use array pools for audio buffers
    let rentAudioBuffer (size: int) =
        ArrayPool<float32>.Shared.Rent(size)
    
    let returnAudioBuffer (buffer: float32[]) =
        ArrayPool<float32>.Shared.Return(buffer, true)
    
    /// Zero-copy audio processing
    [<Struct>]
    type AudioSpan = {
        Data: ReadOnlyMemory<float32>
        SampleRate: int
        Channels: int
    }
    
    let processAudioSpan (span: AudioSpan) =
        // Process without copying
        use pinned = span.Data.Pin()
        let ptr = pinned.Pointer
        // Pass directly to native code
        WhisperNative.whisper_full_with_state(ctx, parameters, ptr, span.Data.Length)
```

### Parallel Processing

```fsharp
/// Process multiple streams in parallel
let parallelTranscription (audioStreams: IObservable<float32[]> list) =
    audioStreams
    |> List.map (fun stream ->
        async {
            let whisper = new WhisperStream(modelPath, config)
            return! stream |> processStream whisper
        })
    |> Async.Parallel
```

## Error Handling and Recovery

```fsharp
type StreamError =
    | ModelLoadError of string
    | AudioProcessingError of string
    | ContextOverflow
    | NetworkTimeout
    
let resilientStream (config: StreamConfig) =
    let rec processWithRetry (samples: float32[]) (retries: int) =
        async {
            try
                return! whisperStream.ProcessChunk(samples)
            with
            | :? OutOfMemoryException when retries > 0 ->
                // Clear context and retry
                whisperStream.Reset()
                return! processWithRetry samples (retries - 1)
            | ex ->
                return ProcessingError(ex.Message)
        }
    
    processWithRetry
```

## Testing Strategy

```fsharp
module Testing =
    open Xunit
    open FsUnit
    
    [<Fact>]
    let ``Stream should handle silence correctly`` () =
        // Arrange
        let silence = Array.zeroCreate<float32> 16000
        let stream = new WhisperStream(testModelPath, defaultConfig)
        
        // Act
        let result = stream.ProcessChunk(silence) |> Async.RunSynchronously
        
        // Assert
        match result with
        | PartialTranscription(text, _, _) ->
            text |> should equal ""
        | _ -> failwith "Expected empty transcription for silence"
    
    [<Fact>]
    let ``Stream should maintain context between chunks`` () =
        // Test that "Hello" + "world" produces "Hello world"
        let chunk1 = loadAudioFile "hello.wav"
        let chunk2 = loadAudioFile "world.wav"
        
        let stream = new WhisperStream(testModelPath, defaultConfig)
        
        let result1 = stream.ProcessChunk(chunk1) |> Async.RunSynchronously
        let result2 = stream.ProcessChunk(chunk2) |> Async.RunSynchronously
        
        match result2 with
        | PartialTranscription(text, _, _) ->
            text |> should contain "Hello world"
        | _ -> failwith "Expected combined transcription"
```

## Complete Feature Comparison with Whisper.NET

| Feature | Whisper.NET | Whisper-FS | Notes |
|---------|-------------|------------|-------|
| **Model Management** | | | |
| Model downloading | ✅ WhisperGgmlDownloader | ✅ IModelDownloader | Full API compatibility |
| Model types (Tiny-Large) | ✅ GgmlType enum | ✅ ModelType DU | All sizes supported |
| Model from path | ✅ WhisperFactory.FromPath | ✅ IWhisperFactory.FromPath | Direct loading |
| Model from buffer | ❌ | ✅ IWhisperFactory.FromBuffer | Memory loading |
| **Processing Modes** | | | |
| Batch processing | ✅ ProcessAsync | ✅ IWhisperProcessor | Full compatibility |
| Streaming segments | ✅ IAsyncEnumerable | ✅ IAsyncEnumerable + Observable | Enhanced |
| Real-time streaming | ❌ | ✅ IWhisperStream | New capability |
| **Configuration** | | | |
| Language setting | ✅ WithLanguage | ✅ WithLanguage | Same API |
| Language detection | ❌ | ✅ DetectLanguageAsync | New feature |
| Translation mode | ✅ WithTranslate | ✅ WithTranslate | Full support |
| Thread count | ✅ WithThreads | ✅ WithThreads | Same API |
| GPU acceleration | ✅ Via runtime package | ✅ WithGpu | Integrated |
| Max segment length | ✅ | ✅ WithMaxSegmentLength | Same API |
| **Advanced Features** | | | |
| Token timestamps | ❌ | ✅ WithTokenTimestamps | New access |
| Token confidence | ❌ | ✅ Token.Probability | New data |
| Custom prompts | ❌ | ✅ WithPrompt | Style control |
| Beam search | ❌ | ✅ WithBeamSearch | Better accuracy |
| Temperature sampling | ❌ | ✅ WithTemperature | Control randomness |
| VAD integration | ❌ | ✅ Built-in VAD | Silence handling |
| **Results** | | | |
| Segment text | ✅ | ✅ | Same |
| Segment timing | ✅ TimeSpan | ✅ float32 + TimeSpan | Both formats |
| Full transcript | ✅ | ✅ | Same |
| Processing time | ✅ | ✅ | Same |
| Confidence scores | ❌ | ✅ Per segment/token | New metrics |
| **Resource Management** | | | |
| IDisposable | ✅ | ✅ | Same pattern |
| Memory efficiency | ❌ Buffers all | ✅ Streaming chunks | Improved |
| **API Patterns** | | | |
| Builder pattern | ✅ | ✅ Enhanced | Superset |
| Async/await | ✅ | ✅ | F# async |
| Observables | ❌ | ✅ | Reactive streams |

## Migration Path from Current Implementation

### Phase 1: Drop-in Replacement (Week 1-2)
- Implement core Whisper.NET-compatible API surface
- Ensure 100% backward compatibility for PTT mode
- Run side-by-side testing with existing implementation
- Migration requires only namespace change

### Phase 2: Enhanced PTT Features (Week 3-4)
- Enable token-level timestamps and confidence scores
- Add language detection for auto-language support
- Implement custom prompts for domain-specific vocabulary
- Add VAD for better silence handling in PTT mode

### Phase 3: Streaming Introduction (Week 5-6)
- Roll out experimental streaming mode
- A/B test streaming vs batch for user preference
- Tune stability thresholds based on feedback
- Monitor performance and accuracy metrics

### Phase 4: Full Migration (Week 7-8)
- Replace Whisper.NET completely
- Remove old dependencies from project
- Update documentation and examples
- Optimize based on real-world usage patterns

## Conclusion

Whisper-FS represents a comprehensive unification of transcription capabilities, providing complete feature parity with Whisper.NET while adding advanced streaming support. This "one-stop shopping" library eliminates the need for multiple dependencies and provides a consistent F# API for all transcription scenarios.

### Key Achievements:

1. **100% Whisper.NET Compatibility**: Drop-in replacement with identical API for existing PTT workflows
2. **Extended Feature Set**: Access to token-level data, confidence scores, language detection, and advanced whisper.cpp features
3. **True Streaming Support**: Real-time transcription with configurable stability/latency tradeoffs
4. **Unified Architecture**: Single library handles both batch and streaming modes seamlessly
5. **F# Idiomatic Design**: Leverages functional programming patterns for cleaner, more maintainable code
6. **Performance Optimized**: Zero-copy operations, memory pooling, and efficient streaming pipelines

### Feature Completeness:

The library provides:
- **All existing Whisper.NET features** for backward compatibility
- **All whisper.cpp native features** previously inaccessible
- **New streaming capabilities** for real-time scenarios
- **Enhanced observability** through confidence scores and token-level data
- **Better resource management** with streaming-friendly memory patterns

By consolidating all transcription needs into a single, well-designed library, Whisper-FS simplifies development, reduces dependencies, and provides a clear upgrade path from batch-only to streaming-capable transcription.

### Next Steps

1. Prototype the core P/Invoke bindings
2. Implement basic streaming with a simple audio source
3. Test with real-world audio to tune parameters
4. Integrate with the existing Mel application
5. Gather user feedback and iterate

This design provides a solid foundation for building a production-ready streaming transcription system that learns from our past experiments and leverages the best of what whisper.cpp has to offer.