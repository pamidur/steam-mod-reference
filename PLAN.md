# SMR — Implementation Plan & Status

Scaffold generated on 2026-09-13. This file is the single source of truth for
"what actually works right now" vs. "what's stubbed/TODO" — keep it updated as
work lands, don't let it drift from the code.

## Legend
- [x] done and expected to work
- [~] partially done / stubbed / needs validation against real steamcmd
- [ ] not started

---

## Phase 1 — Core pipeline (single project, happy path)

- [x] Project skeleton: sln, Tasks csproj (net472;net8.0), props, targets, nuspec, sample consumer
- [x] `SteamModReference` item shape documented (Include / Assemblies / AllowDuplicates)
- [x] `SteamModAppId` property with RimWorld default (294100)
- [x] `SteamModDefaultAssemblies` fallback-pattern property
      (`1.6/Assemblies/*.*;Assemblies/*.*`), consumed by `AssemblyResolver.ResolveDllPaths`
      — tries patterns in order, first one that matches ≥1 dll wins
- [x] `UsingTask` dual-registration for net472 (`MSBuildRuntimeType != Core`) vs.
      net8.0 (`MSBuildRuntimeType == Core`)
- [x] `SteamCmdBootstrapper`: Windows zip download+extract path
- [x] `SteamCmdBootstrapper`: Linux/macOS tar.gz path — uses `System.Formats.Tar`
      (.NET 7+ BCL, `TarFile.ExtractToDirectoryAsync`) over a `GZipStream`, so
      there's no external `tar` process and no third-party archive library
      (e.g. SharpZipLib). This only works because the Linux/macOS branch only
      ever runs under the net8.0 build of this task (net472 only loads under
      Windows-only full-framework MSBuild - see `#if NET8_0_OR_GREATER` in the
      file). `TarFile` preserves each entry's Unix file mode - including the
      executable bit - on extraction, so `File.SetUnixFileMode` afterward
      (also managed, no `chmod` process) is defensive insurance rather than
      load-bearing. **Not yet validated against a real Linux agent** — logic
      is correct per BCL docs but flag as unverified until run once for real.
- [x] steamcmd "first run self-updates and may report a nonzero/odd exit"
      quirk — handled via a one-time `+quit` warm-up run in
      `SteamCmdBootstrapper.RunWarmupAsync` whose exit code is deliberately
      ignored (only run on non-Windows for now; Windows steamcmd.exe hasn't
      exhibited this in practice, revisit if it does)
- [x] `SteamCmdRunner`: builds one batched `+runscript` covering every distinct
      workshop id, single process invocation (avoids N logins for N mods)
- [x] `AssemblyResolver.ResolveDllPaths`: bare-directory, `*`/`?` glob, and `**`
      recursive-segment matching against the mod's content folder
- [ ] Full regex support in `Assemblies` (only glob-via-`Directory.GetFiles` today)
- [x] `ResolveModReferencesTask`: wires bootstrap → batched download → per-item
      resolve → emits `ResolvedAssemblies` as `Reference`-shaped `ITaskItem`s
      (`HintPath`, `Private=true`, `SteamModWorkshopId` metadata for traceability)
- [x] `.targets`: `ResolveSteamModReferences` runs `BeforeTargets="ResolveAssemblyReferences;CoreCompile"`,
      folds `_SteamModResolvedReference` into `@(Reference)`
- [ ] **Never actually run against real steamcmd / real workshop ids yet.**
      First real task before anything else: run the sample consumer against the
      two example mod ids from the spec and fix whatever's wrong with path
      assumptions (this is a scaffold — assume the file-layout guesses above are
      wrong until proven otherwise)

## Phase 2 — Dedup + regex + AllowDuplicates

- [x] Dedup semantics implemented as specified: `AssemblyResolver.ApplyDedup`
      seeds a `HashSet<string>` of assembly names from `@(Reference)` (existing
      project references), then walks `@(SteamModReference)` items **in
      declaration order**, adding each kept dll's name to the set as it goes —
      so a broader early item can claim a name and a later narrower item with
      `AllowDuplicates="true"` can still force it back in (matches the 3rd
      example in the spec)
- [x] Skip-with-message logging when a dll is dropped for being a duplicate
- [ ] Regex support (see Phase 1) — once added, decide whether `Assemblies`
      auto-detects "is this glob or regex" or needs an explicit
      `AssembliesMatchType="Glob|Regex"` metadata to avoid ambiguity (e.g. is
      `1.6/Assemblies/*.*` a glob or would someone write `.*\.dll` and mean regex?)
