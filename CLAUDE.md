# CLAUDE.md — mock_room

This file is read automatically by the autonomous agent before working on any issue.
It defines project conventions, structure, and rules Claude must follow.

---

## Project overview

`mock_room` is a C# 14 / .NET 10 desktop application built with:

- **Avalonia** — cross-platform UI framework (compiled bindings only — required for NativeAOT)
- **Silk.NET** — OpenGL bindings for hardware-accelerated rendering
- **NativeAOT** — ahead-of-time native compilation for production builds
- **LexCore.Client** (`src/LexCore.Client/`) — self-hosted license activation client library, ships with the app. Talks to a privately-operated LexCore server. Uses the agnostic `ILicenseProvider` abstraction.

The licensing layer is intentionally provider-agnostic. `ILicenseProvider` is the abstraction; `LexCoreProvider` is the current implementation. New providers go in `src/MockRoom/Licensing/<ProviderName>/`.

---

## Repository structure

```
src/
  MockRoom/
    Program.cs
    App.axaml / App.axaml.cs
    MainWindow.axaml / MainWindow.axaml.cs
    Licensing/
      ILicenseProvider.cs     # provider abstraction
      LicenseResult.cs        # result record
      LicenseStatus.cs        # status enum
      LicenseManager.cs       # high-level façade
      LexCore/
        LexCoreProvider.cs    # adapter: MockRoom.Licensing → LexCore.Client
    MockRoom.csproj

  LexCore.Client/             # license client library — ships with the app
    ILicenseProvider.cs
    LicenseResult.cs / LicenseStatus.cs
    LexCoreProvider.cs        # ILicenseProvider implementation
    LexCoreClient.cs          # HTTP client (cert pinning, request signing)
    MachineFingerprint.cs     # CPU + mobo + MAC + drive + OS GUID → HMAC hash
    VmDetector.cs             # CPUID / SMBIOS / MAC OUI VM detection
    SecureTokenStore.cs       # DPAPI (Win) / AES-GCM (Linux) token storage
    LicenseKeyVerifier.cs     # offline ECDSA P-256 key signature check
    ClockDriftGuard.cs        # server-time drift detection
    LexCore.Client.csproj

tests/
  MockRoom.Tests/
    LicensingTests.cs
    MockRoom.Tests.csproj

MockRoom.sln
```

---

## Commands

```bash
# Build (debug)
dotnet build

# Run tests
dotnet test

# Run the app
dotnet run --project src/MockRoom

# NativeAOT publish (Linux x64 example)
dotnet publish src/MockRoom -r linux-x64 -c Release /p:PublishAot=true

# Format check
dotnet format --verify-no-changes
```

### Packaging (Phase 9)

Installers are built from a NativeAOT publish. **NativeAOT does not
cross-compile** — build Linux artifacts on Linux x64, Windows artifacts on
Windows x64. Scripts live in `packaging/`; output lands in `packaging/dist/`.
See `packaging/README.md` for prerequisites and details.

```bash
# Linux: Debian package  → packaging/dist/mockroom_<ver>_amd64.deb
packaging/linux/build-deb.sh

# Linux: portable AppImage → packaging/dist/MockRoom-<ver>-x86_64.AppImage
packaging/linux/build-appimage.sh

# Both accept --no-publish to reuse an existing linux-x64 publish.
```

```powershell
# Windows (run on Windows x64): Inno Setup installer
#   → packaging/dist/MockRoom-<ver>-Setup.exe
pwsh packaging/windows/build.ps1
```

The product version comes from `<Version>` in `src/MockRoom/MockRoom.csproj`;
bump it there and every artifact name follows.

---

## Code style

- **C# 14 / .NET 10** — use modern syntax (`is not null`, `required`, primary constructors, collection expressions)
- **Nullable reference types** enabled — no `!` suppression without justification
- **No reflection** in hot paths — required for NativeAOT compatibility
- **Avalonia bindings** must use compiled bindings (`x:DataType` or code-behind); no `{Binding}` without `x:DataType`
- **Silk.NET GL calls** go through the `GL` API object, not static calls
- Line length: 120 characters
- Naming: PascalCase for types/members, camelCase for locals/parameters, `_camelCase` for private fields
- One public type per file; file name matches type name

---

## Testing conventions

- All tests in `tests/MockRoom.Tests/`
- Test files named `<Subject>Tests.cs`
- Use **xunit** — no MSTest or NUnit
- Test the licensing layer via `ILicenseProvider` stubs, not real LexCore server calls
- Every new public type or method needs at least one test
- Run `dotnet test` before committing

---

## NativeAOT rules

- No `Assembly.Load`, `Type.GetType`, or `Activator.CreateInstance`
- No unbound generic reflection (`typeof(T).MakeGenericType(...)`)
- Avalonia XAML uses compiled bindings only — `{Binding}` without `x:DataType` will fail at AOT
- P/Invoke is fine (Silk.NET and LexCore.Client both use it)
- Trim-unsafe code must be annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`

---

## Git conventions

- Branch names: `agent/issue-<number>`
- Commit style: [Conventional Commits](https://www.conventionalcommits.org/)
  - `feat:` new feature
  - `fix:` bug fix
  - `chore:` maintenance, deps, config
  - `test:` adding or updating tests
  - `docs:` documentation only
- Keep commits atomic — one logical change per commit
- Always run `dotnet test` before committing

---

## Agent-specific rules

- **Never commit** real LexCore server URLs, ECDSA private keys, admin API keys, or any credentials
- **Licensing abstraction first** — new licensing features go through `ILicenseProvider`, not directly in UI code
- **NativeAOT compatibility** — every change must remain AOT-publishable; verify with `dotnet publish /p:PublishAot=true` if in doubt
- Before committing, run `dotnet test` — all tests must pass
