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
// FIXME: 1. how to get to map; 2. what if we're minified and stuffed somewhere?
// gotta
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

        // these come from event handlers in TemperatureInfo, the below is guaranteed.
        // building must be === parent for these.
        public void Notify_DoorOpened(Building door) {
            if (ReferenceEquals(parent, door)) {
                tstate.UpdateFlicks(ThingOpenState.Open);
            }
        }

        public void Notify_DoorClosed(Building door) {
            if (ReferenceEquals(parent, door)) {
                tstate.UpdateFlicks(ThingOpenState.Closed);
            }
        }

        public void Notify_BuildingHitPointsChanged(Building building) {
            if (ReferenceEquals(parent, building)) {
                tstate.UpdateDamage(building.HitPoints, building.MaxHitPoints);
            }
        }
        // since we receive flicks directly, we must ping TemperatureInfo to update the cache
        // now how to get at the TemperatureInfo object for the map we're in?
        void Notify_FlickHappened() {
            if (this.parent is Building) { // do flickable non-buildings make any sense at all?
                Building building = (Building) this.parent;
                var tinfo = building.Map.TemperatureInfo(); // hate this backwards syntax sugar
                tinfo.Notify_BuildingFlicked(building);
            }
        }

/*  this one is called by
        ThingWithComps::BroadcastCompSignal()
            which is mostly called by thing's various comps

    and Notify_SignalReceived(signal) is called by
        SignalManager::SendSignal() for registered receivers of ISignalReceiver iface
          Thing registers itself if (this.def.receivesSignals)

*/

        public override void ReceiveCompSignal(string signal)
        {
            if (parent is Building_Vent) // Vents
                switch (signal.ToLowerInvariant()) {
                    case "flickedon":
                        tstate.UpdateFlicks(ThingOpenState.Open);
                        Notify_FlickHappened();
                        return;
                    case "flickedoff":
                        tstate.UpdateFlicks(ThingOpenState.Closed);
                        Notify_FlickHappened();
                        return;
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
                        Notify_FlickHappened();
                        return;
                    case "airoff":
                    case "bothoff":
                        tstate.UpdateFlicks(ThingOpenState.Closed);
                        Notify_FlickHappened();
                        return;
                    default:
                        return;
                }
            else return;

        }
    }
}
