# Mel (Melchisidek)

# GPU-Accelerated Voice Transcription Using Whisper.NET

Mel is a high-performance, native AOT-compiled voice transcription service built in F# targeting .NET 10. It combines OpenAI's Whisper speech recognition with CUDA GPU acceleration to provide fast, local voice transcription without cloud dependencies.

## Key Features

- **🚀 GPU Accelerated**: CUDA support for NVIDIA RTX 3050 and similar GPUs
- **📦 Single Executable**: Native AOT compilation (~150MB standalone binary)
- **🎯 Local Processing**: No cloud dependencies, all transcription happens locally
- **⚡ Real-time Performance**: 5-10x real-time transcription speed with GPU
- **🎮 System Tray UI**: Avalonia-based desktop interface with system tray integration
- **🔧 Model Management**: Built-in Whisper model download and configuration

____

![Omar Sharif as Melchisidek in "The 13th Warrior"](img/Sharif_Melchisidek.png)<br>
*Omar Sharif as Melchisidek (the translator) in "The 13th Warrior" [1999]*

____

## Technology Stack

| Component | Technology |
|-----------|------------|
| **Language** | F# (.NET 10) |
| **UI Framework** | Avalonia + ReactiveElmish |
| **GPU Support** | CUDA 12 / DirectML fallback |
| **Voice Engine** | Whisper.net (whisper.cpp bindings) |
| **Architecture** | Native AOT with TensorPrimitives optimization |
| **Target Hardware** | NVIDIA RTX 3050 (8GB VRAM) |

## Quick Start

### Prerequisites
- .NET 10 SDK (RC1 or later)
- NVIDIA GPU with CUDA 12 support (driver 581.29+)
- Windows 11 Pro or compatible Linux distribution

### Build & Run
```bash
# Clone and restore dependencies
git clone https://github.com/your-repo/Mel.git
cd Mel
dotnet restore

# Build native executable
dotnet publish -c Release -r win-x64 --self-contained

# Run the service
./bin/Release/net10.0/win-x64/publish/Mel.exe
```

## Architecture Overview

```
┌─────────────────────────────────────────────┐
│              Mel Service                    │
├─────────────────────────────────────────────┤
│  System Tray UI (Avalonia + ReactiveElmish) │
│  ├─ Settings Management                     │
│  ├─ Model Download Manager                  │
│  ├─ Real-time Status Display                │
│  └─ Transcript History                      │
├─────────────────────────────────────────────┤
│  Core Voice Pipeline                        │
│  ├─ Voice Activity Detection                │
│  ├─ Audio Capture (WASAPI/ALSA)             │
│  ├─ Whisper.NET Transcription               │
│  └─ GPU-Accelerated Processing              │
├─────────────────────────────────────────────┤
│  .NET 10 Native AOT Runtime                 │
│  └─ CUDA 12 / TensorPrimitives              │
└─────────────────────────────────────────────┘
```

## Performance Characteristics

| Metric | RTX 3050 Performance |
|--------|----------------------|
| **Model Loading** | <500ms (Base model) |
| **Real-time Factor** | 5-10x (Base model) |
| **Transcription Latency** | 50-100ms per second of audio |
| **Memory Usage** | ~400MB + model size |
| **VRAM Usage** | 1-2GB (Base model) |
| **Startup Time** | <200ms (AOT compiled) |

## Supported Whisper Models

| Model | Size | VRAM Usage | Accuracy | Speed |
|-------|------|------------|----------|-------|
| **Tiny** | 39MB | <1GB | Good | Fastest |
| **Base** | 142MB | ~2GB | Better | Fast |
| **Small** | 466MB | ~3GB | Great | Moderate |
| **Medium** | 1.5GB | ~4GB | Excellent | Slower |

*Quantized variants (q4_0, q5_1) available for reduced memory usage*

## Project Structure

```
src/
├── Core/                        # Core service
│   ├── Audio/
│   │   ├── Capture.fs           # WASAPI audio capture
│   │   └── VAD.fs               # Voice activity detection
│   ├── Transcription/
│   │   └── Whisper.fs           # Whisper.NET integration
│   ├── Service/
│   │   └── Host.fs              # Background service host
│   └── Program.fs               # Service entry point
│
├── UI/                          # Desktop UI
│   ├── Models/                  # Elmish state models
│   ├── ViewModels/              # ReactiveElmish VMs
│   ├── Views/                   # Avalonia XAML views
│   └── Tray/                    # System tray service
│
design/                          # design docs
│   ├── main_design.md           # Core implementation
│   └── ui_design.md             # UI architecture
│
img/
    └── Sharif_Melchisidek.png   # README image
```

## Configuration

### GPU Settings
- **CUDA Acceleration**: Enabled by default for encoder layers
- **CPU Threads**: Configurable for decoder performance (default: 8)
- **Model Selection**: Base model recommended for RTX 3050

### Audio Settings
- **Sample Rate**: 16kHz (optimal for Whisper)
- **Voice Activity Detection**: Configurable thresholds
- **Buffer Size**: 4096 samples default

## Hardware 

**Optimized for NVIDIA RTX 3050:**
- 2560 CUDA cores for encoder acceleration
- 8GB VRAM accommodates Medium models
- 224 GB/s memory bandwidth for fast inference
- CUDA 12 compatibility with driver 581.29+

## Contributing

Mel is designed as an interim solution for voice to text (VTT). Contributions welcome for:
- Additional audio input methods
- UI improvements
- Performance optimizations
- Cross-platform compatibility enhancements

## License

MIT License - See LICENSE file for details.

