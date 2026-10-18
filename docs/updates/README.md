# Release manifest

`update.json` is the manifest the desktop client reads for its update check
(Settings → Updates). The default URL is

```
https://raw.githubusercontent.com/VY1-pick/XenMothon/main/docs/updates/update.json
```

and can be repointed at any HTTPS URL in Settings.

## Shape

| Field | Required | Meaning |
| --- | --- | --- |
| `version` | yes | Release version, pre-release labels allowed (`0.9.0-beta.4`) |
| `downloadUrl` | yes | Absolute HTTPS URL of the release page or installer |
| `releaseNotesUrl` | no | Absolute HTTPS URL of the release notes |
| `publishedAtUtc` | no | ISO 8601 timestamp |
| `isMandatory` | no | Reserved; the client only notifies and never installs |

Version comparison is numeric first and then pre-release aware, so `0.9.0-beta.4` supersedes
`0.9.0-beta.3` and any pre-release sorts below its final release. Clients never install anything: the
manifest can only produce a tray notification and the status line in Settings.

## Publishing a new beta

1. Publish the GitHub release, then copy its URL.
2. Update `version`, `downloadUrl`, `releaseNotesUrl` and `publishedAtUtc` here.
3. Commit and push to `main` so the raw URL serves the new manifest.

## Public hosting

Beta 05 uses this public repository's raw HTTPS URL. The manifest and release assets can be read without repository credentials. If a future fork is private, point the update setting at a separate public HTTPS manifest; the desktop client does not send GitHub account credentials.

Catalog hosting remains a separate configuration: place the catalog and its relative media on a reachable HTTPS host.
