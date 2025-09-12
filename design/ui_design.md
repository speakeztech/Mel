# Mel UI with Avalonia.ReactiveElmish
*System Tray Application with Settings Window for Voice Transcription*

## Overview

Adding a desktop UI to Mel using Avalonia.ReactiveElmish provides:
- System tray icon with status indicators
- Settings window for configuration
- Real-time transcription display
- Model download manager
- Cross-platform support (Windows/Linux/macOS)

## Architecture

```
┌─────────────────────────────────────────────┐
│          System Tray Application            │
├─────────────────────────────────────────────┤
│  TrayIcon (Always Running)                  │
│  ├─ Status: Idle/Recording/Transcribing     │
│  ├─ Right-Click Menu                        │
│  │  ├─ Toggle Recording (F9)                │
│  │  ├─ Settings...                          │
│  │  ├─ Show Transcript                      │
│  │  └─ Exit                                 │
│  └─ Double-Click → Settings Window          │
├─────────────────────────────────────────────┤
│  Settings Window (ReactiveElmish)           │
│  ├─ Model Selection (Tab)                   │
│  ├─ Audio Device Config (Tab)               │
│  ├─ Hotkey Configuration (Tab)              │
│  └─ About/Diagnostics (Tab)                 │
├─────────────────────────────────────────────┤
│  Transcript Overlay (Optional)              │
│  └─ Floating window with live text          │
└─────────────────────────────────────────────┘
```

## Project Structure

```
Mel.UI/
├── Mel.UI.fsproj
├── Models/
│   ├── AppModel.fs
│   ├── SettingsModel.fs
│   └── TranscriptionModel.fs
├── ViewModels/
│   ├── MainViewModel.fs
│   ├── SettingsViewModel.fs
│   ├── ModelManagerViewModel.fs
│   └── TranscriptViewModel.fs
├── Views/
│   ├── MainWindow.axaml
│   ├── SettingsWindow.axaml
│   ├── ModelManagerView.axaml
│   └── TranscriptView.axaml
├── Services/
│   └── TrayIconService.fs
├── CompositionRoot.fs
├── App.axaml.fs
└── Program.fs
```

## Implementation

### 1. Project Configuration

```xml
<!-- Mel.UI.fsproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationIcon>Assets/mel-icon.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <!-- Avalonia and ReactiveElmish -->
    <PackageReference Include="Avalonia.Desktop" Version="11.1.0" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.1.0" />
    <PackageReference Include="ReactiveElmish.Avalonia" Version="1.4.0" />
    
    <!-- System Tray Support -->
    <PackageReference Include="Avalonia.Controls.ItemsRepeater" Version="11.1.0" />
    <PackageReference Include="Avalonia.Tray" Version="0.1.0" />
    
    <!-- Additional UI Components -->
    <PackageReference Include="Avalonia.Controls.DataGrid" Version="11.1.0" />
    <PackageReference Include="LiveChartsCore.SkiaSharpView.Avalonia" Version="2.0.0" />
    
    <!-- Reference to Mel Core -->
    <ProjectReference Include="..\Mel\Mel.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaResource Include="Assets\**" />
  </ItemGroup>
</Project>
```

### 2. Elmish Models

