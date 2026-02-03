using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Celsius
{
    static class TemperatureUtility
    {
        public const float TemperatureChangePrecision = 0.01f;

        internal static readonly Dictionary<Map, TemperatureInfo> temperatureInfos = new Dictionary<Map, TemperatureInfo>();
        internal static readonly ThermalsCache thermalsCache = new ThermalsCache();

        // A map gets the TemperatureInfo MapComponent in Map::FillComponents()
        // which just instantiates all existing subclasses of MapComponent for each map.
        // Map:GetComponent<> is linear search over a List filled there.
        //
        // This can return null IFF TemperatureInfo constructor threw.
        // Said constructor is exacly one line:  this.map = map.
        //
        // So no point checking it everywhere.
        //
        // Now, it's not like we have dozens of maps. usually just one or three.
        // Might make sense to not look it up in a dict every time.
        //
        public static bool TryGetTemprComp(this Map map, out TemperatureInfo tinfo) {
            if (map == null) {
                // this happens f.ex. on mapgen when the generator replaces mineable walls
                // with ore and whatnot.
                tinfo = null;
                return false;
            }
            if (!temperatureInfos.TryGetValue(map, out tinfo)) {
                var log = new LogContext("TryGetTemprComp");
                tinfo = map.GetComponent<TemperatureInfo>();
                if (tinfo == null) { // Happens with Map Preview for example
                    log.Spam($"MapComponent null on set for a map {map}");
                    return false;
                }
                log.Spam($"First lookup for {map}");
                temperatureInfos.Add(map, tinfo);
            }
            return true;
        }

        public static void MeltIceNine() {
            foreach (TemperatureInfo temperatureInfo in temperatureInfos.Values) {
                temperatureInfo.MeltIceNine();
            }
        }

        public static void FlushCaches() {
            // Flush ThermalProps cache
            thermalsCache.Flush();

            // Invalidate per-map ThermalProps layers.
            foreach (TemperatureInfo temperatureInfo in temperatureInfos.Values) {
                temperatureInfo.ThermalPropertiesInvalidated();
            }
        }

        public static void ColorSettingsChanged() {
            // Invalidate temperatures layer in ui.
            foreach (TemperatureInfo temperatureInfo in temperatureInfos.Values) {
                temperatureInfo.ColorSettingsChanged();
            }
        }

#region mapaccess
        public static bool TryPushHeat(IntVec3 cell, Map map, float energy) {
            if (!cell.InBounds(map)) {
                return false;
            }
            if (map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                tinfo.PushHeat(cell, energy);
                return true;
            }
            LC.Error("TemperatureUtility.TryPushHeat", $"no tinfo for '{map}'");
            return false;
        }

        public static float GetTemperatureForCell(this IntVec3 cell, Map map) {
            if (map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                tinfo.GetTemperatureForCell(cell);
            }
            return map.mapTemperature.OutdoorTemp;
        }

        public static float GetSurroundingTemperature(this IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map)) {
                return map.mapTemperature.OutdoorTemp;
            }
            TemperatureInfo tinfo;
            if (!map.TryGetTemprComp(out tinfo)) {
                return map.mapTemperature.OutdoorTemp;
            }
            float sum = cell.GetTemperatureForCell(map);
            foreach (IntVec3 c in GenAdjFast.AdjacentCellsCardinal(cell))
                sum += c.InBounds(map) ? c.GetTemperatureForCell(map) : cell.GetTemperatureForCell(map);
            return sum / 5;
        }

        public static float GetTemperature(this Room room) {
            if (room == null) {
                LC.Error("TemperatureUtility.GetTemperature", "room is null");
                return TemperatureTuning.DefaultTemperature;
            }
            if (room.Map == null) { // Room:Temperature should work since tempTracker is always there.
                return room.Temperature;
            }
            if (room.TouchesMapEdge) {
                return room.Map.mapTemperature.OutdoorTemp;
            }
            TemperatureInfo tinfo;
            if (!room.Map.TryGetTemprComp(out tinfo)) {
                return room.Map.mapTemperature.OutdoorTemp;
            }
            return tinfo.GetRoomAverageTemperature(room);
        }
#endregion mapaccess
        #region DIFFUSION
        public static float RoundWithPrecision(this float value, float precision = 1) => Mathf.Round(value / precision) * precision;

        public static bool AlmostZero(this float az) { return Mathf.Abs(az) < float.Epsilon; }

        public static void CalculateHeatTransferCells(float interactingTemperature, ThermalProps props, float airflow,
                                                      float modelStepFactor, ref float energy, ref float heatFlow)
        {
            // props is neigh cell.  interactingTemperature neigh temp
            // aiflow is this cell airflow.
            //
            // interactingTemperature = temperatures[neighbourIndex],
            // props = GetThermalPropertiesAt(neighbourIndex)
            // airflow   =  localProps.airflow
            //


            // Air has heat capacity = 1 and conductivity = 1
            // this below obscurely shortcuts the case when both cells are air.
            if (AlmostZero(airflow - 1.0f) && props.isAir)
            {
                heatFlow += /* 1.0f  * */ modelStepFactor; // that magic 1.0 was here initially as heatFlow++
                energy += interactingTemperature * modelStepFactor;
                return;
            }

            // If one of the interacting cells is not air, need to take airflow into account
            float hf = airflow.AlmostZero() || props.airflow.AlmostZero()
                ? props.HeatFlow
                : props.HeatFlow * Mathf.Pow(Settings.ConvectionConductivityEffect, airflow * props.airflow);

            hf = hf * modelStepFactor;
            heatFlow += hf;
            energy += interactingTemperature * hf;
        }

        public static void CalculateHeatTransferTerrain(float terrainTemperature, ThermalProps props, float modelStepFactor, ref float energy, ref float heatFlow)
        {
            heatFlow += props.HeatFlow * modelStepFactor;
            energy += terrainTemperature * props.HeatFlow * modelStepFactor;
        }

        public static void CalculateHeatTransferEnvironment(float environmentTemperature, ThermalProps props, RoofDef roof,
                                                            float modelStepFactor, ref float energy, ref float heatFlow) {
            float hf;
            if (roof == null)
            {
                if (props.isAir)  // Air-to-air exchange (most common way on most maps)
                {
                    heatFlow += Settings.EnvironmentDiffusionFactor * modelStepFactor;
                    energy += environmentTemperature * Settings.EnvironmentDiffusionFactor * modelStepFactor;
                    return;
                }
                // Buildings without a roof
                hf = Settings.EnvironmentDiffusionFactor * props.HeatFlow * modelStepFactor;
                if (!AlmostZero(props.airflow))
                    hf *= Mathf.Pow(Settings.ConvectionConductivityEffect, props.airflow);
            }
            else if (!roof.isThickRoof)  // Thin roof: use insulation value from the settings (precomputed)
                hf = props.HeatFlow * Settings.RoofDiffusionFactor * modelStepFactor;
            else return;  // Thick roof: no heat exchange at all
            heatFlow += hf;
            energy += environmentTemperature * hf;
        }

        #endregion DIFFUSION
    }
}
