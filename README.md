# tafs for Windows

The tafs desktop app for Windows 10 and 11: the same product, account and words as the iPad app.
Native: C# · .NET 8 · WinUI 3.

It draws what tafs cloud sends. Nothing in this first version runs actions on the computer.

## Layout

| Folder | What |
| :- | :- |
| `src/Tafs.Core` | Portable logic: API, contracts, pairing, approvals, words. No UI. |
| `tests/Tafs.Core.Tests` | xunit tests, each tagged with its part. |
| `tools/Tafs.Ci` | CI helper: parts, pin sync, drift check. |
| `ci/parts.json` | Which paths belong to which test part. |
| `shared-pin.json` | The commit of the shared repository the words and contracts are read at. |
| `docs/` | One page per area. Start with [docs/ci.md](docs/ci.md). |

## Run the tests

```
dotnet run --project tools/Tafs.Ci -- sync          # words and contracts at the pin, into shared/
dotnet run --project tools/Tafs.Ci -- test all      # or one part: test approvals
```

Requires the .NET 8 SDK and git.
