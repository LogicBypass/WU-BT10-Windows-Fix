# Contributing

Contributions are welcome through pull requests.

1. Install the SDK version selected by `global.json`.
2. Run `dotnet restore WUBT10Fix.slnx`.
3. Run `dotnet build WUBT10Fix.slnx -c Release -warnaserror`.
4. Run `dotnet test WUBT10Fix.slnx -c Release --no-build`.

Keep repair changes narrowly scoped and reversible. New chipset-specific
workarounds must include detection, verification, and restoration behavior.
Avoid adding background services, startup tasks, analytics, or network access.

By contributing, you agree that your contribution is licensed under the MIT
License.
