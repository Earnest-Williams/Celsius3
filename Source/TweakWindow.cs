using System;
using RimWorld;
using Verse;
using UnityEngine;
using HarmonyLib;


namespace Celsius
{
    using static Settings;

    public static class SettingsWindow {
        static bool flushButtonPressed = false;
        static bool meltIceButtonPressed = false;
        static bool colorsResetButtonPressed = false;
        static bool modelResetButtonPressed = false;

        static TemperatureInfo tinfo = null;

        static Vector2 scrollPosition = new Vector2();
        static Rect viewRect;
        static Listing_Standard content = new Listing_Standard();

        public static bool spammed = false;

        public static void DoWindowContents(Rect rect, bool mainTabMode) {
            // I have no idea what I'm doing here, but these ui struggles
            // take entirely too much time.
            if (!spammed) { viewRect = new Rect(0, 0, 0, 0); spammed = true; }
            if (mainTabMode) {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, 0f, rect.width - 150f - 17f, 35f), "Celsius");
                Text.Font = GameFont.Small;
                rect = new Rect(0f, 40f, rect.width, rect.height - 40f );
            }

            if (viewRect.height <= 0) {
                viewRect = new Rect(0, 0, rect.width - GenUI.ScrollBarWidth, 0);
            }
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            content.ColumnWidth = viewRect.width;
            content.Begin(viewRect);

            content.CheckboxLabeled("Celsius_Settings_ShowTempTooltip".Translate(), ref ShowTemperatureTooltip, "Celsius_Settings_ShowTempTooltip_tooltip".Translate());
            content.CheckboxLabeled("Celsius_Settings_FreezingAndMelting".Translate(), ref FreezingAndMeltingEnabled, "Celsius_Settings_FreezingAndMelting_tooltip".Translate());

            bool b;
            b = AutoignitionEnabled;
            content.CheckboxLabeled("Celsius_Settings_AdvAutoignition".Translate(), ref AutoignitionEnabled, "Celsius_Settings_AdvAutoignition_tooltip".Translate());
            if (b != AutoignitionEnabled) { UpdateAutoignition(); }

            content.CheckboxLabeled("Celsius_Settings_PawnWeatherEffects".Translate(), ref PawnWeatherEffects, "Celsius_Settings_PawnWeatherEffects_tooltip".Translate());

            b = MainTabButtonVisible;
            content.CheckboxLabeled("Celsius_Settings_MainTabButtonVisible".Translate(), ref MainTabButtonVisible);
            if (b != MainTabButtonVisible) { UpdateMainTabButtonVisibility(); }

            content.Gap();
            var fmt = TemperatureDisplayFormatString;
            content.Label("Celsius_Settings_TempDisplayDigits".Translate(TemperatureDisplayDigits.ToString(fmt)), tooltip: "Celsius_Settings_TempDisplayDigits_tooltip".Translate(TemperatureDisplayDigits_Default));
            TemperatureDisplayDigits = Mathf.RoundToInt(content.Slider(TemperatureDisplayDigits, 0, 2));
            RecalculateValues(); // updates TemperatureDisplayFormatString from TemperatureDisplayDigits

            content.Label("Celsius_Settings_SnowMeltRainFactor".Translate(SnowMeltRainFactor.ToStringPercent()), tooltip: "Celsius_Settings_SnowMeltRainFactor_tooltip".Translate(SnowMeltRainFactor_Default));
            SnowMeltRainFactor = content.Slider(SnowMeltRainFactor, 1, 5).RoundWithPrecision(0.05f);

            content.Gap();

            //content.Label("Celsius_Settings_Colors".Translate());

            b = UseVanillaTemperatureColors;
            content.CheckboxLabeled("Celsius_Settings_Vanilla".Translate(), ref b, "Celsius_Settings_Vanilla_tooltip".Translate());
            if (b != UseVanillaTemperatureColors) { UseVanillaTemperatureColors = b; TemperatureUtility.ColorSettingsChanged(); }

            content.Gap();

