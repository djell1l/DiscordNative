# Diswin

> *"Am I a genius? Obviously. Or maybe I'm just dangerously over-imaginative. Either way, you're welcome."*

**Respect your ecosystem.** Running a full Chromium browser in the background just to send messages on Windows is an insult to your RAM. **Diswin** is a 100% native Windows 11 & Windows 10 Discord client built from the ground up in **C# (.NET 8)** and **WinUI 3** — zero Electron, zero CEF, zero WebView wrappers.

<p align="left">
  <img src="docs/assets/banner1.gif" width="240" alt="Showcase 1" />
  <img src="docs/assets/banner2.gif" width="240" alt="Showcase 2" />
</p>

---

## Philosophy

Most "lightweight" clients still wrap a web browser and call it a day. Diswin talks directly to the Discord Gateway and REST APIs and renders everything through native WinUI 3 controls, Mica materials, and hardware-accelerated DirectComposition.

- **Pure WinUI 3 & Fluent Design**: Native Mica backdrop, dark/light theme synchronization, squircle geometry, adjustable UI scaling, and custom background wallpapers with blur and opacity inspired by [Files](https://github.com/files-community/Files).
- **Frictionless Authentication**: Official Discord Remote Auth v2 QR Code login (scan from your phone, done), full TOTP/SMS/Backup 2FA support, and direct token login.
- **Complete Native Experience**: Replies, inline editing, pinned messages, mentions inbox, stickers, polls, voice channels, profile banners, hover-only animated avatars, right-click context menus, and a built-in auto-updater.
- **Multilingual Ready**: Designed for multiple languages (`en-US`, `fr-FR`).

---

## Get It

Grab the latest release from [GitHub Releases](https://github.com/djell1l/DiscordNative/releases):

- **WinUI 3 Setup (`Diswin-Setup-v0.3.1-win-x64.zip`)** — Native WinUI 3 installer wizard (`Setup.exe`) with custom install path, Desktop shortcut, and Start Menu integration.
- **Portable Archive (`Diswin-v0.3.1-win-x64-portable.zip`)** — Extract anywhere and launch `DiscordNative.App.exe`.

Updates are checked automatically on launch with one-click in-app installation.

---

## Build from Source

Requires Windows 10 (1809+) or Windows 11, .NET 8 SDK, and Visual Studio 2022 with the Windows App SDK workload.

```powershell
git clone https://github.com/djell1l/DiscordNative.git
cd DiscordNative
.\build.ps1
```

---

## Credits & Contributors

- **[djell1l](https://github.com/djell1l)** — Creator & lead developer
- **Claude** & **Gemini** (via **Antigravity**) — AI engineering collaborators
- **[SakuraCord](https://github.com/SakuraCordApp/SakuraCord)**, **[Files](https://github.com/files-community/Files)** & **[Oleglog](https://github.com/Oleglog)** — Inspiration for native WinUI 3 architecture and great community fork contributions

---

## License

Licensed under the **GNU General Public License v3.0 (GPL-3.0)**. See [LICENSE](LICENSE) for full terms.

Any fork, modification, or redistribution must remain open-source under the same license and credit the original authors. Turning this project into closed-source software is strictly prohibited.

---

## Disclaimer

Unofficial third-party client built for educational and experimental purposes. Not affiliated with Discord Inc. Use at your own discretion.
