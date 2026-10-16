# Diswin

A fast, lightweight, and 100% native Discord desktop client engineered for **Windows 11 & Windows 10** using **C# (.NET 8)**, **WinUI 3**, and the **Windows App SDK** — with zero Electron, zero CEF, and zero WebView wrappers. Respect your ecosystem.

<img src="docs/assets/banner1.gif" width="360" alt="Diswin Showcase" />

---

## Highlights

<img src="docs/assets/banner2.gif" width="360" alt="Interface Demo" />

- **Fluent Design & Personalization**: Deep integration with Windows 11 design standards, including Mica materials (`SystemBackdrop`), dark and light mode synchronization, squircle geometries, adjustable interface scaling, and customizable wallpaper backgrounds with blur and opacity inspired by [Files](https://github.com/files-community/Files).
- **Frictionless Authentication**: Instant QR Code login via official Discord Remote Auth v2 (scan from your mobile app, done), full 2FA support (TOTP, SMS, Backup codes), and direct token login.
- **Complete Messaging & Timeline**: Replies (`ReferencedMessage`), inline message editing, pinned messages drawer, mentions inbox, stickers, native polls, file attachment cards, clipboard image paste, automatic conversion of `>4000` character messages into `message.txt`, and built-in message translation.
- **Hover-Only Icon Animations**: Server and user avatars only play animations when hovered by the cursor, matching official Discord behavior.
- **Rich Context Menus & Member List**: Native right-click context flyouts for messages, channels, servers (join, create invite, leave), and friends (All, Pending, Blocked filters), plus a toggleable right-side participant panel.
- **Interactive Profiles & Banners**: Discord banner support, custom bio display, quick Direct Message input, and in-app profile editing directly from the user card.
- **Discord-style Startup Experience**: Animated launch sequence with automatic update and gateway connectivity check.
- **Lightweight Architecture**: Instant startup and minimal memory usage compared to Chromium-based clients.
- **Multilingual Support**: Extensible localization architecture (`en-US`, `fr-FR`).

---

## Architecture

```
DiscordNative.sln
├── src/
│   ├── DiscordNative.Protocol/       Standalone .NET class library (Gateway, REST, Remote Auth v2, Models)
│   ├── DiscordNative.App/            WinUI 3 Diswin application with MVVM architecture
│   └── DiscordNative.Installer/      Pure WinUI 3 setup wizard
└── docs/
    └── assets/                       Showcase assets and media
```

---

## Installation & Distribution

### 1. WinUI 3 Installer (`Diswin-Setup-v0.3.1-win-x64.zip`)
- Download and extract the installer bundle from [Releases](https://github.com/djell1l/DiscordNative/releases).
- Launch `Setup.exe` — a modern Windows 11 setup wizard written in C# and WinUI 3.
- Custom installation path, automatic Desktop and Start Menu shortcut creation.

### 2. Standalone Portable Package (`Diswin-v0.3.1-win-x64-portable.zip`)
- Extract anywhere.
- Run `DiscordNative.App.exe` directly without setup.

### 3. Automatic Updates
Diswin includes an integrated WinUI 3 `InfoBar` notification banner that checks GitHub Releases on startup and allows seamless in-app automatic updates with one click.

---

## Building from Source

### Prerequisites
- Windows 10 (version 1809+) or Windows 11
- .NET 8 SDK
- Visual Studio 2022 (v17.10+) with **.NET Desktop Development** and **Windows App SDK** workloads

### Build & Run
```powershell
# Clone the repository
git clone https://github.com/djell1l/DiscordNative.git
cd DiscordNative

# Compile and launch
.\build.ps1
```

---

## Contributors & Credits

- **[djell1l](https://github.com/djell1l)** — Lead developer & creator
- **Claude** & **Gemini** (via **Antigravity**) — AI pair programming collaborators and architecture co-design
- **[SakuraCord](https://github.com/SakuraCordApp/SakuraCord)** — Concept and native client philosophy
- **[Files](https://github.com/files-community/Files)** — UI/UX inspiration for WinUI 3 controls and background customization
- **[Oleglog](https://github.com/Oleglog)** — Community fork contributions on QR & MFA auth flows

---

## License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)**. See the [LICENSE](LICENSE) file for complete details.

Under this strong copyleft license:
- Any fork, derivative work, or redistribution **must remain completely free and open-source**.
- It is strictly prohibited to take this code, make it proprietary, or re-publish it in closed-source format without giving full attribution and releasing the modified source code under the same GPL-3.0 license.

---

## Disclaimer

Diswin is an unofficial third-party client built for experimental and educational purposes. Using third-party clients may be against Discord's Terms of Service. Use at your own discretion.
