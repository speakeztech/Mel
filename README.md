# SpeakEZ (Project Mel)

<table>
  <tr>
    <td align="center" width="100%">
      <strong>⚠️ Caution: Experimental ⚠️</strong><br>
      This project is in early development and <i>not</i> intended for production use.
    </td>
  </tr>
</table>

## Push-to-Talk Voice Transcription with Whisper.NET

SpeakEZ is a batch-mode voice transcription application built in F# for .NET 10. It uses OpenAI's Whisper models for accurate speech-to-text conversion in a push-to-talk interface. 

**Important Note:** This is a batch transcription system - you hold F9 to record, and transcription happens when you release. This design choice is based on Whisper's architecture, which is optimized for processing complete audio segments rather than real-time streaming.

## Why Batch Mode?

Whisper models are designed for high-accuracy transcription of complete speech segments, not real-time streaming. This application embraces that limitation to provide:
- **95%+ accuracy** with sufficient audio context
- **Complete sentence understanding** 
- **Proper punctuation and capitalization**
- **No chunky or incorrect partial transcriptions**

**Coming Soon:** A parallel version using Vosk for true real-time streaming transcription is in development.

## Key Features

- **🎤 Push-to-Talk Interface**: Hold F9 to record, release to transcribe
- **🚀 GPU Accelerated**: CUDA support for NVIDIA GPUs
- **🎯 Local Processing**: No cloud dependencies, all transcription happens locally
- **📝 Direct Text Input**: Types transcribed text at cursor position in any application
- **🎮 System Tray Application**: Runs quietly in background with settings window
- **🔧 Model Selection**: Choose from Tiny, Base, Small, or Medium Whisper models

____

![Omar Sharif as Melchisidek in "The 13th Warrior"](img/Sharif_Melchisidek.png)<br>
*Omar Sharif as Melchisidek (the translator) in "The 13th Warrior" [1999]*

____

## Technology Stack

| Component | Technology |
|-----------|------------|
| **Language** | F# (.NET 10) |
| **UI Framework** | Avalonia 11 |
| **GPU Support** | CUDA 12 via Whisper.NET |
| **Voice Engine** | Whisper.NET (whisper.cpp bindings) |
| **Transcription Mode** | Batch processing (push-to-talk) |
| **Target Hardware** | NVIDIA RTX GPUs |

## How It Works

1. **Hold F9**: Start recording audio
2. **Speak**: Your speech is buffered locally
3. **Release F9**: Audio is sent to Whisper for transcription
4. **Text appears**: Transcribed text is typed at your cursor position

This batch lacks the "smoothness" of multi-threaded streaming transcription like most expect due to phone VTT behavior that's commonplace. This approach is based on whisper model behavior that uses this approach to ensure accurate transcription.

## Quick Start

### Prerequisites
- .NET 10 SDK (RC1 or later)
- NVIDIA GPU with CUDA support (optional but recommended)
- Windows 10/11

### Build & Run
```bash
# Clone and restore dependencies
git clone https://github.com/your-repo/SpeakEZ.git
cd Mel
dotnet restore

# Build and run in debug mode
./run-debug.ps1

# Or build for release
dotnet publish -c Release -r win-x64 --self-contained
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

## Hardware 

Right now the support is pretty "skinny" as it was an experiment to see whether this approach would work *at all*. So with this proof in place, other experiments with true multi-threaded text transcription (via Vosk) will take place. Once that's "in the can" then multi-platform support will be rolled out.

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
