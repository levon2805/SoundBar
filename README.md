# SoundBar

## How it looks!
(Note: This is with my custom background, showing off the potential!)


<p align="center">
  <img alt="Home" src="https://github.com/user-attachments/assets/30a1ccfd-ddf6-4eda-88fe-06bb4cbb5f56" width="30%" />
  <img alt="SettingsMenu" src="https://github.com/user-attachments/assets/2e9dae97-62c0-49f2-827b-48f65a495014" width="30%" />
  <img alt="MediaPlayer" src="https://github.com/user-attachments/assets/e40498c5-f609-4bfd-a5f4-55d4cfc0a76b" width="30%" />
</p>


## Features
* **Individual App Control:** Granular volume control over every active application on your PC.
* **App Nicknaming:** Give your apps custom, friendly names just by clicking on them.
* **Master Volume:** Control your entire system's audio.
* **Music Player Mode:** A beautiful, full-sized music player view that pulls in album artwork and provides a scrubbable timeline for the currently playing song.
* **Global Media Controls:** Convenient buttons at the bottom of the app to play, pause, skip, and mute system audio.
* **Output Device Switching:** Seamlessly change your default Windows playback device directly from the overlay.
* **Light & Dark Themes:** Fully supports Windows 11 native light, dark, and system themes, letting you customise the look to your exact preference.
* **Custom Backgrounds:** Drop any `.jpg` or `.png` into your backgrounds folder to deeply personalise the UI with an edge-to-edge frosted wallpaper.
* **Smart Dimming:** Background dimming elegantly shifts to ensure text remains perfectly readable whether you use Light or Dark mode.
* **Hearing Protection:** An optional excessive loudness warning helps you protect your ears if the volume stays high for too long.
* **Always-on-Top & Start at Login:** Pin the SoundBar overlay above all other windows, and optionally have it run quietly in the background when your PC starts up.
* **Do Not Disturb:** One-click toggle to mute all system notification sounds when you need peace and quiet.
* **App Blacklist:** Hide specific applications from your mixer interface to reduce clutter.
* **Background Audio Controls:** Automatically captures invisible background services and allows you to optionally expose and control them.
* **Automated Updates:** The app will periodically check GitHub for updates and notify you when a new release is available with a neat, 1-click update button.
* **Modern WinUI 3 Design:** Built natively with the Windows App SDK for a sleek interface with smooth, responsive controls.

## Download & Install (For Users)
The application is built as a completely self-contained, portable `.exe`. You do not need to install anything or download the Windows App SDK runtime!

1. Go to the [Releases](../../releases) tab on the right side of this GitHub repository.
2. Download the latest `SoundBar.zip` file.
3. Extract the folder anywhere on your computer.
4. Double click `SoundBar.exe` to run the app!

> **Note on Windows SmartScreen:** Because this is a new open-source application, Windows may show a "Windows protected your PC" warning when you first run the app. This is completely normal for new independent software. To run the app, simply click **"More info"** and then **"Run anyway"**.

## Development Setup
This project has recently been refactored into a multi-project enterprise structure:
* `src/SoundBar/` contains the WinUI 3 application.
* `tests/SoundBar.Tests/` contains the xUnit automated tests.

Open `SoundBar.slnx` or `src/SoundBar/SoundBar.csproj` in Visual Studio 2022 to get started.

### Running Automated Tests
We use **xUnit** and **Moq** to test our internal services and debouncing logic. 
> **Note:** If you encounter `ExpandPriContent` MSBuild errors when attempting to run `dotnet test` from the command line, this is a known environmental quirk with the Windows App SDK preview tooling.
> 
> **To run tests reliably:** Open the solution in **Visual Studio 2022** and use the built-in **Test Explorer** (`Test > Test Explorer > Run All Tests`).
