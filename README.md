# SoundBar

A sleek, customisable, and powerful audio mixer overlay for Windows, built natively with the Windows App SDK (WinUI 3). 

**[Jump to: How to Install?](#how-to-install)**

## How it looks!
<p align="center">
  <img width="30%" height="469" alt="image" src="https://github.com/user-attachments/assets/f109dd51-1771-4fb3-b68e-f0a8d6367e25" />
  <img width="30%" height="471" alt="image" src="https://github.com/user-attachments/assets/88ce2b7e-2506-4699-a645-7f55477b24ce" />
  <img width="30%" height="469" alt="image" src="https://github.com/user-attachments/assets/61a8ca16-11a7-4436-954b-094548ef6af0" />
</p>

## Features

### Core Audio Control
* **Individual App Control:** Granular volume control over every active application on your PC.
* **Master Volume:** Command your entire system's audio from one slider.
* **Output Device Switching:** Seamlessly change your default Windows playback device directly from the overlay.
* **Global Media Controls:** Convenient buttons at the bottom of the app to play, pause, skip, and mute system audio.
* **Music Player Mode:** A beautiful, full-sized music player view that pulls in album artwork and provides a scrubbable timeline for the currently playing song.

### Mobile Companion App
* **Remote Control:** Control your PC's audio mixer, media playback, and output devices directly from your smartphone or tablet over your local network.
* **Progressive Web App (PWA):** Install the companion directly onto your phone's home screen for a native app experience.
* **Real-Time Sync:** Volume and media state instantly synchronise between your PC and mobile device.
* **Secure Pairing:** Connect safely via a rotating 4-digit security code and dynamic QR code generation.

### Personalisation & UI
* **Light & Dark Themes:** Fully supports Windows 11 native light, dark, and system themes, letting you customise the look to your exact preference.
* **Custom Backgrounds:** Drop any `.jpg` or `.png` into your backgrounds folder to deeply personalise the UI with an edge-to-edge frosted wallpaper.
* **Smart Dimming:** Background dimming elegantly shifts to ensure text remains perfectly readable whether you use Light or Dark mode.
* **App Nicknaming:** Give your apps custom, friendly names just by clicking on them.

### Advanced Features
* **Customisable Hotkeys:** Focused on a game or meeting? Quickly adjust your focused application's volume with custom global hotkeys!
* **App Blacklist:** Hide specific applications from your mixer interface to reduce clutter.
* **Background Audio Controls:** Automatically captures invisible background services and allows you to optionally expose and control them.
* **Hearing Protection:** An optional excessive loudness warning helps protect your ears if the volume stays high for an extended period.
* **Do Not Disturb:** A one-click toggle to mute all system notification sounds when you need peace and quiet.
* **Always-on-Top & Start at Login:** Pin the SoundBar overlay above all other windows, and optionally have it run quietly in the background when your PC starts up.

### Convenience
* **Automated Updates:** The app will periodically check GitHub for updates and notify you when a new release is available with a neat, 1-click update button.
* **Modern WinUI 3 Design:** Built natively with the Windows App SDK for a sleek interface with smooth, responsive controls.

## How to Install?

### Option 1: Install via Winget (Recommended)
You can easily install SoundBar using the Windows Package Manager (Winget). Open your command prompt or PowerShell and run:
```powershell
winget install levon2805.SoundBar
```

### Option 2: Portable Download
The application is also built as a completely self-contained, portable `.exe`. You do not need to install anything or download the Windows App SDK runtime!

1. Go to the [Releases](../../releases) tab on the right side of this GitHub repository.
2. Download the latest `SoundBar.zip` file.
3. Extract the folder anywhere on your computer.
4. Double-click `SoundBar.exe` to run the app!

> **Note on Windows SmartScreen:** Because this is a new open-source application, Windows may show a "Windows protected your PC" warning when you first run the app. This is completely normal for new independent software. To run the app, simply click **"More info"** and then **"Run anyway"**.

## Development Setup
Structure:
* `src/SoundBar/` contains the main WinUI 3 application.
* `tests/SoundBar.Tests/` contains the xUnit automated tests.
* `ReleaseTools/` contains internal deployment utilities.

Open `SoundBar.slnx` or `src/SoundBar/SoundBar.csproj` in Visual Studio 2022 to get started.

### Running Automated Tests
We use **xUnit** and **Moq** to test our internal services and debouncing logic. 
> **Note:** If you encounter `ExpandPriContent` MSBuild errors when attempting to run `dotnet test` from the command line, this is a known environmental quirk with the Windows App SDK preview tooling.
> 
> **To run tests reliably:** Open the solution in **Visual Studio 2022** and use the built-in **Test Explorer** (`Test > Test Explorer > Run All Tests`).
