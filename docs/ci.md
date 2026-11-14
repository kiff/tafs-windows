# CI and the shared pin

## Words and contracts come from one pinned commit

The words (`app/strings/<lang>/*.json`) and the contract fixtures (`contracts/*.json`) belong to
the shared repository. This app never copies them by hand and never commits them.

- `shared-pin.json` names the repository, a **full 40-character commit sha** (never a branch or a
  tag) and the paths to read.
- `dotnet run --project tools/Tafs.Ci -- sync` fetches those paths at that commit into `shared/`
  (ignored by git). It does nothing when `shared/` is already at the pin.
- **Bumping the pin is one small PR** that changes only `shared-pin.json`.

## The drift check

The `pin-drift` job fails when main of the shared repository is **more than 20 commits or more than
7 days** ahead of the pin. "Days behind" is the time between the pinned commit and main's latest
commit; a pin that is at main is never behind, however old that commit is. The job runs on every PR
and push, and nightly, so a stale pin turns the nightly run red even when nobody pushes.

```
dotnet run --project tools/Tafs.Ci -- drift
```

## What runs

| Job | When | What |
| :- | :- | :- |
| `test` | every PR and push to main, and nightly | sync the pin, run the tests |
| `pin-drift` | the same | the drift check above |

Tests are split by part in the next change; this page grows with it.

NuGet packages are cached: every run restores, only main saves. The organisation shares 20 parallel
jobs and this repo uses no macOS job.
