# YahtzeeSim – Reverse-Engineered Requirements

This document was reverse-engineered from the repository as it exists today. It records **what the code does**, groups that behaviour into
**software requirements (SW-xxx)**, and maps every software requirement to a higher-level **user requirement (UR-xx)**.
A status column shows where the code meets, misses or disagrees with the intent expressed in [TODO.md](../TODO.md).

## 1. Scope and system context

| Item | Description |
|---|---|
| Product | Solo Yahtzee scorecard/simulator with dice rolling, hold, scoring, odds guidance and a 6-card "Full Sheet" mode |
| Front end | [YahtzeeSim.Web](../YahtzeeSim.Web) – Blazor WebAssembly PWA, static-hosted on Cloudflare Pages, installable via "Add to Home Screen". The only front end: the MAUI and WinForms apps were removed (history stays in git) |
| Shared engine | [YahtzeeSim.Engine](../YahtzeeSim.Engine) – the single game engine (decided), a class library with no UI dependencies, referenced by Web and the test project, namespace `YahtzeeSim.Engine` |
| Strategy analysis | [YahtzeeSim.Analysis](../YahtzeeSim.Analysis) – replays Full Sheet games and builds rule-based strategy reports (section 3.12): for every player via an owner script, and for the signed-in player in the app under Account, My strategy |

Status legend: ✅ Implemented · 🟡 Partial · ❌ Not implemented. Engine behaviour is covered by the xUnit tests in [YahtzeeSim.Engine.Tests](../YahtzeeSim.Engine.Tests).

Terminology: **Card** = one 13-category scoresheet (a "game" column G1–G6). **Turn** = up to three rolls ending in one score entry.
**Scratch** = a category sacrificed with a 0, shown as **X**. **Full Sheet** = six cards played together.

## 2. User requirements

| ID | User requirement |
|---|---|
| UR-01 | I can play a complete game of Yahtzee on a digital scorecard from start to finish. |
| UR-02 | I can roll five dice up to three times per turn and choose which dice to keep between rolls. |
| UR-03 | I can record each turn in a category of my choice, or sacrifice (scratch) a category when nothing fits. |
| UR-04 | Scores follow standard Yahtzee rules so the results can be trusted. |
| UR-05 | I can play each game individually (Single), or play six games at once on one sheet (Full Sheet) and see a combined total. |
| UR-06 | I get odds and pacing guidance to help me decide what to keep and where to score. |
| UR-07 | I can undo a mistaken score. |
| UR-08 | I can mark the scorecard with pen colours of my choice, at sensible moments only. |
| UR-09 | The app celebrates when I score a Yahtzee. |
| UR-10 | Subtotals, bonus and totals are calculated automatically when a game ends. |
| UR-11 | The app is comfortable on a phone: big touch targets, readable, layout adapts to screen size. |
| UR-12 | I can use it on Android, Windows, iPhone and any device with a browser, from one web-based solution (no App Store). |
| UR-13 | The app starts reliably, keeps working offline once installed, and I can recover from a bad cached/broken state. |
| UR-14 | As the owner I can build, deploy and maintain it with one source of truth for the rules. |
| UR-15 | I can sign in with my Google account (no separate password) and pick a nickname, so my scores belong to me. Facebook is planned later. |
| UR-16 | I can see a leaderboard of the best scores, separately for Single and Full Sheet, for all time and for this week, and see where I rank. |
| UR-17 | My finished games still count if I was offline when I finished them. |
| UR-18 | As the owner I can analyse how people play, so every roll, hold, score and undo of a signed-in player is recorded with its timestamp. |
| UR-19 | As the owner I can run a script that shows how each player approaches Full Sheet games, with rule-based findings and the evidence behind them (no AI opinions); and as a player I can open the same report for my own games from the account menu. |
| UR-20 | As a player I get a short, friendly story of my play style (what I go for with each kind of roll, how I fill the sheet, highlights and one tip) and a play-style badge shown next to my name on the leaderboard. As the owner I can preview and tune both, and read every player's full report, in a Windows research app. |

## 3. Software requirements

Source links point to the file that implements the behaviour.
"Web" = [Home.razor](../YahtzeeSim.Web/Pages/Home.razor) / [Home.razor.css](../YahtzeeSim.Web/Pages/Home.razor.css).

### 3.1 Game rules and card model (RUL)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-RUL-01 | The game shall use five dice, each showing 1–6, drawn uniformly at random (`System.Random`). | [YahtzeeGameEngine.cs](../YahtzeeSim.Engine/YahtzeeGameEngine.cs) | UR-02 | ✅ |
| SW-RUL-02 | Each card shall contain 13 categories in this order: Aces, Twos, Threes, Fours, Fives, Sixes (upper); Three of a Kind, Four of a Kind, Full House, Small Straight, Large Straight, Yahtzee, Chance (lower). | [ScoreRules.cs](../YahtzeeSim.Engine/ScoreRules.cs) | UR-01 | ✅ |
| SW-RUL-03 | An upper category shall be valid when at least one die shows its face and shall score face × count of that face. | `ScoreRules.EvaluateCategory` | UR-04 | ✅ |
| SW-RUL-04 | Three of a Kind (≥3 same) and Four of a Kind (≥4 same) shall be valid accordingly and score the sum of all five dice. | `ScoreRules.EvaluateCategory` | UR-04 | ✅ |
| SW-RUL-05 | Full House shall be valid only for exactly three of one face plus exactly two of another, and score 25. Five-of-a-kind is **not** a Full House in Full Sheet mode (in Single mode a Joker Yahtzee scores Full House, see SW-RUL-11). | `ScoreRules.IsFullHouse` | UR-04 | ✅ (house rule for Full Sheet) |
| SW-RUL-06 | Small Straight (4 consecutive faces) shall score 30; Large Straight (5 consecutive faces) shall score 40. | `ScoreRules.IsSmallStraight/IsLargeStraight` | UR-04 | ✅ |
| SW-RUL-07 | Yahtzee (five of a kind) shall score 50. | `ScoreRules.EvaluateCategory` | UR-04 | ✅ |
| SW-RUL-08 | Chance shall always be valid and score the sum of all dice. | `ScoreRules.EvaluateCategory` | UR-04 | ✅ |
| SW-RUL-09 | A category cell on a card shall be fillable exactly once; a filled cell shall not be editable (except via Undo, SW-UND-02). | `YahtzeeGameEngine.TryApplyScore` | UR-01 | ✅ |
| SW-RUL-10 | A card shall be complete when all 13 cells are filled (value or scratch). | `GameCardState.IsComplete` | UR-01 | ✅ |
| SW-RUL-11 | **Single mode only.** If the Yahtzee box holds 50, each further Yahtzee on the card shall earn a +100 bonus, recorded as one check mark per extra Yahtzee in a "Yahtzee Bonus" row of the Lower Section, as on a real scoresheet. Each check mark is worth 100 and is included in the lower subtotal and game total. Undo shall restore the check count. The roll shall then be placed by the Joker rules: (1) the matching Upper box if open; (2) otherwise any open Lower box, where Full House, Small Straight and Large Straight score 25 / 30 / 40; (3) otherwise a 0 in any open Upper box. If the Yahtzee box holds 0, no bonus is awarded but Joker placement still applies. **Full Sheet mode** applies neither the bonus nor the Joker rules. | [YahtzeeGameEngine.cs](../YahtzeeSim.Engine/YahtzeeGameEngine.cs) (`GetPickStatus`, `TryApplyScore`) | UR-04 | ✅ |
| SW-RUL-12 | Categories shall have human-readable display names ("Three of a Kind", etc.). | `ScoreRules.GetDisplayName` | UR-01 | ✅ |