            static bool Neareq(float a, float b, float epsilon = 0.0001f) {
                return Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(b)) < epsilon;
            }

            if (!ShowColorSettings) {
                ShowColorSettings = content.ButtonText("Celsius_Settings_ShowColorSettings".Translate());
            } else  {
                ShowColorSettings = !content.ButtonText("Celsius_Settings_HideColorSettings".Translate());

                float n;
                content.Label("Celsius_Settings_T_MinMapped".Translate(T_MinMapped.ToStringTemperature()));
                n = content.Slider(T_MinMapped, -75, 100);
                if (!Neareq(n, T_MinMapped)) { T_MinMapped = n; TemperatureUtility.ColorSettingsChanged(); }

                content.Label("Celsius_Settings_T_MinComfort".Translate(T_MinComfort.ToStringTemperature()));
                n = content.Slider(T_MinComfort, -75, 100);
                if (n < T_MinMapped) n = T_MinMapped;
                if (!Neareq(n, T_MinComfort)) { T_MinComfort = n; TemperatureUtility.ColorSettingsChanged(); }

                content.Label("Celsius_Settings_T_MaxComfort".Translate(T_MaxComfort.ToStringTemperature()));
                n = content.Slider(T_MaxComfort, -75, 100);
                if (n < T_MinComfort) n = T_MinComfort;
                if (!Neareq(n, T_MaxComfort)) { T_MaxComfort = n; TemperatureUtility.ColorSettingsChanged(); }

                content.Label("Celsius_Settings_T_MaxMapped".Translate(T_MaxMapped.ToStringTemperature()));
                n = content.Slider(T_MaxMapped, -75, 100);
                if (n < T_MaxComfort) n = T_MaxComfort;
                if (!Neareq(n, T_MaxMapped)) { T_MaxMapped = n; TemperatureUtility.ColorSettingsChanged(); }

                content.Label("Celsius_Settings_T_Saturation".Translate(T_Saturation.ToStringPercent()));
                n = content.Slider(T_Saturation * 100.0f, 0.0f, 100.0f).RoundWithPrecision(0.1f) / 100.0f;
                if (!Neareq(n, T_Saturation)) { T_Saturation = n; TemperatureUtility.ColorSettingsChanged(); }

                content.Label("Celsius_Settings_PressureExponent".Translate(PressureExponent.ToStringDecimalIfSmall()),
                    tooltip: "Celsius_Settings_PressureExponent_tooltip".Translate(PressureExponent_Default));

                n = content.Slider(PressureExponent, 1, 8).RoundWithPrecision(0.1f);
                if (!Neareq(n, PressureExponent)) { PressureExponent = n; TemperatureUtility.ColorSettingsChanged(); }

                if (content.ButtonText("Celsius_Settings_ResetColors".Translate())) {
                    colorsResetButtonPressed = true;
                } else {
                    if (colorsResetButtonPressed) {
                        ResetColors();
                        TemperatureUtility.ColorSettingsChanged();
                        colorsResetButtonPressed = false;
                    }
                }
            }

            content.Gap();

            content.Label("Celsius_Settings_UpdateInterval".Translate(TicksPerUpdate), tooltip: "Celsius_Settings_UpdateInterval_tooltip".Translate(TicksPerUpdate_Default));
            TicksPerUpdate = (int)content.Slider(TicksPerUpdate, 1, 120).RoundWithPrecision(1);

            content.Label("Celsius_Settings_CollectInterval".Translate(TicksPerCollect), tooltip: "Celsius_Settings_CollectInterval_tooltip".Translate(TicksPerCollect_Default));
            int i = (int)content.Slider(TicksPerCollect, 1, 120).RoundWithPrecision(1);
            if (i >= TicksPerUpdate) {
                TicksPerCollect = i;
            }

            content.Label("Celsius_Settings_TakeOwnRisk".Translate().Colorize(Color.red));

            content.Label($"{"Celsius_Settings_ConvectionConductivityEffect".Translate()} x{ConvectionConductivityEffect}", tooltip: "Celsius_Settings_ConvectionConductivityEffect_tooltip".Translate(ConvectionConductivityEffect_Default));
            ConvectionConductivityEffect = Mathf.Round(content.Slider(ConvectionConductivityEffect, 1, 50));

            content.Label("Celsius_Settings_EnvironmentDiffusion".Translate(EnvironmentDiffusionFactor.ToStringPercent()), tooltip: "Celsius_Settings_EnvironmentDiffusion_tooltip".Translate(EnvironmentDiffusionFactor_Default.ToStringPercent()));
            EnvironmentDiffusionFactor = content.Slider(EnvironmentDiffusionFactor, 0, 1).RoundWithPrecision(0.05f);

            content.Label("Celsius_Settings_RoofInsulation".Translate(RoofInsulation), tooltip: "Celsius_Settings_RoofInsulation_tooltip".Translate(RoofInsulation_Default));
            RoofInsulation = content.Slider(RoofInsulation, 0, 20).RoundWithPrecision(0.5f);

            content.Label("Celsius_Settings_HeatPush".Translate(HeatPushMultiplier.ToStringPercent()), tooltip: "Celsius_Settings_HeatPush_tooltip".Translate());
            HeatPushMultiplier = content.Slider(HeatPushMultiplier, 0, 2).RoundWithPrecision(0.1f);
            HeatPushEffect = HeatPushEffect_Base * HeatPushMultiplier;

            if (mainTabMode) {
                content.Gap();
                if (content.ButtonText("Celsius_Settings_FlushCaches".Translate())) {
                    flushButtonPressed = true;
                } else {
                    if (flushButtonPressed) {
                        RecalculateValues();
                        TemperatureUtility.FlushCaches();
                        flushButtonPressed = false;
                    }
                }
            }
            if (content.ButtonText("Celsius_Settings_ResetModel".Translate())) {
                modelResetButtonPressed = true;
            } else {
                if (modelResetButtonPressed) {
                    ResetModel();
                    TemperatureUtility.FlushCaches();
                    modelResetButtonPressed = false;
                }
            }

            content.Gap();
            content.CheckboxLabeled("Celsius_Settings_DebugMode".Translate(), ref DebugMode, "Celsius_Settings_DebugMode_tooltip".Translate());

            if (mainTabMode) {
                content.Gap();
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.3f, 0.35f);
                b = content.ButtonText("Celsius_Settings_MeltIceNine".Translate());
                GUI.color = oldColor;
                if (b) {
                    meltIceButtonPressed = true;
                } else {
                    if (meltIceButtonPressed) {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Celsius_ReallyMeltIceNine".Translate(),
                                                                                    delegate { TemperatureUtility.MeltIceNine(); },
                                                                                   true, null, WindowLayer.Dialog));
                        meltIceButtonPressed = false;
                    }
                }
            } else {
                content.Gap();
                content.CheckboxLabeled("Celsius_Settings_ForceIceConversion".Translate(), ref CompatForceIceConversion, "Celsius_Settings_ForceIceConversion_tooltip".Translate());
            }

            if (mainTabMode && TemperatureUtility.TryGetTemprComp(Find.CurrentMap, out tinfo)) {
                var mms = tinfo.modelMsecEma.ema;
                var mrt = tinfo.modelRunTickEma.ema;
                var mpt = tinfo.modelPeriodTickEma.ema;
                var cms = tinfo.collectorMsecEma.ema;
                var crt  = tinfo.collectorRunTickEma.ema;
                var cpt  = tinfo.collectorPeriodTickEma.ema;
                var mst = tinfo.modelStepEma.ema;
                var vms = tinfo.vacuumMsecEma.ema;

                content.Label($"Model run {mms:F1} ms / {mrt:F1} ticks; period {mpt:F1} stepFactor {mst:F3}");
                content.Label($"Collector run {cms:F1} ms / {crt:F1} ticks; period {cpt:F1} ticks.");
                if (tinfo.IsThereVacuum()) { content.Label($"Vacuum update {vms:F1} ms"); }
                TemperatureUtility.thermalsCache.Counts(out int things, out int terrains, out int terrFlam, out int whitelist);
                content.Label($"Interesting number: {TemperatureUtility.threadIdChanges}");
                content.Label($"Caches: ThermalProps {things} defWhitelist {whitelist}");
                content.Label($"TerrainDefs {terrains} Terrain flammability {terrFlam}");
                content.Label($"vacUpdates {tinfo.vacUpdates} vacCount {tinfo.vacCount} min {tinfo.vacMin:F2} max {tinfo.vacMax:F2} ");
                content.Gap();
                content.Label($"Model is being run too frequently if model run time in ticks");
                content.Label($"approaches its period. Or, the same thing, actual period lags");
                content.Label($"behind one set by the slider.");

/* TODO: figure out what I was thinking
 *
 * how to know if you run the model too frequently: it is when
                if (mms > 1.0f) {
                    const float targetTPS = 60.0f;
                    const float sixtyTPSBudget = 1000.0f / 60.0f;
                    const float
                    sixtyTPSBudget / mms
                    content.Gap();
                    content.Label($"Recommended model update period for 60 tps: {rmp}");
                }
*/
            }

            viewRect.height = content.MaxColumnHeightSeen + 40;
            content.End();
            Widgets.EndScrollView();
        }
    }
}