```fsharp
// Models/AppModel.fs
module Mel.UI.Models.App

open System
open Elmish
open ReactiveElmish
open Mel.Transcription.Whisper

type Model = {
    Status: AppStatus
    Settings: Settings
    TranscriptionHistory: TranscriptionEntry list
    CurrentTranscription: string option
    SelectedView: AppView
    ModelDownloads: ModelDownload list
}

and AppStatus =
    | Idle
    | Recording
    | Transcribing
    | Error of string

and AppView =
    | SettingsView
    | TranscriptView
    | ModelManagerView
    | MinimizedToTray

and Settings = {
    SelectedModel: WhisperModel
    AudioDevice: AudioDevice
    Hotkey: HotkeyConfig
    UseGpu: bool
    ThreadCount: int
    Language: string
    ShowTranscriptOverlay: bool
    MinimizeToTray: bool
    StartWithWindows: bool
}

and WhisperModel = {
    Name: string
    Type: string  // tiny, base, small, medium, large
    Path: string
    Size: int64
    IsDownloaded: bool
    IsQuantized: bool  // q4_0, q5_1, etc
}

and AudioDevice = {
    Id: string
    Name: string
    SampleRate: int
    Channels: int
}

and HotkeyConfig = {
    Key: string
    Modifiers: string list  // ["Ctrl", "Alt", "Shift"]
}

and TranscriptionEntry = {
    Id: Guid
    Timestamp: DateTime
    Duration: TimeSpan
    Text: string
    AudioLength: float
}

and ModelDownload = {
    Model: WhisperModel
    Progress: float
    Status: DownloadStatus
}

and DownloadStatus =
    | Queued
    | Downloading
    | Completed
    | Failed of string

type Msg =
    // Status
    | SetStatus of AppStatus
    | StartRecording
    | StopRecording
    | TranscriptionReceived of string * TimeSpan
    | ErrorOccurred of string
    
    // Settings
    | UpdateSettings of Settings
    | SelectModel of WhisperModel
    | SelectAudioDevice of AudioDevice
    | UpdateHotkey of HotkeyConfig
    | ToggleGpu of bool
    | SetThreadCount of int
    
    // Model Management
    | DownloadModel of WhisperModel
    | ModelDownloadProgress of WhisperModel * float
    | ModelDownloadComplete of WhisperModel
    | ModelDownloadFailed of WhisperModel * string
    | DeleteModel of WhisperModel
    
    // Navigation
    | ShowView of AppView
    | MinimizeToTray
    | RestoreFromTray
    
    // Transcript
    | ClearHistory
    | ExportTranscript
    | CopyToClipboard of string
```

### 3. Elmish Update Logic

```fsharp
// Models/AppUpdate.fs
module Mel.UI.Models.AppUpdate

open Mel.UI.Models.App
open Elmish

let init () : Model * Cmd<Msg> =
    let defaultSettings = {
        SelectedModel = { 
            Name = "Whisper Base English"
            Type = "base.en"
            Path = "./models/ggml-base.en.bin"
            Size = 142L * 1024L * 1024L
            IsDownloaded = false
            IsQuantized = false
        }
        AudioDevice = { 
            Id = "default"
            Name = "Default Audio Device"
            SampleRate = 16000
            Channels = 1
        }
        Hotkey = { Key = "F9"; Modifiers = [] }
        UseGpu = true
        ThreadCount = 8
        Language = "en"
        ShowTranscriptOverlay = false
        MinimizeToTray = true
        StartWithWindows = false
    }
    
    let model = {
        Status = Idle
        Settings = defaultSettings
        TranscriptionHistory = []
        CurrentTranscription = None
        SelectedView = MinimizedToTray
        ModelDownloads = []
    }
    
    model, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SetStatus status ->
        { model with Status = status }, Cmd.none
        
    | StartRecording ->
        { model with 
            Status = Recording
            CurrentTranscription = None 
        }, Cmd.ofSub (fun dispatch ->
            // Start audio capture
            dispatch (SetStatus Recording)
        )
        
    | StopRecording ->
        { model with Status = Transcribing }, Cmd.none
        
    | TranscriptionReceived (text, duration) ->
        let entry = {
            Id = Guid.NewGuid()
            Timestamp = DateTime.UtcNow
            Duration = duration
            Text = text
            AudioLength = duration.TotalSeconds
        }
        { model with 
            Status = Idle
            CurrentTranscription = Some text
            TranscriptionHistory = entry :: model.TranscriptionHistory
        }, Cmd.none
        
    | SelectModel whisperModel ->
        { model with 
            Settings = { model.Settings with SelectedModel = whisperModel }
        }, Cmd.none
        
    | DownloadModel whisperModel ->
        let download = {
            Model = whisperModel
            Progress = 0.0
            Status = Downloading
        }
        { model with 
            ModelDownloads = download :: model.ModelDownloads
        }, Cmd.ofSub (fun dispatch ->
            // Start async download
            async {
                // Download logic here
                dispatch (ModelDownloadComplete whisperModel)
            } |> Async.Start
        )
        
    | ModelDownloadProgress (whisperModel, progress) ->
        let downloads = 
            model.ModelDownloads 
            |> List.map (fun d -> 
                if d.Model = whisperModel then 
                    { d with Progress = progress }
                else d
            )
        { model with ModelDownloads = downloads }, Cmd.none
        
    | ShowView view ->
        { model with SelectedView = view }, Cmd.none
        
    | MinimizeToTray ->
        { model with SelectedView = MinimizedToTray }, Cmd.none
        
    | _ ->
        model, Cmd.none
```

