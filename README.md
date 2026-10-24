<div align="center">

# 🚀 EdgeDrop

**Blazing fast, peer-to-peer local network file sharing for Windows with an intuitive Edge Screen Drawer and mobile Web Client.**

[![Build Status](https://github.com/LegoManchik/EdgeDrop/actions/workflows/ci.yml/badge.svg)](https://github.com/LegoManchik/EdgeDrop/actions/workflows/ci.yml)
[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg?style=flat-square)](https://github.com/LegoManchik/EdgeDrop/releases)
[![.NET](https://img.shields.io/badge/.NET-8.0--windows-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078D6.svg?style=flat-square&logo=windows)](https://microsoft.com/windows)
[![Author](https://img.shields.io/badge/author-LegoManchik-orange.svg?style=flat-square)](https://github.com/LegoManchik)
[![License](https://img.shields.io/badge/license-MIT-green.svg?style=flat-square)](LICENSE)

[Features](#-features) • [Installation](#-installation) • [How It Works](#-how-it-works) • [Building](#-building-from-source) • [Architecture](#-architecture) • [Security](#-security--privacy)

</div>

---

## 📖 Overview

**EdgeDrop** is a modern, lightweight, privacy-focused Windows application designed for seamless file and folder transfers across local networks (Wi-Fi / LAN). It eliminates cloud dependencies, subscription barriers, and file size limits. 

With its signature **Edge Drop Drawer**, you can send files to preset companion computers simply by dragging files to the border of your monitor. For smartphones (iOS / Android) and non-Windows machines, EdgeDrop serves a responsive, standalone **Web Client** directly over your local Wi-Fi with automatic QR-code pairing.

---

## ✨ Features

- 🖥️ **Screen Edge Drop Drawer**:
  - Drag files to configured monitor edges (Right, Left, Top, Bottom) to reveal a sleek slide-out drop target and dispatch files instantly to designated devices.
- 📱 **Cross-Platform Mobile Web App**:
  - Embedded high-speed HTTP server ([ASP.NET Core Kestrel](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel)).
  - Instant pairing via automatically generated QR codes in the desktop interface.
  - Send and receive files seamlessly on iOS, Android, macOS, and Linux without installing third-party apps.
- 🔍 **Automatic Device Discovery**:
  - Zero-configuration peer discovery via lightweight UDP broadcast beacons.
  - Real-time online/offline presence monitoring and heartbeat status.
- ⚡ **High-Speed & Robust File Streaming**:
  - Multi-gigabyte file transfers with chunked TCP streaming and buffered memory efficiency.
  - Live progress visualization (speed, ETA, bytes sent/received, percentage) on both sender and receiver.
  - Ability to pause and resume transfers.
- 🗂️ **Windows Explorer Integration**:
  - Native Windows shell context menu ("Send with EdgeDrop") to dispatch files directly from File Explorer.
- 📜 **Transfer History & Filter**:
  - Persistent transfer records with timestamps, peer metadata, transfer speed, and status.
  - Multi-select filtering system (by peer, direction, status).
  - Quick actions: open target folder, inspect details, clean history.
- 🎨 **Modern Windows 11 Fluent UI**:
  - Clean Dark and Light themes with system accent matching and smooth WPF animations.
  - Minimizes gracefully to the System Tray with background listening and native Windows toast notifications.
- 🌐 **Bilingual Localization**:
  - Full support for English and Russian languages with on-the-fly switching.
- 🔒 **100% Local & Private**:
  - No cloud storage, no account creation, no external servers, no telemetry, and no tracking.

---

## 🚀 Installation

### Option 1: Installer (Recommended)
1. Download the latest installer `EdgeDrop_Setup_v1.0.0.exe` from the [Releases](https://github.com/LegoManchik/EdgeDrop/releases) page.
2. Run the installer (supports both Russian and English wizards).
3. Optional tasks:
   - Add Desktop shortcut.
   - Launch EdgeDrop automatically on Windows startup.
   - Register "Send with EdgeDrop" Windows Explorer context menu.

### Option 2: Portable Release
1. Download `EdgeDrop-v1.0.0-portable.zip` from Releases.
2. Extract the archive into any folder.
3. Launch `EdgeDrop.exe`.

---

## 🎯 How It Works

### 1. Peer-to-Peer PC Transfer
```
┌────────────────┐                     ┌────────────────┐
│   Windows PC   │      Local Network  │   Windows PC   │
│   (EdgeDrop)   │ ◄─────────────────► │   (EdgeDrop)   │
└────────────────┘    UDP Discovery    └────────────────┘
        │              TCP Stream               │
        └───────────────────────────────────────┘
```
1. Ensure both PCs are connected to the same local Wi-Fi or LAN network.
2. Launch EdgeDrop on both machines. Devices will automatically discover each other.
3. Drag any file to your configured screen edge or use the main window device list to start sending.

### 2. PC to Smartphone / Tablet Transfer
1. Open EdgeDrop and click **Mobile Transfer** (or view the Web Server section).
2. Scan the displayed **QR Code** using your phone's camera or enter the local address (e.g. `http://192.168.1.5:11000`) in your browser.
3. Upload or download files directly from the browser interface.

---

## 🛠️ Building from Source

### Prerequisites
- Windows 10 (version 1809+) or Windows 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (v17.8+ with *.NET Desktop Development* workload) or [VS Code](https://code.visualstudio.com/)
- *(Optional)* [Inno Setup 6](https://jrsoftware.org/isdl.php) (for building the installer)

### Step-by-Step Build

1. **Clone the repository:**
   ```bash
   git clone https://github.com/LegoManchik/EdgeDrop.git
   cd EdgeDrop
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore EdgeDrop.sln
   ```

3. **Build the solution in Release mode:**
   ```bash
   dotnet build EdgeDrop.sln -c Release
   ```

4. **Publish self-contained / framework-dependent package:**
   ```bash
   dotnet publish EdgeDrop/EdgeDrop.csproj -c Release -r win-x64 --self-contained false -o publish
   ```

5. **(Optional) Build the Inno Setup installer:**
   Ensure Inno Setup compiler (`ISCC.exe`) is in your system `PATH`:
   ```bash
   iscc EdgeDrop_Setup.iss
   ```
   The compiled installer will be saved to `./installer_output/EdgeDrop_Setup_v1.0.0.exe`.

---

## 🏛️ Architecture & Project Structure

```
EdgeDrop/
├── .github/workflows/          # GitHub Actions CI workflow
├── EdgeDrop/
│   ├── Models/                 # Data contracts & DTOs (Settings, History, Queue)
│   ├── Services/
│   │   ├── DeviceDiscoveryService.cs   # UDP broadcast beacon discovery & heartbeat
│   │   ├── FileReceiver.cs             # TCP socket streaming receiver & progress tracker
│   │   ├── FileSender.cs               # TCP socket streaming sender & chunking
│   │   ├── TransferService.cs          # Transfer lifecycle coordinator & queue
│   │   ├── WebServer.cs                # Embedded Kestrel web host & mobile endpoints
│   │   ├── ShellContextMenuManager.cs  # Windows Explorer context menu registry hooks
│   │   ├── SenderApprovalManager.cs    # Sender verification & incoming prompt dialogs
│   │   └── NotificationAggregator.cs   # Windows desktop notification throttling
│   ├── Utils/
│   │   ├── LocalizationManager.cs      # Russian & English dictionary provider
│   │   ├── ThemeManager.cs             # Dark/Light fluent themes & accent brushes
│   │   ├── AppLogger.cs                # Diagnostic rolling file logging
│   │   ├── FileNameHelper.cs           # Safe sanitized file path handling
│   │   ├── QrCode.cs                   # Pure-managed QR code generator
│   │   └── SvgIconHelper.cs            # Vector SVG icon rendering helpers
│   ├── Web/
│   │   └── MobileHtml.html             # Responsive single-page web app for mobile devices
│   ├── icons/                          # SVG vector asset icons
│   ├── App.xaml / App.xaml.cs          # Application entry point & single-instance mutex
│   ├── MainWindow.xaml / .cs           # Core UI window & partial implementation files
│   └── EdgeDropWindow.xaml / .cs       # Edge slide-out drawer window
├── EdgeDrop.sln                # Visual Studio solution file
├── EdgeDrop_Setup.iss          # Inno Setup installation script
└── README.md                   # Project documentation
```

---

## 🔒 Security & Privacy

- **Zero Cloud Exposure**: EdgeDrop operates strictly within your local subnets (`192.168.x.x`, `10.x.x.x`, `172.16-31.x.x`). No internet traffic is routed through external servers.
- **Sender Verification**: Incoming transfers require manual acceptance by the recipient unless a device has been explicitly marked as trusted.
- **Path Traversal Protection**: All received filenames and directories are strictly sanitized to prevent directory traversal vulnerabilities (`../` attacks).
- **No Analytics / Telemetry**: Zero user tracking, analytics scripts, or telemetry collection.

---

## 🤝 Contributing

Contributions, bug reports, and suggestions are welcome!
1. Fork the Project (`https://github.com/LegoManchik/EdgeDrop/fork`)
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m "Add some AmazingFeature"`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).

---

## 👤 Author

Developed by **[LegoManchik](https://github.com/LegoManchik)**.
