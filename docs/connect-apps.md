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

**Not yet:**
- **tafs cloud's list.** Until it's read, the page shows only its title, never an empty catalog; `--sample` shows the
  iPad's. The read brings the search, More apps (`next_cursor`) and the logos.
- **Connecting.** Connect is dimmed, as the iPad's is with nothing to connect for. To come: the taf to connect for, the
  approval sheet, the vendor's sign-in.
- **A connected app's detail** (what it can do, which tafs use it, Disconnect), with its chevron.
