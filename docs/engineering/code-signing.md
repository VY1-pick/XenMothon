# Signing the XenMotion installer

The public beta installer is unsigned, so Windows shows a publisher/reputation warning and some
corporate machines block it outright. This document is the exact procedure to remove that warning.
Nothing here is implemented yet because it needs a paid, identity-verified certificate.

## 1. What signing fixes — and what it does not

| Fixed | Not fixed |
| --- | --- |
| Publisher name shown as "XenMotion" instead of "Unknown publisher" | SmartScreen warnings for a brand-new *standard* (OV) certificate; they fade with download volume |
| Tamper detection: a modified installer fails signature validation | Antivirus false positives coming from the app's behaviour |
| Enterprise allow-listings that require a valid Authenticode signature | Per-file hashes: our `SHA256SUMS.txt` still matters for integrity checks |

The signature proves *who published* the file. It never replaces the checksum, and it does not make
the app's own behaviour safer.

## 2. Choose a certificate type

| Option | Cost (2026, approximate) | Lead time | Notes |
| --- | --- | --- | --- |
| **Azure Trusted Signing** (recommended) | ~$10/month | 1–3 weeks identity validation | No hardware token, short-lived certificates are issued automatically, best fit for an automated release script |
| **Standard OV code signing** | ~$200–400/year | 3–10 business days | Private key on a hardware token or in a cloud HSM. SmartScreen reputation builds over time |
| **EV code signing** | ~$300–600/year | 1–3 weeks | Immediate SmartScreen reputation, but requires a hardware token in most CAs' processes |

Prerequisites for any of them:

1. Legal identity to validate — a registered company, or a sole trader / individual where the CA
   supports individual validation (expect a government ID and a selfie check).
2. A phone number and email address that the CA can verify.
3. Confirmation that the certificate's subject name will be **XenMotion** or the legal entity name;
   the publisher name Windows shows comes from the certificate subject.

> Store the certificate, its password and any cloud-HSM credentials **outside this repository**.
> `.gitignore` already excludes the artifact folders; never add a `.pfx` or a secret to the repo.

## 3. Sign the release locally

Signed certificates must cover the installer first, and ideally the application executables too.

```powershell
# 1. Build the release exactly as usual (publish + Inno Setup).
./tools/Build-Release.ps1

# 2. Sign the published binaries with a standard OV certificate in the current-user store.
#    (Azure Trusted Signing uses its own CLI; see the link at the end.)
./tools/Sign-Release.ps1 `
    -PublisherPath 'artifacts\publish\0.9.0-beta.5' `
    -InstallerPath 'artifacts\releases\0.9.0-beta.5\XenMotion-0.9.0-beta.5-Setup-x64.exe' `
    -Thumbprint '<certificate thumbprint>'

# 3. Verify before publishing.
signtool verify /pa /v 'artifacts\releases\0.9.0-beta.5\XenMotion-0.9.0-beta.5-Setup-x64.exe'
Get-AuthenticodeSignature 'artifacts\releases\0.9.0-beta.5\XenMotion-0.9.0-beta.5-Setup-x64.exe' |
    Format-List Status, SignerCertificate
```

`tools/Sign-Release.ps1` signs `XenMotion.exe` and the native assemblies under `QualityProbe` and
`Runtime` first, then the installer, always with SHA-256 and an RFC 3161 timestamp
(`http://timestamp.digicert.com`). Timestamping is what keeps the signature valid after the
certificate expires. Re-run `tools/Verify-Release.ps1` afterwards: it re-hashes every file, so run it
**after** signing, or the checksums will not match.

## 4. Wire it into the release pipeline

The release script already stops at "compile the installer". To make signing automatic:

1. Add a `-Sign` switch to `tools/Build-Release.ps1` that calls `tools/Sign-Release.ps1` with the
   thumbprint from an environment variable (`$env:XENMOTION_SIGN_THUMBPRINT`), not a literal.
2. Issue the certificate on a machine or CI runner that holds the private key; the build machine only
   needs the thumbprint and read access to the key.
3. Regenerate `SHA256SUMS.txt` after signing, then re-run `tools/Verify-Release.ps1`.
4. Keep the old workflow (unsigned) available for local test builds, and never sign a build that has
   not passed the test suite.

## 5. Timeline to a clean first-run experience

| Week | Work |
| --- | --- |
| 1 | Start identity validation with the CA or Azure Trusted Signing; continue beta work in parallel |
| 2 | Certificate issued; run `Sign-Release.ps1` on the current release; verify with `signtool` |
| 3 | Publish the signed installer; confirm the publisher name and that no "Unknown publisher" dialog appears |
| Later | SmartScreen reputation settles as download volume grows (immediate for EV / Trusted Signing) |

## 6. Why the app itself cannot self-update unsigned

XenMotion currently only *notifies* about new releases; it never downloads or installs one. If an
auto-updater is added later, the updater binary must be signed and must verify the signature of the
downloaded payload, otherwise the update channel becomes a remote-code-execution path. That is a
prerequisite for automatic installation, not a nice-to-have.

## References

- Azure Trusted Signing: <https://learn.microsoft.com/azure/trusted-signing/>
- `signtool` options: <https://learn.microsoft.com/windows/win32/seccrypto/signtool>
- Authenticode timestamping: <https://learn.microsoft.com/windows/win32/seccrypto/time-stamping-authenticode-signatures>
