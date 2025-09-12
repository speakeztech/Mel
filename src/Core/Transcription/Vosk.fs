module Mel.Core.Transcription.Vosk

open System
open System.IO
open System.Text
open Vosk
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open Mel.Core.Transcription.VoskTypes

type VoskTranscriber(config: VoskConfig) =
    let mutable model: Model option = None
    let mutable recognizer: VoskRecognizer option = None
    let mutable isStreaming = false
    
    // Initialize model
    let initModel() =
        if Directory.Exists(config.ModelPath) then
            printfn $"Loading Vosk model from {config.ModelPath}"
            try
                Vosk.Vosk.SetLogLevel(-1) // Suppress Vosk logging
                let m = new Model(config.ModelPath)
                model <- Some m
                printfn "Vosk model loaded successfully"
            with ex ->
                printfn $"Failed to load Vosk model: {ex.Message}"
                raise ex
        else
            raise (DirectoryNotFoundException($"Model directory not found: {config.ModelPath}"))
    
    do
        initModel()
    
    interface IVoskTranscriber with
        member _.StartStream() =
            match model with
            | Some m ->
                let rec' = new VoskRecognizer(m, config.SampleRate)
                rec'.SetMaxAlternatives(config.MaxAlternatives)
                rec'.SetWords(config.Words)
                rec'.SetPartialWords(config.PartialWords)
                recognizer <- Some rec'
                isStreaming <- true
            | None ->
                raise (InvalidOperationException("Model not loaded"))
        
        member _.ProcessAudio(samples: float32[]) =
            match recognizer with
            | Some rec' when isStreaming ->
                // Convert float32 samples to bytes (16-bit PCM)
                let bytes = Array.zeroCreate<byte> (samples.Length * 2)
                for i in 0 .. samples.Length - 1 do
                    let sample = int16 (Math.Max(-32768.0f, Math.Min(32767.0f, samples.[i] * 32767.0f)))
                    let b = BitConverter.GetBytes(sample)
                    bytes.[i * 2] <- b.[0]
                    bytes.[i * 2 + 1] <- b.[1]
                
                // Process audio chunk
                if rec'.AcceptWaveform(bytes, bytes.Length) then
                    // Final result for this chunk
                    let resultJson = rec'.Result()
                    let jobj = JObject.Parse(resultJson)
                    let text = jobj.["text"].ToString()
                    if not (String.IsNullOrWhiteSpace(text)) then
                        Some { 
                            Partial = text
                            Timestamp = DateTime.UtcNow 
                        }
                    else
                        None
                else
                    // Partial result
                    let partialJson = rec'.PartialResult()
                    let jobj = JObject.Parse(partialJson)
                    let partial = jobj.["partial"].ToString()
                    if not (String.IsNullOrWhiteSpace(partial)) then
                        Some { 
                            Partial = partial
                            Timestamp = DateTime.UtcNow 
                        }
                    else
                        None
            | _ ->
                None
        
        member _.FinishStream() =
            match recognizer with
            | Some rec' when isStreaming ->
                isStreaming <- false
                let finalJson = rec'.FinalResult()
                let jobj = JObject.Parse(finalJson)
                let text = jobj.["text"].ToString()
                
                // Parse words if available
                let words =
                    if config.Words && jobj.ContainsKey("result") then
                        let wordsArray = jobj.["result"] :?> JArray
                        wordsArray
                        |> Seq.map (fun w ->
                            {
                                Word = w.["word"].ToString()
                                Start = float32 (w.["start"].Value<float>())
                                End = float32 (w.["end"].Value<float>())
                                Confidence = float32 (w.["conf"].Value<float>())
                            })
                        |> List.ofSeq
                        |> Some
                    else
                        None
                
                Some {
                    Text = text
                    Confidence = None
                    Words = words
                    Timestamp = DateTime.UtcNow
                }
            | _ ->
                None
        
        member _.Reset() =
            match recognizer with
            | Some rec' ->
                rec'.Dispose()
                recognizer <- None
            | None -> ()
            isStreaming <- false
        
        member _.IsModelLoaded = model.IsSome
        
        member this.Dispose() =
            (this :> IVoskTranscriber).Reset()
            match model with
            | Some m ->
                m.Dispose()
                model <- None
            | None -> ()

// Helper function to download Vosk models
let downloadModel (modelName: string) (targetPath: string) =
    async {
        let modelUrl = 
            match modelName with
            | "small-en" -> "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip"
            | "en" -> "https://alphacephei.com/vosk/models/vosk-model-en-us-0.22.zip"
            | _ -> failwith $"Unknown model: {modelName}"
        
        printfn $"Downloading Vosk model {modelName} from {modelUrl}"
        
        // Download and extract logic here
        // For now, users need to manually download and extract models
        
        printfn $"Please download and extract the model to: {targetPath}"
    }

let getModelInfo (modelName: string) =
    match modelName with
    | "small-en" -> 
        {
            Name = "vosk-model-small-en-us-0.15"
            Language = "en-US"
            Size = 40L * 1024L * 1024L  // ~40MB
            Url = Some "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip"
        }
    | "en" ->
        {
            Name = "vosk-model-en-us-0.22"
            Language = "en-US"
            Size = 1800L * 1024L * 1024L  // ~1.8GB
            Url = Some "https://alphacephei.com/vosk/models/vosk-model-en-us-0.22.zip"
        }
    | _ ->
        {
            Name = modelName
            Language = "unknown"
            Size = 0L
            Url = None
        }