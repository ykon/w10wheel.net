# W10Wheel.NET
W10Wheel.NET is the Mouse Wheel Simulator for Windows 10 / 11.  
[W10Wheel (Java Edition)](https://github.com/ykon/w10wheel) ported to .NET (F#).
## Supported
Windows 10 / Windows 11 (x64), within the Windows versions supported by .NET 11.  
<https://github.com/dotnet/core/blob/main/release-notes/11.0/supported-os.md>  
The release build is self-contained, so no .NET installation is required (a build without the runtime is also available, see [Download](#download)).
### Scroll inactive windows
Enable the Windows option to scroll inactive windows when hovering over them.
- Windows 11: Settings > Bluetooth & devices > Mouse > "Scroll inactive windows when hovering over them"
- Windows 10: Settings > Devices > Mouse > "Scroll inactive windows when I hover over them"
## Download
1. Download `W10Wheel.NET-<version>.zip` from [Releases](https://github.com/ykon/w10wheel.net/releases).
2. Extract it to any folder and run `W10Wheel.exe`.

Since 2.9.1, the release also has `W10Wheel.NET-<version>-no-runtime.zip`, a build without the .NET runtime. It requires the [.NET 11 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/11.0) (x64) to be installed, which you can then keep up to date yourself (e.g. with Microsoft Update), without waiting for a new W10Wheel when .NET has a security fix. If the runtime is not installed, `W10Wheel.exe` shows a message to download it.

`W10Wheel.exe` is not code-signed, so Windows SmartScreen may show "Windows protected your PC" when you run it for the first time.
If you downloaded it from the Releases page above, click "More info" and then "Run anyway".
## Upgrading from 2.7.x
- Exit the running W10Wheel first (tray menu > Exit).
- Extract the new zip to a new folder instead of overwriting the old one (`FSharp.Core.dll` from the old version is no longer needed).
- Your settings files (`.W10Wheel*.properties` in your user folder) are kept and used as they are.
- If you start W10Wheel from a shortcut (e.g. in the Startup folder), change its target to the new `W10Wheel.exe`.
## Usage
Please refer to this link: https://github.com/ykon/w10wheel.net/wiki
## Build
Requires the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0).
```
dotnet publish W10Wheel/W10Wheel.fsproj -p:PublishProfile=win-x64
```
The self-contained single-file `W10Wheel.exe` is written to `W10Wheel/bin/publish/win-x64/`.
For the build without the runtime, use `-p:PublishProfile=win-x64-no-runtime` (written to `W10Wheel/bin/publish/win-x64-no-runtime/`).

To build the distribution zips (`artifacts/W10Wheel.NET-<version>.zip` and `artifacts/W10Wheel.NET-<version>-no-runtime.zip`):
```
pwsh build/package.ps1
```
## Release
1. Update the version in `W10Wheel/src/AppDef.fs`, `W10Wheel/src/AssemblyInfo.fs` and `dist/Readme.ja.txt` (including the history line).
2. Commit, then push a tag that matches the version: `git tag v2.8.0` and `git push origin v2.8.0`.
3. GitHub Actions builds the two zips and creates a draft release with them. Review the draft on GitHub and publish it.

## License
The MIT License
