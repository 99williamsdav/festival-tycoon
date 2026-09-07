# Festival Tycoon

Festival Tycoon is a Windows x64, offline festival-management game. M0.01 provides the reproducible .NET/Godot foundation only: a pure simulation library, a fixed smoke runner, architecture tests, and an empty Godot shell that displays its build identity.

## Prerequisites

- Windows x64 and PowerShell 7 or Windows PowerShell 5.1.
- .NET SDK 8.0.417 x64. `global.json` rejects other SDK versions so builds do not silently change toolchains.
- Network access for the first bootstrap and NuGet restore.
- About 2 GB free for the ignored, repository-local Godot editor/templates cache.

Godot does not need to be installed globally. The bootstrap downloads the official Godot .NET 4.7.2 editor and matching .NET export templates, verifies their SHA-256 hashes from `eng/toolchain.json`, and enables Godot self-contained mode under `.tools/`.

## Commands

Run all commands from the repository root:

```powershell
.\tools\bootstrap-toolchain.ps1
.\tools\build.ps1
.\tools\test.ps1
.\tools\run-runner.ps1
.\tools\run-game.ps1
.\tools\export-windows.ps1
.\artifacts\windows\FestivalTycoon.exe
```

The default build/test/runner configuration is Debug because Godot's development launcher loads Debug assemblies. Use `-Configuration Release` consistently on those three scripts when a non-export Release build is specifically needed. `export-windows.ps1` performs Godot's separate `ExportRelease` publish automatically. For a bounded non-graphical startup check, run `.\tools\run-game.ps1 -Headless -QuitAfter 2`; the script requires the application startup marker and rejects logged Godot errors even if Godot returns exit code 0.

The runner must print exactly:

```text
festival-tycoon-smoke|build=0.0.1-m0.01|seed=0|checksum=0000000000000000
```

To reproduce the clean-build acceptance check, close Godot and run:

```powershell
.\tools\clean-generated.ps1
.\tools\build.ps1
.\tools\test.ps1
.\tools\run-runner.ps1
.\tools\export-windows.ps1
```

The clean script removes only an explicit allowlist: `artifacts/`, `game/.godot/`, and the `bin/`/`obj/` directories belonging to the four known project paths. It does not discover directories recursively by name, and intentionally preserves the downloaded pinned toolchain to avoid a 1.2 GB template re-download.

## Project boundaries

- `src/Festival.Simulation` is an ordinary `net8.0` library with no Godot package or assembly reference.
- `src/Festival.Runner` references the simulation library and provides headless scenarios.
- `tests/Festival.Tests` tests simulation contracts without launching Godot.
- `game` is the Godot .NET presentation and references the simulation library.

## M0.01 decision record

Selected 7 September 2026: Godot .NET 4.7.2 stable, its matching .NET export templates, .NET SDK 8.0.417, and MSTest.Sdk 4.4.0. Godot 4.7.2 was the latest stable maintenance release; 4.8 was a development release. Godot's stable C# prerequisites require the .NET-enabled editor and .NET SDK 8 or later. .NET 8 is the conservative supported target shared by Godot and MSTest, and was already installed on the implementation machine. Compatibility rendering keeps the empty shell aligned with the technical specification's low-cost starting point.

Official references: [Godot 4.7.2 downloads](https://godotengine.org/download/archive/4.7.2-stable/), [C# prerequisites](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html), [command-line export](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html), [Windows export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_windows.html), and [MSTest SDK](https://www.nuget.org/packages/MSTest.Sdk/4.4.0).
