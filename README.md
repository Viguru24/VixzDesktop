<div align="center">

# 🎬 Vixz Desktop

### Modern Glassmorphic YouTube Player & AI Copilot for Windows 10/11
*Built with .NET 9, WPF & WebView2 — Zero Ads, SponsorBlock, Local AI Summaries & Native Fluent Design.*

<br/>

[![Download MSIX](https://img.shields.io/badge/📥%20Download%20MSIX-v1.0.0-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/Viguru24/VixzDesktop/releases/download/v1.0.0/VixzDesktop-v1.0.0.msix)
[![GitHub Release](https://img.shields.io/github/v/release/Viguru24/VixzDesktop?style=for-the-badge&color=2ea44f)](https://github.com/Viguru24/VixzDesktop/releases/latest)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D6?style=for-the-badge&logo=windows)](https://github.com/Viguru24/VixzDesktop)
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=for-the-badge)](LICENSE)
[![Companion App](https://img.shields.io/badge/Android%20App-Vixz%20Player-3DDC84?style=for-the-badge&logo=android)](https://github.com/Viguru24/YouTube)

<p align="center">
  <a href="#-quick-install">📥 <b>Quick Install</b></a> •
  <a href="#-features">✨ <b>Features</b></a> •
  <a href="#-keyboard-shortcuts">⌨️ <b>Shortcuts</b></a> •
  <a href="#-comparison">📊 <b>Comparison</b></a> •
  <a href="#-build-from-source">🛠️ <b>Build from Source</b></a>
</p>

</div>

---

## ⚡ Quick Install (Windows 10/11)

1. **[📥 Download VixzDesktop-v1.0.0.msix](https://github.com/Viguru24/VixzDesktop/releases/download/v1.0.0/VixzDesktop-v1.0.0.msix)** directly.
2. Double-click the downloaded `.msix` package to install via the native Windows App Installer.
3. Launch **Vixz Desktop** from your Start Menu!

---

## ✨ Key Features

- **🤖 Built-in AI Copilot & Voice Assistant**:
  - *"Summarise this video"* (Generates 3-sentence TL;DR, Key Takeaways, and Interactive Clickable Timestamp Chapters).
  - Natural voice controls: *"Play latest news"*, *"Skip 30 seconds"*, *"Set sleep timer for 25 mins"*.
- **🛡️ 100% Ad-Free & In-Video SponsorBlock**:
  - Automatically skips in-video sponsorships, intros, and self-promotions with millisecond precision.
- **📥 One-Click Video Downloader**:
  - Save 1080p MP4 videos directly to your PC with real-time download progress.
- **⧉ Always-On-Top Pop-Out Mini Player**:
  - Floating PiP window with custom window controls and instant docking.
- **📸 Smart Screenshot Capture**:
  - Dedicated screenshot hotkey (`S`) with customizable album destination folders.
- **🌙 Sleep Timer**:
  - Smooth audio fade-out and auto-pause with quick presets (15m, 30m, 45m, 60m).
- **🎨 Windows 11 Acrylic Glassmorphism**:
  - High-contrast typography, gold accents, smooth animations, and clean Windows window chrome.

---

## 📊 Feature Comparison

| Feature | Browser YouTube | FreeTube | 🎬 **Vixz Desktop** |
| :--- | :---: | :---: | :---: |
| **Native Windows 11 Acrylic Glass UI** | ❌ | ❌ | **✅ Built-in** |
| **SponsorBlock Auto-Skip** | ❌ (Needs Ext) | ✅ | **✅ Native Built-in** |
| **AI Video Summaries & Key Points** | ❌ | ❌ | **✅ 1-Click** |
| **Voice Command Assistant** | ❌ | ❌ | **✅ Built-in** |
| **1-Click 1080p MP4 Downloader** | ❌ (Needs Premium) | ⚠️ | **✅ Built-in** |
| **Always-On-Top Floating PiP Window** | ⚠️ | ⚠️ | **✅ Dedicated Mode** |
| **Sleep Timer with Audio Fade-out** | ❌ | ❌ | **✅ Built-in** |

---

## ⌨️ Keyboard Shortcuts

| Key | Action |
| :--- | :--- |
| `Space` / `K` | Play / Pause |
| `Left` / `Right` | Seek -5s / +5s |
| `J` / `L` | Seek -10s / +10s |
| `M` | Mute / Unmute |
| `S` | Capture Screenshot |
| `D` | Download Current Video (MP4) |
| `N` | Play Next Video |
| `F` | Toggle Fullscreen |
| `T` | Pin Window Always-on-Top |
| `F12` | Open Developer Tools |

---

## 🛠️ Build from Source

### Prerequisites
- Windows 10 or 11 (64-bit)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)

```powershell
# Clone the repository
git clone https://github.com/Viguru24/VixzDesktop.git
cd VixzDesktop

# Build and Run
dotnet run --project src/VixzDesktop
```

---

## 📱 Mobile Companion App
Looking for the Android version? Check out **[Vixz YouTube Player for Android](https://github.com/Viguru24/YouTube)** with 12-language support, fluid gestures, and background PiP playback.

---

<div align="center">
  <sub>Distributed under the MIT License • Built with ❤️ by <a href="https://github.com/Viguru24">Viguru24</a></sub>
</div>
