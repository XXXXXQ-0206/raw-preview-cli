# Security Policy

## Supported Versions

Security fixes are applied to the latest release and the default branch. Older releases may not receive fixes.

## Reporting a Vulnerability

Please do not open a public issue for a suspected vulnerability. Use GitHub private vulnerability reporting when it is enabled for this repository. If private reporting is unavailable, contact the repository maintainers through the GitHub security contact shown on the repository page and include:

- affected version or commit;
- operating system and architecture;
- a minimal reproduction or precise trigger;
- impact and any required Photos or Raw Image Extension version;
- logs with paths, usernames, credentials, and personal media removed.

Allow maintainers reasonable time to investigate before public disclosure. Do not submit ARW images or Microsoft private binaries unless specifically requested through a private channel.

## Security Design Notes

- The CLI passes structured arguments to a local Worker and uses a named pipe for the package path.
- Output is written to a unique partial file and validated before the final move.
- Repository boundary checks reject media, Photos payloads, and machine-specific paths.
- Microsoft Photos and Raw Image Extension remain external dependencies and are not redistributed.
