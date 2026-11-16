# Words and contracts

Both come from the shared repository at the commit in `shared-pin.json` (see [ci.md](ci.md)).
Run `dotnet run --project tools/Tafs.Ci -- sync` first: they land in `shared/`, never in git.

## Words (`Tafs.Core.Words`)

`WordBook.Load(stringsRoot, language)` reads `app/strings/<language>/*.json`.

- `Get(key, ("name", value)...)` fills `{name}`, `{name|possessive}`, and `{name|this}` or `{name|This}`
  ("this" before the name, `This` to open a sentence). A form it doesn't read is an error, like a missing value.
- `Count(key, n, ...)` picks the plural form the language gives `n` and fills `{count}` too.
- A missing key or a missing value **throws**. There is no English fallback and no `{hole}` on screen.
- `PluralRules` has the CLDR forms for whole numbers in the 8 shipped languages (Polish has
  one/few/many/other).
- The possessive rule exists in English and German only. Other languages keep the name and word the
  line around it; a test fails if one of their lines uses `|possessive`.
- The "this" rule exists in French only (`THIS` in the shared generate.py): "ce PC", "cet appareil". Other
  languages write their own word; a test fails if one of their lines uses `|this`. Another fills every line
  at the pin, in every language, and fails on any brace left, so a new form fails CI before the pin moves.

The `strings` part checks, for each shipped language: every English key is there and no other, each
line fills the same values as English, each count has the forms its numbers ask for.

Nothing is translated here. A new key goes into the shared repository first, then the pin moves.

Typed accessors (a key that isn't there doesn't compile) come with the first screen that reads words:
they are generated from the same files.

## Contracts (`Tafs.Core.Contracts`)

`ContractFile` reads a fixture; `Answers.Decode<T>` decodes it with **this app's own types**
(`Answers.cs`). A field a type requires and an answer lacks fails the decode, so a change in what
tafs cloud answers fails the `contracts` part on the pin bump, before any screen. Fields the app
doesn't use are ignored.

Covered today: the AI-data consent (200, 403, 409), Coach's first start (with and without the
batch), the time zone of a new account, and Home's part of the recipes. Each new fixture in the
shared repository gets its type and a test here when a screen needs it.
