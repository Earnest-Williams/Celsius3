using RimWorld;
using System.Text;
using UnityEngine;
using Verse;

namespace Celsius
{
    public class StatWorker_Insulation : StatWorker
    {
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            if (req.HasThing) {
                if (TemperatureUtility.thermalsCache.TryGetThermals(req.Thing, out ThermalPair tpair)) {
                    CompThermal compThermal = req.Thing.TryGetComp<CompThermal>();
                    if (compThermal != null) {
                        if (compThermal.tstate.IsOpen()) {
                            return tpair.open.insulation;
                        }
                    }
                    return tpair.closed.insulation;
                }
            } else {
                if (TemperatureUtility.thermalsCache.TryGetThermals(req.Def, req.StuffDef, out ThermalPair tpair)) {
                    return tpair.closed.insulation;
                }
            }
            return 0;
        }

        void BuildExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense,
                                                    ThermalProps thermalProps, StringBuilder expl)
        {
            if (thermalProps == null) {
                return;
            }
            string defLabel = req.Def.label;
            string stuffLabel = req.StuffDef?.label;

            expl.AppendInNewLine("Celsius_Stat_Insulation_BaseInsulation"
                                    .Translate(defLabel, thermalProps.insulation.ToString("F1")).CapitalizeFirst());

            if (stuffLabel != null) {
                expl.AppendInNewLine("Celsius_Stat_Insulation_StuffInsulation".Translate(stuffLabel).CapitalizeFirst());
                expl.Append($"x{thermalProps.stuffInsulationFactor.ToStringPercent()}");
            }

            expl.AppendInNewLine("Celsius_Stat_Insulation_Airflow".Translate(thermalProps.airflow.ToStringPercent()));
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
