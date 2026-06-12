# MockRoom

A cross-platform desktop app for mocking up a room and seeing how much usable
floor space is left. Enter room dimensions, drop in editable furniture and
doors, and the app subtracts each footprint from the floor area — shown live in
a 2D top-down plan and an interactive 3D viewport, with the unused floor
highlighted in blue.

Built with **C# 14 / .NET 10**, **[Avalonia](https://avaloniaui.net/)** for the
UI, **[Silk.NET](https://dotnet.github.io/Silk.NET/)** for hardware-accelerated
OpenGL rendering, and **NativeAOT** for self-contained native builds.

## Features

- **Metric / imperial** units, switchable live — everything is stored
  canonically in meters; units are purely a presentation concern.
- **Editable box primitive** backs every furniture type (tables, chairs,
  couches, TV stands, coffee tables, beds, dressers, chests) plus **doors**.
  Adding a new furniture preset is data, not code.
- **Occupancy-grid space calculation** rasterizes footprints (and door swing)
  onto a ~5 cm grid, so overlaps are counted once and the numbers match the
  blue free-floor overlay exactly.
- **2D floor plan** with click-to-select and drag-to-move, snap-to-grid, and
  dimension labels.
- **3D viewport** with two switchable cameras — **first-person** (stand in the
  room, drag to look, slider for eye height) and **orbit** (circle the room,
  wheel to zoom) — plus **click-to-select** item ray-picking.
- **JSON save/load** (`.mockroom` files) via a source-generated serializer
  (NativeAOT-safe).
- **Provider-agnostic licensing** framework (`ILicenseProvider`), currently
  bypassed for development.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- A GPU/driver with OpenGL (or OpenGL ES) support
- For NativeAOT publish: the platform C toolchain (e.g. `clang` + `zlib1g-dev`
  on Linux, MSVC build tools on Windows)

## Build, run, test

```bash
dotnet build                          # build (debug)
dotnet test                           # run the xunit suite
dotnet run --project src/MockRoom     # launch the app
dotnet format --verify-no-changes     # style check
```

NativeAOT publish (per-RID; NativeAOT does not cross-compile):

```bash
dotnet publish src/MockRoom -r linux-x64 -c Release -p:PublishAot=true
```

## Packaging

Installer build scripts live in [`packaging/`](packaging/README.md):

- **Linux** — `packaging/linux/build-deb.sh` (`.deb`) and
  `packaging/linux/build-appimage.sh` (AppImage).
- **Windows** — `packaging/windows/build.ps1` drives an Inno Setup installer.

See [`packaging/README.md`](packaging/README.md) for prerequisites and details.

## Project layout

```
src/
  MockRoom.Core/   # pure domain + services (units, geometry, rooms, spatial,
                   # rendering math, persistence) — no UI, fully unit-tested
  MockRoom/        # Avalonia app: views, view models, 2D canvas, 3D GL viewport
  LexCore.Client/  # provider-agnostic license activation client
tests/
  MockRoom.Tests/  # xunit tests
packaging/         # NativeAOT publish + installer scripts
```

The domain lives in `MockRoom.Core` so it stays UI-free, reflection-free, and
NativeAOT-clean. See [`CLAUDE.md`](CLAUDE.md) for the full conventions.

## Licensing layer

`MockRoom` ships a provider-agnostic licensing abstraction (`ILicenseProvider`).
During development it runs through `BypassLicenseProvider` and never contacts any
server — no server URL, product id, or keys are part of this repository. The
single switch lives in `src/MockRoom/Licensing/LicensingOptions.cs`.

## License

Released under the [MIT License](LICENSE).
