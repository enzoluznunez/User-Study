# User-Study

A data visualization app for Meta Quest 3, built in Unity, for exploring 13F
holdings: which investment managers reported holding which securities at the end
of a quarter. You reshape the view with your hands, and a voice assistant, Ada,
answers questions about the holdings and drives the same tools when asked.

## What You Can Do

The holdings stand in front of you in passthrough as a 3D network, a bar sheet,
or both side by side. In the network, filers are orange cubes sized by their
reported portfolio, securities are blue spheres sized by the value held in them,
and each holding is an edge between the two, thicker for a larger position. On
the sheet, each row is a security, each column a filer, and each bar one
position in dollars.

Six tools sit on the tool panel, and each works on whichever view you touch:

| Tool | On the graph | On the sheet |
|---|---|---|
| **Profile** | Poke a node: a breadth-first search lights what it reaches, 1, 2 or 3 hops out (pick in the panel), and a card totals the positions inside. Poke it again to let go. | Press a bar and sweep along a row or column to raise it as a strip, with its count, range, average and total. |
| **Filter** | Two lists, Investors and Holdings, name every filer and security on the graph, all on to begin with. Untick an investor to hide it and its edges; its holdings stay until you untick them too. Poking a node switches it off. The amounts hide edges below $10M, $50M, $100M or $250M. | Open By Company or By Metric, then poke a bar or tick a name. |
| **Sort** | Arrange filers and securities in ranked columns by value, holdings or name; Layout restores the data's own positions. | Pinch a line and slide it into place. |
| **Move**, **Rotate**, **Scale** | Place the view in the room. | Same. |

Undo takes back any of them, across both views, newest first. Everything the
tools do, Ada can do by voice, and Ada can answer from the whole dataset
whatever is on view: ask who holds a security, what a filer holds, or what two
filers have in common.

### Choosing the views

The **Data** object in `File Reader.unity` has two switches on its
HoldingsLoader component: **Show Graph** (on by default) and **Show Sheet**
(off by default). Turn both on to stand the sheet and the graph side by side;
the graph moves to the right of the sheet so neither stands inside the other.
Ada's prompt and tools follow the switches, and with both on she asks which
view you mean when a request could fit either.

## What's in the Repository

| Path | What it holds |
|---|---|
| `Assets/Source/` | The app's C# code: `Main` starts the scene and camera rig, `Data` loads the holdings, `Graph` draws the network, `Sheet` draws the bar sheet, `Tools` and `UI` are the hand interface, `InteractionAPI` holds the hand poses, `Audio` wraps the native microphone and speaker plugins, `Poster` builds the figures `Assets/Editor/PosterCapture.cs` renders, and `Gemini` is the voice assistant and the tools it calls. |
| `Assets/Scenes/File Reader.unity` | The one scene the app runs. |
| `Assets/StreamingAssets/13f_sample_*.csv` | The data, and its only copy. See **The Data** below. |

The app ships its data and needs no server: the only network call it makes is to Gemini, for voice.

## The Data

Two CSVs in `Assets/StreamingAssets/` are the single source of truth. Edit them
directly; nothing generates them.

| File | One row per | Joins on |
|---|---|---|
| `13f_sample_nodes.csv` | filer (`node_type` = `filer`) and security (`security`), with its `x`,`y`,`z` layout | — |
| `13f_sample_edges.csv` | position: one filer holding one security | `filer_cik` to a filer, `cusip` to a security |

They are a sample of 13F filings for the quarter ending 30 June 2026. Keep them clean the way they are now:

- **CUSIPs are uppercase**, and the spelling each arrived in is kept in `cusip_raw`.
- **Each security appears once.** Two spellings of one CUSIP are one security.
- **A filer holds a security at most once.** Positions under two spellings are summed.
- **`display_name` tells securities apart.** It carries the share class (`Alphabet Inc. Class A`, `… Class C`), then the issue where names still collide (`iShares Trust · CORE S&P500 ETF`). `share_class` holds the class on its own.
- **`filers_holding_in_sample` and `combined_value_in_sample_usd`** must agree with the edges.

The app refuses to load a file that breaks the join or lists a CUSIP twice, and says why in a notice.

# Getting Started

## Prerequisites

- [ ] A Meta Quest 3 with a USB-C cable
- [ ] A Google Gemini API key on a paid tier
- [ ] Internet access on the headset, for voice

## Prepare the Meta Quest 3

1. Pair the headset with the Meta Horizon app on your phone.
2. Create or join an organization at developer.meta.com and verify your account.
3. In the Horizon app, open your headset's settings, turn on Developer Mode, and restart the headset.
4. Plug the headset into your computer, put it on, and accept **Allow USB debugging** — check *Always allow from this computer*.
5. Run Space Setup. The app requires passthrough and reads your room's scene data.

## Download and Set Up Unity

1. Install Unity Hub on your computer and sign in or create an account.
2. Install the Unity version **6000.4.0f1**.
3. During installation, check **Android Build Support** and both of its sub-modules, **OpenJDK** and **Android SDK & NDK Tools**.

## Download and Set Up the Project

1. Clone the repository, or download the ZIP and unpack it.
2. In Unity Hub choose **Add → Add project from disk**, select the cloned folder, and open
   it with 6000.4.0f1. The first import takes several minutes.
3. Open `Assets/Scenes/File Reader.unity`.

## Add Your Gemini API Key

Create a file at `Assets/StreamingAssets/gemini.key` holding your API key on one
line and nothing else. Git ignores it, so it stays on your machine. Without it
everything works except the voice assistant.

Everything in `StreamingAssets` is packed into the build, so a built `.apk`
carries the key. Don't share an `.apk` with anyone you wouldn't give the key to.

## Building to the Meta Quest 3

1. Connect the headset by USB and put it on, so it stays awake.
2. Open **File → Build Profiles**, select **Quest Default**, and choose your headset in the
   device list. Platform, architecture, and SDK levels are already set in that profile —
   change nothing.
3. Click **Build And Run**. The first build takes 10 to 30 minutes; later builds are far
   quicker.
4. In the headset, accept the microphone prompt at launch. Denying it leaves everything
   working except voice.

Done when you are standing in passthrough and Ada greets you. A "No Data" notice
means a CSV in `StreamingAssets` is missing or does not read, and says which.

## FAQ

**Does it cost anything?** Yes, to pay for any usage of the Google Gemini API.

**Can I try it without a headset?** Not meaningfully — hand input and passthrough are the interface.

**Where does the data come from, and how do I change it?** From 13F filings with
the SEC. Edit the two CSVs in `Assets/StreamingAssets/` directly, keeping them
to the rules under **The Data**.
