# Personal FFXIV plugins

Private development repository for Quest Item Clicker. Experimental and not yet tested in FFXIV. The plugin watches target changes, not actual mouse clicks. It starts disarmed and requires compatible local Questionable quest-path data. Untargetable ground objectives require /qic use.

## Build and test

Open Actions and run the Quest Item Clicker build workflow. Download its test artifact, extract the nested latest.zip into a stable folder, then add the extracted plugin via Dalamud developer plugin settings. No public releases are configured. Test in dry-run first. Third-party plugins are not officially permitted by FFXIV.

Commands: /qic import "C:\path\to\QuestPaths", /qic status, /qic arm, /qic off, /qic reload, /qic use.

The custom feed is intentionally empty until a tested release is published. A private GitHub raw URL is not a normal Dalamud feed.
