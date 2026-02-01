# Celsius 2.7

This is a bugfix release for Rimworld mod Celsius based on Jul 10 2025 2.6 release

Original repo: [https://github.com/GarwelGarwel/Celsius](https://github.com/GarwelGarwel/Celsius)

Original Steam page: [https://steamcommunity.com/sharedfiles/filedetails/?id=2725863762](https://steamcommunity.com/sharedfiles/filedetails/?id=2725863762)

Most of the mod except the diffusion model itself ended up rewritten in the course of fixing
bugs and performance optimization.

As of now I hope I fixed all race conditions and data loss that were plaguing this otherwise excellent mod.

Odyssey compatability is still lacking, in that the mod is not vacuum-aware.

Odyssey-specific water freeze-melt should work in parallel with this mod's freeze-melt.

All in all this is very much work in progress.

Since the model now can run at least eight times per tick (compared to 120 ticks per run previously),
some model parameters need tuning. I tried to keep the model as intact as possible, but there were some bugs
in its implementation too, so it might behave strangely. Please report anything untoward.

I would not recommend using this update on important saves/playthroughs - while it should not eat your save
(there is a button for this in mod settings), the 2.6 version would not be able to load the save from this version.

Please help with testing, bugreports and suggestions are very welcome.

This project is hosted at [https://codeberg.org/lxnt/Celsius3](https://codeberg.org/lxnt/Celsius3)

This project intergates Anomaly and Odyssey TerrainDef patches from
[Celsius Patches](https://steamcommunity.com/sharedfiles/filedetails/?id=3072081544) mod by
[albinodog](https://steamcommunity.com/profiles/76561198078199193). Many thanks.

