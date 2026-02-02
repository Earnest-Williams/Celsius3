using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using Verse;
using RimWorld;

namespace Celsius
{
    public class ThermalProps
    {
        static ThermalProps air;
        public static ThermalProps Air => air;

        static ThermalProps() => Init();

        internal static void Init() => air = new ThermalProps(1, 1, 1, true);

        public readonly float heatCapacity;
        public readonly float insulation;  // Effective insulation (taking into cosideration airflow)
        public readonly float airflow;
        public readonly bool isAir;

        public readonly float conductivity;
        public readonly float heatflow;

        public float HeatFlow => heatflow;

        PhaseTransitionType phaseTransition;
        float transitionTemperature;
        public readonly TerrainDef turnsInto;

        public ThermalProps(float heatCapacity, float insulation, float airflow, bool isAir)
        {
            this.heatCapacity = heatCapacity;
            this.insulation = Mathf.Lerp(insulation, 1, airflow);
            this.airflow = airflow;
            this.conductivity = Mathf.Pow(Settings.ConductivityPowerBase, this.insulation);
            this.heatflow = this.heatCapacity * this.conductivity / Settings.ConvectionConductivityEffect;
            this.isAir = isAir;
        }

        public ThermalProps(TerrainThermalProperties ttp) : this(ttp.heatCapacity, ttp.insulation, airflow: 0.0f, isAir: false) {
            // airflow usually undef for terrains. hmm,
            phaseTransition = ttp.phaseTransition;
            transitionTemperature = ttp.transitionTemperature;
            turnsInto = ttp.turnsInto;
        }

        public ThermalProps(ThingThermalProperties ttp, StuffThermalProperties stp, bool open) {
            float aflow = open ? ttp.airflowWhenOpen : ttp.airflow;
            float heatcap = ttp.heatCapacity;
            float insul = ttp.insulation;

            if (stp != null) {
                heatcap = Mathf.Lerp(1, stp.volumetricHeatCapacity, ttp.volume);
                insul = ttp.insulation * stp.insulationFactor;
            }
            this.heatCapacity = heatcap;
            this.insulation = Mathf.Lerp(insul, 1, aflow);
            this.airflow = aflow;
            this.conductivity = Mathf.Pow(Settings.ConductivityPowerBase, this.insulation);
            this.heatflow = this.heatCapacity * this.conductivity / Settings.ConvectionConductivityEffect;
            this.isAir = false;
        }

        public bool Freezable() => phaseTransition == PhaseTransitionType.Freeze;

        public bool Meltable() => phaseTransition == PhaseTransitionType.Melt;

        public bool FreezesAt(float temperature) => phaseTransition == PhaseTransitionType.Freeze && temperature < transitionTemperature;

        public bool MeltsAt(float temperature) => phaseTransition == PhaseTransitionType.Melt && temperature > transitionTemperature;

        public override string ToString() => $"Heat capacity: {heatCapacity}. Insulation: {insulation}. Conductivity: {conductivity:P1}. Heat flow: {HeatFlow:F3}.";
    }

    public class StuffThermalProperties : DefModExtension
    {
        public float volumetricHeatCapacity = float.NaN;
        public float insulationFactor = float.NaN;

        public override IEnumerable<string> ConfigErrors() {
            if (float.IsNaN(volumetricHeatCapacity)) { yield return "volumetricHeatCapacity is not set; defaulting to 1000.0"; }
            if (float.IsNaN(insulationFactor)) { yield return "insulationFactor is not set; defaulting to 1.0"; }
        }

        public override string ToString() => $"Volumetric heat capacity: {volumetricHeatCapacity}/m3. InsulationFactor: {insulationFactor:P1}.";
    }

    public class ThingThermalProperties : DefModExtension
    {
        public float heatCapacity = float.NaN;
        public float volume = float.NaN;
        public float insulation = float.NaN;
        public float airflow = 0;
        public float airflowWhenOpen = 0;

        public ThingThermalProperties() { }

        public override IEnumerable<string> ConfigErrors() {
            if (float.IsNaN(insulation)) {
                insulation = 1;
                yield return "insulation is not set; defaulting to 1.0";
            }
            if (float.IsNaN(heatCapacity) && float.IsNaN(volume)) {
                heatCapacity = 1;
                yield return "neither heatCapacity nor volume set; defaulting to 1.0";
            }
        }

        public override string ToString() => $"Heat capacity: {heatCapacity:F1}. Volume: {volume:F3} m^3. Insulation: {insulation:F1}. Airflow: {airflow:P0}/{airflowWhenOpen:P0}.";

    }

