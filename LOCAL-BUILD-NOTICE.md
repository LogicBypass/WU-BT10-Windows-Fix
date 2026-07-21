# Local build notice

The executables in the locally generated audited package folder are
self-contained development builds and are **not Authenticode-signed**. Their
SHA-256 hashes are provided so the files can be checked for accidental changes.

Do not represent these local files as a signed public release. A production
release must be built by the included GitHub Actions release workflow with a
verified Azure Artifact Signing identity (or another trusted Authenticode
certificate), RFC 3161 timestamped, and checked for a `Valid` signature before
publication. See `docs/SIGNING.md`.
