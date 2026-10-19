# Release manifest

Settings → Updates reads the public GitHub Releases API by default:

```
https://api.github.com/repos/VY1-pick/XenMothon/releases?per_page=100
```

The client selects the highest release version with an uploaded `XenMotion-<version>-Setup-x64.exe` asset. Published betas are included; draft releases and unfinished uploads are ignored. The first 100 releases are checked. No GitHub account or token is sent.

The previous built-in raw `update.json` URL and blank sources migrate to this default. Custom HTTPS manifest URLs and the user's disabled-update preference are preserved. `update.json` remains available for older builds and custom manifest hosting.

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

1. Publish a GitHub release with its version tag and uploaded Windows x64 installer. New builds detect it directly.
2. Also update `update.json` for older builds that still read the legacy manifest.

## Public hosting

The published Beta 05 installer still reads the raw HTTPS manifest. 0.1.2 Beta uses GitHub Releases directly. Both sources and the release assets are public. A private fork needs a separately accessible HTTPS source.

Catalog hosting remains a separate configuration: place the catalog and its relative media on a reachable HTTPS host.

## Numbering reset

`0.1.2-beta` follows the historical `0.9.0-beta.*` series. The GitHub update source explicitly orders that legacy series before the new versions; the generic semantic-version parser and custom manifest ordering are unchanged. Published Beta 05 binaries need a one-time manual install because they compare numeric versions only.