    public enum PhaseTransitionType
    {
        NotSet = 0,
        None,
        Freeze,
        Melt
    }

    public class TerrainThermalProperties : DefModExtension {
        public float heatCapacity = float.NaN;
        public float insulation = float.NaN;
        public float transitionTemperature = float.NaN;
        public PhaseTransitionType phaseTransition = PhaseTransitionType.NotSet;
        public TerrainDef turnsInto = null;

        public override IEnumerable<string> ConfigErrors()
        {
            if (phaseTransition != PhaseTransitionType.NotSet
                && phaseTransition != PhaseTransitionType.None) {
                if (float.IsNaN(transitionTemperature)) {
                    phaseTransition = PhaseTransitionType.None;
                    yield return $"transitionTemperature is not set, setting PhaseTransitionType to None.";
                }
                if (turnsInto == null) {
                    phaseTransition = PhaseTransitionType.None;
                    yield return $"turnsInto is not set, setting PhaseTransitionType to None.";
                }
            }
            if (float.IsNaN(insulation)) {
                insulation = 1.0f;
                yield return "insulation is not set; defaulting to 1.0";
            }
            if (float.IsNaN(heatCapacity)) {
                heatCapacity = 1000.0f;
                yield return "heatCapacity is not set; defaulting to 1000.0";
            }
        }

        public override string ToString() =>
            $"Heat capacity: {heatCapacity:F1}. Insulation: {insulation:F1}. {phaseTransition}s at {transitionTemperature:F1}{(turnsInto != null ? $" into {turnsInto.defName}" : "")}.";
    }

    // this is guaranteed to have either both null or both non-null.
    public struct ThermalPair {
        public ThermalProps open;
        public ThermalProps closed;

        public ThermalPair(ThingThermalProperties ttp, StuffThermalProperties stp) {
            open = new ThermalProps(ttp, stp, true);
            closed = new ThermalProps(ttp, stp, false);
        }
        public ThermalPair() {
            open = null;
            closed = null;
        }
    }

    public class ThermalsCache {
        // TODO: Over a few dozen pairs a linear search could be faster
        // than a dict. We leave dicts here as a generic solution until
        // we have more perfomance data.

        // TerrainDef.shortHash map to terrain ThermalProps. Prepopulated.
        Dictionary<ushort, ThermalProps> terrainCache = new Dictionary<ushort, ThermalProps>(16);
        Dictionary<ushort, ThermalProps> _terrainCache = new Dictionary<ushort, ThermalProps>(16);

        // TerrainDef.shortHash map to terrain Flammability stat. Prepopulated.
        Dictionary<ushort, float> terrainFlammability =  new Dictionary<ushort, float>(16);
        Dictionary<ushort, float> _terrainFlammability =  new Dictionary<ushort, float>(16);

        // Set of all ThingDefs that have CompThermal applied to.
        Dictionary<ushort, bool> defWhitelist = new Dictionary<ushort, bool>(64);
        Dictionary<ushort, bool> _defWhitelist = new Dictionary<ushort, bool>(64);

        // ThingDef.SH<<16|StuffDef.SH map to thing ThermalProps. live update.
        // also caches negative hits as nulls.
        ConcurrentDictionary<uint, ThermalPair> thingCache = new ConcurrentDictionary<uint, ThermalPair>();

        LogContext log;

        ulong thingHits = 0;
        ulong thingMisses = 0;

        public float MissRatio => (float)((double)thingMisses/(double)(thingHits+thingMisses));
        public int Count => thingCache.Count;

        public void Counts(out int things, out int terrains, out int terrFlam, out int whitelist) {
            things = thingCache.Count;
            terrains = terrainCache.Count;
            terrFlam  = terrainFlammability.Count;
            whitelist = defWhitelist.Count;
        }

        private static Mutex flushMutex = new Mutex();

        ~ThermalsCache() { flushMutex.Dispose(); }

        public void Flush() {
            log = new LogContext("ThermalsCache.Flush");
            flushMutex.WaitOne(); // lock everyone out while we repopulate dicts
            _terrainCache.Clear();
            _terrainFlammability.Clear();
            foreach (TerrainDef tdef in DefDatabase<TerrainDef>.AllDefsListForReading) {
                var ttp = tdef.GetModExtension<TerrainThermalProperties>();
                if (ttp != null) {
                    _terrainCache.Add(tdef.shortHash, new ThermalProps(ttp));
                    if (ttp.heatCapacity < 0.001f) {
                        // TODO: was curious why all these checks of (!null and hC >0)
                        log.Info("terrain {tdef} has low heatCapacity of {ttp.heatCapacity}");
                    }
                }
                float tf = tdef.GetStatValueAbstract(StatDefOf.Flammability, null);
                if (tf > Fire.MinFireSize) { // map.terrainGrid.TerrainAt(i).Flammable()
                    _terrainFlammability.Add(tdef.shortHash, tf);
                }
            }

            _defWhitelist.Clear();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs) {
                if (CompThermal.ShouldApplyTo(def)) {
                    _defWhitelist.Add(def.shortHash, false);
                }
            }

