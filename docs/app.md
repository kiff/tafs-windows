# The Windows app (`src/Tafs.App`)

WinUI 3 on .NET 8, unpackaged (`WindowsPackageType=None`): team builds only, until the signing
certificate is decided. It builds with plain `dotnet build`, no Visual Studio:

```
dotnet run --project tools/Tafs.Ci -- sync
dotnet build src/Tafs.App -c Release -p:Platform=x64
```

The build refuses to start without the words: they're copied from `shared/` (the pin) into the app's
`Strings/` folder, never into the repository.

## The shell

One sidebar for the whole app, as on the iPad ([the sidebar and the Overview](home.md)):
- the mascot and "tafs", which open the Overview;
- Needs you, Record, Calendar, Plan & usage, Connect apps;
- Your tafs;
- Coach and Account at the foot.

The chosen screen opens beside it. The colours are the iPad's `Theme.swift`, light and dark (`Theme.xaml`), and they follow Windows' setting. The open screen's row is heavier, with no fill, as on the owner's iPad.

Built so far:
- [Sign in with KIFF](sign-in.md);
- the sidebar and the Overview's empty state, as on the iPad ([home](home.md));
- Add this Windows PC, with its view-only notice ([pairing](pairing.md));
- Needs you's cards, as on the iPad ([needs you](needs-you.md)).

Signing in is the only screen that reaches KIFF so far. Other screens show only their title until they're built, in the order of the desktop spec. 

## Language

The app speaks the first of the person's Windows languages that tafs ships (`AppLanguage.Pick`), otherwise English. Every line comes from `WordBook`: nothing is written in code.

## Options

| Option | What |
| :- | :- |
| `--lang fr` | speak a shipped language |
| `--theme light` or `dark` | force the appearance |
| `--page pair`, `sign-in` or `needs-you` | open Add this Windows PC, Sign in with KIFF, or Needs you |
| `--view-only` | show the session as view-only |
| `--sample` | show the iPad's two sample held calls in Needs you |
| `--screenshot out.png` | save the window (1280×800) as a PNG and quit |

CI's `app` job builds the app on Windows when `src/Tafs.App/**` changes, and nightly. It saves sign-in, the Overview, the view-only notice, the pairing screen and Needs you (with the samples) in en, fr and pt, light and dark, as the `screenshots` artifact of the run. The job fails if any of the 30 is missing: a crash at launch, or a word missing at the pin, stops the app before it saves.