### 4. Main View Model

```fsharp
// ViewModels/MainViewModel.fs
namespace Mel.UI.ViewModels

open System
open ReactiveElmish
open ReactiveElmish.Avalonia
open Elmish
open Mel.UI.Models.App
open Mel.UI.Models.AppUpdate

type MainViewModel(root: CompositionRoot) =
    inherit ReactiveElmishViewModel()
    
    let store =
        Program.mkProgram init update
        |> Program.withErrorHandler (fun (_, ex) -> 
            printfn $"Error: {ex.Message}"
        )
        |> Program.mkStore
    
    // Bindable Properties
    member this.IsRecording = 
        this.Bind(store, fun m -> m.Status = Recording)
    
    member this.IsTranscribing = 
        this.Bind(store, fun m -> m.Status = Transcribing)
    
    member this.StatusText = 
        this.Bind(store, fun m ->
            match m.Status with
            | Idle -> "Ready"
            | Recording -> "Recording..."
            | Transcribing -> "Transcribing..."
            | Error msg -> $"Error: {msg}"
        )
    
    member this.StatusIcon = 
        this.Bind(store, fun m ->
            match m.Status with
            | Idle -> "Assets/Icons/idle.png"
            | Recording -> "Assets/Icons/recording.png"
            | Transcribing -> "Assets/Icons/processing.png"
            | Error _ -> "Assets/Icons/error.png"
        )
    
    member this.CurrentTranscription = 
        this.Bind(store, _.CurrentTranscription >> Option.defaultValue "")
    
    member this.TranscriptionHistory = 
        this.BindList(store, _.TranscriptionHistory)
    
    // Commands
    member this.ToggleRecording() =
        let currentStatus = store.Model.Status
        match currentStatus with
        | Recording -> store.Dispatch StopRecording
        | _ -> store.Dispatch StartRecording
    
    member this.ShowSettings() =
        store.Dispatch (ShowView SettingsView)
        root.GetView<SettingsViewModel>()
    
    member this.ShowTranscript() =
        store.Dispatch (ShowView TranscriptView)
        root.GetView<TranscriptViewModel>()
    
    member this.Exit() =
        Application.Current.Shutdown()
    
    // Design-time support
    static member DesignVM = MainViewModel(Design.stub)
```

### 5. Settings View Model

