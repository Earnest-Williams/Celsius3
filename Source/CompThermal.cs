using RimWorld;
using Verse;

namespace Celsius
{
    public enum ThingOpenState {
        None,
        Open,
        Closed,
    };

    public struct ThermallyRelevantState {
        public ThermallyRelevantState() {}
        public ThingOpenState ostate = ThingOpenState.None;
        public float damage = 0; // that is normalized damage.


        public void UpdateDamage(int hp, int maxHp) {
            damage = (float)hp/(float)maxHp;
        }
        public void UpdateFlicks(ThingOpenState state) {
            if (state != ThingOpenState.None) {
                ostate = state;
            }
        }
        public bool IsOpen() {
            return ostate == ThingOpenState.Open;
        }
    }

    public class CompThermal : ThingComp
    {
        public ThermallyRelevantState tstate;

        internal static bool ShouldApplyTo(ThingDef thingDef) => thingDef.category == ThingCategory.Building && thingDef.HasModExtension<ThingThermalProperties>();

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);

            ThingOpenState state = ThingOpenState.None;
            if (parent is Building_Door door) {
                state = door.Open ? ThingOpenState.Open : ThingOpenState.Closed;
            } else if (parent is Building_Vent) {
                state = parent.GetComp<CompFlickable>().SwitchIsOn ? ThingOpenState.Open : ThingOpenState.Closed;
            } else if (parent.GetType().FullName == "OpenTheWindows.Building_Window") {
                state = parent.GetComp<CompFlickable>().SwitchIsOn ? ThingOpenState.Open : ThingOpenState.Closed;
            }
            tstate.UpdateFlicks(state);
            tstate.UpdateDamage(parent.HitPoints, parent.MaxHitPoints);
        }

        // these come from Harmony as map events don't compile for me.
        public void Notify_DoorOpened(Building door) {
            tstate.UpdateFlicks(ThingOpenState.Open);
        }

        public void Notify_DoorClosed(Building door) {
            tstate.UpdateFlicks(ThingOpenState.Closed);
        }

        public void Notify_BuildingHitPointsChanged(Building building) {
            tstate.UpdateDamage(building.HitPoints, building.MaxHitPoints);
        }

/*  this one is called by
        ThingWithComps::BroadcastCompSignal()
            which is mostly called by thing's various comps

    and Notify_SignalReceived(signal) is called by
        SignalManager::SendSignal() for registered receivers of ISignalReceiver iface
          Thing registers itself if (this.def.receivesSignals)

    since we receive flicks directly, we must ping TemperatureInfo to update the cache
*/

        public override void ReceiveCompSignal(string signal)
        {
            if (parent is Building_Vent) // Vents
                switch (signal.ToLowerInvariant()) {
                    case "flickedon":
                        tstate.UpdateFlicks(ThingOpenState.Open);
                        break;
                    case "flickedoff":
                        tstate.UpdateFlicks(ThingOpenState.Closed);
                        break;
                    default:
                        return;
                }
            // OpenTheWindows mod
            else if (parent.GetType().FullName == "OpenTheWindows.Building_Window")
                switch (signal.ToLowerInvariant())
                {
                    case "airon":
                    case "bothon":
                        tstate.UpdateFlicks(ThingOpenState.Open);
                        break;
                    case "airoff":
                    case "bothoff":
                        tstate.UpdateFlicks(ThingOpenState.Closed);
                        break;
                    default:
                        return;
                }
            else return;

            if (this.parent is Building) { // do flickable non-buildings make any sense at all?
                Building building = (Building) this.parent;
                if (building.Map.TryGetTemprComp(out TemperatureInfo tinfo)) {
                    tinfo.Notify_BuildingFlicked(building);
                }
            }
        }
    }
}