### 3.2 Turn flow (TRN)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-TRN-01 | A new game shall start with all dice blank (value 0), none held, and roll count 0. | `YahtzeeGameEngine.Reset` | UR-01 | ✅ |
| SW-TRN-02 | Rolling shall be allowed only while the roll count is below 3 and the game is not complete. | `CanRoll` | UR-02 | ✅ |
| SW-TRN-03 | A roll shall re-randomise only non-held dice and increment the roll count. | `RollDice` | UR-02 | ✅ |
| SW-TRN-04 | Holding a die shall be possible only after the first roll, before the third roll, and only for a die that has a value; toggling shall flip its held flag. | `ToggleHold` | UR-02 | ✅ |
| SW-TRN-05 | After the third roll all dice shall be flagged held, hold toggling shall be disabled, and the player shall be required to score. | `RollDice`, `AwaitingScoreSelection` | UR-02 | ✅ |
| SW-TRN-06 | A category shall be selectable only after at least one roll, only if its cell is unfilled, and only on an enabled card. | `CanSelectCategory`, `TryApplyScore` | UR-03 | ✅ |
| SW-TRN-07 | A category that is valid for the current dice shall score its value and end the turn, at any roll. A valid category can never be scratched. Choosing a category that is not valid shall ask the player to confirm a scratch (SW-TRN-08); declining leaves the state unchanged. | `GetPickStatus`, `TryApplyScore` | UR-03 | ✅ |
| SW-TRN-08 | After at least one roll, a confirmed scratch of an unfilled, not-valid cell shall be recorded as value 0, shown "X", and end the turn. After the third roll the player must score or scratch. | `TryScratch` | UR-03 | ✅ |
| SW-TRN-09 | After a score is applied the turn shall reset: roll count 0, dice cleared, holds cleared. | `StartNextTurn` | UR-01 | ✅ |
| SW-TRN-10 | Held dice shall persist across rolls within a turn and be visibly distinguished. | engine + both UIs | UR-02 | ✅ (TODO.md said "hold is broken"; verified working in the browser: held dice keep their values across rolls and are highlighted) |
| SW-TRN-11 | When every card in play is complete, Roll shall be disabled. | `IsGameComplete`, `CanRoll` | UR-01 | ✅ |

### 3.3 Scoring, totals and pace (SCR)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-SCR-01 | A scratched cell shall store value 0, be flagged `IsScratch`, and display "X"; other filled cells display their number; empty cells display blank. | [ScoreCellState.cs](../YahtzeeSim.Engine/ScoreCellState.cs) | UR-03 | ✅ |
| SW-SCR-02 | Each filled cell shall remember the pen colour hex in force when it was filled. | `TryApplyScore` | UR-08 | ✅ |
| SW-SCR-03 | Upper subtotal, lower subtotal, upper bonus and game total shall be computed only when the card is complete; before that they shall be unavailable and displayed as "--". | [GameCardState.cs](../YahtzeeSim.Engine/GameCardState.cs) | UR-10 | ✅ (TODO.md: "calculated automatically at game end") |
| SW-SCR-04 | The upper bonus shall be 35 when the upper subtotal (excluding bonus) is ≥ 63, otherwise 0. | `GameCardState` | UR-10 | ✅ |
| SW-SCR-05 | Game total shall equal upper subtotal + lower subtotal + bonus. | `GameCardState.GameTotal` | UR-10 | ✅ |
| SW-SCR-06 | A "Card Total" shall equal the sum of game totals and shall be shown only when all cards in play are complete (Single: card 1; Full Sheet: all six), in the G1 column of the Card Total row (full width in the narrow layout, SW-UIW-10). | Web `GetCardTotalDisplay` | UR-05 | ✅ |
| SW-SCR-07 | An "Upper Pace" row shall show, per card, Σ((count − 3) × face) over filled upper cells (scratch counts as −3 × face), with "+" for positive values, and "--" for disabled cards. | `ScoreRules.GetUpperPaceDelta` | UR-06 | ✅ |
| SW-SCR-08 | The Upper Pace figure shall be tracked for every game in both Single and Full Sheet modes. A blank upper cell counts as exactly three of its face, so the figure equals the projected upper subtotal minus 63 (≥ 0 means the 35-point bonus is on track). No further display is required. | `ScoreRules.GetUpperPaceDelta` | UR-06 | ✅ Covered by SW-SCR-07 (decided: keep the +/- number and colour tone as is) |