```fsharp
// ViewModels/SettingsViewModel.fs
namespace Mel.UI.ViewModels

open System
open System.Collections.ObjectModel
open ReactiveElmish
open ReactiveElmish.Avalonia
open Mel.UI.Models.App
open DynamicData

type SettingsViewModel(store: IStore<Model, Msg>) =
    inherit ReactiveElmishViewModel()
    
    // Model Selection
    member this.AvailableModels = 
        ObservableCollection([
            { Name = "Tiny (39MB)"; Type = "tiny"; Path = ""; Size = 39L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
            { Name = "Tiny Quantized (20MB)"; Type = "tiny.q4_0"; Path = ""; Size = 20L * 1024L * 1024L; IsDownloaded = false; IsQuantized = true }
            { Name = "Base (142MB)"; Type = "base"; Path = ""; Size = 142L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
            { Name = "Base Quantized (71MB)"; Type = "base.q4_0"; Path = ""; Size = 71L * 1024L * 1024L; IsDownloaded = false; IsQuantized = true }
            { Name = "Small (466MB)"; Type = "small"; Path = ""; Size = 466L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
            { Name = "Small Quantized (233MB)"; Type = "small.q4_0"; Path = ""; Size = 233L * 1024L * 1024L; IsDownloaded = false; IsQuantized = true }
            { Name = "Medium (1.5GB)"; Type = "medium"; Path = ""; Size = 1500L * 1024L * 1024L; IsDownloaded = false; IsQuantized = false }
        ])
    
    member this.SelectedModel 
        with get() = this.Bind(store, fun m -> m.Settings.SelectedModel)
        and set(value) = store.Dispatch (SelectModel value)
    
    // GPU Settings
    member this.UseGpu 
        with get() = this.Bind(store, fun m -> m.Settings.UseGpu)
        and set(value) = store.Dispatch (ToggleGpu value)
    
    member this.ThreadCount 
        with get() = this.Bind(store, fun m -> m.Settings.ThreadCount)
        and set(value) = store.Dispatch (SetThreadCount value)
    
    member this.GpuInfo = 
        "NVIDIA RTX 3050 (8GB VRAM) - CUDA 12 Ready"
    
    // Audio Settings
    member this.AudioDevices = 
        ObservableCollection([
            { Id = "default"; Name = "Default Audio Device"; SampleRate = 16000; Channels = 1 }
            { Id = "mic1"; Name = "USB Microphone"; SampleRate = 48000; Channels = 1 }
            { Id = "headset"; Name = "Bluetooth Headset"; SampleRate = 16000; Channels = 1 }
        ])
    
    member this.SelectedAudioDevice 
        with get() = this.Bind(store, fun m -> m.Settings.AudioDevice)
        and set(value) = store.Dispatch (SelectAudioDevice value)
    
    // Hotkey Settings
    member this.HotkeyDisplay = 
        this.Bind(store, fun m ->
            let mods = String.Join("+", m.Settings.Hotkey.Modifiers)
            if String.IsNullOrEmpty(mods) then
                m.Settings.Hotkey.Key
            else
                $"{mods}+{m.Settings.Hotkey.Key}"
        )
    
    member this.RecordNewHotkey() =
        // Open hotkey recording dialog
        ()
    
    // General Settings
    member this.MinimizeToTray 
        with get() = this.Bind(store, fun m -> m.Settings.MinimizeToTray)
        and set(value) = 
            store.Dispatch (UpdateSettings { store.Model.Settings with MinimizeToTray = value })
    
    member this.StartWithWindows 
        with get() = this.Bind(store, fun m -> m.Settings.StartWithWindows)
        and set(value) = 
            store.Dispatch (UpdateSettings { store.Model.Settings with StartWithWindows = value })
    
    member this.ShowTranscriptOverlay 
        with get() = this.Bind(store, fun m -> m.Settings.ShowTranscriptOverlay)
        and set(value) = 
            store.Dispatch (UpdateSettings { store.Model.Settings with ShowTranscriptOverlay = value })
    
    // Commands
    member this.SaveSettings() =
        // Save settings to disk
        printfn "Settings saved"
    
    member this.RestoreDefaults() =
        let defaultModel, _ = init()
        store.Dispatch (UpdateSettings defaultModel.Settings)
    
    static member DesignVM = SettingsViewModel(Design.stub)
```

### 6. Settings Window XAML

