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

The chosen screen opens beside it. The colours are the iPad's `Theme.swift`, light and dark (`Theme.xaml`), and they follow Windows' setting. The chosen row is a rounded fill, as on the iPad.

The shared controls are the iPad's:
- **An outline button** (Pause, Connect, Add this Windows PC, Revoke) is the iPad's `OutlineLook`: its edge in the ink at 40% (`TafsInkLine`), 48 high. Revoke's edge is the red at 40% (`TafsNoLine`).
- **A segmented control** (Day / Week / Month, Last / This period, Theme, a taf's tabs) is the system's: a capsule track in the system grey (radius 17), and a capsule segment (radius 15). In dark the chosen segment is the system's #636366, so it stands out from the track.

Built so far:
- [Sign in with KIFF](sign-in.md), its session kept across launches ([the session](session.md));
- the sidebar and the Overview's empty state, as on the iPad ([home](home.md));
- Add this Windows PC, with its view-only notice ([pairing](pairing.md));
- Needs you's cards, as on the iPad ([needs you](needs-you.md));
- the Record, as on the iPad ([record](record.md));
- Calendar, Day, Week and Month, as on the iPad ([calendar](calendar.md));
- Plan & usage, as on the iPad ([plan & usage](usage.md));
- Connect apps, as on the iPad ([connect apps](connect-apps.md));
- Account, as on the iPad ([account](account.md));
- Coach mode, as on the iPad ([coach](coach.md));
- a taf's page, on its Activity, Card and Setup, as on the iPad ([a taf's page](taf.md)).

Signing in is the only screen that reaches KIFF so far. Other screens show only their title until they're built, in the order of the desktop spec. 

## Language

The app speaks the first of the person's Windows languages that tafs ships (`AppLanguage.Pick`), otherwise English. Every line comes from `WordBook`: nothing is written in code.

## Options

| Option | What |
| :- | :- |
| `--lang fr` | speak a shipped language |
| `--theme light` or `dark` | force the appearance |
| `--page pair`, `sign-in`, `needs-you`, `record`, `calendar`, `calendar-week`, `calendar-month`, `usage`, `connect-apps`, `connect-connections`, `account`, `coach`, `coach-chats`, `taf`, `taf-setup` or `taf-card` | open Add this Windows PC, Sign in with KIFF, Needs you, the Record, Calendar in Day, Week or Month, Plan & usage, Connect apps (on its first app's connections with `connect-connections`), Account, Coach mode over the Overview (on its Previous conversations with `coach-chats`), or the sample's Refunds on its Activity, Setup or Card (`taf`, `taf-setup`, `taf-card`, with `--sample`) |
| `--view-only` | show the session as view-only |
| `--sample` | show the iPad's sample held calls, Record, Plan & usage and Connect apps |
| `--home contract.json` | read Home's list from a contract file (`shared/contracts/home.json`) in place of tafs cloud, and each taf's runs, report, Activity and Setup from the contracts beside it (Your brief from a `brief.json` beside it, if there is one); with `--page taf` or `taf-setup`, open the first taf there |
| `--screenshot out.png` | save the window (1280×800) as a PNG and quit |

CI's `app` job builds the app on Windows when `src/Tafs.App/**` changes, and nightly. It saves sign-in, the Overview, Needs you, the Record, Plan & usage, Connect apps, Account and a taf's Activity, Setup and Card (with the samples), Coach mode (with the sample, and Coach's thread from the contracts), Calendar in Day and Week, the view-only notice and the pairing screen in en, fr and pt, light and dark, as the `screenshots` artifact of the run. The job fails if any of the 114 is missing: a crash at launch, or a word missing at the pin, stops the app before it saves.
