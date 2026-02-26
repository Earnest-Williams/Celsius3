using RimWorld;
using System.Text;
using Verse;

namespace Celsius
{
    public class StatWorker_HeatCapacity : StatWorker
    {
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            if (req.HasThing) {
                if (TemperatureUtility.thermalsCache.TryGetThermals(req.Thing, out ThermalPair tpair)) {
                    CompThermal compThermal = req.Thing.TryGetComp<CompThermal>();
                    if (compThermal != null) {
                        if (compThermal.tstate.IsOpen()) {
                            return tpair.open.heatCapacity;
                        }
                    }
                    return tpair.closed.heatCapacity;
                }
            } else {
                if (TemperatureUtility.thermalsCache.TryGetThermals(req.Def, req.StuffDef, out ThermalPair tpair)) {
                    return tpair.closed.heatCapacity;
                }
            }
            return 0;
        }

        void BuildExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense,
                                                    ThermalProps thermalProps, StringBuilder expl) {
            if (thermalProps == null) {
                return;
            }
            string defLabel = req.Def.label;
            string stuffLabel = req.StuffDef?.label;

            if (stuffLabel != null) {
                if (float.IsNaN(thermalProps.volumetricHeatCapacity)) {
                    expl.AppendInNewLine("Celsius_Stat_HeatCapacity_StuffVolumetricHeatCapacity".Translate(stuffLabel, thermalProps.volumetricHeatCapacity).CapitalizeFirst());
                }
            }

            expl.AppendInNewLine("Celsius_Stat_HeatCapacity_Volume".Translate(defLabel, thermalProps.volume.ToString("F3")).CapitalizeFirst());
            expl.AppendInNewLine("Celsius_Stat_HeatCapacity_AirHeatCapacity".Translate((1 - thermalProps.volume).ToStringByStyle(stat.toStringStyle, numberSense)));
        }

        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            string baseExpl = base.GetExplanationUnfinalized(req, numberSense);
            if (req.Def == null) {
                return baseExpl;
            }
            if (!TemperatureUtility.thermalsCache.TryGetThermals(req.Def, req.StuffDef, out ThermalPair tpair)) {
                return baseExpl;
            }

            StringBuilder expl = new StringBuilder();

            BuildExplanationUnfinalized(req, numberSense, tpair.closed, expl);
            if (tpair.open != null) {
                expl.AppendInNewLine("Celsius_Stat_Open_State".Translate());
                BuildExplanationUnfinalized(req, numberSense, tpair.open, expl);
            }
            expl.AppendInNewLine(baseExpl);
            return expl.ToString();
        }
    }
}
