# Personal FFXIV plugins

## Quest Item Clicker 0.2.6.0 - optional debug logging

This plugin is a **research prototype**, not verified in-game. The Duty List fallback checks the game's tracked quest `QuestTodoList.TrackedQuest.ItemIds` when a quest handler's `GetEventItemId()` is zero. It requires a tracked quest with a matching accepted quest handler and an unambiguous held item. The new mode still triggers on target selection, not actual mouse clicks. It uses client-side quest event handlers to look for an associated event item, checks whether the quest is accepted, and checks whether that item appears in your Key Items inventory. No external Questionable quest path database is required for native hints.

**Quiet by default (0.2.6):** Routine automatic messages (candidate matching, item-use attempts, ground-click diagnostics, mount/dismount retries, and dry-run output) no longer fill chat unless debug logging is enabled. Use `/qic debug on` to see them, `/qic debug off` to hide them, or `/qic debug` to toggle. `/qic debug status` reports the state. The setting persists across plugin reloads and game restarts in the plugin's local `settings.json`. Explicit command confirmations, errors from commands, `/qic status`, `/qic inspect`, and `/qic area` remain visible when requested. With debug off, dry-run attempts are silent; to inspect candidates, enable debug or use the explicit inspection commands.

**Experimental ground AoE clicks (0.2.5):** On a left click in the 3D game world, the plugin raycasts the terrain. If the click is within 12 yalms of a game-authored area-marker center, within its radius, within 25 yalms of you, and belongs to exactly one accepted quest with exactly one held linked quest item, a dry-run or action is generated. When armed, it uses `ActionManager.UseActionLocation(ActionType.EventItem, itemId, location: &clickedGround)`. Clicks on ImGui windows and on targetable objects are ignored by the AoE path. `/qic area` shows quest-area markers for debugging. This does **not** recognize clicking on the world map or minimap, and map area markers are not proof a given item works on that objective. Ground-target mode is experimental and requires in-game confirmation.

**New quest-linked fallback (0.2.4):** When a selected object identifies exactly one accepted quest and neither its event handler nor the tracked Duty List offers an item ID, the plugin examines the quest association stored on held Key Items. If exactly one distinct owned item belongs to that quest, it reports a `QUEST-LINKED KEY ITEM DRY RUN` or attempts it when armed. This is a heuristic, not proof the item works on that exact objective; the game can reject it. It never tries multiple candidate items. Use `/qic inspect` to review candidates.

**Mounted target behavior (0.2.3):** Select an objective while riding and dismount normally. The plugin keeps that selection pending and, after the mount transition has finished, evaluates it once without another click. It will not dismount you automatically and it will not repeatedly spam item-use attempts. `/qic off` cancels pending actions; `/qic arm` enables actual item-use attempts.

**Limitations:** `EventHandler.GetEventItemId()` is an unverified hint and may return zero or an ID that is not actually usable on this target. Selecting a target is not a true mouse-click interaction; targets without quest handlers, multiple possible matches, quest state changes, and untargetable ground objectives may not work. The plugin does not brute-force items. Some item-use actions require special conditions not exposed by this detection. An optional older route importer remains for known mapped objectives.

### Testing AoE ground clicks
1. Stand near an active in-world quest area circle.
2. Use `/qic area` to verify its accepted quest marker and held item.
3. With `/qic off`, click the **ground near the marker center**. Look for `GROUND DRY RUN` with quest/item/world coordinates.
4. Check that the item and objective are correct. Then enable `/qic arm` and repeat the click.
5. If no dry run appears, the quest area may not be represented by Map.QuestMarkers or the click may not reach the plugin's draw callback. Report `/qic area` output before we try invasive hooks.

### Testing safely
1. Install from the existing custom Dalamud feed: https://raw.githubusercontent.com/SIMPWarlord/personal_ffxiv/main/pluginmaster.json (published version may lag until a tested build is released).
2. To test a new build before publication, use the successful GitHub Actions artifact in Dalamud developer plugin locations.
3. Make sure the relevant quest is visible (tracked) on your Duty List. Select a quest NPC / enemy / targetable ground object.
4. Type `/qic inspect` to print native quest handlers, Duty List item IDs, and held state. If the game's Duty List only shows a single valid quest item, deselect and reselect to check the dry-run output.
5. Deselect and reselect while DISARMED to see a `NATIVE DRY RUN` message when there is exactly one matching accepted-quest, owned key item.
6. If the object has no native item ID, you can use `/qic bind 2003405` to save a user-verified item mapping for the currently selected accepted quest and target (replace with the correct ID). For quest 4570, the three known Great Trunk mappings are included as a diagnostic test. Neither approach brute-forces items.
7. Only if the dry-run output is correct, try `/qic arm`. This opt-in allows item-use attempts on a unique candidate. `/qic off` disarms immediately.

### Commands

- `/qic debug [on|off|status]`: toggle optional diagnostic messages (quiet by default, persistent)
- `/qic inspect`: print quest-event handlers, item hint IDs, Duty List shortcut item IDs, quest-linked key items and known/learned mappings
- `/qic area`: list nearby circular quest-area markers and held quest-linked items
- `/qic bind <itemId>`: learn a single selected target-to-item mapping, saving to the existing local routes file (disarms)
- `/qic forget`: remove the currently selected target's user-learned mapping (disarms)
- `/qic status`: show current state
- `/qic arm`: opt in to native and legacy item-use attempts
- `/qic off`: disable item use (dry-run only)
- `/qic use`: manually attempt current target, or optional legacy untargetable location
- `/qic import "C:\path\to\QuestPaths"`: optional legacy Questionable route import
- `/qic reload`: reload legacy mappings and disarm

### Development and release

Pushes to `QuestItemClicker/**` trigger the Windows GitHub Actions build workflow. A compiled test artifact is NOT an automatic public release. Only manually run the **Publish Quest Item Clicker (manual)** workflow after checking the build and testing it in FFXIV. **Public release:** use the version in `pluginmaster.json`; release packages are hosted in versioned `dist/QuestItemClicker-v*/latest.zip` paths. Future updates remain manual unless separately authorized.

Third-party plugins and gameplay automation are not officially supported by FFXIV and may violate the game's terms.

### Why the Duty List fallback exists

In Patching Up (quest 4570), Great Trunk (2012900) returned EventItemId 0. Questionable's verified route documents item 2003405. The Duty List displays clickable quest-item shortcuts; the game has tracked-quest item IDs we can read. **We have not tested whether this specific quest displays 2003405 through that field**, so check `/qic inspect` before enabling item usage. More than one held item linked to a tracked quest means no automatic attempt. Items linked to an accepted quest aren't automatically a valid action for every target in it.
