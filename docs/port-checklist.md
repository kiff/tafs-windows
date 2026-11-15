# The port checklist

Each iPad view file (kiff/tafs `ios/Tafs/Views`, at d14217f) against its Windows status. A file is **ported** when
every branch it draws is on Windows, **partial** when part of it is (what's missing is named), **not started**, or
**not for Windows** (and why). What an open pull request brings is marked with its number, "(#64, open)". The PR that
ports a file updates its line here.

## Ported

| iPad file | Lines | What | Models | Windows |
| :- | -: | :- | :- | :- |
| QuestionsCard.swift | 121 | Several questions Coach asks at once: each question's choices, Something else…, Send answers, Chat about this, the answered line | QuestionAnswers, SetupComponent | `QuestionsCardView.cs`, `WrapRow.cs`, `Tafs.Core/Coach/CoachCards.cs` |

## Partial

| iPad file | Lines | What | Windows has | Missing |
| :- | -: | :- | :- | :- |
| OnboardingView.swift | 63 | The welcome: what tafs is, Sign in with KIFF, Try it with sample data | The mascot, the title and lead, the three points, Sign in with KIFF (`SignInPage.xaml.cs`, `Tafs.Core/SignIn/SignInScreen.cs`) | Try it with sample data: the sample opens only with `--sample`, for screenshots |
| SignInView.swift | 333 | `SignInView`: Sign in with KIFF. Also `SettingsView`, `PlanRow`, `AccountView`, `AccountLines` (Account) | `SignInView`: the code, Open KIFF, the link for another browser, the wait, why it failed and Sign in with KIFF again, adding this PC from the phone instead, the terms line (`SignInPage.xaml.cs`, `Tafs.Core/SignIn`). `SettingsView` as Account: Mode, View only, the Alerts row, the theme, the language and Change language, How it works, Legal, About, sign out with its question, Clear the sample (`AccountPage.xaml.cs`, `Tafs.Core/Account/AccountScreen.cs`) | `SignInView`'s Copy. The KIFF account's rows from `GET /v1/me` (KIFF account, Signed in as, `AccountLines`); People & apps; the Plan section (`PlanRow`); Coach's suggestions; the AI-data consent; the time zone; Your chats; `AccountView` (its lines, using this computer's zone, Export my chats, Delete account). The Alerts row opens a placeholder |
| ShareViews.swift | 405 | Sharing a taf (`ShareLine`, `ShareTafSheet`), and the Overview (`OverviewView`) | `OverviewView`: the title and subtitle, NEEDS YOU's held calls, the grid with each taf's NOW, CARD and LAST RESULT (`lastResult`, each taf's report read), "Each line opens the taf.", Home from tafs cloud (`OverviewPage.xaml.cs`, `Tafs.Core/Home/Sidebar.cs`, `HomeList.cs`, `Tafs.Core/Taf/TafRuns.cs`) | Who uses each taf (`shares`); what else waits, in tafs cloud's words; a held call opening its taf's page (it opens Needs you). Sharing itself |
| NeedsYouView.swift | 603 | Needs you: held calls, "Did this happen?", steps, Coach's waits, knocks, alerts | `NeedsYouView`'s title, held calls and empty state; `HeldCallCard`'s face, countdown, amount, what it asks, why it was held, the taf's words, and the expired state; in place of the answers, why this PC can't approve and Add this Windows PC (`NeedsYouPage.xaml.cs`, `Tafs.Core/NeedsYou/NeedsYouScreen.cs`). The held calls are the sample's | KIFF's held calls (a signed read); answering here (approve, extra room, next steps, refuse, Allow from now on, install), "someone else answers", Answer in KIFF; Coach's own hold; `DidThisHappenCard`, `AlertsCard`, `AlertsOfferSheet`, `CoachWaitingCard`, `SetAllAsideButton`; steps, Card-unit exceptions and knocks; scrolling to a call; refreshing |
| AccountPreferencesView.swift | 37 | Account's theme and time zone pickers | `ThemePicker`: Light, Dark and System, kept across launches (`AccountPage.xaml.cs`, `Tafs.Core/Account/ThemeChoice.cs`) | `TimeZonePicker`: the search, the zones, choosing one, and using this computer's |
| RecordView.swift | 689 | The Record, its rows, filters and receipts | `RecordView`'s list: the title and lead, the taf chips, the filters, the days, `RecordRow`'s dot, title, time and detail, from KIFF's activity (`RecordPage.xaml.cs`, `Tafs.Core/Record/RecordScreen.cs`, `KiffActivity.cs`). `ReceiptView`: its head, facts, steps, the receipt's facts without the checked heading, Copy receipt and mismatches (`ReceiptSheet`, #56, open) | `ReceiptView`'s signature check; search and Export receipts; reads folded into one line a day, and the day's spending; what a line acted on; tafs' own events; answers given here; "no longer waiting"; the taf chips from the account's tafs |
| TafDetailView.swift | 1467 | A taf's page: head, tabs, Card bar, Activity, Card, Setup, panels | `header`, `StateChip`, the tabs (Chat off), `CardBar` (it opens Card); the sample's `activity(_:)` with `ActivityFilter`; `TafActivityList` for a tafs cloud taf; `setup(_:)`'s Card-unit exceptions, job, `ConnectedToolsRing`, Starts when and Results go to (off), `MemoryPanel` with Forget and Pause (the sample's), `MemorySummaryPanel`; the Card tab's `CardStack`, `WalletLayout`, `TafCardFace`, `EnvironmentPill`, `CardLines`, Revoke's question (`TafPage.xaml.cs`, `Tafs.Core/Taf`) | The Chat tab; `headerLine` (Versions, Share); the side column; held calls on Activity; on Card: the time zone lines, `eachItem`, `CountedView`, pending, unit words, `HaltRow`, Versions, Usage, Revoke's action; in Setup: `ConnectAgainPanel`, Improve in Coach, Remove, the sample's `ConnectionsPanel`; `HealthLines`, `RunsPanel`, `PromiseRow`, `WorkPanel`, `VersionsScreen`, `VersionsPanel`, `VersionCardSheet`, `DraftCardsView`, `CardFace`; a taf that's gone |
| TafPresentationViews.swift | 492 | A taf's Activity list and its rows, from tafs cloud; Setup's ring and memory | `TafActivityList`'s filters, days, pages, why a read failed with Try again, and its end; `SourceActivityRow`'s title, detail, time and amount; `CardUnitExceptionPanel`'s title, words and history; `ConnectedToolsRing` with its nodes; `MemorySummaryPanel` (`TafPage.xaml.cs`, `Tafs.Core/Taf/ActivityPages.cs`, `TafActivity.cs`, `TafSetup.cs`) | `TafActivityList`'s held calls, Card-unit exceptions, work, runs, `RetainedRuns`, `TafKnocksPanel`, and All apps; a row's receipt; `NextStepRow`; `ConnectedAppSheet` and a node's week; the exceptions' Coach and Dismiss; the memory's See all; refreshing |
| UsageView.swift | 348 | Plan & usage: the balance, buying, who bills, members' requests | `UsageView`'s title and order; `BalancePanel`'s tone, words, meter, detail and warning; the Credits panel shown or left out. Its reads are the sample's (`UsagePage.xaml.cs`, `Tafs.Core/Usage/UsageScreen.cs`) | tafs cloud's and KIFF's reads, their loading and why they failed; Raise the limit; buying packs and Solo; `planMessage`; `billingPanel`, `askPanel` (Ask an admin), `requestsPanel`; refreshing |
| CreditsViews.swift | 181 | The credits the account's tafs used, and one taf's runs | `CreditsPanel`: Last period and This period, the period's words, each taf's row, Coach, not counted, the note, from the sample (`UsagePage.xaml.cs`, `UsageScreen.cs`) | tafs cloud's reads, their loading and errors; a taf's row opening its runs (`TafCreditsView`, More runs); `CreditsRunRow`; a run's request |
| ConnectAppsView.swift | 1128 | Connect apps, a connected app's detail, connecting's approval | `ConnectAppsView`'s list from tafs cloud: the search, Yours and Discover, the table (`ConnectColumns`) with each row (`ConnectRow`), the logos, "Make one first", More apps, Not listed? Ask Coach (`ConnectAppsPage.xaml.cs`, `Tafs.Core/Connections/ConnectAppsScreen.cs`) | Connecting (Connect is dimmed): "Which taf?" (`StartSheet`), `ConnectApprovalSheet`, the vendor's sign-in; the account's connections; `AppConnectionsView`, `ConnectionDetailView`, Disconnect and Clear (`DisconnectWords`, `ConnectionFixWords`, `ConnectionGroupWords`); the narrow window's rows; a failed read in the app's error line |
| CalendarView.swift | 434 | Calendar: Day, Week, Month | The compact layout: the header, the title, Today, previous and next, the date, Day / Week / Month, the strip, Day's hours, Week's day, Month's grid (`CalendarPage.xaml.cs`, `Tafs.Core/Calendar/CalendarScreen.cs`) | tafs cloud's scheduled work (`GET /v1/calendar`): its chips, its dots, "more" and halted words, a tap that opens the taf; the wide layout (the navigator beside, the agenda; the rule is in `CalendarScreen.LayoutOf`); refreshing |
| HomeSplitView.swift | 584 | The sidebar for the whole app and what it opens | The brand, the screens with Needs you's count, NEEDS YOU's held calls, Your tafs relevant now with what each is doing, Coach and Account at the foot, the chosen row's fill; each screen and a taf's page opened beside it (`MainWindow.xaml.cs`, `Tafs.Core/Home/Sidebar.cs`) | A held call opening its taf's page (it opens Needs you); Shared with you; Other agents; tafs being set up, Finish and Discard; Explore and the recipes strip; + New and its gallery; the halt-all row; Unreachable and Plan lines at the foot; a taf's badge, its signed-out line and its health line's colour; Coach while it drives; refreshing |
| CoachMode.swift | 131 | Coach mode over the app: the panel, its toolbar, the phone's sheet | `CoachView`'s Back and ⤢; `CoachPanel`: a third of the window, sliding in (fading with animations off), the backdrop that closes it, full screen and back, the handle's strip (`CoachPanel.xaml.cs`, `MainWindow.xaml.cs`) | Dragging the edge to widen, narrow or close it, and keeping its width and full screen for next time; a swipe to the right that closes it. `PhoneCoachPanel` is the phone's |
| SetupChatView.swift | 1946 | Coach's conversation | `introBefore` with Start: planque set up first, then Setup's draft and its key (`Tafs.Core/Coach/CoachStart.cs`, #64, open; `Tafs.Core/Kiff/PlanqueEnrolment.cs`); `thread`: times between parts, the person's and Coach's bubbles, a run's start, `WhileYouWereAway`; `SetupPlan` and `StepMark`; `SetupComponentView`'s notice, confirm and result (a hand-over, an improvement); `CardNote`, `OutcomeLine`, `FlowRow` (`WrapRow.cs`); sending an answer (`CoachPanel.xaml.cs`, `Tafs.Core/Coach`). `SetupApproval`'s cards: the taf's Card, folded outcome lines, Decline and Approve shown off (`CoachPanel.xaml.cs`, `Tafs.Core/Coach/ApprovalCard.cs`) | `startScreen` (tafs cloud's first start: the tools and Cards signed together, the draft alone, Coach's Cards in KIFF's words drawn first; `FirstStart`, #65, open); `starts`, its recipes and `startSwitch`; `ended`; the composer, sending, the stream, Stop; `UnsentBubble`, `QueuedBubble`, `RevealingBubble`; `NeedsYouPill`, `NeedsYouBar`; `SetupChatsList`, `ArchivedSetupChats`; approving or declining an approval, with planque's words (`ProfileWordsView`) and the key sheet; the sign-in card; `KnobDraftCard`; Coach's words as Markdown; "Taking shape" beside a wide chat |
| WorkLog.swift | 181 | What a taf or Coach does while it answers, and what it did | `WorkLog.rows`, `duration`, `StepList`, and `WorkSummary` for Coach: "Worked for 12 s ›" opening its steps (`CoachPanel.xaml.cs`, `Tafs.Core/Coach/CoachThread.cs`) | `LiveWork` (the stream); `WorkSummary` in a taf's chat; `SteadyMarkdown` |
| PairSignInView.swift | 104 | Signing in from another phone | The same flow as Add this Windows PC: the QR, the steps on the phone, the check code, the wait, why it failed and a new code (`PairingPage.xaml.cs`, `Tafs.Core/Pairing`) | An expired code replaced by itself, with its line (Windows shows the expiry and Show a new code); the iPad's sign-in words for it (Windows uses Add this Windows PC's) |
| CoachToolsStep.swift | 255 | Coach's tools still to give, and what's given as Start's screen draws it | `CoachToolsGiven(start: true)`: Coach's Cards in KIFF's words, then each group and tool under the step's details, folded, or the batch's lines (`CoachPanel.xaml.cs`, #65, open) | `CoachToolsStep` (the step, its row, Give and Not now); `CoachToolsGiven`'s step layout; `taken` |
| ApprovalSetupView.swift | 102 | This device's approval readiness, and the gate in place of an approval's button | In place of an approval's buttons, why this PC can't approve and Add this Windows PC (`NeedsYouScreen.Gate`, `NeedsYouPage.xaml.cs`); Coach's Start off for a session that can't approve (`CoachStart.MayStart`, #64, open) | Reading readiness (Windows Hello not set up) and its words; Open Settings and Check again; `ApprovalNotice`; the gate's button once this PC can approve |

## Not started

| iPad file | Lines | What | Models |
| :- | -: | :- | :- |
| AIConsentView.swift | 74 | The AI-data consent screen | |
| AllowSheetView.swift | 96 | "Allow this from now on" | |
| AppEventApprovalSheet.swift | 54 | An app event's filters before signing a subscription | |
| AttachViews.swift | 267 | Attaching photos and files in a chat | Attachment |
| DeleteAccountView.swift | 60 | Deleting the account | |
| DevicesView.swift | 69 | Account › Devices | |
| DiscardSettingUp.swift | 35 | Discard a taf being set up | |
| DoorsView.swift | 923 | A taf's Doors tab | DoorsModel, PlanqueClient |
| DriveViews.swift | 73 | Coach asks to drive the app, and what it did | DriveSession |
| ExportChatsView.swift | 82 | Export my chats | |
| HaltScreen.swift | 176 | A halt of one taf or every taf | |
| HomeRecipes.swift | 242 | Home with no taf: the recipes | |
| MicButton.swift | 122 | The mic in a chat's message bar | Dictation |
| NotificationSettingsView.swift | 138 | Account › Alerts (Windows has its row only) | |
| PeopleViews.swift | 291 | People & groups, naming a group, adding someone | |
| PlanViews.swift | 268 | Buying credits and Solo, asking an admin, declining a request | Purchases |
| RecipeGallery.swift | 135 | The jobs' gallery | |
| RecordSearchViews.swift | 192 | The Record's search | |
| ReportAnswerSheet.swift | 124 | Report an answer | |
| SetupShapePanel.swift | 130 | What Coach has made so far | SetupShape |
| TafChatView.swift | 826 | A taf's chat | |
| TafDialPanel.swift | 228 | The dial's requests and confirmation | KnobNavigation, DoorsModel |
| TafKnobView.swift | 377 | The taf's remote | KnobNavigation |
| TafSetupViews.swift | 512 | A taf's Setup: Starts when, Results go to, the pending dial | DoorsModel |
| TrialViews.swift | 214 | Trials of a new version | |
| YourStepCard.swift | 46 | A step a taf hands the person | |

## Not for Windows

| iPad file | Lines | Why |
| :- | -: | :- |
| HomeView.swift | 403 | The phone's Home. A wide window uses HomeSplitView. |
| TafsBar.swift | 121 | The phone's bar along the foot. A wide window has the sidebar. |
| PairComputerView.swift | 222 | The phone's side of pairing: the camera reads a computer's code. |
| TafKnobRemote.swift | 64 | Keeps the remote above the phone's tab bar. |

## Shared pieces

| iPad file | Windows |
| :- | :- |
| Design/Theme.swift | `Theme.xaml`: every colour, light and dark, following Windows' setting; the taf colours (`TafColors`, `NeedsYouScreen.cs`) |
| Design/Components.swift | `Theme.xaml`'s styles: `Panel` (`TafsCard`), `PrimaryLook` (`TafsPrimary`), `OutlineLook` (`TafsOutlineButton`), `Eyebrow` (`TafsEyebrow`); `TafAvatar`, `StateChip` and `Meter` where they're drawn; `Legal` (`SignInScreen.Agreement`); `SignOutWay` (`AccountPage.xaml.cs`). Not yet: `AccountTime` (times are this PC's zone), `PanelLink`, `UnreachableLine`, `PlanLine`, `ApprovalKeyLostLine` |
| Design/Compat.swift | `WrapRow` (`WrapRow.cs`) |
| Design/Words.swift | `Tafs.Core/Words`: `WordBook`, every line from the pin; `PluralRules`; `Lists.And` |
| Services/AppLanguage.swift | `AppLanguage.Pick` (`Words/AppLanguage.cs`) |
| Model/ThemePreference.swift | `ThemeChoices` (`Account/ThemeChoice.cs`) |
| Model/Models.swift | `TafCard`, `CardRule`, `Money`, `Window` (`Taf/TafCard.cs`, `CardTotal.cs`); `HeldCall` (`NeedsYou/NeedsYouScreen.cs`) |
| Model/SetupComponent.swift | `SetupComponent` and its questions; `SetupThread`'s cards, answers and plan (`Coach/CoachCards.cs`) |
| Model/QuestionAnswers.swift | `QuestionAnswers`, `QuestionSelection` (`Coach/CoachCards.cs`) |
| KIFF/KIFFClient.swift | the device sign-in (`SignIn/DeviceSignIn.cs`) and sign-out (`Kiff/KeptSession.cs`); `ActivityCall`, `activity()` (`Record/KiffActivity.cs`); KIFF's access and `me` for Coach (`Coach/CoachAccess.cs`) |
| KIFF/KIFFMapping.swift | `taf` (`Home/HomeList.cs`); `record`, `executed` (`Record/KiffActivity.cs`); `toolWords` (`Coach/CoachThread.cs`) |
| KIFF/TafsCloud.swift | Home (`Home/HomeList.cs`); `logoURL` (`Connections/ConnectAppsScreen.cs`); `escaped`, `query`, `refusal` (`Taf/CloudRead.cs`); `Balance`, `Credits` (`Usage/UsageScreen.cs`); `ChatMessage`, `Work` (`Coach/CoachThread.cs`); `CoachStart` (`Coach/CoachStart.cs`, #64, open) |
| KIFF/TafsCloud+Presentation.swift | `ActivityPage`, `SourcePages` (`Taf/TafActivity.cs`, `ActivityPages.cs`) |
| KIFF/TafsCloud+Knob.swift | `ConnectApps`, `connectApps` (`Connections/ConnectAppsScreen.cs`) |
| KIFF/PlanqueClient.swift | `grantsReadProof` (`Kiff/PlanqueProof.cs`); `Grant`, `grants` (`Kiff/PlanqueGrants.cs`) |
| KIFF/JCS.swift | `Jcs` (`Kiff/Jcs.cs`) |
| KIFF/SigningTime.swift | `SigningTime` (`Kiff/PlanqueProof.cs`) |
| App/AppModel.swift | `loadReport` (`Taf/TafRuns.cs`); `mayUseCoach` (`Coach/CoachAccess.cs`); `loadSetupThread`, `answer` (`Coach/CoachThread.cs`); `loadActivityPage` (`Taf/ActivityPages.cs`); `loadMoreConnectApps`'s merge (`Connections/ConnectAppsScreen.cs`); `loadTafSetup` (`Taf/TafSetup.cs`); `useSession`, `forgetSession` (`MainWindow.xaml.cs`); `startSetup` (`Coach/CoachStart.cs`, #64, open); `setUpPlanque`, `enrolPlanque` (`Kiff/PlanqueEnrolment.cs`) |
| Services/TafTemplates.swift | the five sample tafs (`Taf/SampleTafs.cs`) |

## Coverage

Ported 1, partial 19, not started 26, not for Windows 4: the 50 view files at d14217f.