            Gen.Swap(ref terrainCache, ref _terrainCache);
            Gen.Swap(ref terrainFlammability, ref _terrainFlammability);
            Gen.Swap(ref defWhitelist, ref _defWhitelist);

            thingCache.Clear();
            thingHits = 0;
            thingMisses = 0;
            flushMutex.ReleaseMutex();
        }

        public void LogStats() {
            log = new LogContext("ThermalsCache");
            log.Info($"terrainCache.Count = {terrainCache.Count}");
            log.Info($"terrainFlammability.Count = {terrainFlammability.Count}");
            log.Info($"thingCache.Count = {thingCache.Count}");
            log.Info($"thingProps hits={thingHits} misses={thingMisses} missratio {MissRatio:P2}");
        }

        public bool TryGetThermals(TerrainDef terrain, out ThermalProps props)  {
            return terrainCache.TryGetValue(terrain.shortHash, out props);
        }

        public bool TryGetTerrainFlammability(TerrainDef terrain, out float flams) {
            return terrainFlammability.TryGetValue(terrain.shortHash, out flams);
        }

        public bool TryGetThermals(Thing thing, out ThermalPair props) {
            // The Thing can be stuffable or not. A stuff is considered stuffed with itself.
            // stuff can have StuffThermalProperties or not.
            // if stuffable it can have volume defined or not.
            // if not stuffable it can have ThingThermalProperties or not.
            // it is also considered stuffed with itself for key generation.

            // we don't limit the size of this cache since it's expected to grow
            // no larger than a thousand items or so.

            // as far as I understand, defs' shortHashes are global over all defs
            // see Verse:ShortHashGiver
            // so for thing+stuff lookup we use th_SH <<16 | st_SH
            // and for thing.IsStuff == true th_SH = st_SH.
            // and for non-stuffable  st_SH = th_SH
            //   must result in unique cacheKey.

            // the defaults below cover both IsStuff and non-stuffable.
            Def thingDef = thing.def;
            Def stuffDef = thingDef;

            if (thing.def.MadeFromStuff) {
                stuffDef = thing.Stuff;
            }
            return TryGetThermals(thingDef, stuffDef, out props);
        }

        public bool TryGetThermals(Def thingDef, Def stuffDef, out ThermalPair props) {
            if (stuffDef == null) {
                stuffDef = thingDef;
            }
            uint cacheKey = (uint)thingDef.shortHash << 16 | stuffDef.shortHash;
            props = new ThermalPair();

            if (thingCache.TryGetValue(cacheKey, out props)) {
                thingHits += 1;
                return props.closed != null;
            }
            thingMisses += 1;

            bool unused;
            if (!defWhitelist.TryGetValue(thingDef.shortHash, out unused)) {
                thingCache.TryAdd(cacheKey, props); // cache the negative result
                return false;
            }

            log = new LogContext("TryGetThermals");
            // the above guarantees this below succeeds.
            var thingProps = thingDef.GetModExtension<ThingThermalProperties>();
            if (thingProps == null) {
                log.Error($"no ThingThermalProperties for whitelisted ThingDef {thingDef}");
                thingCache.TryAdd(cacheKey, props); // cache negative.
                return false;
            }

        // not stuffed and undefined heatCapacity = fail.
            StuffThermalProperties stuffProps = null;
            if (stuffDef != null) {
                stuffProps = stuffDef.GetModExtension<StuffThermalProperties>();
            }
            if (stuffProps == null) {
                // nonstuffed thingProps must have heatCapacity defined. or we can't do much.
                if (float.IsNaN(thingProps.heatCapacity) || thingProps.heatCapacity < 0.0001f) {
                    log.Error($"Non-stuffable ThingDef {thingDef} with CompThermal but heatCapacity of {thingProps.heatCapacity}");
                    thingCache.TryAdd(cacheKey, props); // cache negative.
                    return false;
                }
            }

            props = new ThermalPair(thingProps, stuffProps);
            thingCache.TryAdd(cacheKey, props);
            return true;
        }
    }
}
