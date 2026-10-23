# VillagePlebs

A Valheim mod: create multiple viking companions, follower NPCs who play like a player. They come on your adventures, fight beside you, live their own lives at home, and get better as they go.

The player-facing description, features and settings are in [VillagePlebs/Package/README.md](VillagePlebs/Package/README.md), which is also the Thunderstore page.

## Requirements

  * [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
  * [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)
  * Optional: Body Replacer by Magic Mike, for more looks

## Building

1. Clone with the shared code: `git clone --recurse-submodules https://github.com/mike420/VillagePlebs.git` (or run `git submodule update --init` in an existing clone).
2. Create `Environment.props` next to `VillagePlebs.sln` with your Valheim folder:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
     <PropertyGroup>
       <VALHEIM_INSTALL>C:\Program Files (x86)\Steam\steamapps\common\Valheim</VALHEIM_INSTALL>
       <MOD_DEPLOYPATH>$(VALHEIM_INSTALL)\BepInEx\plugins</MOD_DEPLOYPATH>
       <MOD_CONFIGPATH>$(VALHEIM_INSTALL)\BepInEx\config</MOD_CONFIGPATH>
     </PropertyGroup>
   </Project>
   ```

3. Build `VillagePlebs.sln` in Visual Studio, or with `msbuild VillagePlebs.sln -restore -p:Configuration=Release`.

A build copies `VillagePlebs.dll` to `BepInEx\plugins\VillagePlebs` and the translations to `BepInEx\config\VillagePlebs\Translations`. A Release build also updates the version in `Package\manifest.json`, regenerates `Nexus\README.txt` from `Package\README.md`, and zips a Thunderstore package into `VillagePlebs\ThunderStore`.

## Translations

One file per language in `VillagePlebs/Assets/Translations/<Language>/VillagePlebs.json`. The game reads them from `BepInEx\config\VillagePlebs\Translations` and reloads them when they change; English is also built into the DLL for any key a file lacks.

## License

MIT, see [LICENSE](LICENSE).
