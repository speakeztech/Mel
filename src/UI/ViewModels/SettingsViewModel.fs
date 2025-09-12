namespace Mel.UI.ViewModels

open System
open System.Collections.ObjectModel
open Mel.UI.Models
open Mel.UI.Models.AppModel
open Elmish

type SettingsViewModel(store: IStore<Model, Msg>) =
    inherit BaseViewModel()
    
    let availableModels = ObservableCollection<WhisperModel>([
        { Name = "Tiny (39MB)"; Type = "tiny"; Path = "./models/ggml-tiny.bin"; Size = 39L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
        { Name = "Tiny English (39MB)"; Type = "tiny.en"; Path = "./models/ggml-tiny.en.bin"; Size = 39L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
        { Name = "Base (142MB)"; Type = "base"; Path = "./models/ggml-base.bin"; Size = 142L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
        { Name = "Base English (142MB)"; Type = "base.en"; Path = "./models/ggml-base.en.bin"; Size = 142L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
        { Name = "Small (466MB)"; Type = "small"; Path = "./models/ggml-small.bin"; Size = 466L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
        { Name = "Small English (466MB)"; Type = "small.en"; Path = "./models/ggml-small.en.bin"; Size = 466L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
        { Name = "Medium (1.5GB)"; Type = "medium"; Path = "./models/ggml-medium.bin"; Size = 1500L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
        { Name = "Medium English (1.5GB)"; Type = "medium.en"; Path = "./models/ggml-medium.en.bin"; Size = 1500L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
    ])
    
    let audioDevices = ObservableCollection<AudioDevice>([
        { Id = "default"; Name = "Default Audio Device"; SampleRate = 16000; Channels = 1 }
    ])
    
    member _.AvailableModels = availableModels
    
    member _.SelectedModel 
        with get() = store.Model.Settings.SelectedModel
        and set(value) = store.Dispatch (SelectModel value)
    
    member _.UseGpu 
        with get() = store.Model.Settings.UseGpu
        and set(value) = store.Dispatch (ToggleGpu value)
    
    member _.ThreadCount 
        with get() = store.Model.Settings.ThreadCount
        and set(value) = store.Dispatch (SetThreadCount value)
    
    member _.GpuInfo = "NVIDIA RTX 3050 (8GB VRAM) - CUDA 12 Ready"
    
    member _.AudioDevices = audioDevices
    
    member _.SelectedAudioDevice 
        with get() = store.Model.Settings.AudioDevice
        and set(value) = store.Dispatch (SelectAudioDevice value)
    
    member _.HotkeyDisplay = 
        let hotkey = store.Model.Settings.Hotkey
        let mods = String.Join("+", hotkey.Modifiers)
        if String.IsNullOrEmpty(mods) then
            hotkey.Key
        else
            $"{mods}+{hotkey.Key}"
    
    member _.MinimizeToTray 
        with get() = store.Model.Settings.MinimizeToTray
        and set(value) = 
            let settings = { store.Model.Settings with MinimizeToTray = value }
            store.Dispatch (UpdateSettings settings)
    
    member _.StartWithWindows 
        with get() = store.Model.Settings.StartWithWindows
        and set(value) = 
            let settings = { store.Model.Settings with StartWithWindows = value }
            store.Dispatch (UpdateSettings settings)
    
    member _.ShowTranscriptOverlay 
        with get() = store.Model.Settings.ShowTranscriptOverlay
        and set(value) = 
            let settings = { store.Model.Settings with ShowTranscriptOverlay = value }
            store.Dispatch (UpdateSettings settings)
    
    member _.DownloadSelectedModel() =
        store.Dispatch (DownloadModel store.Model.Settings.SelectedModel)
    
    member _.SaveSettings() =
        printfn "Settings saved"
    
    member _.RestoreDefaults() =
        let defaultModel, _ = init()
        store.Dispatch (UpdateSettings defaultModel.Settings)