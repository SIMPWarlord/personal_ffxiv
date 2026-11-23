# Personal FFXIV plugins

## Quest Item Clicker 0.2.2.0 - Duty List shortcut detection

This plugin is a **research prototype**, not verified in-game. The Duty List fallback checks the game's tracked quest `QuestTodoList.TrackedQuest.ItemIds` when a quest handler's `GetEventItemId()` is zero. It requires a tracked quest with a matching accepted quest handler and an unambiguous held item. The new mode still triggers on target selection, not actual mouse clicks. It uses client-side quest event handlers to look for an associated event item, checks whether the quest is accepted, and checks whether that item appears in your Key Items inventory. No external Questionable quest path database is required for native hints.

**Limitations:** `EventHandler.GetEventItemId()` is an unverified hint and may return zero or an ID that is not actually usable on this target. Selecting a target is not a true mouse-click interaction; targets without quest handlers, multiple possible matches, quest state changes, and untargetable ground objectives may not work. The plugin does not brute-force items. Some item-use actions require special conditions not exposed by this detection. An optional older route importer remains for known mapped objectives.

### Testing safely
1. Install from the existing custom Dalamud feed: https://raw.githubusercontent.com/SIMPWarlord/personal_ffxiv/main/pluginmaster.json (currently published version: 0.2.0.0).
2. To test a new build before publication, use the successful GitHub Actions artifact in Dalamud developer plugin locations.
3. Make sure the relevant quest is visible (tracked) on your Duty List. Select a quest NPC / enemy / targetable ground object.
4. Type `/qic inspect` to print native quest handlers, Duty List item IDs, and held state. If the game's Duty List only shows a single valid quest item, deselect and reselect to check the dry-run output.
5. Deselect and reselect while DISARMED to see a `NATIVE DRY RUN` message when there is exactly one matching accepted-quest, owned key item.
6. If the object has no native item ID, you can use `/qic bind 2003405` to save a user-verified item mapping for the currently selected accepted quest and target (replace with the correct ID). For quest 4570, the three known Great Trunk mappings are included as a diagnostic test. Neither approach brute-forces items.
7. Only if the dry-run output is correct, try `/qic arm`. This opt-in allows item-use attempts on a unique candidate. `/qic off` disarms immediately.

### Commands

- `/qic inspect`: print quest-event handlers, item hint IDs, Duty List shortcut item IDs, quest-linked key items and known/learned mappings
- `/qic bind <itemId>`: learn a single selected target-to-item mapping, saving to the existing local routes file (disarms)
- `/qic forget`: remove the currently selected target's user-learned mapping (disarms)
- `/qic status`: show current state
- `/qic arm`: opt in to native and legacy item-use attempts
- `/qic off`: disable item use (dry-run only)
- `/qic use`: manually attempt current target, or optional legacy untargetable location
- `/qic import "C:\path\to\QuestPaths"`: optional legacy Questionable route import
- `/qic reload`: reload legacy mappings and disarm

### Development and release

Pushes to `QuestItemClicker/**` trigger the Windows GitHub Actions build workflow. A compiled test artifact is NOT an automatic public release. Only manually run the **Publish Quest Item Clicker (manual)** workflow after checking the build and testing it in FFXIV. **Current public release:** version 0.2.0.0 is available to Dalamud via `pluginmaster.json`, with its verified ZIP hosted under `dist/QuestItemClicker-v0.2.0.0/latest.zip` on the public repository. Future updates remain manual unless separately authorized.

Third-party plugins and gameplay automation are not officially supported by FFXIV and may violate the game's terms.

### Why the Duty List fallback exists

In Patching Up (quest 4570), Great Trunk (2012900) returned EventItemId 0. Questionable's verified route documents item 2003405. The Duty List displays clickable quest-item shortcuts; the game has tracked-quest item IDs we can read. **We have not tested whether this specific quest displays 2003405 through that field**, so check `/qic inspect` before enabling item usage. More than one held item linked to a tracked quest means no automatic attempt. Items linked to an accepted quest aren't automatically a valid action for every target in it.
