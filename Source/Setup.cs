using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Celsius
{
    /// <summary>
    /// Harmony patches and Defs preparation on game startup
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class Setup
    {
        internal static Harmony harmony;

        static Setup()
        {
            if (harmony != null)
                return;
            var log = new LogContext("Setup");
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

            harmony = new Harmony("Garwel.Celsius");
            if (!ApplyHarmonyPatches())
                log.Error($"Failed to apply Harmony patches.");

            // Adding CompThermal to all applicable Things
            List<ThingThermalProperties> ttpList;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.Where(def => CompThermal.ShouldApplyTo(def))) {
                def.comps.Add(new CompProperties(typeof(CompThermal)));
                if ((ttpList = def.modExtensions.OfType<ThingThermalProperties>().ToList()).Count > 1)
                    log.Warn($"{def.defName} has {ttpList.Count} ThingThermalProperties extensions:\n{ttpList.Select(ttp => ttp.ToString()).ToLineList("- ")}");
            }
            TemperatureUtility.SettingsChanged();
            log.Info($"Celsius {ver}");
        }

        public static bool IsHarmonyPatched => harmony.GetPatchedMethods().Any();

        public static bool ApplyHarmonyPatches()
        {
            Type type = typeof(Setup);
            try
            {
                RemoveHarmonyPatches();
                harmony.Patch(
                    AccessTools.Method("Verse.GenTemperature:TryGetDirectAirTemperatureForCell"),
                    prefix: new HarmonyMethod(type.GetMethod("GenTemperature_TryGetDirectAirTemperatureForCell")));
                harmony.Patch(
                    AccessTools.PropertyGetter(typeof(Room), "Temperature"),
                    prefix: new HarmonyMethod(type.GetMethod("Room_Temperature_get")));
                harmony.Patch(
                    AccessTools.Method("Verse.GenTemperature:PushHeat", new Type[] { typeof(IntVec3), typeof(Map), typeof(float) }),
                    prefix: new HarmonyMethod(type.GetMethod("GenTemperature_PushHeat_IntVec3")));
                harmony.Patch(
                    AccessTools.Method("Verse.GenTemperature:PushHeat", new Type[] { typeof(Thing), typeof(float) }),
                    prefix: new HarmonyMethod(type.GetMethod("GenTemperature_PushHeat_Thing")));
                harmony.Patch(
                    AccessTools.Method("Verse.GenTemperature:ControlTemperatureTempChange"),
                    postfix: new HarmonyMethod(type.GetMethod("GenTemperature_ControlTemperatureTempChange")));
                harmony.Patch(
                    AccessTools.Method("RimWorld.SteadyEnvironmentEffects:MeltAmountAt"),
                    postfix: new HarmonyMethod(type.GetMethod("SteadyEnvironmentEffects_MeltAmountAt")));
                harmony.Patch(
                    AccessTools.Method("Verse.AttachableThing:Destroy"),
                    prefix: new HarmonyMethod(type.GetMethod("AttachableThing_Destroy")));
                harmony.Patch(
                    AccessTools.Method("RimWorld.JobGiver_SeekSafeTemperature:ClosestRegionWithinTemperatureRange"),
                    prefix: new HarmonyMethod(type.GetMethod("JobGiver_SeekSafeTemperature_ClosestRegionWithinTemperatureRange")));
                harmony.Patch(
                    AccessTools.Method("Verse.DangerUtility:GetDangerFor"),
                    postfix: new HarmonyMethod(type.GetMethod("DangerUtility_GetDangerFor")));
                harmony.Patch(
                    AccessTools.Method("Verse.MapTemperature:TemperatureUpdate"),
                    prefix: new HarmonyMethod(type.GetMethod("MapTemperature_TemperatureUpdate")));
                harmony.Patch(
                    AccessTools.Method("RimWorld.GlobalControls:TemperatureString"),
                    prefix: new HarmonyMethod(type.GetMethod("GlobalControls_TemperatureString")));
                harmony.Patch(
                    AccessTools.Method("RimWorld.CompRitualFireOverlay:CompTick"),
                    postfix: new HarmonyMethod(type.GetMethod("CompRitualFireOverlay_CompTick")));

                harmony.Patch(
                    AccessTools.Method("RimWorld.Building_Door:DoorOpen"),
                    postfix: new HarmonyMethod(type.GetMethod("Building_Door_DoorOpen")));
                harmony.Patch(
                    AccessTools.Method("RimWorld.Building_Door:DoorTryClose"),
                    postfix: new HarmonyMethod(type.GetMethod("Building_Door_DoorTryClose")));

                harmony.Patch(
                    AccessTools.Method("Verse.TerrainGrid:SetTerrain"),
                    postfix: new HarmonyMethod(type.GetMethod("TerrainGrid_SetTerrain")));
                harmony.Patch(
                    AccessTools.Method("Verse.TerrainGrid:RemoveTopLayer"),
                    postfix: new HarmonyMethod(type.GetMethod("TerrainGrid_SetTerrain")));
                harmony.Patch(
                    AccessTools.Method("Verse.TerrainGrid:SetFoundation"),
                    postfix: new HarmonyMethod(type.GetMethod("TerrainGrid_SetTerrain")));
                harmony.Patch(
                    AccessTools.Method("Verse.TerrainGrid:RemoveFoundation"),
                    postfix: new HarmonyMethod(type.GetMethod("TerrainGrid_SetTerrain")));
                harmony.Patch(
                    AccessTools.Method("Verse.TerrainGrid:RemoveGravshipTerrainUnsafe"),
                    postfix: new HarmonyMethod(type.GetMethod("TerrainGrid_RemoveGravshipTerrainUnsafe")));
                harmony.Patch(
                    AccessTools.Method("Verse.TerrainGrid:SetTempTerrain"),
                    postfix: new HarmonyMethod(type.GetMethod("TerrainGrid_SetTerrain")));
                harmony.Patch(
                    AccessTools.Method("Verse.TerrainGrid:RemoveTempTerrain"),
                    postfix: new HarmonyMethod(type.GetMethod("TerrainGrid_SetTerrain")));

                harmony.Patch(
                    AccessTools.Method("Verse.Building:SpawnSetup"),
                    postfix: new HarmonyMethod(type.GetMethod("Building_SpawnSetup")));
                harmony.Patch(
                    AccessTools.Method("Verse.Building:DeSpawn"),
                    postfix: new HarmonyMethod(type.GetMethod("Building_DeSpawn")));

                MethodInfo method = AccessTools.Method("VanillaVehiclesExpanded.GarageDoor:SpawnGarage");
                if (method != null)
                    harmony.Patch(
                        method,
                        postfix: new HarmonyMethod(type.GetMethod("VVE_GarageDoor_SpawnGarage")));
            }
            catch (Exception e)
            {
                LC.Error("ApplyHarmonyPatches", $"Harmony patch failed: {e.ToString()}");
                return false;

            }
            return IsHarmonyPatched;
        }

        public static void RemoveHarmonyPatches() => harmony.UnpatchAll("Garwel.Celsius");

        // Replaces GenTemperature.TryGetDirectAirTemperatureForCell by providing cell-specific temperature
        public static bool GenTemperature_TryGetDirectAirTemperatureForCell(ref bool __result, IntVec3 c, Map map, out float temperature)
        {
            temperature = c.GetTemperatureForCell(map);
            __result = true;
            return false;
        }

        // Replaces Room.Temperature with room's average temperature (e.g. for displaying in the bottom right corner)
        // FIXME  Am not understanding this.
        public static bool Room_Temperature_get(ref float __result, Room __instance)
        {
            if (!__instance.Map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                return true;
            }
            __result = __instance.GetTemperature();
            return false;
        }

        // Replaces GenTemperature.PushHeat(IntVec3, Map, float) to change temperature at the specific cell instead of the whole room
        public static bool GenTemperature_PushHeat_IntVec3(ref bool __result, IntVec3 c, Map map, float energy) => __result = TemperatureUtility.TryPushHeat(c, map, energy);

        // Replaces GenTemperature.PushHeat(Thing, float) to push heat evenly from big things (e.g. geysers)
        public static bool GenTemperature_PushHeat_Thing(Thing t, float energy)
        {
            if (!t.MapHeld.TryGetTemprComp(out TemperatureInfo tinfo)) {
                LC.Warn("GenTemperature_PushHeat_Thing", $"no tinfo for map {t.MapHeld} where {t} is held.");
                return true;
            }
            if (t.def.Size == IntVec2.One)
            {
                tinfo.PushHeat(t.PositionHeld, energy);
                return false;
            }
            CellRect cells = t.OccupiedRect();
            energy /= t.def.Size.Area;
            for (int x = cells.minX; x <= cells.maxX; x++)
                for (int z = cells.minZ; z <= cells.maxZ; z++)
                    tinfo.PushHeat(new IntVec3(x, 0, z), energy);
            return false;
        }

        // Attaches to GenTemperature.ControlTemperatureTempChange to implement heat pushing for temperature control things (Heater, Cooler, Vent)
        public static float GenTemperature_ControlTemperatureTempChange(float result, IntVec3 cell, Map map, float energyLimit, float targetTemperature)
        {
            Room room = cell.GetRoom(map);
            float roomTemp = room != null && !room.TouchesMapEdge ? room.GetTemperature() : cell.GetSurroundingTemperature(map);
            if (energyLimit > 0)
                if (roomTemp < targetTemperature - TemperatureUtility.TemperatureChangePrecision)
                {
                    TemperatureUtility.TryPushHeat(cell, map, energyLimit);
                    return energyLimit;
                }
                else return 0;
            else if (roomTemp > targetTemperature + TemperatureUtility.TemperatureChangePrecision)
            {
                TemperatureUtility.TryPushHeat(cell, map, energyLimit);
                return energyLimit;
            }
            else return 0;
        }

        // Disables vanilla snow melting
        public static float SteadyEnvironmentEffects_MeltAmountAt(float result, float temperature) => 0;

        // Attaches to AttachableThing.Destroy to reduce temperature to just below the ignition temperature, when a Fire is destroyed
        public static void AttachableThing_Destroy(AttachableThing __instance) {
            if (Settings.AutoignitionEnabled && __instance is Fire) {
                if (__instance.Map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                    tinfo.Notify_FireSnuffedOut(__instance.Position);
                }
            }
        }

        // Replaces JobGiver_SeekSafeTemperature.ClosestRegionWithinTemperatureRange to only seek regions with no dangerous cells
        public static bool JobGiver_SeekSafeTemperature_ClosestRegionWithinTemperatureRange(ref Region __result, JobGiver_SeekSafeTemperature __instance, IntVec3 root, Map map, FloatRange tempRange, TraverseParms traverseParms)
        {
            var log = LC.Info("JobGiver_SeekSafeTemperature_ClosestRegionWithinTemperatureRange",
                         $"for {root.GetFirstPawn(map)} at {root} (t = {root.GetTemperatureForCell(map):F1}C)");

            Region region = root.GetRegion(map, RegionType.Set_Passable);
            if (region == null)
                return false;
            Region foundReg = null;
            RegionProcessor regionProcessor = delegate (Region r)
            {
                if (r.IsDoorway)
                    return false;
                if (r.Cells.All(cell => tempRange.Includes(cell.GetTemperatureForCell(map))))
                {
                    foundReg = r;
                    return true;
                }
                return false;
            };
            RegionTraverser.BreadthFirstTraverse(region, (Region from, Region r) => r.Allows(traverseParms, false), regionProcessor);
            if (foundReg != null) {
                log.Info($"Safe region found: {foundReg.DebugString}");
            } else {
                log.Info($"No safe region found for {root.GetFirstPawn(map)} at {root} (t = {root.GetTemperatureForCell(map):F1}C)");
            }
            __result = foundReg;
            return false;
        }

        // Attaches to DangerUtility.GetDangerFor to mark specific (too hot or too cold) cells as dangerous
        public static Danger DangerUtility_GetDangerFor(Danger result, IntVec3 c, Pawn p, Map map)
        {
            if (map == null || p == null)
                return result;
            float temperature = c.GetTemperatureForCell(map);
            FloatRange range = p.SafeTemperatureRange();
            Danger danger = range.Includes(temperature) ? Danger.None : (range.ExpandedBy(80).Includes(temperature) ? Danger.Some : Danger.Deadly);
            return danger > result ? danger : result;
        }

        // Disable MapTemperature.TemperatureUpdate, because vanilla temperature overlay is not used anymore
        public static bool MapTemperature_TemperatureUpdate() => false;

        // Replaces temperature display in the global controls view (bottom right)
        public static bool GlobalControls_TemperatureString(ref string __result)
        {
            Map map = Find.CurrentMap;
            if (map == null) { return true; }

            var mcell = UI.MouseCell();
            if (!mcell.InBounds(map)) { return true; }

            int index = map.cellIndices.CellToIndex(mcell);
            if (map.fogGrid.IsFogged(index)) { return true; }

            if (!map.TryGetTemprComp(out TemperatureInfo tinfo)) { return true; }

            __result = tinfo.GetTemperatureForCell(index).ToStringTemperature(Settings.TemperatureDisplayFormatString);

            float terrainTemperature = tinfo.GetTerrainTemperature(index);
            if (!float.IsNaN(terrainTemperature))
                // Localization Key: Celsius_Terrain - Terrain
                __result += $" / {"Celsius_Terrain".Translate()} {terrainTemperature.ToStringTemperature(Settings.TemperatureDisplayFormatString)}";
            return false;
        }

        // When door is opening, update its state and thermal values
        public static void Building_Door_DoorOpen(Building_Door __instance)
        {
            if (__instance.Map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                tinfo.Notify_DoorOpened(__instance);
            }
        }

        // When door is closing, update its state and thermal values
        public static void Building_Door_DoorTryClose(Building_Door __instance, bool __result)
        {
            if (__instance.Map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                tinfo.Notify_DoorClosed(__instance);
            }
        }
        // everything that changes topGrid or foundationGrid - in Verse.TerrainGrid
        // pfx: TerrainGrid_
        //
        // Stuffed into two postfixes since all we need is the IntVec3 and __instance
        //
        // SetTerrain is quite generic and will cause many unrelated notifications.
        // must take care that whatever's done on our side is side-effect free
        //      void SetTerrain(IntVec3 c, TerrainDef newTerr)
        //      void RemoveTopLayer(IntVec3 c, bool doLeavings = true)
        //      void SetFoundation(IntVec3 c, TerrainDef newTerr)
        //      void RemoveFoundation(IntVec3 c, bool doLeavings = true)
        //
        // does not send TerrainChanged event, but still swaps in underterrain to top
        //      void RemoveGravshipTerrainUnsafe(IntVec3 cell, int index)
        //      void SetTempTerrain(IntVec3 c, TerrainDef newTerr)
        //
        // this one does not but calls this.SetTerrain(c, terrainDef.tempTerrain.terrainOnRemoved);
        //      void RemoveTempTerrain(IntVec3 c, bool doLeavings = false, bool preventDestroyEffects = false)

        public static void TerrainGrid_SetTerrain(TerrainGrid __instance, IntVec3 c) {
            if (__instance.map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                tinfo.Notify_TerrainChanged(c);
            }
        }

        public static void TerrainGrid_RemoveGravshipTerrainUnsafe(TerrainGrid __instance, IntVec3 cell) {
            if (__instance.map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                tinfo.Notify_TerrainChanged(cell);
            }
        }

        // Building spawn/despawn
        //
        // this one uses base.Map.blah_etc. for everything, but sends
        //  the event to the parameter map.
        //
        //      void Building:SpawnSetup(Map map, bool respawningAfterLoad)
        //
        // respawningAfterLoad is true when from Map:FinalizeLoading().
        // so we filter that out that here.  That's true for all things btw.
        //
        // skipping because building spawn goes before Map:FinalizeInit()
        // which is the one that calls MapComponentUtility.FinalizeInit(this);
        // as the last thing
        // so those spawns are way before tinfo is initialized.
        //
        // this one also uses base.Map for everything. Why?
        //
        //      void Building:DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        //
        //  because Thing knows which map it's on by mapIndexOrState into Find.Maps
        // and this is updated in the middle of Thing:SpawnSetup
        //
        // so whatever, just do it like they do
        //

        public static void Building_SpawnSetup(Building __instance, Map map, bool respawningAfterLoad) {
            if (!respawningAfterLoad) {
                if (map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                    tinfo.Notify_BuildingSpawned(__instance);
                }
            }
        }
        public static void Building_DeSpawn(Building __instance) {
            if (__instance.Map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                tinfo.Notify_BuildingDespawned(__instance);
            }
        }

        // Adds heat push to ritual fires
        public static void CompRitualFireOverlay_CompTick(CompRitualFireOverlay __instance)
        {
            if (GenTicks.TicksAbs % 60 == 0 && __instance.FireSize > 0)
                TemperatureUtility.TryPushHeat(__instance.parent.Position,
                                               __instance.parent.Map,
                                               __instance.FireSize * Settings.RitualHeatPushPerFireSize);
        }

        // Vanilla Vehicles Expanded: When opening or closing a garage door, update its state and thermal values
        public static void VVE_GarageDoor_SpawnGarage(Building newGarage) {
            if (newGarage == null) {
                (new LogContext("VVE_GarageDoor_SpawnGarage")).Error("newGarage is null.");
                return;
            }
            CompThermal compThermal = newGarage.TryGetComp<CompThermal>();
            if (compThermal == null) {
                (new LogContext("VVE_GarageDoor_SpawnGarage")).Error($"no CompThermal for {newGarage.ThingID}.");
                return;
            }

            if (newGarage.def.defName.EndsWith("Opened")) {
                compThermal.Notify_DoorOpened(newGarage);
            } else {
                compThermal.Notify_DoorClosed(newGarage);
            }
        }
    }
}
