# Building Thexder

This guide is for building the program from source. To play, see [Getting started](README.md#getting-started) in
the README.

## Requirements

- Windows 10 or 11, x64.
- The .NET 10 SDK. `global.json` asks for 10.0.401 and accepts any later 10.0 feature band.
- Git, to clone the repository.
- An editor if you want one: Visual Studio with .NET 10 support, VS Code with the C# Dev Kit, or Rider. The command
  line is enough.
- Your Thexder for Windows 95 CD image (`.iso`), to run what you build.

Check the SDK with:

```powershell
dotnet --version
```

## Build

```powershell
git clone https://github.com/iFocsy/thexder-rev.git
cd thexder-rev
dotnet build Thexder.sln
```

That makes a Debug build in `Build\bin\Debug\`. NuGet restores the three packages (WPF-UI, NAudio, Serilog) on the
first build.

There are three configurations:

| Configuration | Command | Output | Needs on the player's PC |
|---|---|---|---|
| Debug | `dotnet build Thexder.sln` | `Build\bin\Debug\Thexder.exe` with its DLLs and symbols | .NET 10 Desktop Runtime |
| Release | `dotnet build Thexder.csproj -c Release` | `Build\bin\Release\Thexder.exe`, one file | .NET 10 Desktop Runtime |
| Publish | `dotnet build Thexder.csproj -c Publish` | `Build\bin\Publish\Thexder.exe`, one self-contained file | nothing |

Intermediate files go to `Build\obj\` (set in `Directory.Build.props`), so the source folders stay clean. The whole
`Build\` folder can be deleted at any time.

## Run what you built

The program looks for its folders next to `Thexder.exe`, so for a Debug build:

```
Build\bin\Debug\
├── Thexder.exe
├── iso\          put your CD image here (any name ending in .iso)
├── assets\       made on the first start from the image
└── data\         settings, saves and logs, made on the first start
```

You can also start it without an image in `iso\`: it asks you to select one. The first start copies about 240 MB of
game files from the image into `assets\` and checks each one against `Include\assets.txt`.

Command line options:

```
Thexder.exe [--world <1-10> [--level <1-5>]] [--mute] [--fullscreen] [--kiosk]
```

`--world` and `--level` start one level directly, `--kiosk` loops the game's recorded demos until Esc.

## Project layout

```
Core/Assets         Readers for the game's files: resources, images, palettes, levels, WAV
Core/Audio          Sound mixer, sound table, MIDI reader and sequencer
Core/Engine         Frame buffer and blitters, palette cycling, colour tables, bitmap font
Core/Video          AVI reader and Indeo 3 decoder
Core/Game           The game: player, enemies, shots, objects, flow, saves, side windows, demos, network play
Feature             Game loop and input, music, movies, demo loop, network game, presenters
Feature/Procedural  Level generator and generated games
Services            Game file check and repair, audio output, networking, options, settings, logging
UI                  Main window, main menu, side windows, dialogs, About and Controls windows
Utils               Paths, ISO reader, native calls
Include             Icon, and data embedded in the exe: game file list, enemy types, Indeo 3 codebooks, credits
```

## Embedded data

The files in `Include\` are compiled into `Thexder.exe`:

- `assets.txt` lists every game file the program needs, one per line: SHA-256, size in bytes, path inside the CD.
  If a file is missing from `assets\` or its checksum differs, it is copied again from the image.
- `enemy-types.json` holds the statistics of the 56 enemy types, one per line.
- `indeo3-codebooks.json` holds the codebook tables of the movie decoder.
- `credits.txt` is the text of the credits roll in Help > About.

## Version

The version lives in `Thexder.csproj` (`<Version>`). Each build also stamps a short hash of the source files, so
Help > About shows something like `Version 1.0.0 r55928ae7`. The same sources always give the same hash, with or
without Git.

## Contributing

Bug fixes and features are welcome as pull requests. Please keep the classic game playing as it did in 1995: new
behaviour that changes how the game plays goes behind an option, the way Smooth Motion and Smart Laser Aim do.
Never commit game files or CD images.