### 3.4 Game modes (MOD)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-MOD-01 | The app shall open in a not-started state: dice, Roll, Undo and score cells are disabled and the status line prompts "Tap New Game". | Web `OnInitialized` | UR-01 | ✅ |
| SW-MOD-02 | "New Game" shall ask for a mode (Single or Full Sheet) with a Cancel option, reset the engine, and then prompt for a pen colour. | Web `SelectGameMode` | UR-01, UR-05 | ✅ |
| SW-MOD-03 | New Game shall be available at any time and shall ask "Abandon current game?" before discarding a game that has been started (it does not ask when nothing has been rolled or scored yet). | same | UR-01 | ✅ |
| SW-MOD-04 | In Single mode only card G1 shall be enabled; G2–G6 remain visible (at every screen width) but greyed out and cannot be scored. | `IsCardEnabled` | UR-01, UR-05 | ✅ Disabled columns stay visible, greyed out (accepted by decision) |
| SW-MOD-05 | In Full Sheet mode all six cards shall be enabled and played in parallel over one sequence of turns. Each turn ends with exactly one score or scratch, recorded in one unfilled cell on one card; the next turn may target any unfilled cell on any incomplete card, in any order. The same category may be scored on different cards in different turns. A completed card is unavailable while other incomplete cards stay playable. | `IsCardEnabled`, `TryApplyScore` | UR-05 | ✅ |
| SW-MOD-06 | When a Full Sheet completes, the app shall show a summary with per-card Upper, Lower, Bonus, Total and the series total. | Web summary panel | UR-05 | ✅ (no equivalent panel for Single) |

### 3.5 Probability guidance (PRB)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-PRB-01 | An "Odds" column shall show, for each of the 13 categories, the chance of completing it given current dice and rolls remaining, formatted as a percentage with one decimal; blank before the first roll of a turn. | [ProbabilityCalculator.cs](../YahtzeeSim.Engine/ProbabilityCalculator.cs) | UR-06 | ✅ |
| SW-PRB-02 | With no rolls remaining the probability shall be 100% if the category is already satisfied, else 0%; if already satisfied with rolls left, 100%. | `EstimateProbability` | UR-06 | ✅ |
| SW-PRB-03 | For upper categories "complete" shall mean ≥3 of the face, and the probability shall be computed exactly (Markov chain, holding all matching dice). | `UpperSectionExactProbability` | UR-06 | ✅ (TODO.md: upper = three-of-a-kind probability) |
| SW-PRB-04 | For lower categories the probability shall be estimated by Monte-Carlo (4 000 trials) using a greedy per-category hold strategy and a fixed random seed, so the same dice, holds and rolls remaining always give the same figures. | `MonteCarloEstimate`, `BuildHoldMask` | UR-06 | ✅ |
| SW-PRB-05 | Probabilities shall be recalculated after a roll, an undo or a hold toggle, and cleared after a score. | Web `RefreshAll` | UR-06 | ✅ |
| SW-PRB-06 | Probabilities shall be displayed for every category regardless of whether the cell is already filled on the relevant card. | `Calculate` | UR-06 | ✅ Accepted by decision (filled categories keep showing odds) |

### 3.6 Undo (UND)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-UND-01 | Immediately before a score is applied the engine shall snapshot dice, holds, roll count, mode and all cards (deep copy). | `CreateSnapshot` | UR-07 | ✅ |
| SW-UND-02 | Undo shall restore the snapshot (reverting the last score and returning to the same roll state) and then clear it; only one level of undo is supported. | `Undo` | UR-07 | ✅ |
| SW-UND-03 | Undo shall be enabled only when a snapshot exists, and the snapshot shall be cleared on New Game. Rolls cannot be undone. | `CanUndo`, `Reset` | UR-07 | ✅ |

### 3.7 Pen colours and celebration (PEN, CEL)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-PEN-01 | The default pen colour shall be `#1D2939`. | Web `_currentPenColorHex` | UR-08 | ✅ |
| SW-PEN-02 | The pen picker shall be a blended colour wheel (hue and saturation) with a dark-to-light lightness slider and a live hex preview. | `Home.razor` `WheelPointer` / `HslToHex` | UR-08 | ✅ |
| SW-PEN-03 | A mandatory pen-colour picker shall be shown right after a new game starts. | `PromptForPenColorAsync` / `OpenColorPicker` | UR-08 | ✅ |
| SW-PEN-04 | After five of a kind is scored in any box (not as a scratch), the picker shall be shown again with a "Keep Current" option. | `OnScoreClickedAsync` / `ApplyScoreAsync` | UR-08 | ✅ Confirmed by decision: the picker fires on scoring; the celebration fires on rolling (SW-CEL-01) |
| SW-PEN-05 | The colour shall not be changeable at any other time (no other entry point). | UI | UR-08 | ✅ |
| SW-PEN-06 | A filled cell shall render with its stored pen colour as background and a black or white text colour, whichever gives the higher contrast against it. Filled cells keep full strength (not faded) and always show their value or X. | `GetContrastingTextColor` | UR-08 | ✅ |
| SW-CEL-01 | Rolling five of a kind shall show a full-screen splash/animation that dismisses itself after about 2 seconds or on tap. | Web `ShowYahtzeeSplashAsync` | UR-09 | ✅ |
| SW-CEL-02 | Recording a scratch shall inform the user that the category was recorded as X (stored as 0). | same | UR-03 | ✅ Web banner |

