# Diswin

Running an entire Chromium browser in the background just to send a few text messages on Windows is an insult to your hardware. If Apple gets to flex its sleek Swift and SwiftUI native ecosystem, why on earth shouldn't Windows get the exact same first-class treatment? Respect your ecosystem.

Diswin is a 100% native Discord client engineered from scratch in **C# (.NET 8)** and **WinUI 3**. No Electron. No CEF. No disguised web browser quietly devouring half a gigabyte of your RAM. Either way, you're welcome.

<img src="docs/assets/banner1.gif" width="360" alt="Diswin Showcase" />

---

## Highlights

<img src="docs/assets/banner2.gif" width="360" alt="Interface Demo" />

- **Pure WinUI 3 & Fluent Design**: Real Mica backdrop, dark and light theme synchronization, squircle geometry, UI scaling, and custom wallpaper backgrounds with blur and opacity inspired by [Files](https://github.com/files-community/Files). No fake CSS tricks.
- **Frictionless Login**: Official Discord Remote Auth v2 QR Code login (point your phone, scan, and you're in), full 2FA support (TOTP, SMS, Backup codes with automatic fallback), and direct token login if you like taking the direct highway.
- **Timeline Done Right**: Full message replies with header previews, inline editing so you can fix your typos before anyone notices, pinned messages, mentions inbox, Discord stickers, native polls, and proper file cards.
- **Tamed Animations**: Server icons and avatars only animate when your mouse is actually over them, instead of turning your sidebar into a chaotic Vegas casino.
- **No-Nonsense Context Menus**: Right-click anything for quick actions on messages, channels, servers, and friends, plus a toggleable right-side member drawer.
- **Banners & Profile Editing**: Full Discord banner rendering, custom bio display, quick DM input, and the ability to edit your own profile without opening a browser.
- **Instant Launch & Auto-Updater**: Launches immediately, checks for updates in the background, and updates itself in one click.
- **Multilingual**: Native localization support (`en-US`, `fr-FR`).

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

### 1. WinUI 3 Setup (`Diswin-Setup-v0.3.1-win-x64.zip`)
Download, unzip, and run `Setup.exe`. It is a real Windows 11 installer wizard written in WinUI 3 that sets up the app, drops a shortcut on your Desktop, hooks into your Start Menu, and gets out of your way.

### 2. Standalone Portable (`Diswin-v0.3.1-win-x64-portable.zip`)
For the minimalists. Unzip anywhere, run `DiscordNative.App.exe`, and get back to what you were doing.

### 3. Automatic Updates
Diswin watches GitHub Releases on startup. When a new build drops, an in-app banner lets you update and restart in one click.

---

## Building from Source

### Prerequisites
- Windows 10 (version 1809+) or Windows 11
- .NET 8 SDK
- Visual Studio 2022 (v17.10+) with **.NET Desktop Development** and **Windows App SDK** workloads

### Build & Run
```powershell
# Clone the repository
git clone https://github.com/djell1l/Diswin.git
cd Diswin

# Compile and launch
.\build.ps1
```

---

## Contributors & Credits
AI is the future — why whisper about it on GitHub like it's a dirty secret? While everyone else pretends they hand-carved 50,000 lines of boilerplate by candlelight, we own it: pairing human ambition with cutting-edge AI pair programming to build what multi-billion-dollar companies were too lazy to ship.

<p align="left">
  <a href="https://github.com/djell1l">
    <img src="https://github.com/djell1l.png" width="60" height="60" style="border-radius: 50%;" alt="djell1l" />
  </a>
  &nbsp;&nbsp;
  <a href="https://github.com/Oleglog">
    <img src="https://github.com/Oleglog.png" width="60" height="60" style="border-radius: 50%;" alt="Oleglog" />
  </a>
</p>

- **[djell1l](https://github.com/djell1l)** — Lead developer & creator
- **[Oleglog](https://github.com/Oleglog)** — Contributor (integrated QR Remote Auth v2, MFA flows, and CI pipeline for v0.3.1)
- **[Claude](https://github.com/anthropics)** ([@anthropics](https://github.com/anthropics)) & **[Gemini](https://github.com/google-gemini)** ([@google-gemini](https://github.com/google-gemini)) via **[Antigravity](https://github.com/google-deepmind)** ([@google-deepmind](https://github.com/google-deepmind)) — AI pair programming collaborators and architecture co-design
- **[SakuraCord](https://github.com/SakuraCordApp/SakuraCord)** — Concept and native client philosophy
- **[Files](https://github.com/files-community/Files)** — UI/UX inspiration for WinUI 3 controls and background customization

---

## License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)**. See the [LICENSE](LICENSE) file for complete details.

Under this strong copyleft license:
- Any fork, derivative work, or redistribution **must remain completely free and open-source**.
- It is strictly prohibited to take this code, make it proprietary, or re-publish it in closed-source format without giving full attribution and releasing the modified source code under the same GPL-3.0 license.

---

## Disclaimer

Diswin is an unofficial third-party client built for experimental and educational purposes. Using third-party clients may be against Discord's Terms of Service. Use at your own discretion.