- [ ] Decide + document: does dedup consider file name only, or
      name+version/hash? (Spec implies name only — "already have harmony
      referenced" — current implementation matches that.)

## Phase 3 — Incrementality & caching

- [~] `ModManifest` class exists (load/save JSON) but **is not wired into
      `ResolveModReferencesTask`** — every build currently re-runs the full
      steamcmd batch download for every distinct mod id. TODO markers are in:
      - `ResolveModReferencesTask.ExecuteAsync` (skip-logic + manifest write)
      - `.targets` (`Inputs`/`Outputs` on the Target itself, commented TODO)
- [ ] Decide skip granularity: skip steamcmd entirely if content folder exists
      and non-empty (cheap, coarse), vs. compare a content-folder timestamp/hash
      against the manifest (more correct, more code)
- [ ] `SteamModForceRedownload` property exists in props but isn't consumed by
      any code yet — wire it as a bypass for whatever skip-logic gets built
- [ ] Decide cache scope: per-project `obj/steam` (simple, current default) vs.
      a machine/solution-shared cache keyed by AppId+workshopId (saves
      redundant downloads across multiple projects referencing the same mod,
      but adds file-locking/concurrency concerns across parallel project builds
      in the same `dotnet build` — worth doing only if this becomes a real pain
      point; don't build it speculatively)

## Phase 4 — Robustness

- [ ] Retry-on-flake for steamcmd (documented as notoriously flaky, esp. cold
      start) — TODO marker in `SteamCmdRunner.DownloadWorkshopItemsAsync`
- [ ] Per-mod post-download verification (does the content folder actually
      exist and have files) instead of trusting steamcmd's process exit code
      alone — same TODO marker
- [ ] Turn hard failures (mod unavailable anonymously, age-gated, deleted
      workshop item) into either a clear `Log.LogError` (fail build) or a
      `Log.LogWarning` + skip, and make that behavior configurable
      (`SteamModTreatMissingModAsError` or similar) — not yet decided which
      should be the default
- [ ] Respect MSBuild verbosity properly — currently `Low`/`Normal`/`High`
      importance is used ad hoc; do a pass once the happy path is proven

## Clean hook

- [x] `CleanSteamModReferences` target, `AfterTargets="Clean"` in `.targets` —
      removes staged workshop content (`$(SteamModIntermediateDir)steam\steamapps\workshop`)
      and `manifest.json` on every `dotnet clean` / VS Clean. Steamcmd tool binary
      cache is left alone by default; `SteamModCleanRemovesToolCache=true` removes
      that too (new property in `.props`).
- [ ] Not yet validated that `AfterTargets="Clean"` actually fires reliably for
      `dotnet clean` on an SDK-style project with no other customization — this
      is standard MSBuild behavior and should work, but hasn't been run for real
      yet (same caveat as everything else in this scaffold).

## Phase 5 — Packaging & distribution

- [x] nuspec targets `tasks/net472/` + `tasks/net8.0/`, `build/`, `buildTransitive/`
- [x] Sample consumer project for local pack→restore→build loop (see README)
- [ ] Local test harness is manual only (`nuget pack` + local feed, per README)
      — no scripted `./build.sh`/`build.ps1` yet
- [ ] CI: pack + smoke-test build against a real small mod, on every push —
      not started (no CI config file in this scaffold at all yet)
- [ ] Publish pipeline / versioning policy — not started; **do not publish
      0.1.0 until Phase 1's "run against real steamcmd" checkbox is checked**,
      since the file-layout assumptions in `AssemblyResolver` are unverified

---

## Open decisions (need an answer before Phase 2/3 lock in)

1. **Glob vs. regex ambiguity** in `Assemblies` — see Phase 2 note above.
2. **Cache scope** (per-project vs. shared) — see Phase 3 note above.
3. **Anonymous-download failures** — hard error vs. warn-and-skip default.
4. **Steam ToS considerations** for redistributing/auto-downloading workshop
   content in a build pipeline — worth a line in the README disclaiming
   personal/team use; not a code concern but should be resolved before wide
   publication.
5. **Core-hosted task TFM: net8.0 vs. net7.0** — `System.Formats.Tar` only
   requires .NET 7, so targeting `net7.0` instead of `net8.0` for the second
   TFM would lower the "SDK must be installed to build at all on Linux/macOS"
   floor from 8+ to 7+ for free. Currently left at net8.0 since that's the
   more common modern baseline, but worth revisiting if consumers ever report
   being stuck on an older SDK.

## Suggested next session's task list (in order)

1. Get `dotnet build` to succeed on `src/Pamidur.SteamModReference.Tasks` as-is
   (verify package refs resolve, no missing usings).
2. Run the sample consumer for real against the two workshop ids from the
   original spec (`2773943594`, `3125246043`) and fix whatever assumptions
   about steamcmd's on-disk output layout are wrong — on both Windows and a
   real Linux box/container, since the tar/chmod bootstrap path is untested.
3. Run `dotnet clean` on the sample and confirm `obj/steam/.../workshop`
   actually disappears and the next `dotnet build` re-downloads cleanly.
4. Only then: wire up `ModManifest` for incrementality (Phase 3) — no point
   optimizing a pipeline that hasn't been proven correct yet.
