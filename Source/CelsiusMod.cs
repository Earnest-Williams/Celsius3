using UnityEngine;
using Verse;

using static Celsius.Settings;

namespace Celsius
{
    public class CelsiusMod : Mod
    {
        Vector2 scrollPosition = new Vector2();
        Rect viewRect;
        Listing_Standard content = new Listing_Standard();

        public CelsiusMod(ModContentPack content)
            : base(content) =>
            GetSettings<Settings>();

        static bool Neareq(float a, float b, float epsilon = 0.0001f) {
            return Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(b)) < epsilon;
        }

        public override void DoSettingsWindowContents(Rect rect)
        {
            if (viewRect.height <= 0)
                viewRect = new Rect(0, 0, rect.width - GenUI.ScrollBarWidth, 0);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            content.ColumnWidth = viewRect.width;
            content.Begin(viewRect);

            content.CheckboxLabeled("Celsius_Settings_ShowTempTooltip".Translate(), ref ShowTemperatureTooltip, "Celsius_Settings_ShowTempTooltip_tooltip".Translate());
            content.CheckboxLabeled("Celsius_Settings_FreezingAndMelting".Translate(), ref FreezingAndMeltingEnabled, "Celsius_Settings_FreezingAndMelting_tooltip".Translate());
            content.CheckboxLabeled("Celsius_Settings_AdvAutoignition".Translate(), ref AutoignitionEnabled, "Celsius_Settings_AdvAutoignition_tooltip".Translate());
            content.CheckboxLabeled("Celsius_Settings_PawnWeatherEffects".Translate(), ref PawnWeatherEffects, "Celsius_Settings_PawnWeatherEffects_tooltip".Translate());

            var fmt = $"N{TemperatureDisplayDigits}";
            content.Label("Celsius_Settings_TempDisplayDigits".Translate(TemperatureDisplayDigits.ToString(fmt)), tooltip: "Celsius_Settings_TempDisplayDigits_tooltip".Translate(TemperatureDisplayDigits_Default));
            TemperatureDisplayDigits = Mathf.RoundToInt(content.Slider(TemperatureDisplayDigits, 0, 2));
            TemperatureDisplayFormatString = $"F{TemperatureDisplayDigits}";

            content.Label("Celsius_Settings_SnowMeltRainFactor".Translate(SnowMeltRainFactor.ToStringPercent()), tooltip: "Celsius_Settings_SnowMeltRainFactor_tooltip".Translate(SnowMeltRainFactor_Default));
            SnowMeltRainFactor = content.Slider(SnowMeltRainFactor, 1, 5).RoundWithPrecision(0.05f);

            content.Gap();
            content.Label("Celsius_Settings_Colors".Translate());

            bool b = UseVanillaTemperatureColors;
            content.CheckboxLabeled("Celsius_Settings_Vanilla".Translate(), ref b, "Celsius_Settings_Vanilla_tooltip".Translate());
            if (b != UseVanillaTemperatureColors) { UseVanillaTemperatureColors = b; TemperatureUtility.ColorSettingsChanged(); }

            content.Gap();

            float n;
            content.Label("Celsius_Settings_T_MinMapped".Translate(T_MinMapped.ToStringTemperature()));
            n = content.Slider(T_MinMapped, -75, 233);
            if (!Neareq(n, T_MinMapped)) { T_MinMapped = n; TemperatureUtility.ColorSettingsChanged(); }

            content.Label("Celsius_Settings_T_MinComfort".Translate(T_MinComfort.ToStringTemperature()));
            n = content.Slider(T_MinComfort, -75, 233);
            if (n < T_MinMapped) n = T_MinMapped;
            if (!Neareq(n, T_MinComfort)) { T_MinComfort = n; TemperatureUtility.ColorSettingsChanged(); }

            content.Label("Celsius_Settings_T_MaxComfort".Translate(T_MaxComfort.ToStringTemperature()));
            n = content.Slider(T_MaxComfort, -75, 233);
            if (n < T_MinComfort) n = T_MinComfort;
            if (!Neareq(n, T_MaxComfort)) { T_MaxComfort = n; TemperatureUtility.ColorSettingsChanged(); }

            content.Label("Celsius_Settings_T_MaxMapped".Translate(T_MaxMapped.ToStringTemperature()));
            n = content.Slider(T_MaxMapped, -75, 233);
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
                ResetColors();
                TemperatureUtility.ColorSettingsChanged();
            }

            content.Gap();
            content.Label("Celsius_Settings_TakeOwnRisk".Translate().Colorize(Color.red));

            content.Label("Celsius_Settings_UpdateInterval".Translate(TicksPerUpdate), tooltip: "Celsius_Settings_UpdateInterval_tooltip".Translate(TicksPerUpdate_Default));
            TicksPerUpdate = (int)content.Slider(TicksPerUpdate, 1, 120).RoundWithPrecision(1);

            content.Label($"{"Celsius_Settings_ConvectionConductivityEffect".Translate()} x{ConvectionConductivityEffect}", tooltip: "Celsius_Settings_ConvectionConductivityEffect_tooltip".Translate(ConvectionConductivityEffect_Default));
            ConvectionConductivityEffect = Mathf.Round(content.Slider(ConvectionConductivityEffect, 1, 50));

            content.Label("Celsius_Settings_EnvironmentDiffusion".Translate(EnvironmentDiffusionFactor.ToStringPercent()), tooltip: "Celsius_Settings_EnvironmentDiffusion_tooltip".Translate(EnvironmentDiffusionFactor_Default.ToStringPercent()));
            EnvironmentDiffusionFactor = content.Slider(EnvironmentDiffusionFactor, 0, 1).RoundWithPrecision(0.05f);

            content.Label("Celsius_Settings_RoofInsulation".Translate(RoofInsulation), tooltip: "Celsius_Settings_RoofInsulation_tooltip".Translate(RoofInsulation_Default));
            RoofInsulation = content.Slider(RoofInsulation, 0, 20).RoundWithPrecision(0.5f);

            content.Label("Celsius_Settings_HeatPush".Translate(HeatPushMultiplier.ToStringPercent()), tooltip: "Celsius_Settings_HeatPush_tooltip".Translate());
            HeatPushMultiplier = content.Slider(HeatPushMultiplier, 0, 2).RoundWithPrecision(0.1f);
            HeatPushEffect = HeatPushEffect_Base * HeatPushMultiplier;

            content.Gap();
            content.CheckboxLabeled("Celsius_Settings_ForceIceConversion".Translate(), ref CompatForceIceConversion, "Celsius_Settings_ForceIceConversion_tooltip".Translate());

            content.CheckboxLabeled("Celsius_Settings_DebugMode".Translate(), ref DebugMode, "Celsius_Settings_DebugMode_tooltip".Translate());

            if (content.ButtonText("Celsius_Settings_ResetDefault".Translate()))
                Reset();

            if (content.ButtonText("Celsius_Settings_MeltIceNine".Translate())) {
                TemperatureUtility.MeltIceNine();
            }

            viewRect.height = content.MaxColumnHeightSeen + 40;
            content.End();
            Widgets.EndScrollView();
        }

        public override string SettingsCategory() => "Celsius";

        public override void WriteSettings()
        {
            LC.Spam("CelsiusMod.SettingsChanged", "WriteSettings");
            TemperatureUtility.SettingsChanged();
            base.WriteSettings();
            Settings.Dump();
        }
    }
}
