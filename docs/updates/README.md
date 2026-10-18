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

## Private repository caveat

This repository is private, so `raw.githubusercontent.com` and release assets require authentication.
An update check from a private desktop build therefore fails and the client logs it quietly, leaving
the user on "the check failed" in Settings. To make update notifications work for testers, either

- point the manifest URL at any public HTTPS location (a static site, object store or the public CDN
  used for the catalog), or
- make the repository public, or
- attach the manifest to the release and give testers a URL they can reach.

The same constraint applies to the catalog: a private repository cannot serve the static
`catalog.json` to unauthenticated clients.
