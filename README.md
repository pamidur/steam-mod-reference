# Steam-Mod-Reference

MSBuild tasks/props/targets that let a .NET project reference DLLs shipped inside Steam Workshop mods
, by downloading them via SteamCMD at build time.

No SteamCMD credentials are required - all downloads are anonymous, 
which is sufficient for public workshop items

Game agnostic but tested on Rimworld

## Requirements

It requires net10 SDK installed. 
Your mods can target whatever they want -> mono, net461 etc

NOT TESTED ON WINDOWS WHATSOEVER

## Usage

```xml

<!-- RimWorld config -->
<PropertyGroup>
  <SteamModAppId>294100</SteamModAppId>
  <SteamModDefaultAssemblies>1.6/Assemblies/*.*;Assemblies/*.*</SteamModDefaultAssemblies>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="SteamModReference" Version="0.2.0" PrivateAssets="all" />
</ItemGroup>

<ItemGroup>
  <SteamModReference Include="2872901762" />
  <!-- <SteamModReference Include="2872901762" Assemblies="1.6/Assemblies" /> -->
  <!-- <SteamModReference Include="2872901762" Assemblies="1.6/Assemblies/*MyStuff.dll" AllowDuplicates="true" /> -->
</ItemGroup>
```

| Metadata           | Meaning                                                                                                |
|--------------------|--------------------------------------------------------------------------------------------------------|
| `Include`          | Workshop item id. Required                                                                             |
| `Assemblies`       | Path/glob under the mod's content root to find dll(s). Omit to use `$(SteamModDefaultAssemblies)`.     |
| `AllowDuplicates`  | `true` to force-reference even if an assembly of the same name is already referenced. Default `false`. |

## Properties

| Property                       | Default                              | Meaning |
|--------------------------------|--------------------------------------|---------|
| `SteamModAppId`                | `294100` (RimWorld)                  | Steam AppId the workshop ids belong to. |
| `SteamModDefaultAssemblies`    | `1.6/Assemblies/*.*;Assemblies/*.*`  | Ordered, `;`-separated fallback patterns tried when an item has no `Assemblies` metadata. First pattern that matches ≥1 dll wins. |
| `SteamModIntermediateDir`      | `$(BaseIntermediateOutputPath)steam\`| Where steamcmd + downloaded workshop content is staged. |
| `SteamModCleanRemovesToolCache`| `false`                              | `dotnet clean` always clears staged mod content + manifest. Set `true` to also remove the cached steamcmd binary itself on clean (fully-from-scratch reset, re-downloads steamcmd next build). |

## Clean behavior

`dotnet clean` (and VS "Clean") normally only touches the SDK's own obj/bin outputs -
it has no idea about the workshop content staged under `$(SteamModIntermediateDir)`.

This package hooks `AfterTargets="Clean"` so that running clean also removes:

- staged workshop mod content (`.../steam/steamapps/workshop`)
- the manifest file (once Phase 3 incrementality lands - see PLAN.md)

The cached `steamcmd` tool binary itself is left alone by default (set `SteamModCleanRemovesToolCache=true` to remove that too).


## Repo layout

```
src/                                    the MSBuild task assembly (net472 + net8.0)
build/                                  props/targets shipped to direct consumers
buildTransitive/                        thin pass-through for transitive consumers
tests/ConsumerSample/                   a project that consumes the package, for local testing
```

## License

MIT
