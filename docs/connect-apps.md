# Connect apps

Connect apps is the iPad's `ConnectAppsView` at its full width. It lists what tafs cloud offers to connect, in its
order and words. The core is `ConnectAppsScreen`; the app's `ConnectAppsPage` only draws it.

- **Yours, then Discover.**
  - Yours holds the person's own apps: connected, signed in, or to sign in again. Discover holds the rest.
  - An app under Yours isn't under Discover in another catalog form (`stripe@3` beside `connector:composio:stripe`).
    The forms match by the entry's last part without its version, and by vendor, folded.
- **The table:** App, How it connects, Whose, Status, with the column names over the first table shown. App gets its room
  first (at least 200); the other three keep 150, 130 and 230, then give way down to 96, 84 and 180 (the iPad's `ConnectColumns`).
- **The list** is drawn as the reference set's `account.png` measures it: a radius of 26, 20 in, section labels at 17 semibold.
- **A row:**
  - **App:** the name (`name`, else `title`) on one line, soft for an app to set up, the subtitle under it.
  - **How it connects:** `method` where sent, else `how`.
  - **Whose:** tafs cloud's `whose`, as sent.
  - **Status:** Connected in green, or Sign in again in amber. Under it, "Used by …" from `used_by`, for the person's
    own apps only, joined with CLDR's "and" list for the language (`Lists.And`, as the iPad's ListFormatter).
  - **At the end:** a ✓, or Connect / Sign in again as wide as its words.
  - `needs_setup` shows tafs cloud's `words` and no button.
- **"Apps connect for a taf. Make one first"** heads Discover, in 13 dim, while no taf can have an app (this PC reads
  no tafs yet), as the iPad says it (`SaysMakeATaf`). It's why Connect is dimmed.
- **Not listed? Ask Coach** shows with `by_key`, in the accent. The sample sends no `used_by`, as the iPad's
  (the reference set's `connect-apps.png`). **Reading:** an app with no `entry` is left out, never the list.

- **tafs cloud's list** (as the iPad's `loadConnectApps`): `GET /v1/connect/apps`, as the session, with this PC's signed
  grants read (`Planque-Device-Signature`, as Setup sends it). A spinner until it's read; if it can't be, "The apps couldn't
  be read." and why, in red under the search.
  - **Search apps:** read again as the person types, 0.3 s after the last key, at once when emptied (`?q=`). Only the
    newest search replaces the list.
  - **More apps** (`next_cursor`): the same search's next page from its catalog (`provider`, `cursor`), added under the
    list, no app twice; "Loading…" meanwhile; "More apps couldn't be listed:" and why if it fails.
  - **The logos,** 24 across beside the name, only from tafs cloud's own `/v1/logos/` with a 32-character hex key, fetched
    as images with no token.
  - `--home` with `--page connect-apps` reads `connect-apps.json` beside the Home file. tafs cloud has no contract for this
    read yet; the tests use answers shaped as the iPad's decoder.

## An app's connections (`AppConnections`, `AppConnectionsPage`)

The iPad's `AppConnectionsView` and its model (`refreshConnections`, `connectionGroups`, `clearPlan`, `disconnect`).

- **The account's connections** are read with the list: planque's grants, signed by this device as Setup's read is
  (`Planque-Device-Signature`), never with KIFF's token. An app's are those whose `entry` is the app's, or the grant tafs
  cloud names for it, and not ended (`disconnected`, `failed`).
- **Which rows open them** (`AppConnections.Opens`, as Android's and the iPad's `opensConnections`, kiff/tafs#1219): a
  connected app always does, and so does one this PC holds a connection to that isn't connected, such as a sign-in kept
  after its card was declined (planque's `listed`), so it can be ended. `connected` keeps its meaning. Such a row ends in a ›
  named "Your Stripe connections"; it opens in the same place, with ‹ Back.
- **The page,** "Your Stripe connections":
  - a section per taf each was made for, by its agent in KIFF, those needing attention (a sign-in that waits, or to sign
    in again) first, then by name; each connection with planque's name for it (else the app's) and planque's status words,
    or "Waiting for the sign-in at Stripe";
  - **From removed tafs:** connections whose agent tafs cloud names in `removed_tafs`, with the removed taf's name as
    planque keeps it, and "Kept: Chargebacks' Card uses it" where tafs cloud says a live Card uses it;
  - **Other connections:** those no taf here can name, never claimed as removed;
  - for whoever manages Cards, "Removing access takes its tools from that taf only…" under them;
  - without this PC's read: "Start from Connected apps on this Windows PC first."
- **Ending one,** for whoever manages Cards (KIFF's `cards.manage`):
  - **Remove access** asks first, naming the taf it ends ("Remove access to Stripe for Rae?") and what stops and stays
    (a key's own line for a key); **Cancel** for a sign-in that waits ("Cancel the sign-in to Stripe?", Keep waiting), and
    **Cancel the request** for a request nobody answered;
  - then Windows Hello in the question's words, then planque's `DELETE /v1/accounts/{tenant}/grants/{id}`, signed by this
    device (`AppConnections.EndAsync`). Closed: "Not done: it wasn't confirmed.", and nothing is sent. Refused: "Access not
    removed." and why. A sign-out meanwhile sends nothing more;
  - then the connections and the list are read again.
- **Clear these connections,** under From removed tafs, for whoever manages Cards: only those tafs cloud lists with no live
  Card using them (`AppConnections.ClearPlan`). Asked first ("Clear 2 connections from removed tafs?"), then one Windows Hello
  for all of them, then each one's DELETE. Then how it went: "Cleared 2. Kept 1 for Chargebacks, whose Card uses it.", and
  what couldn't be, to try again.
- `--page connect-connections` with `--home` opens the first app with connections, from a `planque-grants.json` beside the
  contract; there, ending one says it wasn't confirmed.

**Not yet:**
- **A failed read's words** show on the page; the iPad shows them in its app-wide error line.
- **Connecting.** Connect is dimmed, as the iPad's is with nothing to connect for. To come: the taf to connect for, the
  approval sheet, the vendor's sign-in.
- **On an app's connections:** Sign in again and Replace the key, a connection's own detail page (planque's `row`), a
  connection made under a taf that doesn't need it (`misplaced`), its last failed call, and the other tafs whose Card uses
  its tools ("Also on Ana's Card", and in Remove access's question).
