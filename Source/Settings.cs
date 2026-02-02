using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace Celsius
{
    public class Settings : ModSettings
    {
        public static bool UseVanillaTemperatureColors;
        public static bool ShowTemperatureTooltip;
        public static bool FreezingAndMeltingEnabled;
        public static bool AutoignitionEnabled;
        public static bool PawnWeatherEffects;
        public static int TicksPerUpdate;
        public static int TicksPerCollect;
        public static float ConductivityPowerBase;
        public static float ConvectionConductivityEffect;
        public static float EnvironmentDiffusionFactor;
        public static float RoofInsulation;
        public static float RoofDiffusionFactor;
        public static float HeatPushMultiplier;
        public static float HeatPushEffect;
        public static int TemperatureDisplayDigits;
        public static string TemperatureDisplayFormatString;
        public static bool DebugMode = Prefs.LogVerbose;

        public static bool CompatForceIceConversion;
        public static float RitualHeatPushPerFireSize;
        public static float SnowMeltRainFactor;

        public static float PressureExponent;
        public static float T_MinMapped;
        public static float T_MinComfort;
        public static float T_MaxComfort;
        public static float T_MaxMapped;
        public static float T_Saturation;

        public static bool ShowColorSettings;
        public static bool MainTabButtonVisible;

        public const int TicksPerUpdate_Default = 20;
        public const int TicksPerCollect_Default = 60;
        public const float ConductivityPowerBase_Default = 0.5f;
        public const float ConvectionConductivityEffect_Default = 10;
        public const float EnvironmentDiffusionFactor_Default = 0.10f;
        public const float RoofInsulation_Default = 3;
        public const float HeatPushEffect_Base = 0.15f;
        public const int TemperatureDisplayDigits_Default = 0;
        public const float RitualHeatPushPerFireSize_Default = 21.0f;
        public const float SnowMeltRainFactor_Default = 2.0f;

        public const float PressureExponent_Default = 5.0f;
        public const float T_MinMapped_Default  = -12.0f;
        public const float T_MinComfort_Default =  15.5f;
        public const float T_MaxComfort_Default =  26.5f;
        public const float T_MaxMapped_Default  =  60.0f;
        public const float T_Saturation_Default  =  0.8f;

        public static bool MainTabButtonVisible_Default = true;
        public static bool ShowColorSettings_Default = false;

        static readonly LogContext log = new LogContext("Settings");

        public Settings() => Reset();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref UseVanillaTemperatureColors, "UseVanillaTemperatureColors");
            Scribe_Values.Look(ref ShowTemperatureTooltip, "ShowTemperatureTooltip", true);
            Scribe_Values.Look(ref FreezingAndMeltingEnabled, "FreezingAndMeltingEnabled", true);
            Scribe_Values.Look(ref AutoignitionEnabled, "AutoignitionEnabled", true);
            Scribe_Values.Look(ref PawnWeatherEffects, "PawnWeatherEffects", true);
            Scribe_Values.Look(ref TicksPerUpdate, "TicksPerUpdate", TicksPerUpdate_Default);
            Scribe_Values.Look(ref TicksPerCollect, "TicksPerCollect", TicksPerCollect_Default);
            Scribe_Values.Look(ref ConvectionConductivityEffect, "ConvectionConductivityEffect", ConvectionConductivityEffect_Default);
            Scribe_Values.Look(ref EnvironmentDiffusionFactor, "EnvironmentDiffusionFactor", EnvironmentDiffusionFactor_Default);
            Scribe_Values.Look(ref RoofInsulation, "RoofInsulation", RoofInsulation_Default);
            Scribe_Values.Look(ref HeatPushMultiplier, "HeatPushMultiplier", 1);
            Scribe_Values.Look(ref TemperatureDisplayDigits, "TemperatureDisplayDigits", TemperatureDisplayDigits_Default);
            Scribe_Values.Look(ref DebugMode, "DebugMode", forceSave: true);
            Scribe_Values.Look(ref CompatForceIceConversion, "CompatForceIceConversion", false);
            Scribe_Values.Look(ref RitualHeatPushPerFireSize, "RitualHeatPushPerFireSize", RitualHeatPushPerFireSize_Default);
            Scribe_Values.Look(ref SnowMeltRainFactor, "SnowMeltRainFactor", SnowMeltRainFactor_Default);

            Scribe_Values.Look(ref PressureExponent, "PressureExponent", PressureExponent_Default);
            Scribe_Values.Look(ref T_MinMapped, "T_MinMapped", T_MinMapped_Default);
            Scribe_Values.Look(ref T_MinComfort, "T_MinComfort", T_MinComfort_Default);
            Scribe_Values.Look(ref T_MaxComfort, "T_MaxComfort", T_MaxComfort_Default);
            Scribe_Values.Look(ref T_MaxMapped, "T_MaxMapped", T_MaxMapped_Default);
            Scribe_Values.Look(ref T_Saturation, "T_Saturation", T_Saturation_Default);

            Scribe_Values.Look(ref MainTabButtonVisible, "MainTabButtonVisible", MainTabButtonVisible_Default);
            Scribe_Values.Look(ref ShowColorSettings, "ShowColorSettings", ShowColorSettings_Default);

            if (Scribe.mode == LoadSaveMode.LoadingVars) {
                LC.Spam("Settings", "Settings loaded.");
                ApplyAll();
            }
        }

        public static void ResetColors() {
            PressureExponent = PressureExponent_Default;
            T_MinMapped = T_MinMapped_Default;
            T_MinComfort = T_MinComfort_Default;
            T_MaxComfort = T_MaxComfort_Default;
            T_MaxMapped = T_MaxMapped_Default;
            T_Saturation = T_Saturation_Default;
        }

        public static void ResetModel() {
            TicksPerUpdate = TicksPerUpdate_Default;
            TicksPerCollect = TicksPerCollect_Default;
            ConvectionConductivityEffect = ConvectionConductivityEffect_Default;
            EnvironmentDiffusionFactor = EnvironmentDiffusionFactor_Default;
            RoofInsulation = RoofInsulation_Default;
            HeatPushMultiplier = 1;
            RecalculateValues();
        }

        public static void Reset()
        {
            UseVanillaTemperatureColors = false;
            ShowTemperatureTooltip = true;
            FreezingAndMeltingEnabled = true;
            AutoignitionEnabled = true;
            PawnWeatherEffects = true;
            TemperatureDisplayDigits = TemperatureDisplayDigits_Default;
            CompatForceIceConversion = false;
            RitualHeatPushPerFireSize = RitualHeatPushPerFireSize_Default;
            SnowMeltRainFactor = SnowMeltRainFactor_Default;
            MainTabButtonVisible = MainTabButtonVisible_Default;
            ShowColorSettings = ShowColorSettings_Default;

            ResetColors();
            ResetModel();

            LC.Spam("Settings", "Settings reset.");
            Dump();
        }

        public static void UpdateMainTabButtonVisibility() {
            var mtbd = DefDatabase<MainButtonDef>.GetNamedSilentFail("CelsiusTweak");
            if (mtbd != null) { mtbd.buttonVisible = Settings.MainTabButtonVisible; }
        }

        public static void UpdateAutoignition() {
            // This disables vanilla autoignition.
            AccessTools.Field(typeof(SteadyEnvironmentEffects), "AutoIgnitionTemperatureRange").SetValue(null,
                Settings.AutoignitionEnabled
                    ? new FloatRange(float.MaxValue/2, float.MaxValue) // disable for good.
                    : new FloatRange(240, 1000));  // that's in vanilla code. why limit at 1000 though?
        }

        public static void ApplyAll() {
            RecalculateValues();
            UpdateMainTabButtonVisibility();
            UpdateAutoignition();
            TemperatureUtility.ColorSettingsChanged();
            TemperatureUtility.FlushCaches();
        }

        public static void RecalculateValues() {
            TemperatureDisplayFormatString = $"F{TemperatureDisplayDigits}";
            ConductivityPowerBase = Mathf.Pow(ConductivityPowerBase_Default, (float)TicksPerUpdate_Default / TicksPerUpdate);
            RoofDiffusionFactor = EnvironmentDiffusionFactor * Mathf.Pow(ConductivityPowerBase, RoofInsulation);
            HeatPushEffect = HeatPushEffect_Base * HeatPushMultiplier;
        }

        public static void Dump()
        {
            log.Spam($"UseVanillaTemperatureColors: {UseVanillaTemperatureColors}");
            log.Spam($"ShowTemperatureTooltip: {ShowTemperatureTooltip}");
            log.Spam($"FreezingAndMeltingEnabled: {FreezingAndMeltingEnabled}");
            log.Spam($"AutoignitionEnabled: {AutoignitionEnabled}");
            log.Spam($"PawnWeatherEffects: {PawnWeatherEffects}");
            log.Spam($"TicksPerUpdate: {TicksPerUpdate}");
            log.Spam($"ConductivityPowerBase: {ConductivityPowerBase}");
            log.Spam($"ConvectionConductivityEffect: {ConvectionConductivityEffect}");
            log.Spam($"EnvironmentDiffusionFactor: {EnvironmentDiffusionFactor}");
            log.Spam($"RoofInsulation: {RoofInsulation}");
            log.Spam($"RoofDiffusionFactor: {RoofDiffusionFactor}");
            log.Spam($"HeatPushMultiplier: {HeatPushMultiplier.ToStringPercent()}");
            log.Spam($"HeatPushEffect: {HeatPushEffect}");
            log.Spam($"TemperatureDisplayDigits: {TemperatureDisplayDigits}");
            log.Spam($"CompatForceIceConversion: {CompatForceIceConversion}");
            log.Spam($"RitualHeatPushPerFireSize: {RitualHeatPushPerFireSize}");
            log.Spam($"SnowMeltRainFactor: {SnowMeltRainFactor}");
        }
    }
}
