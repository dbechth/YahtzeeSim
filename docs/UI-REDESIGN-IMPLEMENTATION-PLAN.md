# YahtzeeSim UI redesign implementation plan

## Purpose

Refresh the Web UI so the six-card Full Sheet is easy to read and play on desktop and mobile. Keep the familiar scorecard, make the current dice and which score destinations are open clear, and remove repeated or artificial-sounding interface copy.

This is a UI implementation plan, not a request to change the rules or engine. The visual concept at [YahtzeeSim-redesign-concept.html](../YahtzeeSim-redesign-concept.html) is a reference for layout, colour and responsive treatment. It is a prototype, not a source of gameplay requirements; do not copy its placeholder data, omitted features or interaction behavior without checking this plan and the existing product behavior.

## Correct gameplay model

Full Sheet is six independent scorecards played in parallel over one sequence of turns.

1. A turn consists of one set of five dice and up to three rolls. The player may hold dice between rolls.
2. After any roll, the player may choose one unfilled category on one enabled card and record the current dice there. A valid score ends the turn immediately.
3. A category that is not valid for the current dice may be scratched after confirmation. Recording the scratch also ends the turn immediately.
4. The dice reset after that single score or scratch. The next turn may target any unfilled category on any incomplete card.
5. The same category may be scored on different cards on different turns. For example, a Full House scored on G1 does not prevent scoring a later Full House on G2.
6. A cell can be filled only once per card. A completed card is unavailable, but the other incomplete cards remain available.
7. Full Sheet is complete only when all six cards are complete. At that time, show the existing per-card summary and combined total.

In particular, Full Sheet does **not** mean six sequential games, one shared hand scored on all six cards, or six score entries from one turn. The existing engine already models one score per turn and accepts a destination card and category for that score. Keep it as the rules source of truth; do not add multi-score-per-turn behavior.

## Current implementation context

- The interactive page and scorecard are in [Home.razor](../YahtzeeSim.Web/Pages/Home.razor). It renders the six game columns and sends a selected game/category to the engine.
- Responsive layout and visual styling are in [Home.razor.css](../YahtzeeSim.Web/Pages/Home.razor.css).
- Turn, card availability, scoring, and undo behavior are in [YahtzeeGameEngine.cs](../YahtzeeSim.Engine/YahtzeeGameEngine.cs).
- The requirements currently describe Full Sheet destinations in SW-MOD-05 and one-score turn flow in SW-TRN-07 through SW-TRN-09 in [REQUIREMENTS.md](./REQUIREMENTS.md). Those lines already describe one score per turn; only the SW-MOD-05 wording ("each turn's dice may be scored to any unfilled cell") is ambiguous and should be tightened. SW-UIW-01, SW-UIW-07 and SW-UIW-09 will need updating after the redesign (see step 7). Implementation must follow the confirmed one-score-per-turn behavior above.
- Engine rules already have focused coverage in [TurnFlowTests.cs](../YahtzeeSim.Engine.Tests/TurnFlowTests.cs) and [ScoringAndUndoTests.cs](../YahtzeeSim.Engine.Tests/ScoringAndUndoTests.cs).

## Design direction

### Desktop and wide screens

- Retain the side-by-side composition: the dice and turn controls on the left; the full scorecard on the right.
- Keep G1 through G6 visible together as columns. Do not introduce a game selector, tabs, or a selected-card-only view.
- Keep the score sheet as the main destination-selection surface. A player must be able to compare the current hand against all six cards without changing views.
- Keep the title and current turn state compact. Give dice, Roll, Undo, score destinations, category names and totals the strongest hierarchy.
- Follow the visual language of `YahtzeeSim-redesign-concept.html`: dark forest page with a rounded cream app shell, a dark topbar with the YahtzeeSim brand, a dark-green turn card (roll-progress segments, cream dice that lift and gain a gold border when held, a mint Roll button and a text Undo) beside a cream scorecard card with a serif header and uppercase column labels. Do not copy its placeholder data, Settings button, tap-again scratch confirmation or omitted features. Use color as reinforcement, not the only signal for state.
- Design goal (SW-UIW-09): fill the screen. The page uses the full viewport height and the score rows stretch to use the height left over, so a full game plays without scrolling. The turn card (dice, Roll, Undo) is always visible: the page never scrolls, and only the score card scrolls (with a sticky header row) when the viewport is too short for the minimum row height. The wide layout (turn card beside the sheet) applies to landscape screens at least 760 x 560.

