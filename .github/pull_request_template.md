## Summary

Describe the problem and the smallest safe change that solves it.

## Verification

- [ ] `dotnet build WUBT10Fix.slnx -c Release -warnaserror`
- [ ] `dotnet test WUBT10Fix.slnx -c Release --no-build`
- [ ] Elevated changes are detected, scoped, verified, and restored on failure
- [ ] No driver, service, startup item, telemetry, or secret was added

## Device impact

List the Windows build, Bluetooth/audio chipset, Casio model, and before/after
device state used for testing.