namespace Celsius {

    public class TweakWindow :  RimWorld.MainTabWindow
    {
        public TweakWindow() {
            //forcePause = true;Dialog_ModSettings dms;
            //dms = new Dialog_ModSettings(CelsiusMod.getModHandle());
            SettingsWindow.spammed = false;
        }

        public override void DoWindowContents(Rect inRect) {
            SettingsWindow.DoWindowContents(inRect, mainTabMode: true);
            // dms looks nicer
            //dms.DoWindowContents(inRect);
        }

        public override void PreOpen() {
            base.PreOpen();

            windowRect.width  = UI.screenWidth > 512 ? 512 : UI.screenWidth;
            windowRect.height = UI.screenHeight - 36;
            windowRect.y = (float)(UI.screenHeight - 36) - windowRect.height;
        }
    }

    [HarmonyPatch(typeof(MainButtonsRoot))]
    [HarmonyPatch("MainButtonsOnGUI")]
    static public class MainButtonsRoot_MainButtonsOnGUI
    {
        private static KeyBindingDef kb;

        public static bool Prepare() {
            kb = DefDatabase<KeyBindingDef>.GetNamedSilentFail("Celsius_Tweak");
            return kb != null;
        }
        static void Postfix() {
            if (kb.KeyDownEvent) {
                MainButtonDef mb = DefDatabase<MainButtonDef>.GetNamedSilentFail("CelsiusTweak");
                if ( mb != null ) {
                    Find.MainTabsRoot.SetCurrentTab(mb, true);
                }
            }
        }
    }
}