### Phone and narrow screens

- Keep all six games available on the same screen and make each destination identifiable as G1 through G6. Do not hide other cards behind tabs or require switching to a different game to place the next result.
- Use the same compact six-column table as the wide layout: one row per category with its name, Odds and six score cells labelled G1 to G6 in the header. Each cell maps to exactly one card. Keep the Upper and Lower Section headings, the Bonus label, the Yahtzee Bonus row, and the per-card Upper Pace, Upper Total, Lower Total, Game Total and Card Total values (SW-SCR-03, 06, 07; SW-UIW-08).
- Ensure all six columns fit at 320 px wide without horizontal page scrolling.
- Put a compact turn card (status, dice, Roll with roll count, Undo) above or below the table, with the total in the Card Total row of the table. Hide the one-line hint on short portrait screens.
- Dice, Roll, Undo and New Game stay at least 44 px tall. Score cells fill their table cell (about 24 px tall at 844 px, about 17 px at 664 px). This is a recorded exception to the 44 px target, chosen by the product owner to avoid scrolling; the scratch confirm and Undo mitigate mistaken taps.

## Content and interaction requirements

- Use short, ordinary language. Avoid slogan-like AI copy, repeated instructions, and label fragments joined by decorative separators such as middle dots. Prefer concise sentences such as “Roll up to three times, then score one open box.”
- Show turn progress in a compact form such as “Roll 2 of 3” and “Choose a score”. Keep the Joker status text in Single mode.
- Do not show a points preview in open cells. Empty cells stay blank (SW-SCR-01); the player works out the score, and the app does not add scoring hints beyond the existing Odds and Upper Pace. This also keeps scoring rules solely in the engine.
- Preserve the existing Odds and Upper Pace guidance. Odds stay visible for every category, including filled ones (SW-PRB-06), and blank before the first roll. The mobile treatment may reposition or abbreviate these values, but must not remove them. Upper Pace stays per card, with `--` for disabled cards.
- Visually distinguish filled cells, available cells, scratch-only cells and unavailable cells (including cells made unavailable by the Single mode Joker rule). Keep the score or X visible for filled cells. Do not rely on color alone, and do not show an X-like mark in a cell that has not been scratched. Filled cells keep the player's pen colour (SW-PEN-06); choose state styling that cannot be confused with arbitrary pen colours, and keep filled text legible.
- A valid open-cell selection records that one score and advances the turn immediately. A scratch-only selection keeps the existing confirmation step; confirming it records X and advances the turn. Never ask for six choices or wait for every card to receive a score.
- Keep New Game mode selection and abandon confirmation, save/resume prompts, pen selection, Yahtzee celebration, Undo, per-card totals and the Full Sheet completion summary. The redesign is not authorization to remove or change these flows.
- Preserve the existing Single mode behavior. G2–G6 stay visible and greyed out (SW-MOD-04) at every width; narrow screens may resize them but not hide them. Gameplay must remain one active card with the existing inactive-card semantics unless the product owner separately approves a change.

## Accessibility and input

- Give every score button an accessible name identifying its category and game, plus whether it is available, already filled, a valid score or a scratch.
- Give each die an accessible name for its face and held state. Expose held state semantically, such as with `aria-pressed`. The engine flags all dice held after the third roll (SW-TRN-05); do not announce that as the player holding every die.
- Keep all actions keyboard operable with a visible focus indicator. Confirm that focus order follows the reading and play order.
- Announce meaningful state changes, including roll progress, scoring, scratch confirmation and game completion, without repeatedly reading the full scorecard.
- Maintain readable contrast, support browser zoom/reflow, and respect reduced-motion preferences for dice or celebration effects.
- Treat the color picker and confirmation/resume overlays as accessible dialogs with clear names and keyboard focus behavior. The pen picker after New Game is mandatory (SW-PEN-03), so Escape and outside clicks must not dismiss it; it has no other entry point (SW-PEN-05). Do not add the prototype's Settings button.

## Implementation sequence

