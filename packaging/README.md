# Packaging (Phase 9)

Build scripts that turn a **NativeAOT** publish of MockRoom into installable
artifacts for Linux and Windows. Output lands in `packaging/dist/`.

> **NativeAOT does not cross-compile.** Build the Linux artifacts on Linux x64
> and the Windows artifacts on Windows x64. There is no single host that can
> produce both.

The app icon is generated procedurally by `make-icons.py` (no image-tool
dependency) into `packaging/icons/`. The build scripts regenerate it if missing.

```
packaging/
  make-icons.py            # PNG set (16–512) + Windows .ico generator
  icons/                   # generated (gitignored)
  dist/                    # generated installers (gitignored)
  linux/
    mockroom.desktop       # XDG desktop entry
    build-deb.sh           # → mockroom_<ver>_amd64.deb
    build-appimage.sh      # → MockRoom-<ver>-x86_64.AppImage
  windows/
    mockroom.iss           # Inno Setup script
    build.ps1              # publish + compile installer
```

## Linux

Prereqs: .NET 10 SDK, the AOT toolchain (`clang`, `zlib1g-dev`), and `dpkg-deb`
+ `fakeroot` for the `.deb`. The AppImage script fetches `appimagetool`
automatically.

```bash
# Debian / Ubuntu package
packaging/linux/build-deb.sh
sudo apt install ./packaging/dist/mockroom_1.0.0_amd64.deb   # installs /opt/mockroom + `mockroom` on PATH

# Portable AppImage
packaging/linux/build-appimage.sh
./packaging/dist/MockRoom-1.0.0-x86_64.AppImage
```

Pass `--no-publish` to either script to reuse an existing
`src/MockRoom/bin/Release/net10.0/linux-x64/publish/` instead of rebuilding.

The `.deb` installs the AOT binary and its sibling native libs
(`libSkiaSharp.so`, `libHarfBuzzSharp.so`) under `/opt/mockroom/`, a `mockroom`
launcher on `PATH`, the desktop entry, and hicolor icons. Runtime `Depends`
cover the X11/fontconfig/GL libraries Avalonia + Skia need;
`InvariantGlobalization` means no ICU dependency.

## Windows

Prereqs (on Windows x64): .NET 10 SDK, the MSVC/Desktop-C++ build tools the
NativeAOT linker needs, and [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`ISCC.exe` on `PATH`).

```powershell
pwsh packaging/windows/build.ps1
# → packaging/dist/MockRoom-1.0.0-Setup.exe
```

The installer drops `MockRoom.exe` (+ any sibling native DLLs) into
`Program Files\MockRoom`, adds Start-menu / optional desktop shortcuts, and
optionally registers the `.mockroom` file association.

`build.ps1 -NoPublish` reuses an existing win-x64 publish; `-Iscc <path>` points
at `ISCC.exe` if it is not on `PATH`.

### MSIX (alternative)

For Store / enterprise distribution, wrap the same win-x64 publish with the
Windows App SDK `MakeAppx.exe` + `SignTool`. Not scripted here — Inno Setup is
the default because it needs no signing certificate for local installs.

## Versioning

All scripts read `<Version>` from `src/MockRoom/MockRoom.csproj` (currently
`1.0.0`). Bump it there and every artifact name follows.
