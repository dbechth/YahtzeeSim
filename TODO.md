Yahtzee sim todo

All items from the original list are implemented; see docs/REQUIREMENTS.md for the requirements, their status and the remaining work (section 5).

Accounts (Google sign-in, nickname) and the Single / Full Sheet leaderboard are implemented (SW-ACC, SW-LDB). Remaining: the one-time Google/D1/secrets setup in the README, then Facebook as a second provider.

Original list, for reference:
- hold function fixed and verified
- one web-based solution for iPhone, Android and Windows (Blazor WebAssembly PWA)
- real-card scorecard layout
- upper bonus pace tracked for every game
- upper-section probability is the three-of-a-kind probability
- Roll (80%) and Undo (20%) directly under the dice
- bonus and totals calculated automatically at game end
- Single game and 6-game (Full Sheet) modes, with card total
- scratch a cell that is not a valid selection (marked X)
- Yahtzee splash screen
- pen colour picked at game start and after a scored Yahtzee, never at other times

Owner setup to turn on accounts and the leaderboard (details in README.md, "Accounts and Leaderboard"):
- [ ] Create a Google OAuth web client (Google Cloud Console -> APIs & Services -> Credentials). Redirect URIs: `https://<project>.pages.dev/api/auth/callback` and `http://localhost:8788/api/auth/callback`. Consent screen privacy link: `https://<project>.pages.dev/privacy`.
- [ ] Run `npx wrangler d1 create yahtzeesim` and paste the printed `database_id` into `wrangler.toml`.
- [ ] Add the Pages secrets `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET` (Cloudflare dashboard -> Pages project -> Settings -> Variables and Secrets, Production).
- [ ] Add `Account -> D1 -> Edit` to the `CLOUDFLARE_API_TOKEN` so the deploy can apply migrations.
- [ ] Push to `main` (or run the deploy workflow), then test a real Google sign-in and a leaderboard score.
- [ ] Later: add Facebook as a second provider (`functions/_lib/providers.ts`, Pages secrets, button in `SignInGate.razor`).