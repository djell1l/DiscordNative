# Diswin

Running an entire Chromium browser in the background just to send a few text messages on Windows is an insult to your hardware. Respect your ecosystem.

**Diswin** is a 100% native Discord client built from scratch in **C# (.NET 8)** and **WinUI 3**. No Electron. No CEF. No disguised web wrappers eating half a gigabyte of RAM for breakfast. Either way, you're welcome.

<p align="left">
  <img src="docs/assets/banner1.gif" width="240" alt="Diswin Showcase" />
  <img src="docs/assets/banner2.gif" width="240" alt="Interface Demo" />
</p>

---

## Why It Exists

Most "lightweight" clients still wrap a web browser, slap a theme on it, and call it native. Diswin talks directly to the Discord Gateway and REST APIs and renders everything through real Windows 11 WinUI 3 controls, Mica materials, and hardware-accelerated DirectComposition.

- **Pure WinUI 3 & Fluent Design** — Real Mica backdrop, dark/light theme sync, squircle geometry, UI scaling, and custom wallpaper backgrounds with blur and opacity inspired by [Files](https://github.com/files-community/Files).
- **Zero-Drama Login** — Instant QR Code login via Discord Remote Auth v2 (scan from your phone, done), full 2FA support (TOTP, SMS, Backup codes), or direct token login.
- **Built Properly** — Replies, inline editing, pinned messages, mentions inbox, stickers, polls, voice rooms, profile banners, hover-only animated icons, right-click context menus, and a seamless in-app auto-updater.
- **Multilingual** — Built-in localization support (`en-US`, `fr-FR`).

---

## Installation

Grab the latest build from [Releases](https://github.com/djell1l/DiscordNative/releases):

- **WinUI 3 Setup (`Diswin-Setup-v0.3.1-win-x64.zip`)** — A native C# / WinUI 3 installer (`Setup.exe`) that sets up the app, Desktop shortcut, and Start Menu entry cleanly.
- **Portable (`Diswin-v0.3.1-win-x64-portable.zip`)** — Extract anywhere and run `DiscordNative.App.exe`. No strings attached.

---

## Build from Source

Requires Windows 10 (1809+) or Windows 11, .NET 8 SDK, and Visual Studio 2022 with the Windows App SDK workload.

```powershell
git clone https://github.com/djell1l/DiscordNative.git
cd DiscordNative
.\build.ps1
```

---

## Credits

- **[djell1l](https://github.com/djell1l)** — Creator & lead developer of Diswin
- **Claude** & **Gemini** (via **Antigravity**) — AI engineering collaborators
- Inspired by **[Files](https://github.com/files-community/Files)** and **[SakuraCord](https://github.com/SakuraCordApp/SakuraCord)**, with a nod to **[Oleglog](https://github.com/Oleglog)** for his community fork PR/commits on auth flows

---

## License

Licensed under the **GNU General Public License v3.0 (GPL-3.0)**. See [LICENSE](LICENSE) for full details.

Any fork, derivative work, or redistribution must remain open-source under the same GPL-3.0 license and credit the original project. Stealing this code to make it closed-source is strictly off the table.

---

## Disclaimer

Diswin is an independent third-party client built for educational and experimental purposes. Not affiliated with Discord Inc. Use at your own discretion.
