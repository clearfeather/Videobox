<p align="center">
  <img width="128" align="center" src="Screenbox/Assets/StoreLogo.scale-400.png">
</p>
<h1 align="center">
  VideoBox
</h1>
<p align="center">
  A personal media library player for Windows.
</p>

VideoBox is a ClearFeather fork of [Screenbox](https://github.com/huynhsontung/Screenbox), the modern media player by Tung Huynh. It keeps the original open-source foundation and adds personal-library features focused on browsing local video collections.

This fork is not the official Screenbox build. For the original project, releases, and upstream support, visit [huynhsontung/Screenbox](https://github.com/huynhsontung/Screenbox).

VideoBox is built on top of [LibVLCSharp](https://github.com/videolan/libvlcsharp) and the Universal Windows Platform (UWP).

## Features

- Local video folder browsing with a folder-first library view
- All Videos view with sorting, search, and thumbnail size controls
- Favorites, recent media, playlists, and single-tag organization
- Folder covers from any selected JPEG image
- Optional scanned frame thumbnails and user-selected custom thumbnails
- Automatic title cleanup for common filename patterns
- Metadata side panel for selected videos
- Local-only playback focus with internet capability removed
- Startup audio preference for remembered volume, mute, or a chosen default level
- Optional app PIN lock

## Install

VideoBox is currently distributed as a sideloaded Windows app package.

1. Download the latest `VideoBox_..._x64.msixbundle` from [Releases](https://github.com/clearfeather/Videobox/releases).
2. Open the downloaded `.msixbundle`.
3. Choose Install or Update.

Windows may show a publisher/certificate warning for local sideload builds that are not Microsoft Store signed.

This fork is not currently published to the Microsoft Store or winget.

## Build

### Prerequisites

- Visual Studio 2026 or Visual Studio 2022
- UWP / WinUI application development workload
- Windows 11 SDK
- Git

### Quick start

1. Clone this repository.
2. Open `Screenbox.sln` in Visual Studio.
3. Set the solution platform to `x64`.
4. Build the solution.
5. Start debugging with Local Machine.

The internal project and namespace names still use `Screenbox` in many places to keep the fork scoped and avoid unnecessary churn. The visible app name is VideoBox.

## Attribution

VideoBox is derived from Screenbox by Tung Huynh.

- Original project: [huynhsontung/Screenbox](https://github.com/huynhsontung/Screenbox)
- Original author: [Tung Huynh](https://github.com/huynhsontung)

Thank you to Tung Huynh and the Screenbox contributors for the original application.

## License

VideoBox remains open source under the GNU General Public License v3.0, the same license used by Screenbox. See [LICENSE](LICENSE).

Third-party notices are listed in [NOTICE.md](NOTICE.md).