### 3.8 Web user interface (UIW)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-UIW-01 | The single page `/` shall present the app using HTML/CSS with a semantic score table (explicit table roles so the narrow layout keeps its semantics), reusing the engine unchanged. No scoring rules shall exist in the UI. | Home.razor | UR-12 | ✅ |
| SW-UIW-02 | Roll shall show a "rolling" visual on pointer-down for non-held dice and roll on click; Roll/Undo use an 8fr/2fr grid. | `RollButtonPressed/ClickedAsync`, Home.razor.css | UR-11 | ✅ |
| SW-UIW-03 | Score buttons shall be enabled for any unfilled cell on an enabled card once rolled; picking a valid cell scores and ends the turn immediately; picking a cell that is not valid shall ask to confirm a scratch (SW-TRN-07, SW-TRN-08). Empty cells shall stay blank: no points preview is shown (SW-SCR-01). | `GetPickStatus`, `ApplyScoreAsync` | UR-03 | ✅ |
| SW-UIW-04 | Feedback messages (scratch, Yahtzee, invalid) shall appear in a notice banner that is cleared on undo and on a new game. | `_noticeText` | UR-03 | ✅ |
| SW-UIW-05 | Mode selection, pen colour, resume and confirmation prompts shall use in-page modal overlays that behave as accessible dialogs (SW-UIW-12). | Home.razor | UR-01 | ✅ |
| SW-UIW-06 | Dice buttons shall be disabled unless holding is allowed. | Home.razor | UR-02 | ✅ |
| SW-UIW-07 | The layout shall adapt with CSS: one score table (SW-UIW-09) is used on every screen, with the wide layout adding a side-by-side turn card. All six cards are always shown, without tabs or a card selector. | Home.razor.css | UR-11 | ✅ |
| SW-UIW-08 | The scorecard shall use real-card layout and wording: "Upper Section" and "Lower Section" headings, a "Bonus 35 if 63+" label, a "Yahtzee Bonus" row of check marks, and the official box order and names. | Home.razor | UR-01 | ✅ |
| SW-UIW-09 | **Fill the screen.** The page shall use the full viewport height and in the wide layout the category rows shall stretch to use all the height left after the turn area, so a full game can be played without scrolling. Landscape screens at least 760 px wide and 560 px tall (smallest target 1292×698) put the turn card in a column beside the score card. Portrait and narrow screens stack a compact turn card above or below the same table. The turn card (dice, Roll, Undo) shall always stay visible: the page itself never scrolls, and only the score card scrolls, inside its own area with a sticky header row, when the viewport is shorter than the minimum row height allows (about 360×650 and shorter on phones, short landscape phones). | Home.razor, Home.razor.css | UR-11 | ✅ |
| SW-UIW-10 | **Compact table on narrow and portrait screens.** The same six-column table is used: category, Odds and G1–G6 on one row per category, with section headings, Upper Pace, Bonus, Yahtzee Bonus and totals. The turn card is compact (status with Undo beside the turn number, dice, full-width Roll with roll count) and the only total shown is the Card Total row in the score table (there is no total in the top bar). Dice are always square at every screen size (never stretched or capped by height). Roll, Undo and New Game shall be at least 44 px tall. The 13 category rows (the click targets) shall stretch to use all the height left after the turn area, while the heading, Upper Pace, Upper Total, Bonus, Yahtzee Bonus, Lower Total, Game Total and Card Total rows stay compact (about 15 px). Category rows are about 33 px tall at 844 px and keep a minimum of about 18 px on short phones, so the whole table fits; the turn card stays compact. They are a recorded exception to the 44 px target size; the scratch confirm (SW-UIW-03) and Undo mitigate mistaken taps. At 320 px wide there shall be no horizontal page scrolling. | Home.razor.css | UR-11 | ✅ |
| SW-UIW-11 | Score cells shall be distinguishable without relying on colour alone: filled (value or X, pen colour, solid heavy border), open and valid (solid green border and green fill), open and scratch-only (plain default cell, no highlight, so only cells that score light up), unavailable (flat, faded, no border). Cells made unavailable by the Single mode Joker rule are unavailable. | Home.razor, Home.razor.css | UR-11 | ✅ |
| SW-UIW-12 | **Accessibility.** Every score button shall have an accessible name giving category, game and state (filled with value or X, open and scores, open and scratch only, unavailable). Each die shall have an accessible name giving its face, and `aria-pressed` for held; after the third roll dice are final and not announced as held. All actions shall be keyboard operable with a visible focus indicator. Roll progress, scores, scratches, undo and game completion shall be announced through a polite live region without re-reading the scorecard. Modal overlays shall have `role="dialog"`, a name, and move focus into the dialog; the page behind is inert while one is open. Escape cancels the mode and confirm prompts and, in the Yahtzee pen picker, keeps the current colour; it never dismisses the mandatory pen picker (SW-PEN-03) or the resume prompt. Motion (dice, splash) shall respect `prefers-reduced-motion`. | Home.razor, Home.razor.css | UR-11 | ✅ |
| SW-UIW-13 | Interface copy shall be short and plain. The status line shall show the mode, turn progress ("Roll 2 of 3") and a single next step ("Choose a score"), keeping the Joker explanation in Single mode and the "Tap New Game" prompt before a game starts (SW-MOD-01). | Home.razor | UR-11 | ✅ |
| SW-UIW-14 | **Dice position.** In landscape (wide layout) a top-bar button shall move the turn card with the dice to the left or the right of the score card; the default is the right (right-handed play). In portrait and narrow layouts a top-bar button shall move it above or below the score card; the default is the bottom. Each choice is remembered in browser storage (`yahtzeesim.diceSide`, `yahtzeesim.diceEdge`), the buttons are at least 44 px, name the current and target position, and announce the change. | Home.razor, Home.razor.css | UR-11 | ✅ |

### 3.9 Platform, packaging and reliability (PLT)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-PLT-01 | The product shall be delivered as one web-based solution (the Web PWA) running on Android, Windows and iPhone. The MAUI and WinForms projects shall be removed (history stays in git). Revisit if a single web solution proves not possible. | [YahtzeeSim.Web.csproj](../YahtzeeSim.Web/YahtzeeSim.Web.csproj) | UR-12 | 🟡 Done in code (MAUI and WinForms removed); device testing on Android and iPhone still needed |
| SW-PLT-02 | The Web app shall be a Blazor WebAssembly (net9) static site. The only server code is the small account and leaderboard API (section 3.11), run as Cloudflare Pages Functions with a D1 database. Game play itself needs no server. | [YahtzeeSim.Web.csproj](../YahtzeeSim.Web/YahtzeeSim.Web.csproj), [functions](../functions) | UR-12 | ✅ |
| SW-PLT-03 | There shall be exactly one game engine: a class library `YahtzeeSim.Engine` (namespace `YahtzeeSim.Engine`) referenced by Web and the test project. No other copy of game logic shall exist. | YahtzeeSim.Web.csproj | UR-14 | ✅ |
| SW-PLT-04 | The Web app shall publish a PWA manifest (standalone, any orientation, theme colour, dice-themed 192/512 and maskable icons) and Apple meta tags (standalone web-app mode, 180×180 touch icon, default status bar) so it can be added to an iOS/iPadOS/Android home screen and opens as a full-screen app. | [manifest.webmanifest](../YahtzeeSim.Web/wwwroot/manifest.webmanifest), [index.html](../YahtzeeSim.Web/wwwroot/index.html) | UR-12 | ✅ |
| SW-PLT-05 | The Web app shall work offline once installed. A service worker shall cache the app shell and serve it network-first with a cached fallback, so users get the latest version when online. A stale or broken cache shall never block startup; "Reset app data" (SW-PLT-06) remains the recovery path. The worker shall never answer `/api/*` (including the sign-in redirects) from its cache. Because sign-in is required (SW-ACC-01), offline start relies on the cached profile (SW-ACC-07). | index.html, [service-worker.published.js](../YahtzeeSim.Web/wwwroot/service-worker.published.js) | UR-13 | ✅ Verified on a published build: the app loads and a game can be played and finished with the server stopped |
| SW-PLT-06 | The Web app shall show an error bar with "Reload" and "Reset app data" (unregister workers, clear caches, reload). | index.html (`__yahtzeeSimResetApp`) | UR-13 | ✅ |
| SW-PLT-07 | Game state shall be autosaved after every action. On launch the app shall offer to resume the saved game, and New Game shall clear it. Corrupt or incompatible saved data shall be discarded. | Web `PersistAsync` (localStorage keys `yahtzeesim.save`, `yahtzeesim.pen`), engine `Serialize`/`TryLoad` | UR-01 | ✅ |