1. **Establish behavior baseline.** Run the existing engine test project. In the running app, verify Single and Full Sheet turn flow, valid scoring, scratch confirmation, Undo, odds, upper pace, saving/resume, colour marks and completion summary. Record any pre-existing failures separately.
2. **Restructure the Blazor markup.** In `Home.razor`, keep the engine and current event handlers as the source of behavior. Refactor the scorecard presentation so desktop retains all six columns and narrow layouts present all six destinations per category. Keep cells bound to the correct `(gameIndex, category)` pair.
3. **Update responsive styles.** In `Home.razor.css`, implement the desktop/phone hierarchy, readable sizing, clear cell states, touch-sized score targets and visible keyboard focus. Avoid fixed viewport sizing that clips the card or makes its type microscopic.
4. **Tighten copy and state feedback.** Replace repeated explanatory prose with concise turn state and targeted feedback. Keep scratch confirmation and meaningful status announcements.
5. **Check every related flow.** Verify the unchanged mode selection, start/resume, abandon, scoring, scratch, Undo, Yahtzee splash and pen-color flows against the new layout. Test both portrait and landscape, plus narrow-width and short-height cases.
6. **Validate and report.** Run the targeted engine tests and a Web project build. Complete the manual acceptance checklist below. Report any requirement that cannot be met without a product decision; do not compensate by changing the game rules.
7. **Update requirements.** Revise SW-UIW-01, SW-UIW-07 and SW-UIW-09 in REQUIREMENTS.md to match the shipped layout, and tighten the SW-MOD-05 wording.

## Acceptance criteria

### Full Sheet gameplay

- After a roll, an open category cell on any of G1–G6 can receive the one score for that turn.
- Scoring a valid cell on one card immediately ends the turn, records only that card's cell, clears the dice and permits a new turn.
- Scratching an invalid cell requires confirmation, records X on that card only, and ends the turn after confirmation.
- In a later turn, the same category on a different card remains selectable. Specifically, after scoring Full House on G1, a later Full House may be scored on G2 if G2's cell is open.
- A second score cannot be recorded from the same turn after the first score has reset the dice.
- Filled cells cannot be scored again. A completed card cannot receive scores, while other incomplete cards remain playable.
- Full Sheet completion and its combined summary appear only when all six cards are complete.

### Responsive presentation

- At 1292 x 698 landscape, dice and controls appear beside the scorecard, with G1–G6 visible together.
- At 390 x 844 and 390 x 664 portrait, the turn card is below the table by default and the full six-column table is visible without scrolling.
- At 320 CSS px wide, there is no horizontal page overflow and category names remain on one line. Very short screens (for example 320 x 568) may scroll vertically.
- At 1292 x 698 and larger landscape, and at 360 x 740 and taller portrait, a full game plays without page scrolling and the rows stretch to fill the height.
- In Single mode, the same responsive UI does not accidentally enable G2–G6.
- Dice position (SW-UIW-14): a top-bar button moves the turn card left or right in landscape (default right) and top or bottom in portrait (default bottom). The choice persists across reloads. At every viewport tested (down to 320 x 568 and 844 x 390) the page does not scroll, the dice stay fully visible and only the score card scrolls.

### Feature preservation and accessibility

- Odds and upper-pace guidance are still present and intelligible, and no points preview appears in open cells.
- Roll count, die value and held state are perceivable without relying solely on color.
- All score choices, Roll, Undo, New Game and modal actions work by keyboard and have visible focus.
- Scratch confirmation, resume, abandon, pen color, Yahtzee celebration, Undo, saved progress and completion summary still work.
- The engine remains the only game-rules implementation; no duplicate UI scoring rules are introduced.

## Out of scope

- Changing game rules, the Full Sheet turn model, scoring values, probabilities, or engine state transitions.
- Scoring one turn on multiple cards.
- Adding game tabs, a sequential six-game flow, or a requirement to fill all six cards on every turn.
- Removing features because they are absent from the visual prototype.
- Replacing the Blazor UI framework or adding a new UI/component dependency without a separate technical justification.

## Suggested verification

```powershell
dotnet test .\YahtzeeSim.Engine.Tests\YahtzeeSim.Engine.Tests.csproj
dotnet build .\YahtzeeSim.Web\YahtzeeSim.Web.csproj
```

Manual checks should use the running Web app and the viewport sizes in the acceptance criteria. Engine tests verify rules; they do not verify layout, target size, text legibility, keyboard focus or screen-reader announcements.
