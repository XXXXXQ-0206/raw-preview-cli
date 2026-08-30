# Changelog

All notable changes to this project are documented here.

The format follows Keep a Changelog, and releases use Semantic Versioning.

## [1.0.0] - 2026-08-30

### Added

- Windows CLI for exporting Sony ARW files through the installed Photos resize path.
- doctor, inspect, export, and setup-raw commands.
- Portrait-aware metadata handling, EXIF Orientation validation, atomic output, and JSONL results.
- Separate Photos Worker with capability probing and x64/ARM64 publishing.
- Offline unit tests, optional Photos integration tests, repository boundary checks, CodeQL, CI, and Dependabot configuration.

### Changed

- JPEG validation uses a streaming reader instead of loading the whole output into a managed byte array.
- Photos support modules are loaded once per Worker process and released at item boundaries where required.

### Known Limitations

- Real Photos integration requires a local Photos installation, Raw Image Extension, Windows App Runtime, and an ARW fixture.