### 3.10 Build, deployment and quality (OPS, NFR)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-OPS-01 | A GitHub Actions workflow shall deploy the Web app and API on push to `main` (paths for the Web, Engine, tests, `functions/`, `migrations/`, `wrangler.toml`, `package*.json` or the workflow file) and on manual dispatch. | [deploy-cloudflare-pages.yml](../.github/workflows/deploy-cloudflare-pages.yml) | UR-14 | ✅ |
| SW-OPS-02 | The workflow shall validate the three Cloudflare secrets, type-check and test the API, publish Release, verify the Pages project exists (listing available projects on failure), apply pending D1 migrations (skipped with a warning while `wrangler.toml` still has the placeholder database id), then deploy with Wrangler to production from the repository root so `functions/` is included. | same | UR-14 | ✅ |
| SW-OPS-03 | A local PowerShell script shall publish and deploy using the same environment variables, from the repository root. | [deploy-cloudflare-pages.ps1](../scripts/deploy-cloudflare-pages.ps1) | UR-14 | ✅ |
| SW-OPS-04 | The host shall send `no-store` for `index.html`, service-worker files and the manifest, and `nosniff` for all files. | [_headers](../YahtzeeSim.Web/wwwroot/_headers) | UR-13 | ✅ |
| SW-OPS-05 | Engine unit tests shall cover every SW-RUL, SW-TRN, SW-SCR and SW-UND requirement, and the API tests (vitest, `functions-tests/`) shall cover validation, sessions and leaderboard logic; these and the strategy analysis tests run in CI on every push, and a failure blocks the deploy. | [YahtzeeSim.Engine.Tests](../YahtzeeSim.Engine.Tests), [ci.yml](../.github/workflows/ci.yml), [deploy-cloudflare-pages.yml](../.github/workflows/deploy-cloudflare-pages.yml) | UR-14 | ✅ |
| SW-NFR-01 | The engine shall have no dependency on any UI framework. | Engine folder | UR-14 | ✅ |
| SW-NFR-02 | Updating the odds after a roll, undo or hold toggle shall take under 100 ms on a mid-range phone; if it exceeds that, the calculation shall move off the UI thread. | `ProbabilityCalculator` | UR-11 | 🟡 Engine test asserts under 100 ms natively; in an unoptimised browser build the odds are drawn after the dice so the UI stays responsive. On-phone timing still to be confirmed |
| SW-NFR-03 | The number of cards per sheet shall be fixed at six (`GameCount`). | `YahtzeeGameEngine.GameCount` | UR-05 | ✅ |

### 3.11 Accounts and leaderboard (ACC, LDB)

