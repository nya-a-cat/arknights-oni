<div align="center">

# arknights-oni

**Bring Arknights operators into Oxygen Not Included.**

Operators are available today. Voice, base furniture, enemies, and visual effects are on the roadmap. Arknights is the first reference implementation for a future reusable ONI content framework.

[English](./README.md) · [简体中文](./README.zh-CN.md) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3765340857) · [Usage: EN / 中文 / 日本語](./docs/usage_multilingual.md) · [Roadmap](#current-progress--roadmap) · [Installation](#installation)

[![Version](https://img.shields.io/badge/version-0.3.3--dev-f39c12)](https://github.com/nya-a-cat/arknights-oni/tree/develop)
![ONI tested](https://img.shields.io/badge/ONI_tested-740622-ea6b35)
![C#](https://img.shields.io/badge/C%23-Unity-512BD4?logo=csharp&logoColor=white)
[![Repository](https://img.shields.io/badge/GitHub-arknights--oni-181717?logo=github)](https://github.com/nya-a-cat/arknights-oni)

</div>

![Arknights Operators Alpha gameplay montage](./docs/images/arknights-oni-alpha-v0.3.2-workshop.png)

> [!IMPORTANT]
> The published Stable version is `0.3.2-alpha.2`. The local `develop` line is now `0.3.3-dev` and implements the next **Arknights Operators** update.
>
> Each duplicant can keep its own operator, skin, and model. A global default remains available for new duplicants and for duplicants without an individual override.
>
> This is an Alpha release. Several game-integration scenarios remain under validation.
>
> Steam Workshop title: **Arknights Operators / 明日方舟干员 [Alpha]**. In-game Mods menu title: **Arknights Operators（明日方舟干员）**.

The recorded four-duplicant game-test baseline used the `0.3.2-alpha.1` candidate on Oxygen Not Included build 740622. Texas, Amiya, Kal'tsit, and Exusiai were assigned to four different duplicants, saved, and restored after a full save reload.

## Upcoming 0.3.3 update

The current local `develop` build contains the following changes for the next release:

- A paged 96px operator gallery with 20 cards per page, visible-page-only thumbnail loading, retryable failures, and offline name placeholders.
- In-world skin/model previews and a persistent `Apply to this duplicant` action that survives closing the picker and save reloads.
- A default visual size of `125%` plus per-appearance `75–200%` overrides keyed by `char_id + skin + model`.
- A user-entered `128–2000 MiB` on-demand cache capacity with `512 MiB` as the default, plus permanent retention mode.
- A movement-compatibility filter. The source snapshot still catalogs 449 operators; the picker exposes 420 operators and removes 30 skins whose metadata has no base model for walking. This excludes 29 combat-only characters from new selections.

These changes remain on `develop` until the final Dev and RC game checks are complete. The existing Steam Workshop item continues to use the published Stable package.

## What makes it special?

- Search a 449-operator metadata snapshot by Chinese, English, or Japanese name, PRTS redirect alias, or `char_id`; the picker exposes 420 movement-compatible operators.
- Use automatically selected Chinese or English option labels; operator display names prefer Chinese, Japanese, or English according to the current game language and available PRTS metadata.
- Select an operator, movement-compatible skin, and model through linked controls.
- Browse operators as 20-card pages with cached 96px PRTS avatars; the selected avatar is enlarged on the right and missing/offline images fall back to name cards.
- Select a duplicant and press `Ctrl+F8` to assign its operator, skin, and model live; use `Ctrl+Shift+F8` for lightweight global resource, model-switching, and size settings.
- Render Spine 3.8 Region/Mesh attachments, clipping, multiple atlas pages, and common blend modes directly in C#.
- Map ONI movement, work, rest, sleep, stress, and death states to available operator animations.
- Automatically use base models for daily/sleep states and front combat models for digging, combat, stun, and death.
- Select a duplicant and press `Ctrl+F9` to open its action wheel for manual animation performances; the center button restores automatic mapping.
- On the `0.3.3` development line, choose a configurable `128–2000 MiB` on-demand LRU cache (default `512 MiB`) or permanent retention of downloaded resources.
- On the `0.3.3` development line, use a default visual size of `125%`, configurable from `75%` to `200%`; changing it updates loaded overlays without another asset download.
- Merge concurrent requests for the same resource while allowing each duplicant to cancel its own wait independently.
- Verify downloads with HTTPS source restrictions, temporary files, a SHA-256 index, and a 64 MiB per-file limit.
- Prepare a verified loader and cloud builder for a versioned 449-operator GitHub Release fallback snapshot. The initial `assets-v1.0.0` snapshot is still unpublished.
- Fall back through the original duplicant visual, an optional bundled Spine asset, and the legacy frame path when an appearance cannot be loaded.

## Installation

### Prerequisites

- Oxygen Not Included for Windows installed through Steam.
- WSL with Mono `mcs` available.

The repository does not install a compiler, browser, or large dependency automatically.

### Build and install

The commands below build and install the Stable identity used by the existing local compatibility path:

```bash
cd arknights_oni_mod_work/ArknightsOperatorsMod
./build.sh
./install_local.sh
```

The default local Mod directory is:

```text
C:\Users\<you>\Documents\Klei\OxygenNotIncluded\mods\Local\ArknightsOperatorsMod
```

Set `ONI_GAME_ROOT` to override the game directory or `ONI_LOCAL_MOD_DIR` to override the installation target.

Earlier local prototypes used the `AmiyaDuplicantMod` directory. The default installer migrates that local directory plus its configuration/cache when the new target does not exist. The hidden legacy `staticID` remains as a compatibility key so existing saves continue to recognize the renamed Mod.

> [!TIP]
> Start the game through Steam, enable **Arknights Operators（明日方舟干员）** in the Mods menu, and restart when prompted. Launching the game executable directly can trigger Klei's Mod Safe Mode in some Steam environments.

The Git source repository does not contain Arknights artwork, Spine skeletons, atlases, or copied PRTS web bundles. PRTS remains the current on-demand source. The fallback loader and GitHub Actions builder are ready for a pinned manifest and the selected operator's package, while the initial `assets-v1.0.0` snapshot remains unpublished. Once that snapshot is reviewed and published, contributors can generate it in the cloud without downloading the full 449-operator asset set locally.

The existing 64 MiB limit applies to an individual Spine source file as a download safety check. Once the fallback snapshot is published, its packages will be fetched only for the selected operator and will retain a separate 512 MiB technical safety ceiling. The 100 MB preference used during local development is a disk-space preference: the cloud builder reports larger packages and continues.

## Resource strategies

The published `0.3.2-alpha.2` package uses a fixed `512 MiB` on-demand budget. The `0.3.3` development line adds an integer capacity setting from `128` to `2000 MiB`, with `512 MiB` as the default.

| Mode | Behaviour | Best for |
| --- | --- | --- |
| On-demand cache (recommended) | Fetch only the selected appearance. Set an integer budget from `128` to `2000 MiB`; the default is `512 MiB`. The settings page shows current usage and the target, and evicts least-recently-used resources that are neither active, downloading, nor leased. | Keeping disk usage bounded |
| Keep downloaded resources | Fetch only the selected appearance and retain successfully cached files without capacity eviction. The capacity field is disabled while this mode is active and keeps its saved value for a later switch. | Reusing visited appearances offline |

Neither mode pre-downloads the full operator catalog.

After the snapshot is published, fallback packages follow the same retention choice. On-demand mode may evict an unused operator package under the selected LRU budget; permanent mode keeps successfully downloaded packages. The configurable cache budget does not change the `64 MiB` per-source-file limit or the `512 MiB` fallback-package safety ceiling.

See the [GitHub Release fallback design](./docs/github_release_asset_fallback.md) for the manifest contract, cloud build flow, trust boundary, and publication checklist.

## Current progress & Roadmap

### Operators

- [x] Searchable 449-operator catalog with Chinese, English, Japanese, redirect-alias, and `char_id` lookup
- [x] Linked operator, skin, and model selection
- [x] Movement-compatibility filtering: 420 selectable operators; 29 combat-only characters and 30 skins without a base walking model are hidden from new selections
- [x] Paged 96px operator-avatar gallery with visible-page-only loading, name placeholders, and in-world Spine skin/model preview (code complete; game validation pending)
- [x] Live per-duplicant switching from `Ctrl+F8`, with lightweight global runtime and resource settings in Mod Options
- [x] Runtime animation mapping and ground alignment
- [x] Semantic build/battle animation profiles and a per-duplicant `Ctrl+F9` action wheel
- [x] Per-duplicant operator, skin, and model settings with save persistence
- [ ] Operator-specific collision profiles for visual size differences, with validation for pathfinding, ladders, beds, selection bounds, and save compatibility
- [ ] Per-duplicant voice settings
- [ ] Operator voice with language selection, preview, cooldown, and priority
- [ ] Favourites, presets, and Printing Pod assignment pools

### Arknights content

- [ ] Base furniture, room themes, and animated decorations
- [ ] Enemy and creature appearance packages
- [ ] Assignable enemy and boss skins for duplicants, including examples such as The Demon King Amiya and Patriot
- [ ] Skill, combat, work, and environmental effects
- [ ] Typed content packages: `operator`, `voice`, `furniture`, `enemy`, and `effect`

### Platform quality

- [x] Automatic Chinese/English localization for the operator options interface
- [x] Chinese/English/Japanese operator-name search from PRTS encyclopedia metadata
- [x] Versioned all-operator fallback manifest, verified Release-package loader, and sharded GitHub Actions builder
- [ ] Evolve content delivery to `local cache → pinned GitHub Release → bounded PRTS fallback`, with immutable manifest references that pin the Release tag, byte length, and SHA-256
- [ ] Build versioned per-operator packages through low-concurrency GitHub Actions jobs; prohibit full-catalog prefetching, apply retry backoff and rate limits, and extend the GitHub Release fallback/snapshot pipeline to Spine assets, thumbnails, voices, furniture, enemies, and effects
- [ ] Configurable `128–2000 MiB` cache (default `512 MiB`): code and regression tests complete on `develop`; game validation pending
- [ ] Generate, inspect, and publish the initial 449-operator `assets-v1.0.0` snapshot
- [ ] Move remaining runtime errors and diagnostics into ONI `STRINGS` resources and add more interface locales
- [ ] Cache manager, download status, and diagnostics export
- [ ] Versioned configuration migration and catalog updates
- [ ] Compatibility controls for other appearance Mods

### Long-term framework direction

- [ ] Re-evaluate `arknights-oni` as a reusable ONI content framework after the Arknights content pipeline matures
- [ ] Extract stable content lifecycle, cache, selection, event mapping, and package contracts into a reusable core
- [ ] Keep Arknights as the first reference content pack and compatibility suite
- [ ] Evaluate content packs inspired by other games, with **BanG Dream!** as an example candidate

See the [complete code review and roadmap](./docs/code_review_and_roadmap_20260715.md) for priorities, acceptance criteria, performance limits, and resource boundaries.

## Development

The current development version is `0.3.3-dev`. `main` contains game-tested stable code, `develop` carries integrated development work, and isolated `feature/*` branches cover higher-risk changes. Nightly and RC packages use a separate Testing identity; the current Steam Workshop item receives Stable packages only. See [Release channels and branch policy](./docs/release_channels.md).

From the repository root, package each identity with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build_mod_release.ps1 -Channel Stable -Version v0.3.3
powershell -ExecutionPolicy Bypass -File .\tools\build_mod_release.ps1 -Channel Dev -Nightly
powershell -ExecutionPolicy Bypass -File .\tools\build_mod_release.ps1 -Channel RC -Version v0.3.3-rc.1
powershell -ExecutionPolicy Bypass -File .\tools\install_testing_mod.ps1 -PackagePath <testing-zip>
```

The first command produces the Stable identity. Dev and RC produce the isolated Testing identity; the final command installs only that Testing package.

Run the complete local packaging probe after the channel DLLs are available:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\test_packaging_artifacts.ps1
```

This probe validates all three package identities, ZIP/sidecar hashes, the isolated Testing installer, and Nightly retention. It uses a temporary root under the repository `.cache` directory and never touches the real game or Local Mods directory.

```bash
cd arknights_oni_mod_work/ArknightsOperatorsMod
./build.sh
./tests/run_operator_animation_mapper_tests.sh
./tests/run_operator_appearance_catalog_tests.sh
python3 ./tests/test_operator_catalog_thumbnails.py
./tests/run_operator_thumbnail_loader_tests.sh
./tests/run_mod_localization_tests.sh
./tests/run_resource_index_tests.sh
python3 ./tests/test_fallback_release_builder.py
./tests/run_operator_fallback_tests.sh
./tests/run_operator_asset_resolver_integration.sh
```

The final integration test downloads a real, small PRTS fixture. The fallback test uses an in-memory Release package and simulated primary-source failure. The remaining tests use only local code and fixtures.

## Repository layout

- `arknights_oni_mod_work/ArknightsOperatorsMod/src`: Mod entry points, settings, cache, resource resolution, rendering, and animation mapping.
- `arknights_oni_mod_work/ArknightsOperatorsMod/tests`: Logic tests and the real small-resource integration test.
- `arknights_oni_mod_work/ArknightsOperatorsMod/lib`: PLib plus the pinned Spine C# runtime sources and provenance notes.
- `docs`: PRTS asset research, architecture and acceptance notes, and the detailed roadmap.

## Project boundaries & third-party components

This is a non-commercial fan project with no affiliation with or endorsement by Klei, Hypergryph, or PRTS Wiki. Game and character rights belong to their respective owners. The public Git repository contains original Mod source code, tests, development notes, lightweight catalog metadata, separately licensed third-party code, and a promotional montage made from real gameplay screenshots. Runtime artwork and animation assets currently come from PRTS on demand. The prepared Release fallback becomes available after its first versioned asset snapshot is reviewed and published.

No additional open-source license is currently granted for the original code. PLib, the Spine runtime, and catalog metadata remain subject to their respective licenses and source notices. See [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md) and [DATA_NOTICE.md](./DATA_NOTICE.md).
