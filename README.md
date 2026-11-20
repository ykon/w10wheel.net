# W10Wheel.NET
W10Wheel.NET is the Mouse Wheel Simulator for Windows 10.  
[W10Wheel (Java Edition)](https://github.com/ykon/w10wheel) ported to .NET (F#).
## Supported
Windows 10 / Windows 11 (x64), within the Windows versions supported by .NET 11.  
<https://github.com/dotnet/core/blob/main/release-notes/11.0/supported-os.md>  
The release build is self-contained, so no .NET installation is required.
### Windows 10
Enable the option.  
<https://www.thurrott.com/windows/windows-10/2435/windows-10-tip-scroll-inactive-windows>
## Build
Requires the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0).
```
dotnet publish W10Wheel/W10Wheel.fsproj -p:PublishProfile=win-x64
```
The self-contained single-file `W10Wheel.exe` is written to `W10Wheel/bin/publish/win-x64/`.
## Usage
Please refer to this link: https://github.com/ykon/w10wheel.net/wiki

## License
The MIT License
