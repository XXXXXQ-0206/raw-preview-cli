## Summary

<!-- What problem does this change solve? -->

## Design

<!-- Explain the relevant CLI/Worker or packaging boundary. -->

## Validation

- [ ] dotnet build RawPreview.sln --no-restore
- [ ] dotnet test RawPreview.sln --no-restore
- [ ] scripts/Test-RepositoryBoundary.ps1
- [ ] Relevant real Photos integration test, or explain why it was skipped

## Release and Privacy Check

- [ ] No ARW, JPEG, PNG, Photos DLL, WinMD, credential, personal path, or private log is included.
- [ ] Both README languages are updated when user-facing behavior changed.
