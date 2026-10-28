---
status: accepted
---

# Pathing Adapter: file-based Marker Pack installs, advisory Marker Categories in v0.1, and opt-in live category control

Pathing (issue #10, issue #11) provides markers and trails for Guild Wars 2 in Blish HUD. Supporting
Pathing requires two distinct capabilities: downloading needed Marker Packs, and configuring Marker
Category visibility. Investigations of Pathing 1.13.2 (`2d802ad`) revealed that Marker Pack downloads
can operate entirely through public files and APIs without touching Pathing internals, whereas live
category manipulation requires internal reflection that can break across Pathing updates.

## Decision

- **Marker Pack downloads are file-based and official-only (#11).**
  - **Official repository:** Marker packs are discovered from Pathing's official repository list at
    `https://mp-repo.blishhud.com/repo.json`.
  - **Markers directory:** Packs reside as `.taco` or `.zip` files in Blish HUD's shared `markers`
    directory (`DirectoriesManager.GetFullDirectoryPath("markers")`).
  - **Reload via public module API:** Pathing scans the markers directory only upon initialization
    (`Init` → `LoadAllPacks`). Installing or deleting a pack restarts Pathing by disabling and
    re-enabling it through Blish HUD's public module API (`IBlishHudWorld.SetModuleEnabledAsync`).
    Pathing's built-in pack manager matches local files to the official repo by `FileName`
    (`LoadLocalPackInfo`), taking over future pack auto-updates.
  - **Review required (ADR-0005):** The `download_pack` Action is allowed only for packs present in
    `repo.json` (unofficial packs are rejected by the validator). Downloading brings third-party
    content onto disk, so `download_pack` always requires Review, even when Auto-apply is enabled.
    The Review display states the pack's download size in MB and that Pathing will restart.
  - **Apply workflow:** The pack is downloaded to a temporary file, checked to verify it is a valid
    zip archive with at least one entry, moved into the markers directory as its canonical `FileName`,
    and Pathing is restarted.
  - **Undo workflow:** The installed pack file is deleted from the markers directory, and Pathing is
    restarted to unload it.
  - **Pack Lookup:** An adapter Lookup (`list_marker_packs`) lists available packs from `repo.json`
    and installed packs from the shared markers folder, requiring no internal Pathing code.

- **Marker Categories are advisory in v0.1 (#31).**
  - Pathing persists category visibility in `markers\data\states\category_preferences.txt` (`+namespace`
    for shown, `-namespace` for hidden; categories without explicit preferences follow their pack's
    `defaulttoggle`).
  - Live category changes cannot be performed via public API; doing so requires reflection into
    Pathing's internal `PathingModule.PackInitiator.PackState` and `CategoryStates.SetInactive`.
  - In v0.1, category handling is strictly advisory (#31): the AI reads installed `.taco` packs
    (stable TacO XML format) and `category_preferences.txt` (read-only) via `find_marker_categories`,
    telling the Player which category display path to turn on and whether it is currently enabled.
    The AI never claims to have turned anything on.
  - Live show/hide category actions belong to #10's experimental opt-in adapter, guarded by an
    explicit module setting and reflection validation checks.

- **Continuation after Marker Pack download (#11, #31).**
  - After a Marker Pack download is applied and Pathing has restarted, the Continuation turn uses
    advisory category guidance (#31 `find_marker_categories`) rather than attempting live show/hide.
    The AI inspects the newly installed pack's categories and advises the Player which category to
    enable in Pathing's menu for their current map or goal.

## Considered Options

- **Invoke Pathing's internal pack downloader (`PackHandlingUtil.BeginPackDownload`).** Rejected:
  requires reflection or internal references, whereas downloading to the shared markers directory and
  restarting Pathing uses only public APIs and standard file operations.
- **Allow arbitrary pack download URLs.** Rejected: downloading arbitrary zip/taco files poses
  security risks (ADR-0005). Restricting downloads to Pathing's official `repo.json` ensures that all
  downloaded packs come from community-curated, verified sources.
- **Auto-apply marker pack downloads when Auto-apply is enabled.** Rejected: marker packs can be tens
  of megabytes and restart Pathing; the Player must explicitly review and approve any content download
  (ADR-0005).
- **Mutate `category_preferences.txt` directly to change category visibility.** Rejected: Pathing
  caches category preferences in memory, periodically writes the file every 5 seconds while dirty,
  and overwrites the file on unload, creating race conditions and corruption risks.

## Consequences

- Marker Pack downloads require network access to `mp-repo.blishhud.com` and the pack host, but
  require no dependencies on Pathing's internals.
- Restarting Pathing takes a brief moment to reload packs; the Review makes this transparent to the
  Player.
- Downloaded packs without Pathing's optional pack optimization (`OptimizeMarkerPacks`) work
  immediately as standard TacO zips, and Pathing's own auto-updater can optimize or update them later.
- Category guidance remains fully safe and resilient across Pathing updates.