```xml
<!-- Views/SettingsWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:vm="using:Mel.UI.ViewModels"
        x:Class="Mel.UI.Views.SettingsWindow"
        x:DataType="vm:SettingsViewModel"
        Title="Mel Settings"
        Width="700" Height="500"
        WindowStartupLocation="CenterScreen"
        Icon="/Assets/mel-icon.ico">
    
    <Design.DataContext>
        <vm:SettingsViewModel/>
    </Design.DataContext>
    
    <DockPanel>
        <!-- Window Controls -->
        <Border DockPanel.Dock="Bottom" 
                Background="{DynamicResource SystemControlBackgroundListLowBrush}"
                Padding="10">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="10">
                <Button Content="Restore Defaults" Command="{Binding RestoreDefaults}"/>
                <Button Content="Save" Command="{Binding SaveSettings}" Classes="accent"/>
                <Button Content="Cancel" Click="CloseWindow"/>
            </StackPanel>
        </Border>
        
        <!-- Settings Tabs -->
        <TabControl Margin="10">
            <!-- Model Tab -->
            <TabItem Header="Model">
                <ScrollViewer>
                    <StackPanel Margin="20" Spacing="20">
                        <TextBlock Text="Whisper Model Selection" Classes="h2"/>
                        
                        <DataGrid ItemsSource="{Binding AvailableModels}"
                                  SelectedItem="{Binding SelectedModel}"
                                  AutoGenerateColumns="False"
                                  Height="200">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Model" 
                                                    Binding="{Binding Name}" 
                                                    Width="200"/>
                                <DataGridTextColumn Header="Size" 
                                                    Binding="{Binding Size, StringFormat='{}{0:N0} bytes'}" 
                                                    Width="100"/>
                                <DataGridCheckBoxColumn Header="Downloaded" 
                                                         Binding="{Binding IsDownloaded}" 
                                                         Width="100"/>
                                <DataGridCheckBoxColumn Header="Quantized" 
                                                         Binding="{Binding IsQuantized}" 
                                                         Width="100"/>
                            </DataGrid.Columns>
                        </DataGrid>
                        
                        <Border Background="{DynamicResource SystemControlBackgroundListLowBrush}"
                                CornerRadius="4" Padding="10">
                            <TextBlock Text="Note: Whisper.net currently supports GGML (.bin) models. GGUF models are not yet compatible."
                                       TextWrapping="Wrap"
                                       FontStyle="Italic"/>
                        </Border>
                        
                        <Button Content="Download Selected Model" 
                                Command="{Binding DownloadModel}"
                                HorizontalAlignment="Left"/>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
            
            <!-- GPU/Performance Tab -->
            <TabItem Header="Performance">
                <ScrollViewer>
                    <StackPanel Margin="20" Spacing="15">
                        <TextBlock Text="GPU Acceleration" Classes="h2"/>
                        
                        <CheckBox IsChecked="{Binding UseGpu}"
                                  Content="Enable GPU Acceleration (CUDA)"/>
                        
                        <TextBlock Text="{Binding GpuInfo}"
                                   Foreground="Green"
                                   FontWeight="Bold"/>
                        
                        <Border Background="{DynamicResource SystemControlBackgroundListLowBrush}"
                                CornerRadius="4" Padding="10">
                            <TextBlock TextWrapping="Wrap">
                                <Run Text="Important: "/>
                                <Run Text="CUDA accelerates encoder layers only. Decoder operations and ThreadCount affect CPU performance."
                                     FontStyle="Italic"/>
                            </TextBlock>
                        </Border>
                        
                        <StackPanel Orientation="Horizontal" Spacing="10">
                            <TextBlock Text="CPU Thread Count:" 
                                       VerticalAlignment="Center"/>
                            <NumericUpDown Value="{Binding ThreadCount}"
                                           Minimum="1" Maximum="16"
                                           Width="100"/>
                            <TextBlock Text="(Affects decoder performance)"
                                       FontStyle="Italic"
                                       VerticalAlignment="Center"/>
                        </StackPanel>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
            
            <!-- Audio Tab -->
            <TabItem Header="Audio">
                <ScrollViewer>
                    <StackPanel Margin="20" Spacing="15">
                        <TextBlock Text="Audio Input Device" Classes="h2"/>
                        
                        <ComboBox ItemsSource="{Binding AudioDevices}"
                                  SelectedItem="{Binding SelectedAudioDevice}"
                                  HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock>
                                        <TextBlock.Text>
                                            <MultiBinding StringFormat="{}{0} ({1}Hz, {2}ch)">
                                                <Binding Path="Name"/>
                                                <Binding Path="SampleRate"/>
                                                <Binding Path="Channels"/>
                                            </MultiBinding>
                                        </TextBlock.Text>
                                    </TextBlock>
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                        
                        <Button Content="Test Audio Device"
                                HorizontalAlignment="Left"/>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
            
            <!-- Hotkey Tab -->
            <TabItem Header="Hotkeys">
                <ScrollViewer>
                    <StackPanel Margin="20" Spacing="15">
                        <TextBlock Text="Keyboard Shortcuts" Classes="h2"/>
                        
                        <Grid ColumnDefinitions="200,*,Auto" RowDefinitions="Auto,Auto">
                            <TextBlock Grid.Row="0" Grid.Column="0" 
                                       Text="Toggle Recording:"
                                       VerticalAlignment="Center"/>
                            <TextBox Grid.Row="0" Grid.Column="1" 
                                     Text="{Binding HotkeyDisplay}"
                                     IsReadOnly="True"
                                     Margin="5,0"/>
                            <Button Grid.Row="0" Grid.Column="2" 
                                    Content="Record"
                                    Command="{Binding RecordNewHotkey}"/>
                        </Grid>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
            
            <!-- General Tab -->
            <TabItem Header="General">
                <ScrollViewer>
                    <StackPanel Margin="20" Spacing="15">
                        <TextBlock Text="Application Settings" Classes="h2"/>
                        
                        <CheckBox IsChecked="{Binding MinimizeToTray}"
                                  Content="Minimize to system tray"/>
                        
                        <CheckBox IsChecked="{Binding StartWithWindows}"
                                  Content="Start with Windows"/>
                        
                        <CheckBox IsChecked="{Binding ShowTranscriptOverlay}"
                                  Content="Show transcript overlay window"/>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
        </TabControl>
    </DockPanel>
</Window>
```

