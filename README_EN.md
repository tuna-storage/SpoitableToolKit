# SpoitableToolKit

[日本語](README.md) | English

A Windows app to use alongside VRChat.
It works both on the desktop and as a SteamVR overlay.
It sends the heart rate from a heart rate monitor to your avatar, turns the URL of a video that failed to play into one that plays, and lists what happened in VRChat from its logs.
More features can be added as modules.

## Features

### Heart rate

The app receives the heart rate from Bluetooth (BLE) heart rate monitors and from Health Data Server (HDS), an Apple Watch app.
The heart rate can then be:

- Sent to your VRChat avatar over OSC (the parameter names come from the VRCHR or VRCOSC preset)
- Recorded, with statistics and graphs to look back on (records can be exported to CSV)
- Broadcast over WebSocket, so streaming software can show it in an overlay

### Video URL

When a video fails to play in VRChat, the app resolves its URL into one that plays and copies it to the clipboard.
Resolving uses yt-dlp.
If you would rather not have yt-dlp on this PC, you can leave resolving to a server you set up yourself ([docs/videourl-server.md](docs/videourl-server.md)).

### VRChat logs

The app lists events from VRChat's logs, such as joins, leaves, world changes and video playback.
The list can be searched and filtered, and you can go back to a date and time.

### VR

Looking at your wrist in SteamVR shows a shortcut bar.
The shortcut bar shows the heart rate and the time, and its shortcuts open the app's screens.
A screen opened this way appears in a VR window, either above your wrist or placed in the space in front of you.

The app is available in Japanese and English, with light and dark themes.

## Download

Download the installer (`SpoitableToolKit-Setup.exe`) from [Releases](../../releases).
It runs on 64-bit Windows only, and has been tested on Windows 11.

Once installed, the app checks for updates by itself and shows a notice at the top of the screen when a new version is out.
The update channel can be set to Stable or Nightly (development versions) in the settings.

The installer is not code-signed, so Windows SmartScreen may ask for confirmation the first time you open it.

Settings and heart rate records are saved in `%LOCALAPPDATA%\SpoitableToolKit`.
This folder is separate from where the app is installed, so uninstalling does not delete it.

## Usage

Choose the heart rate monitor under "Heart rate" in the settings.
If left empty, the app connects to the first monitor it finds.

To send to your avatar, turn on OSC in VRChat.
The port is set under "OSC" in the settings (9000 by default).

The VR features work while SteamVR is running.
If you start SteamVR after the app, they appear once the app detects SteamVR.

## Network access

The app communicates outside this PC in these cases:

- Checking for app updates: checks the Releases of this repository (GitHub).
- Adding modules and checking them for updates: downloads releases or source from the module's repository (GitHub), and checks it for new releases.
- Resolving video URLs: downloads yt-dlp and deno from GitHub the first time, then checks for a new version of yt-dlp at most once a day (not when using a yt-dlp found on PATH). When resolving, yt-dlp queries the video's site. If you set the app to use an external server, the video's URL is sent to that server.
- Building a module from source: downloads the packages it needs from NuGet. If the .NET SDK is not found, it is downloaded from Microsoft after you confirm.

The heart rate OSC and broadcast stay on this PC.
Only when receiving from an Apple Watch (HDS) is turned on does the app listen for connections from the LAN.
What modules you add communicate with depends on each module.

## Modules

Add a module under "Modules" in the app by entering the URL of its GitHub repository.
If the repository has a release, the app installs its zip; otherwise it downloads the source and builds it on this PC (it asks before building).

Modules run with the same permissions as the app, so only add modules you trust.

The SDK for writing modules comes with the app.
A minimal module is in [SpoitableToolKit-SampleModule](https://github.com/tuna-storage/SpoitableToolKit-SampleModule).

## Build

Building requires the .NET 10 SDK.
The screens are rendered with CefSharp (Chromium).

```powershell
dotnet build SpoitableToolKit.slnx
dotnet test tests/SpoitableToolKit.Tests
dotnet test tests/SpoitableToolKit.E2E   # starts the app and drives its screens
```

The app's texts are in each project's `locales/`.
How the installer is made is described in [docs/release.md](docs/release.md) (in Japanese).

## License

[MIT](LICENSE)

The bundled libraries and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and can also be viewed under "Licenses" in the app's settings.
