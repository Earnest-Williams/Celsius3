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
                    return tpair.closed.heatCapacity;
                }
            } else {
                if (TemperatureUtility.thermalsCache.TryGetThermals(req.Def, req.StuffDef, out ThermalPair tpair)) {
                    return tpair.closed.heatCapacity;
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
            string label = null;
            string stuffLabel = "stuffLabel placeholder";

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

            StringBuilder explanation = new StringBuilder("Celsius_Stat_HeatCapacity_StuffVolumetricHeatCapacity"
                .Translate(stuffLabel ?? "stuff", stuffThermalProperties.volumetricHeatCapacity)
                .CapitalizeFirst());
            explanation.AppendInNewLine("Celsius_Stat_HeatCapacity_Volume".Translate(label, thingThermalProperties.volume.ToString("F3")).CapitalizeFirst());
            explanation.AppendInNewLine("Celsius_Stat_HeatCapacity_AirHeatCapacity".Translate((1 - thingThermalProperties.volume).ToStringByStyle(stat.toStringStyle, numberSense)));
            explanation.AppendInNewLine(base.GetExplanationUnfinalized(req, numberSense));

            return explanation.ToString();
        }
    }
}