### 7. System Tray Service

```fsharp
// Services/TrayIconService.fs
module Mel.UI.Services.TrayIcon

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Platform
open Avalonia.Tray

type TrayIconService(mainVm: MainViewModel) =
    let trayIcon = TrayIcon()
    
    let createContextMenu() =
        let menu = NativeMenu()
        
        let toggleItem = NativeMenuItem("Toggle Recording (F9)")
        toggleItem.Click.Add(fun _ -> mainVm.ToggleRecording())
        menu.Add(toggleItem)
        
        menu.Add(NativeMenuItemSeparator())
        
        let settingsItem = NativeMenuItem("Settings...")
        settingsItem.Click.Add(fun _ -> mainVm.ShowSettings() |> ignore)
        menu.Add(settingsItem)
        
        let transcriptItem = NativeMenuItem("Show Transcript")
        transcriptItem.Click.Add(fun _ -> mainVm.ShowTranscript() |> ignore)
        menu.Add(transcriptItem)
        
        menu.Add(NativeMenuItemSeparator())
        
        let exitItem = NativeMenuItem("Exit")
        exitItem.Click.Add(fun _ -> mainVm.Exit())
        menu.Add(exitItem)
        
        menu
    
    member _.Initialize() =
        trayIcon.Menu <- createContextMenu()
        trayIcon.ToolTipText <- "Mel Voice Transcription"
        
        // Update icon based on status
        mainVm.StatusIcon
        |> Observable.subscribe (fun iconPath ->
            let assets = AvaloniaLocator.Current.GetService<IAssetLoader>()
            use stream = assets.Open(Uri($"avares://Mel.UI/{iconPath}"))
            trayIcon.Icon <- WindowIcon(stream)
        ) |> ignore
        
        // Double-click to show settings
        trayIcon.Clicked.Add(fun _ -> 
            mainVm.ShowSettings() |> ignore
        )
        
        trayIcon.IsVisible <- true
```

