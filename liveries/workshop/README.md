# The MiG-29SMT's Workshop liveries

The SMT's own two liveries for either side, which used to ship with the mod, published on the Steam Workshop:

| Livery | Workshop id |
|---|---|
| SMT VKS Grey 26 | [3836426901](https://steamcommunity.com/sharedfiles/filedetails/?id=3836426901) |
| SMT Dark Grey 52 | [3836427583](https://steamcommunity.com/sharedfiles/filedetails/?id=3836427583) |

How they were made: `tools/livery_profiles.py` paints the textures (`smt_vks`, `smt_dark`; the build writes them to
`MiG29SMTSource`); `WorkshopBuild.cs` here, placed in the kit's `Assets/Scripts/Editor` with `MiG29LiveryBuilder.cs`, builds one
bundle per livery (`Unity -batchmode -executeMethod WorkshopBuild.Run`, textures in `Assets/Workshop/<bundle>.png`). Each was
uploaded from the game (Workshop > Upload > Aircraft Livery) with Public and Mod Required ticked. To update one, rebuild its
bundle into the game's `Skins/<bundle>` folder, keeping its `workshop.json`, and upload it again: the game offers "Update
Existing".
