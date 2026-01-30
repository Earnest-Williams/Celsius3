using RimWorld;
using System.Text;
using UnityEngine;
using Verse;

namespace Celsius
{
    public class StatWorker_Insulation : StatWorker
    {
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true) {
            if (req.HasThing) {
                if (TemperatureUtility.thermalsCache.TryGetThermals(req.Thing, out ThermalPair tpair)) {
                    return tpair.closed.insulation;
                }
            } else {
                if (TemperatureUtility.thermalsCache.TryGetThermals(req.Def, req.StuffDef, out ThermalPair tpair)) {
                    return tpair.closed.insulation;
                }
            }
            return 0;
        }

        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            ThingThermalProperties thingThermalProperties = null;
            StuffThermalProperties stuffThermalProperties = null;
            ThermalPair tpair = new ThermalPair();
            bool isOpen = false;
            string label = "stuffLabel placeholder";
            string stuffLabel = null;

            if (req.HasThing)
            {
                label = req.Thing.Label;
                //stuffLabel = ;
                CompThermal compThermal = req.Thing.TryGetComp<CompThermal>();
                if (compThermal != null) {
                    isOpen = compThermal.tstate.IsOpen();
                    TemperatureUtility.thermalsCache.TryGetThermals(req.Thing, out tpair);
                }
            } else if (req.Def != null) {
                label = req.Def.label;
                TemperatureUtility.thermalsCache.TryGetThermals(req.Def, req.StuffDef, out tpair);
                thingThermalProperties = req.Def.GetModExtension<ThingThermalProperties>();
                stuffLabel = req.StuffDef?.label;
                stuffThermalProperties = req.StuffDef?.GetModExtension<StuffThermalProperties>();
            }

            var thermalProps = isOpen ? tpair.open : tpair.closed; // nullpair will result in null either way
            if (thermalProps == null)
                return base.GetExplanationUnfinalized(req, numberSense);

            StringBuilder explanation = new StringBuilder("Celsius_Stat_Insulation_BaseInsulation"
                .Translate(label, thingThermalProperties.insulation.ToString("F1"))
                .CapitalizeFirst());
            if (stuffThermalProperties != null)
            {
                explanation.AppendInNewLine("Celsius_Stat_Insulation_StuffInsulation".Translate(stuffLabel ?? "stuff").CapitalizeFirst());
                explanation.Append($"x{stuffThermalProperties.insulationFactor.ToStringPercent()}");
            }
            if (isOpen && thingThermalProperties.airflow != thingThermalProperties.airflowWhenOpen)
                explanation.AppendInNewLine("Celsius_Stat_Insulation_AirflowOpen".Translate(label).Colorize(Color.yellow).CapitalizeFirst());
            if (thermalProps.airflow != 0)
                explanation.AppendInNewLine("Celsius_Stat_Insulation_Airflow".Translate(thermalProps.airflow.ToStringPercent()));

            return explanation.ToString();
        }
    }
}
