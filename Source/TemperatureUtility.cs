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
        public static TemperatureInfo TemperatureInfo(this Map map) { // NB: not nullable.
            TemperatureInfo tinfo = null;
            if (!temperatureInfos.TryGetValue(map, out tinfo)) {
                tinfo = map.GetComponent<TemperatureInfo>();
                temperatureInfos.Add(map, tinfo);
            }
            if (tinfo == null) { // crash early.
                throw new InvalidOperationException("TemperatureInfo MapComponent null for a map");
            }
            return tinfo;
        }

        public static void MeltIceNine() {
            foreach (TemperatureInfo temperatureInfo in temperatureInfos.Values) {
                temperatureInfo.MeltIceNine();
            }
        }

        public static void SettingsChanged(bool afterSettingsLoad = false, bool fromSetup = false, bool afterSettingsReset = false) {
            var log = new LogContext("SettingsChanged");

            if (fromSetup) {
                log.Error("fromSetup");
            } else if (afterSettingsReset) {
                log.Error("afterSettingsReset");
            } else if (afterSettingsLoad) {
                log.Error("afterSettingsLoad");
            } else {
                log.Info("Mod settings have changed. Updating data.");
            }

            // This disables vanilla autoignition.
            AccessTools.Field(typeof(SteadyEnvironmentEffects), "AutoIgnitionTemperatureRange").SetValue(null,
                Settings.AutoignitionEnabled
                    ? new FloatRange(float.MaxValue/2, float.MaxValue) // disable for good.
                    : new FloatRange(240, 1000));  // that's in vanilla code. why limit at 1000 though?

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
            TemperatureInfo temperatureInfo = map.TemperatureInfo();
            int index = map.cellIndices.CellToIndex(cell);
            if (!temperatureInfo.InBounds(index)) {
                var log = new LogContext("TryPushHeat");
                log.Error($"TemperatureInfo for {map} unavailable or cell {cell} is outside map boundaries!");
                return false;
            }
            temperatureInfo.PushHeat(index, energy);
            return true;
        }

        public static float GetTemperatureForCell(this IntVec3 cell, Map map) {
            return map.TemperatureInfo().GetTemperatureForCell(cell);
        }

        public static float GetSurroundingTemperature(this IntVec3 cell, Map map)
        {
            TemperatureInfo tempInfo = map.TemperatureInfo();
            if (!cell.InBounds(map))
                return map.mapTemperature.OutdoorTemp;
            float sum = cell.GetTemperatureForCell(map);
            foreach (IntVec3 c in GenAdjFast.AdjacentCellsCardinal(cell))
                sum += c.InBounds(map) ? c.GetTemperatureForCell(map) : cell.GetTemperatureForCell(map);
            return sum / 5;
        }

        public static float GetTemperature(this Room room) {
            if (room == null) {
                LogUtility.Log($"Trying to GetTemperature for null room!", LogLevel.Error);
                return TemperatureTuning.DefaultTemperature;
            }
            if (room.Map == null) { // Room:Temperature should work since tempTracker is always there.
                return room.Temperature;
            }
            if (room.TouchesMapEdge) {
                return room.Map.mapTemperature.OutdoorTemp;
            }
            return room.Map.TemperatureInfo().GetRoomAverageTemperature(room);
        }
#endregion mapaccess
        #region DIFFUSION

        static bool AlmostZero(float az) { return Mathf.Abs(az) < float.Epsilon; }
        public static void CalculateHeatTransferCells(float interactingTemperature, ThermalProps props, float airflow,
                                                      float modelStepFactor, ref float energy, ref float heatFlow, bool log = false)
        {
            // props is neigh cell.  interactingTemperature neigh temp
            // aiflow is this cell airflow.

            // Air has heat capacity = 1 and conductivity = 1
            if (AlmostZero(airflow - 1.0f) && props.isAir)
            {
                heatFlow += /* 1.0f  * */ modelStepFactor; // that magic 1.0 was here initially as heatFlow++
                energy += interactingTemperature * modelStepFactor;
                return;
            }

            // If one of the interacting cells is not air, need to take airflow into account
            float hf = AlmostZero(airflow) || AlmostZero(props.airflow)
                ? props.HeatFlow
                : props.HeatFlow * Mathf.Pow(Settings.ConvectionConductivityEffect, airflow * props.airflow);
            if (log)
                LogUtility.Log($"Interacting temperature: {interactingTemperature:F1}C. Mutual airflow: {airflow * props.airflow}. Heatflow: {hf:F3}.");
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
