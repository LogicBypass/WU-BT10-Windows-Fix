# Explorer packaging incident

Status: root cause identified; trusted signing remains a release blocker

## Incident

The unsigned x64 self-contained single-file build supplied for second-PC
testing was approximately 178 MiB and embedded a custom nine-frame,
PNG-compressed ICO resource. The user reported that Explorer crashed merely
while the executable was present and later encountered access/permission errors
while trying to handle the file.

That executable and all local variants were renamed with a `.quarantined`
extension. They are not release candidates.

## What is known

- The application source and hardware switching tests passed on the development
  PC.
- All ICO frames decoded locally, and the development PC did not reproduce the
  Explorer crash.
- The executable was not Authenticode signed.
- A compressed single-file variant was smaller but made associated-icon
  extraction materially slower locally.
- A framework-dependent build was smaller but would require a separately
  installed .NET Desktop Runtime.
- No faulting Explorer module or exception code was recovered from the affected
  PC, so the icon cannot be stated as the proven root cause.

## Root cause confirmed on 2026-07-24

The icon-free v2.1.0 candidate reproduced the reported access error when placed
on removable FAT32 drive `E:`. The executable hash still matched the validated
build, permissions allowed access, no Mark-of-the-Web stream existed, and
Defender reported no malware detection.

Microsoft Defender Operational event 1121 identified the actual enforcement:

- ASR rule ID: `B2B3F03D-6A65-4F7B-A9C7-1C7EF74A9BA4`;
- configured action: `1` (Block);
- rule: block untrusted and unsigned processes that run from USB;
- blocked path: `E:\WU-BT10-Windows-Fix-v2.1.0-win-x64-TEST.exe`;
- initiating process: `explorer.exe`.

A direct PowerShell launch reproduced `Access is denied` and created another
event 1121. No corresponding Explorer Application Error event was present. A
copy of that USB-origin executable remained blocked after copying back to local
storage, while the original local file with the same SHA-256 still launched.
This isolates Defender USB-origin enforcement rather than file corruption,
filesystem permissions, the removed icon, or application startup.

The security rule was not weakened. A transport ZIP containing the single EXE
was copied to `E:`, copied back to local storage, extracted locally, hash
verified, and launched successfully. Controlled multi-PC testing must therefore
transport the ZIP, copy it to a fixed local drive, and extract it there. Direct
execution of an unsigned EXE from USB is unsupported. A trusted Authenticode
signature remains the proper production solution.

## Current mitigation

- The custom ICO is no longer assigned or tracked as an executable resource.
- The in-app PNG logo remains an embedded managed resource.
- Compression is not enabled.
- No candidate is labeled a release until Explorer enumeration is tested on a
  clean second PC.

## v2.1.0 x64 multi-PC test candidate

The icon-free, uncompressed, untrimmed, self-contained single-file candidate
was built on 2026-07-24:

- filename: `WU-BT10-Windows-Fix-v2.1.0-win-x64-TEST.exe`;
- size: 186,442,778 bytes;
- SHA-256:
  `CE7765ED13CDBA39C86557CD42E3B4136E8EC463DA0EDB18A82AC49AF70E97B5`;
- exactly one published file, with no separately installed .NET runtime
  required;
- copied-file hash matched the publish output;
- Windows Shell metadata enumeration and icon extraction passed;
- read-only command-line diagnosis passed from the copied path;
- GUI startup, diagnosis, and normal close passed as a standard user;
- GUI startup logged no automatic Audio or MIDI connection;
- all 49 Release tests passed;
- local Microsoft Defender scan passed with current signatures.

This candidate is unsigned and is for controlled multi-PC testing, not public
release. SmartScreen may warn on another PC. The candidate must be transported
inside `WU-BT10-Windows-Fix-v2.1.0-win-x64-TEST.zip`, copied to a fixed local
drive, and extracted there. The clean-PC Explorer, Defender, and functional
checks below remain the release gate.

## Required evidence before release

- Explorer Application Error event, if another crash occurs, including faulting
  module and exception code.
- Standard-user copy, rename, delete, Properties, context-menu, and launch.
- Defender/SmartScreen result.
- Authenticode status and SHA-256.
- Clean-PC Audio/MIDI functional test.

If the shell still crashes without the custom icon, the distribution format
must change to a signed installer or a ZIP containing a normal self-contained
folder. A raw giant executable is not a requirement worth keeping at the cost
of Explorer reliability.