### 8. Composition Root

```fsharp
// CompositionRoot.fs
namespace Mel.UI

open ReactiveElmish.Avalonia
open Microsoft.Extensions.DependencyInjection
open Mel.UI.ViewModels
open Mel.UI.Views
open Mel.Transcription.Whisper
open Mel.Audio.Capture

type AppCompositionRoot() =
    inherit CompositionRoot()
    
    override this.RegisterServices services =
        base.RegisterServices(services)
            .AddSingleton<WhisperTranscriber>(fun sp ->
                WhisperTranscriber({
                    ModelPath = "./models/ggml-base.en.bin"
                    ModelType = GgmlType.Base
                    Language = "en"
                    UseGpu = true
                    ThreadCount = 8
                    MaxSegmentLength = 30
                })
            )
            .AddSingleton<IAudioCapture, WasapiCapture>()
            .AddSingleton<TrayIconService>()
        |> ignore
    
    override this.RegisterViews() =
        Map [
            VM.Key<MainViewModel>(), View.Singleton<MainWindow>()
            VM.Key<SettingsViewModel>(), View.Transient<SettingsWindow>()
            VM.Key<TranscriptViewModel>(), View.Singleton<TranscriptView>()
            VM.Key<ModelManagerViewModel>(), View.Transient<ModelManagerView>()
        ]
```

### 9. Application Entry Point

```fsharp
// App.axaml.fs
namespace Mel.UI

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Markup.Xaml
open Avalonia.Themes.Fluent

type App() =
    inherit Application()
    
    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Dark
        AvaloniaXamlLoader.Load(this)
    
    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let appRoot = AppCompositionRoot()
            let mainVm = appRoot.GetService<MainViewModel>()
            let trayService = appRoot.GetService<TrayIconService>()
            
            // Initialize system tray
            trayService.Initialize()
            
            // Start minimized to tray
            desktop.ShutdownMode <- ShutdownMode.OnExplicitShutdown
            
        | _ -> ()
        
        base.OnFrameworkInitializationCompleted()

// Program.fs
module Mel.UI.Program

open Avalonia

[<EntryPoint>]
let main args =
    AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace()
        .StartWithClassicDesktopLifetime(args)
```

## Key Features

### 1. System Tray Integration
- Always-accessible icon showing current status
- Right-click context menu for quick actions
- Visual status indicators (idle/recording/transcribing)
- Minimize to tray to reduce clutter

### 2. Settings Management
- Model selection with download manager
- GPU configuration with CUDA detection
- Audio device selection and testing
- Hotkey configuration with recording

### 3. Real-time Feedback
- Live transcription display
- Progress indicators for processing
- History of all transcriptions
- Export and clipboard support

### 4. Cross-platform Support
- Works on Windows, Linux, macOS
- Native look and feel on each platform
- Consistent behavior across platforms

## Design Benefits

ReactiveElmish.Avalonia provides:
- Static XAML views with design-time preview in Visual Studio and Rider
- Compiled bindings for type safety and performance
- Elmish state management with F# type safety
- Integration with DynamicData for efficient list updates

This architecture gives you:
1. **Clean separation** between UI and business logic
2. **Type-safe bindings** that catch errors at compile time
3. **Design-time support** for rapid UI development
4. **Reactive updates** that automatically reflect state changes
5. **Testable ViewModels** that can be unit tested independently

The UI complements your Whisper.net service perfectly, providing users with an intuitive way to configure and control voice transcription while maintaining the performance benefits of native AOT compilation.