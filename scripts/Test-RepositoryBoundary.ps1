param(
    [string]$Root = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$Root = [System.IO.Path]::GetFullPath($Root)
if (-not [System.IO.Directory]::Exists($Root)) {
    throw "Repository root was not found: $Root"
}

$ignoredDirectoryPattern = '[\\/](?:\\.git|bin|obj|artifacts|runtime-receipts)[\\/]'
$forbiddenNamePattern = '(?i)(?:\\.arw|\\.jpe?g|\\.png|Photos.*\\.dll|Photos.*\\.winmd|msRAWImage_store\\.dll|Microsoft\\.RawImageExtension.*\\.appx)$'
$forbiddenTextPatterns = @(
    '(?i)[A-Za-z]:\\Users\\',
    ('(?i)\\Windows' + 'Apps\\')
)

$violations = [System.Collections.Generic.List[string]]::new()
$files = Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object {
    $_.FullName -notmatch $ignoredDirectoryPattern
}
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($Root.Length).TrimStart([char]92, [char]47)
    if ($relativePath -match $forbiddenNamePattern) {
        $violations.Add("Forbidden repository payload: $relativePath")
        continue
    }
    if ($file.Extension -notin '.cs', '.csproj', '.props', '.targets', '.ps1', '.md', '.json', '.xml', '.yml', '.yaml') {
        continue
    }
    if ($relativePath -like 'docs\\superpowers\\*' -or $relativePath -eq 'scripts\\Test-RepositoryBoundary.ps1') {
        continue
    }
    foreach ($pattern in $forbiddenTextPatterns) {
        if (Select-String -LiteralPath $file.FullName -Pattern $pattern -Quiet) {
            $violations.Add("Machine-specific path in repository text: $relativePath")
            break
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    throw "Repository boundary verification failed with $($violations.Count) violation(s)."
}

Write-Output "Repository boundary verification passed: $($files.Count) files scanned."
