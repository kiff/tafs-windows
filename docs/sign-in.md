# Signing this computer in

The same device authorization flow the phones use (KIFF's desktop client contract, §1.1). The core is
in `Tafs.Core/SignIn`, and the page is `SignInPage` in the app.

## The flow (`DeviceSignIn`)

1. **Start.** `StartBody` → `POST /v1/auth/device/start`, no auth:
   - the device key and the approval key, both standard base64 of SPKI DER;
   - the name, "tafs on this Windows PC" in the person's language;
   - `platform: windows`, `kind: other`.

   With no approval key, it sends `"approval": "passkey"` instead, and `"key_store": "software"` when
   the device key isn't in hardware. It checks the same rules as the pairing offer: P-256, two
   different keys, and a name of 1 to 64 characters.
2. **The code.** `ReadStart` returns the user code, the link and when it expires:
   - **The app opens only an `https` link on KIFF's own domain**: `kiff.dev`, or a host under it (the sign-in page is on `app.kiff.dev`), on the default port and with no user info. Anything else (`http`, `file:`, another app's scheme, `evilkiff.dev`) refuses the whole answer.
   - The device code is a secret until it's used, so it's never printed.
   - The interval is at least 5 s.
3. **The browser.** The person opens the link, signs in to KIFF, checks the code and approves.
4. **The session.** `PollAsync` → `POST /v1/auth/device/token` with the device code:
   - it waits the interval before each try, and adds 5 s on `slow_down`;
   - it stops on expiry, on `expired_token`, on a refusal, or with the session.

   The session is handed over once. Its `ToString()` never shows the token, and an unreadable answer is never echoed.

Once an account has an approval device, this session is **view-only**: it holds the device key but no
approval authority. The computer gains approval authority only by [pairing](pairing.md) with a device
that can already approve.

## The screen (`SignInScreen`)

| Step | Shows |
| :- | :- |
| Welcome | what tafs is, Sign in with KIFF, or "Already use tafs on your phone? Add this Windows PC with it." |
| Waiting | the code to check, Open KIFF to approve, and the link for another browser |
| Couldn't start / Couldn't finish | why, as KIFF's status only, and Sign in with KIFF again |
| Expired | the code expired, and Sign in with KIFF again |

Every line comes from the pinned words, and is tested in every shipped language. The tests run with
the `pairing` part.

## Signed out, the whole window (`SignedOut`, `SignedOutPage`, `ConnectedPage`)

The approved mockup (16 Nov), as the iPad's and the
Mac's `SignInView`, on tafs's shared words.

- **When:** with no session at launch, and after a sign-out, the whole window signs in, over the app; never the app with
  nothing in it. Not over the sample, nor a screenshot of another screen (`SignedOut.AtLaunch`).
- **Two halves,** side by side, stacked under 860 wide:
  - **Sign in with KIFF,** with **Continue**: KIFF's device code, checked in the browser, in this half (the sign-in page
    below, with no welcome and no "with your phone", which the other half is).
  - **Sign in with your phone,** with **Show the code** and "Scan with tafs". Windows only: the code waits for this tap, so
    Windows Hello asks for this PC's passkey then, never as the window opens. The tap opens the pairing page in this half
    ([pairing](pairing.md)): one Windows Hello, then the QR and its check code. A new code is a tap too.
  - **One at a time:** starting one half stops the other (its page goes, and with it its code or its claim), so two
    sign-ins never land together and one never replaces the other's session.
- **The drawings** are shapes only, with no word, letter or number in them (rule 14): a screen with its button and a lock,
  and a phone scanning a code. Every word is a shared key.
- **The celebration:** either way of signing in ends on it, over the app (`SignedOut.AfterSignIn`, the iPad's
  `justConnected`): the computer and the phone joined by a check, "You're connected. Let's build great things together.",
  and **Let's go**, which opens the app on its Overview. Never over the sample.
- `--page signed-out` and `--page connected` open them for screenshots.

## The page (`SignInPage`)

- **Start:**
  - it opens the device key in the TPM ([keys](keys.md)), or makes it the first time;
  - it asks for a passkey-only session, because on Windows the approval key is a Windows Hello passkey (`Features.WindowsPasskeyApprovals`). The button shows only with that flag on.
