# Personal FFXIV plugins

## Quest Item Clicker 0.2.0.0 - experimental native inspection

This plugin is a **research prototype**, not verified in-game. It uses client-side quest event handlers to look for an associated event item, checks whether the quest is accepted, and checks whether that item appears in your Key Items inventory. No external Questionable quest path database is required for native hints.

**Limitations:** `EventHandler.GetEventItemId()` is an unverified hint and may return zero or an ID that is not actually usable on this target. Selecting a target is not a true mouse-click interaction; targets without quest handlers, multiple possible matches, quest state changes, and untargetable ground objectives may not work. The plugin does not brute-force items. Some item-use actions require special conditions not exposed by this detection. An optional older route importer remains for known mapped objectives.

### Testing safely
1. Install from the existing custom Dalamud feed: https://raw.githubusercontent.com/SIMPWarlord/personal_ffxiv/main/pluginmaster.json (currently published version: 0.2.0.0).
2. To test a new build before publication, use the successful GitHub Actions artifact in Dalamud developer plugin locations.
3. Select a quest NPC / enemy / ground object.
4. Type `/qic inspect` to print native quest handlers and associated EventItemId values.
5. Deselect and reselect while DISARMED to see a `NATIVE DRY RUN` message when there is exactly one matching accepted-quest, owned key item.
6. Only if the debug output is correct, try `/qic arm`. This opt-in allows item-use attempts on a unique candidate. `/qic off` disarms immediately.

### Commands

- `/qic inspect`: print quest-event handlers, item hint IDs, and whether they are held
- `/qic status`: show current state
- `/qic arm`: opt in to native and legacy item-use attempts
- `/qic off`: disable item use (dry-run only)
- `/qic use`: manually attempt current target, or optional legacy untargetable location
- `/qic import "C:\path\to\QuestPaths"`: optional legacy Questionable route import
- `/qic reload`: reload legacy mappings and disarm

### Development and release

Pushes to `QuestItemClicker/**` trigger the Windows GitHub Actions build workflow. A compiled test artifact is NOT an automatic public release. Only manually run the **Publish Quest Item Clicker (manual)** workflow after checking the build and testing it in FFXIV. **Published:** version 0.2.0.0 is available to Dalamud via `pluginmaster.json`, with its verified ZIP hosted under `dist/QuestItemClicker-v0.2.0.0/latest.zip` on the public repository. Future updates remain manual unless separately authorized.

Third-party plugins and gameplay automation are not officially supported by FFXIV and may violate the game's terms.
