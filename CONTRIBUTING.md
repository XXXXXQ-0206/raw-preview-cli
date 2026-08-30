# Contributing to RawPreview CLI

Thank you for contributing. Please keep changes focused on the Windows ARW-to-JPEG workflow and preserve the boundary between the public CLI and the Photos-dependent Worker.

## Before You Start

- Check existing issues and pull requests before opening a duplicate.
- For a security issue, follow SECURITY.md instead of opening a public issue.
- Never upload ARW files, generated images, Photos DLLs, WinMD files, crash dumps, credentials, or machine-specific logs.

## Development Setup

Install the .NET 9 SDK and, for integration work, Microsoft Photos and Raw Image Extension on Windows. Run:

~~~powershell
dotnet restore RawPreview.sln
dotnet build RawPreview.sln --no-restore
dotnet test RawPreview.sln --no-restore
~~~

The real Photos export integration test is opt-in through the RAWPREVIEW_TEST_ARW environment variable; keep that path local and out of commits.

## Pull Requests

1. Create a branch from main.
2. Make the smallest change that solves the problem.
3. Add or update focused tests for behavior and failure paths.
4. Run the build, test suite, repository boundary scan, and any relevant publish command.
5. Update both README languages when user-facing behavior changes.
6. Use a clear imperative commit subject, for example: fix: preserve portrait orientation in export.

Pull requests should explain the problem, the design choice, validation performed, and any Windows or Photos-version dependency. Do not claim real Photos coverage when the integration fixture was skipped.

## Design Constraints

- Keep Photos private ABI and WinRT ownership logic isolated in src/RawPreview.Worker/Photos.
- Do not redistribute Microsoft binaries.
- Preserve atomic output, metadata validation, cancellation, and per-file failure isolation.
- Prefer focused simplification backed by tests and measurements over line-count reduction.

## Style

Follow .editorconfig, use nullable reference types, keep warnings as errors, and avoid unrelated formatting churn.