Accounts use the provider's own sign-in; the app never handles or stores a password. Only Google is enabled; Facebook can be added as another entry in [providers.ts](../functions/_lib/providers.ts). Scores are trusted from the client and only sanity-checked (SW-LDB-03).

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-ACC-01 | The app shall require sign-in before play. The sign-in screen offers "Sign in with Google" and a link to the privacy page. | [SignInGate.razor](../YahtzeeSim.Web/Components/SignInGate.razor), [MainLayout.razor](../YahtzeeSim.Web/Layout/MainLayout.razor) | UR-15 | ✅ |
| SW-ACC-02 | Sign-in shall use the OAuth authorization-code flow with PKCE, state and nonce, performed on the server (`/api/auth/login`, `/api/auth/callback`), requesting only the `openid` scope. The ID token claims (issuer, audience, expiry, nonce, subject) shall be checked. The client secret shall only exist as a Pages secret. | [functions/api/auth](../functions/api/auth), [providers.ts](../functions/_lib/providers.ts) | UR-15 | ✅ Verified locally up to the Google redirect; the real round trip needs the owner's Google credentials |
| SW-ACC-03 | A user shall be identified by (provider, provider subject id). No email, name or photo shall be stored. | [0001_init.sql](../migrations/0001_init.sql) | UR-15 | ✅ |
| SW-ACC-04 | A signed-in session shall be a random token in an `HttpOnly`, `SameSite=Lax`, `Secure` (on https) cookie valid for 30 days. Only the SHA-256 hash of the token shall be stored. Sign-out shall delete the session and cookie. | [http.ts](../functions/_lib/http.ts), [cookies.ts](../functions/_lib/cookies.ts), [logout.ts](../functions/api/auth/logout.ts) | UR-15 | ✅ |
| SW-ACC-05 | A first-time user shall pick a unique nickname (3 to 16 letters, numbers, spaces, `_` or `-`, starting and ending with a letter or number; uniqueness is case-insensitive) before playing. | [NicknameGate.razor](../YahtzeeSim.Web/Components/NicknameGate.razor), [validate.ts](../functions/_lib/validate.ts), [me.ts](../functions/api/me.ts) | UR-15 | ✅ |
| SW-ACC-06 | State-changing API calls shall require a JSON content type and, when an `Origin` header is sent, the same origin as the request (CSRF protection). API responses shall be `no-store`. | [http.ts](../functions/_lib/http.ts) | UR-15 | ✅ |
| SW-ACC-07 | When the server cannot be reached (offline, 5xx or a non-JSON reply) the app shall use the cached profile from the last successful sign-in so the game stays playable; a definite 401 shall sign the user out. | [AccountService.cs](../YahtzeeSim.Web/Services/AccountService.cs) | UR-13, UR-17 | ✅ |
| SW-ACC-08 | An account menu shall show the signed-in nickname and a Sign out button. A privacy page (`/privacy`) shall explain what is stored. | [Home.razor](../YahtzeeSim.Web/Pages/Home.razor), [Privacy.razor](../YahtzeeSim.Web/Pages/Privacy.razor) | UR-15 | ✅ |
| SW-ACC-09 | A signed-in user shall be able to change their nickname from the account menu under the same rules as SW-ACC-05. The account is identified by its internal `id`, so the new nickname applies to all of the user's existing scores and leaderboard rows. | [Home.razor](../YahtzeeSim.Web/Pages/Home.razor), [me.ts](../functions/api/me.ts) | UR-15 | ✅ |
| SW-LDB-01 | Each game shall have a stable `GameId` that survives save/load. A completed game exposes `FinalScore` (the game total in Single mode, the card total in Full Sheet mode). | [YahtzeeGameEngine.cs](../YahtzeeSim.Engine/YahtzeeGameEngine.cs) | UR-16 | ✅ |
| SW-LDB-02 | When a game completes, the app shall submit (game id, mode, total) once. Re-sending the same game id replaces the earlier score, so undoing and re-scoring the last box does not create duplicates. | [Home.razor](../YahtzeeSim.Web/Pages/Home.razor), [scores.ts](../functions/api/scores.ts) | UR-16 | ✅ |
| SW-LDB-03 | The server shall reject totals outside the possible range (Single 0 to 1575, Full Sheet 0 to 2250), unknown modes, users without a nickname, and more than 20 new scores per hour. | [validate.ts](../functions/_lib/validate.ts), [scores.ts](../functions/api/scores.ts) | UR-16 | ✅ |
| SW-LDB-04 | A game finished offline shall be queued in browser storage and uploaded after the app is back online or on the next launch. Entries are tagged with the user id, retried on temporary failures and dropped on permanent ones. | [ScoreQueue.cs](../YahtzeeSim.Web/Services/ScoreQueue.cs) | UR-17 | ✅ Verified: played a game offline, score queued, uploaded after reconnect |
| SW-LDB-05 | The leaderboard shall rank each player by their best score, with separate Single and Full Sheet boards and All-time and This-week (calendar week from Monday 00:00 UTC) views. It opens on Full Sheet and This week, the most common play. It shows the top 50, shares the rank on ties, highlights the signed-in player and shows their rank even when outside the top 50. | [leaderboard.ts](../functions/api/leaderboard.ts), [LeaderboardView.razor](../YahtzeeSim.Web/Components/LeaderboardView.razor) | UR-16 | ✅ |
| SW-LDB-06 | The leaderboard is opened from a top-bar button as a dialog over the game (so a game in progress is never lost) and requires an internet connection. | [Home.razor](../YahtzeeSim.Web/Pages/Home.razor) | UR-16 | ✅ |
| SW-LOG-01 | For a signed-in player the app shall record each Roll (dice after the roll), Hold and Unhold press (which die and the dice at the time, so hold state is rebuilt by replay and a die held then released before a roll shows as a change of mind), Score and Scratch (dice scored, card, category, points, Yahtzee bonus) and Undo (dice after), each with a per-game sequence number, a client timestamp (UTC milliseconds), the turn number and the roll number. Starting a new game is not recorded. | [ActionLog.cs](../YahtzeeSim.Web/Services/ActionLog.cs), [Home.razor](../YahtzeeSim.Web/Pages/Home.razor) | UR-18 | ✅ |
| SW-LOG-02 | Actions shall be queued in browser storage, tagged with the user id, and uploaded in batches of up to 100 (after each score or undo, on launch and on reconnect). Temporary failures are retried; permanent rejections are dropped. Play never waits for the upload. | [ActionLog.cs](../YahtzeeSim.Web/Services/ActionLog.cs), [MainLayout.razor](../YahtzeeSim.Web/Layout/MainLayout.razor) | UR-18 | ✅ |
| SW-LOG-03 | `POST /api/actions` shall require a signed-in user and reject malformed batches (unknown action, impossible dice, turn, card or points, fields that do not belong to the action), more than 10,000 actions per hour and more than 5,000 per game. Re-sending a (game, sequence number) shall be ignored. Rows are stored in `game_actions`. | [actions.ts](../functions/api/actions.ts), [validate.ts](../functions/_lib/validate.ts), [0002_game_actions.sql](../migrations/0002_game_actions.sql) | UR-18 | ✅ |

### 3.12 Full Sheet strategy analysis (ANA)

