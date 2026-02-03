# Celsius 2.7

This is a bugfix release for Rimworld mod Celsius based on Jul 10 2025 2.6 release

Original repo: [https://github.com/GarwelGarwel/Celsius](https://github.com/GarwelGarwel/Celsius)

Original Steam page: [https://steamcommunity.com/sharedfiles/filedetails/?id=2725863762](https://steamcommunity.com/sharedfiles/filedetails/?id=2725863762)

Most of the mod except the diffusion model itself ended up rewritten in the course of fixing
bugs and performance optimization.

As of now I hope I fixed all race conditions and data loss that were plaguing this otherwise excellent mod.

Odyssey compatability is still lacking, in that the mod is not vacuum-aware.

Odyssey-specific water freeze-melt should work in parallel with this mod's freeze-melt.
Or you can just disable Celsius freeze-melt: the switch only affects vanilla water, terrains
with explicit phase transforms in defs will continue to transform.

All in all this is very much work in progress.

I would not yet recommend using this update on important saves/playthroughs - it should not eat your save
(there is a button for this in mod settings), but.. this is not a stable release yet.
Save files are backwards incompatible, that is, 2.6 can't read 2.7 stuff.

Please help with testing, bugreports and suggestions are very welcome.

This project is hosted at [https://codeberg.org/lxnt/Celsius3](https://codeberg.org/lxnt/Celsius3)

This project is not yet uploaded to Steam.

This project intergates Anomaly and Odyssey TerrainDef patches from
[Celsius Patches](https://steamcommunity.com/sharedfiles/filedetails/?id=3072081544) mod by
[albinogod](https://steamcommunity.com/profiles/76561198078199193). Many thanks.

