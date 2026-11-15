# CI, parts and the shared pin

## Words and contracts come from one pinned commit

The words (`app/strings/<lang>/*.json`) and the contract fixtures (`contracts/*.json`) belong to
the shared repository. This app never copies them by hand and never commits them.

- `shared-pin.json` names the repository, a **full 40-character commit sha** (never a branch or a
  tag) and the paths to read.
- `dotnet run --project tools/Tafs.Ci -- sync` fetches those paths at that commit into `shared/`
  (ignored by git). It does nothing when `shared/` is already at the pin.
- **Bumping the pin is one small PR** that changes only `shared-pin.json`. It runs the strings and
  contracts parts, which are the ones that read it.

### The drift check

The `pin-drift` job fails when main of the shared repository is **more than 20 commits or more than
7 days** ahead of the pin. "Days behind" is the time between the pinned commit and main's latest
commit; a pin that is at main is never behind, however old that commit is. The job runs on every PR
and push, and nightly, so a stale pin turns the nightly run red even when nobody pushes.

```
dotnet run --project tools/Tafs.Ci -- drift
```

## Tests are grouped by part

A test says which part it belongs to with `[Part("approvals")]` on its class or method.

| Part | What it covers |
| :- | :- |
| pairing | adding a computer, the view-only sign-in |
| approvals | the signed action digest, the approval window |
| home | the sidebar and the Overview |
| coach | Coach chat, creating a taf, connecting an app |
| record | the Record and a taf's detail |
| connections | connection rows, health, actions |
| calendar | Calendar, Plan and usage |
| account | Account, Devices, Legal, sign out |
| strings | the words: every key, every shipped language |
| contracts | the pinned contract fixtures |
| ci | this tooling: pin, drift, part selection |

`ci/parts.json` says which paths each part owns. A test that names a part missing from that file
fails (`Every_part_named_on_a_test_is_declared`), so a typo can't hide a test.

### What a change runs

- The parts whose paths it touches, **plus strings and contracts, always**.
- **Everything**, when it changes something build-wide (`*.csproj`, `Directory.Build.props`,
  `global.json`, the solution, `.github/`), or code under `src/`, `tests/` or `tools/` that no part
  owns. A new folder therefore errs on the side of running too much until it is added to
  `ci/parts.json`.
- Docs, images and the pin file touch no part of their own.

The `plan` job works this out with `Tafs.Ci parts --base <ref>`, then `test` runs one job per part.

### Running one part alone

On a laptop:

```
dotnet run --project tools/Tafs.Ci -- test approvals
dotnet run --project tools/Tafs.Ci -- test pairing record
dotnet run --project tools/Tafs.Ci -- test all
```

On GitHub: Actions → CI → Run workflow → `part: approvals` (or `all`). Leave it empty to run the
parts the branch changes.

A part with no tests yet passes with a "no test matches" note; the parts fill in as screens land.

## The full suite

Runs nightly (02:17 UTC) and is what a release is cut from (a release job joins this workflow when
packaging lands). `test all` runs **with no filter**, so even a test that names no part runs there.
That is a backstop: `Every_test_names_its_part` already fails, naming the method, when a `[Fact]` or
`[Theory]` has no `[Part]` on itself or its class, because no part's job would ever run it.

A dispatched `part` goes through `Tafs.Ci parts --only`, which accepts only a declared part or `all`.
The actions are pinned by commit sha, with the version in a comment.

## Budget

The organisation shares 20 parallel jobs. This repo uses no macOS job. Part jobs run on Linux while
they only need the portable core; jobs that need Windows (the app build, the TPM key) will be a
separate job that runs only when its paths change. NuGet packages are cached: every run restores,
only main saves.