Library [YahtzeeSim.Analysis](../YahtzeeSim.Analysis) (shared by the script and the app), console tool [YahtzeeSim.Analysis.Cli](../YahtzeeSim.Analysis.Cli), entry point [analyze-strategy.ps1](../scripts/analyze-strategy.ps1), tests [YahtzeeSim.Analysis.Tests](../YahtzeeSim.Analysis.Tests). Players see their own report in the app (SW-ANA-09 to 11).

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-ANA-01 | The analysis shall read the CSV export (`users`, `scores`, `actions`) and use only Full Sheet games that have a submitted score. Each is replayed from its actions (holds rebuilt from Hold/Unhold presses, Undo restores the turn and drops rolls of the next turn) and checked: contiguous sequence numbers, held dice unchanged on reroll, points matching the engine's `ScoreRules`, 78 filled boxes and a replayed total equal to the submitted total. Games that fail, unfinished games and Single games are listed with the reason and left out. | [GameReplayer.cs](../YahtzeeSim.Analysis/Replay/GameReplayer.cs), [AnalysisRunner.cs](../YahtzeeSim.Analysis/AnalysisRunner.cs) | UR-19 | ✅ |
| SW-ANA-02 | A single-card solver shall compute, by backward induction over filled boxes and upper sum (capped at 63), the exact expected future points of any card under Full Sheet rules (no Yahtzee bonus, no Joker), stored in single precision. The value of an empty card is 245.87. The table is cached in a compact file format and rejected when the scoring rules change. | [CardSolver.cs](../YahtzeeSim.Analysis/Solver/CardSolver.cs), [DiceTables.cs](../YahtzeeSim.Analysis/Solver/DiceTables.cs) | UR-19 | ✅ |
| SW-ANA-03 | The reference strategy shall value a placement as points now (plus any upper bonus reached) plus the change in that card's expected future points, choose the best open box on any of the six cards, and choose keeps that maximise the expected value of that choice. It and a greedy baseline (points now only) shall be simulated with seeded dice. | [SheetEvaluator.cs](../YahtzeeSim.Analysis/Solver/SheetEvaluator.cs), [BotSimulator.cs](../YahtzeeSim.Analysis/Solver/BotSimulator.cs) | UR-19 | ✅ |
| SW-ANA-04 | Every keep after roll 1 and 2, every early stop and every box choice shall be graded: cost = value of the reference choice − value of the player's choice (never negative). Each game total shall split exactly into baseline (6 × 245.87) + Full Sheet choice + dice luck − decision cost, and the luck-free score (total − luck) shall be reported. | [DecisionGrader.cs](../YahtzeeSim.Analysis/Grading/DecisionGrader.cs) | UR-19 | ✅ |
| SW-ANA-05 | A descriptive profile per player shall report results per box against the reference, upper bonus rate and average count per face, when boxes get filled, cards in progress, card switches, rolls used, keep patterns (fixed classification) against the reference, decision quality by type, think time, changes of mind and undo use. | [StrategyProfile.cs](../YahtzeeSim.Analysis/Profile/StrategyProfile.cs), [KeepPatterns.cs](../YahtzeeSim.Analysis/Grading/KeepPatterns.cs) | UR-19 | ✅ |
| SW-ANA-06 | Findings shall come only from a fixed, versioned rule table. Each rule has an id, an exact condition, a threshold and a sentence template filled with the counted evidence (cases, opportunities, rate, points lost, examples). Rules that do not reach their threshold are listed as checked. No AI or free-text judgement is produced. | [StrategyRules.cs](../YahtzeeSim.Analysis/Rules/StrategyRules.cs) | UR-19 | ✅ |
| SW-ANA-07 | `scripts/analyze-strategy.ps1` shall write a self-contained HTML report (index and one page per player, in the app's colours, no scripts or external files), a Markdown file per player and `decisions.csv` to `exports/analysis/` (git-ignored personal data). The same export, seed and simulation count shall give byte-identical output. | [HtmlReport.cs](../YahtzeeSim.Analysis/Reports/HtmlReport.cs), [TextReports.cs](../YahtzeeSim.Analysis/Reports/TextReports.cs), [analyze-strategy.ps1](../scripts/analyze-strategy.ps1) | UR-19 | ✅ |
| SW-ANA-08 | Analysis tests shall cover the solver (published optimum, closed-form cases, simulation agreement), replay checks, the exact score split, every rule firing and staying quiet, report determinism, the embeddable report (scoped styles, no scripts or links) and that the files shipped with the app match what the code computes; they run in CI. | [YahtzeeSim.Analysis.Tests](../YahtzeeSim.Analysis.Tests), [ci.yml](../.github/workflows/ci.yml) | UR-19 | ✅ |
| SW-ANA-09 | `GET /api/strategy` shall require a signed-in user and return only that user's last 10 finished Full Sheet games (total, time) with their recorded actions in compact form, oldest first. | [strategy.ts](../functions/api/strategy.ts), [strategy.ts (lib)](../functions/_lib/strategy.ts), [strategy.test.ts](../functions-tests/strategy.test.ts) | UR-19 | ✅ |
| SW-ANA-10 | The account dialog shall offer **My strategy**, which opens a dialog that loads the player's games, runs the same analysis in the browser and shows the same report as the script (player sections, how it works, rules), with loading, no-games, offline and signed-out states. Example rows wrap into cards on phones. | [StrategyService.cs](../YahtzeeSim.Web/Services/StrategyService.cs), [StrategyView.razor](../YahtzeeSim.Web/Components/StrategyView.razor), [Home.razor](../YahtzeeSim.Web/Pages/Home.razor) | UR-19 | ✅ |
| SW-ANA-11 | The app shall ship the solver table (`card-values.bin`, about 1.4 MB, downloaded only when the report is opened) and the seeded reference simulations (`reference.json`) in `wwwroot/analysis/`, written by `YahtzeeSim.Analysis.Cli --write-app-assets`, so the browser never solves or simulates. Both carry a rules fingerprint and are refused if the rules change. | [CardSolver.cs](../YahtzeeSim.Analysis/Solver/CardSolver.cs), [ReferenceData.cs](../YahtzeeSim.Analysis/Solver/ReferenceData.cs), [wwwroot/analysis](../YahtzeeSim.Web/wwwroot/analysis) | UR-19 | ✅ |
| SW-ANA-12 | Every decision after roll 1 and 2 shall be classified by fixed rules: the roll (five/four of a kind, full house, large straight, four in a row, three of a kind, two pair, pair with three in a row, pair, three in a row, nothing) and the response (stopped, kept matching dice, kept a straight draw, kept one die, rerolled all, mixed). | [KeepMoments.cs](../YahtzeeSim.Analysis/Story/KeepMoments.cs) | UR-20 | ✅ |
| SW-ANA-13 | A versioned story (`StoryRules`) shall describe the player's last 10 finished Full Sheet games: the badge with its evidence; up to 4 "when your first roll shows…" lines (situations seen at least 4 times; "almost always" ≥ 80%, "usually" ≥ 60%, "more often than not" ≥ 50%, otherwise the two most common choices) each with an example roll and the kept dice; how the sheet is filled; highlights; and one tip, taken from the fired habit rule with the most points lost (never the overall-cost rule), with an example from the player's own game. | [StoryRules.cs](../YahtzeeSim.Analysis/Story/StoryRules.cs), [StoryReport.cs](../YahtzeeSim.Analysis/Reports/StoryReport.cs) | UR-20 | ✅ Built; shown in the research app and script, not yet in the web app |
| SW-ANA-14 | A versioned badge table (`BadgeRules`) shall give each player one play-style badge, never a skill rating: Yahtzee Hunter / Straight Shooter (keeping matching dice or the straight draw on four-in-a-row and pair-with-three-in-a-row rolls, compared with the reference strategy's rates in the same situations), Bonus Banker, Card Closer, Card Juggler, Sure Thing, Quick Draw, Deep Thinker; the qualifying badge with the greatest strength wins (ties by table order), otherwise All-Rounder. Badges use replayed games only (no solver). | [BadgeRules.cs](../YahtzeeSim.Analysis/Story/BadgeRules.cs) | UR-20 | ✅ Built; not yet on the leaderboard |
| SW-ANA-15 | The Windows research app shall pull data with the export script, analyse every player with the shipped solver table and reference data, and show an overview, each player's full report, the story preview and the badge metrics (against thresholds and the reference strategy); it also saves the reports to `exports/analysis/`. CI compiles it. | [YahtzeeSim.Research](../YahtzeeSim.Research), [research.ps1](../scripts/research.ps1) | UR-20 | ✅ |

## 4. Traceability matrix (UR → SW)

| UR | Software requirements |
|---|---|
| UR-01 Play a complete game | RUL-02, RUL-09, RUL-10, RUL-12, TRN-01, TRN-09, TRN-11, MOD-01, MOD-02, MOD-03, MOD-04, UIW-08, UIW-05, PLT-07 |
| UR-02 Roll and hold | RUL-01, TRN-02, TRN-03, TRN-04, TRN-05, TRN-10, UIW-06 |
| UR-03 Score / scratch | TRN-06, TRN-07, TRN-08, SCR-01, CEL-02, UIW-03, UIW-04 |
| UR-04 Standard rules | RUL-03, RUL-04, RUL-05, RUL-06, RUL-07, RUL-08, RUL-11 |
| UR-05 Individual or six games at once | MOD-02, MOD-04, MOD-05, MOD-06, SCR-06, NFR-03 |
| UR-06 Odds and pace | PRB-01 … PRB-06, SCR-07, SCR-08 |
| UR-07 Undo | UND-01, UND-02, UND-03 |
| UR-08 Pen colours | SCR-02, PEN-01 … PEN-06 |
| UR-09 Yahtzee celebration | CEL-01 |
| UR-10 Automatic totals | SCR-03, SCR-04, SCR-05 |
| UR-11 Touch / responsive | UIW-02, UIW-07, UIW-09, UIW-10, UIW-11, UIW-12, UIW-13, NFR-02 |
| UR-12 Cross-platform | UIW-01, PLT-01, PLT-02, PLT-04 |
| UR-13 Reliable start / recovery | PLT-05, PLT-06, OPS-04, ACC-07 |
| UR-14 Maintainable / deployable | PLT-03, OPS-01, OPS-02, OPS-03, OPS-05, NFR-01 |
| UR-15 Sign in and nickname | ACC-01 … ACC-09 |
| UR-16 Leaderboard | LDB-01, LDB-02, LDB-03, LDB-05, LDB-06 |
| UR-17 Offline games still count | ACC-07, LDB-04 |
| UR-18 Record gameplay for analysis | LOG-01, LOG-02, LOG-03 |
| UR-19 Full Sheet strategy analysis | ANA-01 … ANA-11 |
| UR-20 Play-style story and badge | ANA-12 … ANA-15 |

## 5. Remaining work

The restructure, clean-up and all decided behaviour are done. What is left:

1. **Device testing** on Android and iPhone (SW-PLT-01), including installing the PWA and confirming offline start (SW-PLT-05).
2. **Measure the odds update on a phone** (SW-NFR-02). If it is over 100 ms, reduce the Monte-Carlo trial count or move the work to a Web Worker.
3. **Move shared presentation logic out of the page** (pace text, card-total display, contrast colour) into the engine library so it can be unit tested.
4. **Optional:** hide probabilities for filled categories (declined for now, SW-PRB-06).
5. **Accounts and leaderboard (one-time owner setup):** create the Google OAuth client and the D1 database and add the Pages secrets (see the README); test a real Google sign-in. Add Facebook later as a second provider. Scores are trusted from the client, so add stronger anti-cheat only if abuse appears.

## 6. Decisions

Decided with the owner:

| Topic | Decision |
|---|---|
| Yahtzee bonus and Joker (SW-RUL-11) | Official rules in Single mode only; Full Sheet unchanged (no bonus, no Joker, five-of-a-kind is not a Full House). |
| Scratching (SW-TRN-07/08) | Allowed after any roll, with a confirmation; a valid category always scores its value and cannot be scratched. |
| Yahtzee celebration (SW-CEL-01, SW-PEN-04) | Splash when rolled (about 2 s or tap); pen picker when five of a kind is scored in any box. |
| Offline (SW-PLT-05) | Wanted: service worker, network-first with cached fallback. |
| Platforms and engine (SW-PLT-01, SW-PLT-03) | One web-based solution. Delete MAUI and WinForms now. One engine in a new `YahtzeeSim.Engine` class library, referenced by Web and the tests. Revisit if a single web solution is not possible. |
| Bonus pace (SW-SCR-07/08) | Existing per-game Upper Pace row (+/- number and colour tone) is sufficient, in both modes. |
| Probabilities (SW-PRB-04/05/06) | Seeded Monte-Carlo, recalculated on hold toggle; still shown for filled categories. |
| New Game (SW-MOD-03) | Confirms before abandoning a started game; no prompt when nothing has been played. |
| Single mode layout (SW-MOD-04) | Unused columns stay visible, greyed out, at every width. |
| Redesign (SW-UIW-09 to 14) | Fill the screen, scroll only if absolutely necessary, on landscape and on phone portrait. One compact six-column table everywhere (no tabs). No points preview in open cells. Visual style follows the concept (dark forest, mint, gold). Score cells below 44 px on phones is an accepted trade-off to avoid scrolling. Dice stay visible and only the score card scrolls. Dice dock: right in landscape, bottom in portrait, user-switchable and remembered. |
| Persistence (SW-PLT-07) | Autosave with a resume prompt. |
| Card look (SW-UIW-08) | Real-card headings, bonus label, Yahtzee Bonus check marks, official box order and names. |
| Performance (SW-NFR-02) | Under 100 ms on a mid-range phone. |
| Tests (SW-OPS-05) | Engine unit tests in CI, gating the deploy. |

No open questions remain.