- **A refused start** shows KIFF's status and error code only (`KIFF answered 400 invalid_device_name`), never free text from the answer.
- **The code:** the browser opens on the link, the code is shown to check, and Open KIFF to approve opens it again.
- **The terms line** links tafs's Terms and Privacy policy in the app's language where kiff.dev has it, under KIFF's code (`kiff.dev/<code>/tafs/terms`; zh-Hant is `zh`, sr-Latn `sr`; pt-BR has its own lower-case `pt-br` pages, plain pt stays on `pt`; 40 codes, `SignInScreen.LegalCode`), and in English otherwise (`kiff.dev/tafs/terms`).
- **Signed in:**
  - the window opens the Overview, with the view-only notice when `approval_mode` says view (or says nothing it knows);
  - **the session is kept** across launches, sealed for this Windows user ([the session](session.md)). A session it replaces is signed out at KIFF.
- **The link opened** is https on one of KIFF's own hosts, exactly: `kiff.dev`, `app.kiff.dev` or the API's.
- **Tests:** checks that create state never run against KIFF's production API. They run against a local or CI KIFF.

## The welcome's consent (`Onboarding`, `SignedOutPage`)

As the iPad's `OnboardingView` (kiff/tafs `docs/onboarding.md`), under both halves of the signed-out window:

- **One tick:** "I agree to KIFF's Terms of Service and the tafs Terms", each name a link to its page in the app's language (`Onboarding.ConsentTick`; KIFF's own pages are `kiff.dev/<code>/terms` and `/privacy`). The box is its own control, so a link never ticks it.
- **Privacy as two links,** KIFF's and tafs's, never ticked.
- **The age line:** legal-i18n's `ages.json` at the pin, by this PC's region (`GlobalizationPreferences.HomeGeographicRegion`): the country's own age where the law asks more than 16, else the line every country reads.
- **Continue and Show the code wait on the tick.** The terms version in force (`GET /v1/auth/app/terms`) is read as the welcome shows; nothing is sent without one, and it goes with whichever way the person signs in.
- **The terms changed** (KIFF's 409 `terms_outdated`, on the token or the pairing claim): the box unticks, "The Terms changed…" shows, and the new version is read for the new tick.

## Signing in inside the app (K5, `AppSignIn`, `AppSignInFlow`)

Continue opens KIFF's sign-in in the person's browser, as tafs-linux does (kiff/cloud's `api/internal/appsignin/README.md`):

1. **The link:** `https://api.kiff.dev/oauth/authorize` with `client_id=tafs-app`, `redirect_uri=dev.kiff.tafs:/oauth`, a PKCE S256 challenge (a 43-character verifier from `RandomNumberGenerator`, checked against RFC 7636's vector), a fresh 256-bit `state`, and `ui_locales` where KIFF's page speaks the app's language. Neither the verifier nor the state is ever printed.
2. **The way back:** tafs registers `dev.kiff.tafs` with Windows (`RegisterForProtocolActivation`, as for planque's `tafs:`). The browser's launch is handed to the running tafs (`RedirectActivationToAsync`). Only the waiting attempt's link is taken: `dev.kiff.tafs:/oauth` exactly, no host, no fragment, its `state`, each field once. Anything else is dropped, not even read as a refusal. Another app could register the scheme too; PKCE makes a code useless without this attempt's verifier.
3. **The token:** `POST /v1/auth/app/token` with the code, the verifier, the ticked terms version and the device exactly as `/v1/auth/device/start` sends it (this PC's TPM key). The OAuth fields are written last, so no device field can stand in for one. The session is kept as the device sign-in keeps one, and the window celebrates.
4. **How it ends otherwise:** declined on KIFF's page, nothing is said; `unsupported_client`, KIFF's words or "isn't on yet"; `terms_outdated`, unticked and the code kept 50 of its 60 seconds for the new tick; anything else, "Signing in didn't work.", the tick kept.

## The three cards (`OnboardingCards`)

After the celebration's Let's go, once on this PC (`%LOCALAPPDATA%\tafs\cards-seen`): what a taf is (the sample's taf rows), a Card (the Card tab's own face, `TafPage.CardFace`), and staying in control (Needs you's held call, shown, not live). Skip from any card, Next, then Get started; the last card's lead is `onboarding.control_lead_desktop`. **Account › How tafs works** reopens each card, and Legal lists the four pages, KIFF's and tafs's, each titled as its page is. `--page cards-1`, `cards-2` and `cards-3` show them for screenshots.
