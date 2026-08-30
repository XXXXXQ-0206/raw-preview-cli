# RawPreview CLI

[English](README.md) | [简体中文](README.zh-CN.md)

Windows command-line export of Sony ARW RAW images through the installed Microsoft Photos resize pipeline.

## Project Overview

RawPreview CLI exports Sony ARW files to JPEG by invoking the same Photos resize path used by the Windows Photos application. The project keeps Photos-dependent code in a small Worker process and keeps file enumeration, metadata checks, output validation, and batch policy in the CLI.

The project is Windows-only because the rendering backend depends on Windows Runtime components and the locally installed Photos and Raw Image Extension packages.

## Features

- Batch or single-file ARW to JPEG export.
- Photos-backed RAW rendering instead of an independent third-party demosaicer.
- Portrait-aware output dimensions and EXIF Orientation validation.
- JPEG quality control from 1 to 100.
- Collision detection and optional overwrite mode.
- Atomic output using a unique .partial file and post-write validation.
- JSON and JSONL output for automation.
- doctor capability diagnostics and inspect metadata inspection.
- No Microsoft Photos private DLLs or user photos are stored in this repository.

## Screenshots

This is a command-line tool, so screenshots are not applicable. The CLI emits human-readable output by default and machine-readable JSON/JSONL with the --json option.

## Requirements

- Windows 10 22H2 or Windows 11.
- .NET 9 SDK for building.
- Microsoft Photos.
- Microsoft Raw Image Extension, installed through Microsoft Store or the supported Windows package mechanism.
- x64 or ARM64 Windows matching the selected runtime identifier.

Photos and the Raw Image Extension are third-party platform components. Their installed versions affect rendering output and available private contracts.

## Installation

Clone the repository and install the local Worker package from an elevated PowerShell session when required by AppX registration:

~~~powershell
pwsh -NoLogo -NoProfile -File ./scripts/install-worker.ps1 -Configuration Release -RuntimeIdentifier win-x64
~~~

Use win-arm64 on ARM64 Windows. The installer discovers the matching Photos bridge DLL locally; it does not download or redistribute Microsoft binaries.

## Usage

~~~powershell
dotnet run --project ./src/RawPreview.Cli -- doctor --json
dotnet run --project ./src/RawPreview.Cli -- inspect '<ARW_FILE>' --json
dotnet run --project ./src/RawPreview.Cli -- export '<SOURCE_DIRECTORY>' --output '<OUTPUT_DIRECTORY>' --quality 95 --json
~~~

Published executable usage:

~~~powershell
rawpreview.exe doctor
rawpreview.exe export '<SOURCE_DIRECTORY>' --output '<OUTPUT_DIRECTORY>' --quality 95 --overwrite
~~~

The export keeps the source stem, writes lowercase .jpg, preserves portrait pixel dimensions, validates EXIF Orientation, and reports one JSONL result per input when --json is selected.

## Build Instructions

~~~powershell
dotnet restore RawPreview.sln
dotnet build RawPreview.sln --configuration Release
dotnet test RawPreview.sln --configuration Release --no-build
dotnet publish ./src/RawPreview.Cli/RawPreview.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o artifacts/publish/cli-win-x64
~~~

The Worker must be installed from the same build when testing a local release. Do not place Photos DLLs, WinMD files, ARW files, or generated images in the repository.

## Project Structure

~~~text
src/RawPreview.Cli       Public CLI, batch policy, metadata and JPEG validation
src/RawPreview.Shared    Versioned JSONL worker protocol
src/RawPreview.Worker    Windows Photos discovery and WinRT/ABI adapter
tests/RawPreview.Tests   Offline unit and allocation tests
tests/RawPreview.IntegrationTests  Optional local Photos integration tests
scripts/                  Worker installation and repository boundary checks
packaging/                Minimal full-trust AppX manifest
.github/                  CI, CodeQL, Dependabot and contribution templates
docs/                     Design and simplification audit
~~~

## Roadmap

- Add reproducible real-Photos portrait fixtures to local CI without publishing user media.
- Expand capability negotiation for future Photos and Raw Image Extension versions.
- Measure and document end-to-end export throughput across supported Windows architectures.
- Add a signed, reproducible release artifact when packaging and signing policy is finalized.

## Contributing

Read CONTRIBUTING.md before opening an issue or pull request. Changes should preserve the CLI/Worker boundary, avoid bundling Microsoft payloads, include focused tests, and pass the local quality gates.

## License

The original code in this repository is licensed under the MIT License. Microsoft Photos, Windows Runtime components, and the Raw Image Extension are not part of this license and remain governed by their respective terms.

## FAQ

### Why is this Windows-only?

The renderer depends on Windows Photos private WinRT contracts and Windows RAW support. macOS and Linux do not provide that same backend.

### Why are Microsoft DLLs absent?

They are discovered from the local Photos installation at setup time. Keeping them out of the repository avoids redistributing platform-private binaries and keeps releases portable across Photos versions.

### Does this alter the RAW source?

No. The CLI reads source metadata and writes a new JPEG. It does not modify the ARW file.

## Acknowledgements

- Microsoft Photos and Windows RAW support for the rendering backend used by the Worker.
- The .NET and MSTest teams for the runtime and test infrastructure.

## Disclaimer

This software is provided AS IS, without warranty of any kind, express or implied. To the fullest extent permitted by applicable law, the authors and contributors are not liable for any direct, indirect, incidental, special, consequential, or other damages arising from or related to the software or its use. You use the software at your own risk and are responsible for validating exported files and complying with all applicable laws, licenses, platform terms, and privacy requirements. Do not use this project for unlawful activity, infringement, unauthorized access, or processing data that you are not permitted to process.

Microsoft, Windows, Microsoft Photos, and related marks belong to Microsoft. This project is independent of and not endorsed by Microsoft.
