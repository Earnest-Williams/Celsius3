using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using UnityEngine;
using RimWorld;
using Verse;

namespace Celsius
{
    class EMA {
        public float alpha = 0.5f;
        public float ema = 0.0f;
        public EMA(int lookback, float s0) { ema = s0; alpha = 2.0f / (lookback + 1); } // lb > 2! ; 3 for 0.5
        public float update(float v) { return ema = alpha * v + (1.0f - alpha) * ema; }
    }

    public class TemperatureInfo : MapComponent
    {
        // Minimum possible temperature of autoignition
        const float MinIgnitionTemperature = 100;

        // When previous runs were started.
        int collectorStartTick;
        int modelStartTick;

        // Thread payload run time moving averages
        EMA collectorTickEma = new EMA(3, 1);
        EMA collectorMsecEma = new EMA(3, 1);
        EMA modelTickEma = new EMA(3, 1);
        EMA modelMsecEma = new EMA(3, 1);

        // Time spent in MapComponentTick
        EMA mapComponentTickMsecEma = new EMA(3, 1);

        // Signals from threads that they are done with a payload run.
        bool collectorDone = false;
        bool modelDone = false;

        EventWaitHandle modelHandle = new ManualResetEvent(false);
        EventWaitHandle collectorHandle = new ManualResetEvent(false);

        Thread modelThread;
        Thread collectorThread;

        // Signal to threads to exit.
        bool threadsExitRequested = false;

        // model workset
        float[] temperatures;
        float[] worksetTemperatures;

        // intra-terrain heat propagation is not modelled,
        // so we make do without double-buffering
        float[] terrainTemperatures;

        // Buildings' ThermalProps. double-buffering actually not needed.
        ThermalProps[] readCTProps;
        ThermalProps[] writeCTProps;

        Dictionary<int, float> roomTemperatures = new Dictionary<int, float>();

        // the three are our way to pass map modifications around so that they
        // get applied in a place known not to bother the pathfinder.
        // concurrent because the collector runs a bunch of worker threads
        // from its own thread.
        public struct FireStart {
            public int at;
            public float size;
        };
        ConcurrentBag<FireStart> firesStarted = new ConcurrentBag<FireStart>();

        public struct SnowMelt {
            public int at;
            public float amount;
        };
        ConcurrentBag<SnowMelt> snowsMelted = new ConcurrentBag<SnowMelt>();

        public enum PTDir { Freeze, Melt };
        public struct TerrainChange {
            public int at;
            public PTDir dir;
        };
        ConcurrentBag<TerrainChange> terrainsChanged = new ConcurrentBag<TerrainChange>();

        // delayed PushHeat so that we don't lose data writing to buffer while model reads.
        public struct CannedHeat {
            public int at;
            public float energy;
        };
        List<CannedHeat> readCannedHeat = new List<CannedHeat>(23);
        List<CannedHeat> writeCannedHeat = new List<CannedHeat>(23);

        // phase transition's previous step for terrains w/o turnsInto to freeze/melt to.
        // not ConcurrentDictionary since terrain changes are applied and this is updated
        // from "main" thread only. Key: cell index in the map.
        Dictionary<int, TerrainDef> terrainFlops = new Dictionary<int, TerrainDef>();

        public TerrainDef CelsiusIceNine = null;
        public TerrainDef CelsiusSlade = null;

        CellBoolDrawer overlayDrawer;

        // this disables event handlers until after FinalizeInit() is done
        // since f.ex map generation spams SetTerrain.
        bool initialized = false;

        // state passed on from ExposeData()
        bool mapNeedsInitialization = true;
        bool iceConversionRequested = false;

        public TemperatureInfo(Map map) : base(map) { }

        public override void MapGenerated() {
            LogContext log = new LogContext($"MapGenerated{map}");
            log.Info("called.");
        }

        public override void FinalizeInit()
        {
            LogContext log = new LogContext($"FinalizeInit({map})");
            var stopWatch = Stopwatch.StartNew();
            int NumGridCells = map.cellIndices.NumGridCells;
            log.Info($"started, {map.cellIndices.SizeX}x{map.cellIndices.SizeZ}; {NumGridCells} cells.");

            if (initialized) {
                log.Error("called on initialized object");
            }

            // Not the best time for this but idk at what point
            // they end up accessible. But must be no later than here.
            CelsiusIceNine = TerrainDef.Named("IceNine");
            CelsiusSlade = TerrainDef.Named("Gravel");

            // Prefill ThermalProps grids. Not saving those because data is prone to modlist changes
            RepopulateCTProps();

            // TODO: rename this and temperatures maybe
            worksetTemperatures = new float[NumGridCells];

            if (temperatures == null) {
                temperatures = new float[NumGridCells];
            }

            if (terrainTemperatures  == null) {
                terrainTemperatures = new float[NumGridCells];
            }

            if (mapNeedsInitialization) {
                InitializeTemperatures();
                mapNeedsInitialization = false;
            }
            // old data conversion is delayed until here since LoadSave Scribe phase is too early
            // TODO: think what happens
            if (iceConversionRequested) {
                ConvertIceToIceNine();
                iceConversionRequested = false;
            }

            // do this after the above because ApplyTerrainEffects() also trigger TerrainChanged.

            // T'remains to be seen how bad is this performance-wise.
            // We call door.TryGetComp<CompThermal>() for each door open/close event.
            // Should be negligible. even a large map will have maybe ten popular doors.
            //
            // On the other had it's kinda pointless: all doors usually have CompThermal.
            // Not having it on a door means something's not patched for us.
            //
            // On the gripping had caching them (and removing on despawn) is a place for
            // bug infestations we don't want yet.

            /*
             * this does not compile with the very helpful
             *
             * CS0229: Ambiguity between 'MapEvents.TerrainChanged' and 'MapEvents.TerrainChanged'
             *
             * Resorting to Harmony for now.
             *
            map.events.DoorOpened += this.Notify_DoorOpened;
            map.events.DoorClosed += this.Notify_DoorClosed;
            map.events.TerrainChanged += TerrainChanged;
            map.events.BuildingSpawned += this.Notify_BuildingSpawned;
            map.events.BuildingDespawned += this.Notify_BuildingDespawned;

            // not enabling for now. TODO: damage increases airflow? like, cracks.
            //map.events.BuildingHitPointsChanged += this.Notify_BuildingHitPointsChanged;

            */


            overlayDrawer = new CellBoolDrawer(
                index => !map.fogGrid.IsFogged(index),
                () => Color.white,
                index => TemperatureColorForCell(index),
                map.Size.x,
                map.Size.z);

            collectorStartTick = GenTicks.TicksGame - 1; // so that for the first run
            modelStartTick = GenTicks.TicksGame - 1;     // ticksSinceLastRun is > 0.
            modelThread = new Thread(ModelThreadMethod);
            collectorThread = new Thread(CollectorThreadMethod);
            modelThread.Start();
            collectorThread.Start();

            stopWatch.Stop();
            log.Info($"done in {stopWatch.ElapsedMilliseconds} ms.");
            initialized = true;
        }

        void InitializeTemperatures() {
            var log = new LogContext("InitializeTemperatures");

            var NumGridCells = temperatures.Length;

            float outdoorTemperature = map.mapTemperature.OutdoorTemp;
            float indoorTemperature = map.mapTemperature.SeasonalTemp;

            for (int i = 0; i < NumGridCells; i++) {
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                Room room = cell.GetRoomOrAdjacent(map);
                if (room != null) {
                    temperatures[i] = roomTemperatures[room.ID] = room.Temperature;
                } else {
                    RoofDef roof = map.roofGrid.RoofAt(i);
                    temperatures[i] = roof != null && roof.isThickRoof ? indoorTemperature : outdoorTemperature;
                }
            }

            float snowDepth = map.GetAverageSnowDepth();

            int cellsWithTerrainTemperature = 0;
            for (int i = 0; i < NumGridCells; i++) {
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                TerrainDef terrain = map.terrainGrid.TerrainAtIgnoreTemp(i);
                if (TemperatureUtility.thermalsCache.TryGetThermals(terrain, out ThermalProps unused)) {
                    cellsWithTerrainTemperature++;
                    terrainTemperatures[i] = map.mapTemperature.OutdoorTemp;
                    // orig code only added snow iff a terrain freezed, which is strange.
                    if (snowDepth > 0.0001f && !cell.Roofed(map)) {
                        map.steadyEnvironmentEffects.AddFallenSnowAt(cell, snowDepth);
                    }
                    // FIXME: don't dump this every time
                    log.Info($" cell-with-tertemp [{cell.x},{cell.z}] terr={terrain} t={map.mapTemperature.OutdoorTemp}");
                } else {
                    terrainTemperatures[i] = float.NaN;
                }
            }
            log.Info($"{cellsWithTerrainTemperature}/{NumGridCells} track terrain temperature.");

            CollectTerrainEffects(); // melt/freeze. melt snow for one tick.

            log.Info($"Applying {terrainFlops.Count} terrain flops, {snowsMelted.Count} snowmelts and {firesStarted.Count} autoignitions.");

            // delayed  until the first tick since melts can destroy stuff and who knows
            // what state is that stuff in at this moment.
            //if (false) { ApplyTerrainEffects(); }
        }

#region CTBuffers
        void SwapCTProps() {
            Gen.Swap(ref readCTProps, ref writeCTProps);
        }

        void RepopulateCTProps() {
            LogContext log = new LogContext($"RepopulateCTProps({map})");
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            if (writeCTProps == null) {
                writeCTProps = new ThermalProps[map.cellIndices.NumGridCells];
            }
            PrefillCTProps(writeCTProps);
            if (readCTProps == null) {
                readCTProps = (ThermalProps[])writeCTProps.Clone();
            } else {
                writeCTProps.CopyTo(readCTProps, 0);
            }
            stopWatch.Stop();
            log.Info($"done in {stopWatch.ElapsedMilliseconds} ms.");
        }
        void PrefillCTProps(ThermalProps[] tpbuffer) {
            var log = new LogContext("PrefillCTProps");
            Array.Clear(tpbuffer,0, tpbuffer.Length); // should fill with nulls

            for (int index = 0; index < tpbuffer.Length; index++) {
                bool done = false;
                foreach (Thing thing in map.thingGrid.ThingsListAtFast(index)) {
                    if (TemperatureUtility.thermalsCache.TryGetThermals(thing, out ThermalPair tpair)) {
                        if (!thing.Spawned) { // skip minified and otherwise irrelevant things
                            continue;
                        }
                        if (done) {
                            // unlikely, right now this means two spawned buildings in one cell.
                            log.Error($"Second eligible thing {thing} seen at {index}, ignoring.");
                            break;
                        }
                        // at this point we're pretty sure the thing does have CompThermal
                        var ct = thing.TryGetComp<CompThermal>();
                        tpbuffer[index] = ct.tstate.IsOpen() ? tpair.open : tpair.closed;
                        done = true;
                        break;
                    }
                }
            }
        }
#endregion CTBuffers
#region EventHandlers
        void UpdateCTProps(Building building) {
            if (!initialized) { return; }
            if (building == null ) { return; }

            ThermalProps tprops = null;
            if (building.Spawned) {
                if (TemperatureUtility.thermalsCache.TryGetThermals(building, out ThermalPair tpair)) {
                    var ct = building.TryGetComp<CompThermal>();
                    tprops = ct.tstate.IsOpen() ? tpair.open : tpair.closed;
                }
            }
            foreach (IntVec3 cell in building.OccupiedRect()) {
                writeCTProps[map.cellIndices.CellToIndex(cell)] = tprops;
            }
        }

        public void Notify_BuildingSpawned(Building building) {
            UpdateCTProps(building);
        }
        public void Notify_BuildingDespawned(Building building) {
            UpdateCTProps(building);
        }
        // this is called from CompThermal and in theory we can avoid CT lookups
        // or pass ostate here directly but do not do that for the sake of clarity.
        public void Notify_BuildingFlicked(Building building) {
            UpdateCTProps(building);
        }
        // this is triggered by
        //   SetUnderTerrain()
        //   DoTerrainChangedEffects() which is called from
        //      SetTerrain()
        //      RemoveTopLayer()
        //      SetFoundation() / RemoveFoundation()
        //      RemoveTempTerrain() / SetTempTerrain()
        public void Notify_TerrainChanged(IntVec3 cell) {
            if (!initialized) { return; }
            var i = map.cellIndices.CellToIndex(cell);
            TerrainDef terrain = map.terrainGrid.TerrainAtIgnoreTemp(i);
            if (TemperatureUtility.thermalsCache.TryGetThermals(terrain, out ThermalProps unused)) {
                terrainTemperatures[i] = temperatures[i];
            } else {
                terrainTemperatures[i] = float.NaN;
            }
        }
        public void Notify_DoorOpened(Building_Door door) {
            var ct = door.TryGetComp<CompThermal>();
            if (ct != null) {
                ct.Notify_DoorOpened(door);
                UpdateCTProps(door);
            }
        }
        public void Notify_DoorClosed(Building_Door door) {
            var ct = door.TryGetComp<CompThermal>();
            if (ct != null) {
                ct.Notify_DoorClosed(door);
                UpdateCTProps(door);
            }
        }

        // I'm still not sure why this hack is even needed.
        public void Notify_FireSnuffedOut(IntVec3 cell) {
            var log = new LogContext("Notify_FireSnuffedOut({cell.x}, {cell.z})");
            int index = map.cellIndices.CellToIndex(cell);
            bool seenSomething = false;
            float curTemp = temperatures[index];
            float minTemp = float.MaxValue;
            foreach (Thing thing in map.thingGrid.ThingsListAtFast(index)) {
                if (thing.FireBulwark) { // not likely this could happen - a fire at a bulwark
                    log.Warn("A fire at a bulwark ({thing}).. hmm. T={curTemp}");
                    return;
                }
                if (thing.GetStatValue(StatDefOf.Flammability) > 0) {
                    float iTemp = thing.GetStatValue(DefOf.Celsius_IgnitionTemperature);
                    if (iTemp > MinIgnitionTemperature && iTemp < minTemp) {
                        seenSomething = true;
                        minTemp = iTemp;
                    }
                }
            }
            if (seenSomething) {
                // a roundabout way for delayed temperature modification
                float deltaT = curTemp - minTemp + 1;
                log.Info(" deltaT={deltaT:F1}");
                if (deltaT < 0) {
                    PushHeat(index, -deltaT * GetThermalPropertiesAt(index).heatCapacity / Settings.HeatPushEffect);
                }
            } else {
                log.Info(" nothing flammable here, doing nothing.");
            }
        }
#endregion EventHandlers
#region LoadSave
       TerrainDef BestUnderIceTerrain(int index) {
           var underTerrain = map.terrainGrid.UnderTerrainAt(index);
            if (underTerrain != null)
                return underTerrain;

           // we're called in before TemperatureInfo::Init()
           var tCache = TemperatureUtility.thermalsCache;
           IntVec3 cell = map.cellIndices.IndexToCell(index);
           bool foundGround = false;
           foreach (IntVec3 ncell in GenAdjFast.AdjacentCells8Way(cell)) {
                if (!ncell.InBounds(map))
                    continue;
                ThermalProps tprops = null;
                var nindex = map.cellIndices.CellToIndex(ncell);
                var terrain = map.terrainGrid.TerrainAtIgnoreTemp(nindex);
                if (tCache.TryGetThermals(terrain, out tprops)) {
                    if (tprops.Freezable()) { // something melted nearby
                        return terrain;
                    }
                    // a neighbour meltable into something is nice too
                    if (tprops.Meltable() && tprops.turnsInto != null) {
                        return terrain;
                    }
                    if (!tprops.Meltable()) { // but at least we found some ground
                        foundGround = true;
                    }
                }
                underTerrain = map.terrainGrid.UnderTerrainAt(nindex);
                if (underTerrain == null) {
                    continue;
                }
                if (tCache.TryGetThermals(underTerrain, out tprops)) {
                    if (tprops.Freezable()) { // something froze into something defined nearby
                        return underTerrain;
                    }
                    if (!tprops.Meltable()) { // but at least we found some ground
                        foundGround = true;
                    }
                }
            }
            // well, gotta melt into water.
            if (foundGround)
                return map.Biome == BiomeDefOf.SeaIce ? TerrainDefOf.WaterOceanShallow : TerrainDefOf.WaterShallow;
            return map.Biome == BiomeDefOf.SeaIce ? TerrainDefOf.WaterOceanDeep : TerrainDefOf.WaterDeep;
        }

        void ConvertIceToIceNine() {
            var log = new LogContext($"ConvertIceToIceNine({map})");
            if (map.Biome == BiomeDefOf.SeaIce || map.Biome == BiomeDefOf.IceSheet) {
                if (!Settings.CompatForceIceConversion) {
                    // TODO: make this a popup or something.
                    log.Error($"Map biome is {map.Biome}. Not converting ice to meltable (can be forced in mod options).");
                    return;
                }
            }
            // terrainFlops must be empty at this point
            if (terrainFlops.Count != 0) {
                log.Error($"terrainFlops.Count = {terrainFlops.Count}, wtf? Aborting conversion.");
                return; // means we got here by a huge mistake
            }
            int cnt = 0;
            for (int i = 0 ; i < map.cellIndices.NumGridCells; i++) {
                // direct grid manipulation, since otherwise it's a ton
                // of unnecessary and useless TerrainChanged events.
                // esp. at this point, where game is not running yet.
                var oldTerrain = map.terrainGrid.TerrainAtIgnoreTemp(i);
                if (oldTerrain == TerrainDefOf.Ice) {
                    terrainFlops[i] = BestUnderIceTerrain(i);
                    map.terrainGrid.topGrid[i] = CelsiusIceNine;
                    map.terrainGrid.underGrid[i] = null;
                    cnt ++;
                }
            }
            log.Info($"{cnt} icy cells in {map.Biome} converted to meltable variant.");
        }

        public override void ExposeData() {
            base.ExposeData();
            LogContext log = new LogContext("ExposeData()");

            string modVersion = null;
            string dataVersion = null;

            Scribe_Values.Look(ref modVersion, "version");
            Scribe_Values.Look(ref dataVersion, "format");

            switch(Scribe.mode) {
                case LoadSaveMode.Inactive:
                    log.Info($"{Scribe.mode}");
                    return;
                case LoadSaveMode.Saving:
                    log = new LogContext($"Saving {map}");
                    SaveData();
                    return;
                // the three below are in order as they are called.
                // two latter back to back from one function
                case LoadSaveMode.LoadingVars:
                    if (dataVersion != null) {
                        LoadData(modVersion, dataVersion);
                    } else {
                        LoadOldStyleData(modVersion);
                    }
                    return;
                case LoadSaveMode.ResolvingCrossRefs:
                    log.Info($"{Scribe.mode}");
                    return;
                case LoadSaveMode.PostLoadInit:
                    log.Info($"{Scribe.mode}");
                    log.Info("Can we do the ice here?"); // let's at least try
                    if (iceConversionRequested) {
                        ConvertIceToIceNine();
                        iceConversionRequested = false;
                    }
                    return;
            }
        }

        const int MaxSupportedDataFormatRevisionOrHangMyLaundryOnThemToDry = 1;

        void SaveData() {
            string modVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            string dataVersion = MaxSupportedDataFormatRevisionOrHangMyLaundryOnThemToDry.ToString();
            Scribe_Values.Look(ref modVersion, "version");
            Scribe_Values.Look(ref dataVersion, "format");

            var ms = new MemoryStream(5 + 4 * temperatures.Length);
            ms.WriteFloatArray(temperatures);
            var buffer = ms.ToArray();
            DataExposeUtility.LookByteArray(ref buffer, "buildingTemps");

            ms.Seek(0, SeekOrigin.Begin);
            ms.WriteFloatArray(terrainTemperatures);
            buffer = ms.ToArray();
            DataExposeUtility.LookByteArray(ref buffer, "terrainTemps");

            if (terrainFlops.Count > 0) {
                ms.Seek(0, SeekOrigin.Begin);
                ms.WriteMapInt2Str(terrainFlops);
                buffer = ms.ToArray();
                DataExposeUtility.LookByteArray(ref buffer, "terrainFlops");
            }
        }

        const int MinSupportedDataFormatRevisionAndICanAlsoWriteNovelsInIdentifiers = 1;
        void LoadData(string modVersion, string dataVersion) {
            var log = new LogContext($"LoadData");

            if (modVersion == null) {
                log.Error("null mod version but data format='{dataVersion}'. Weird.");
                return;
            }

            int rev;
            if (!Int32.TryParse(dataVersion, out rev)) {
                log.Error("data format does not parse: '{dataVersion}' (from '{modVersion}'). Weird.");
                return;
            }

            if (rev < MinSupportedDataFormatRevisionAndICanAlsoWriteNovelsInIdentifiers
                || rev > MaxSupportedDataFormatRevisionOrHangMyLaundryOnThemToDry) {
                log.Error("can't load data v{rev} by v{modVersion}");
                return;
            }
            byte[] buffer = null;

            // yeah, well. but they are buildings. even if we track empty cells too
            DataExposeUtility.LookByteArray(ref buffer, "buildingTemps");
            if (buffer != null) {
                var ms = new MemoryStream(buffer);
                temperatures = ms.ReadFloatArray();
            } else {
                log.Error("buildingTemps array missing. All temperature data will be reset.");
                return;
            }

            DataExposeUtility.LookByteArray(ref buffer, "terrainTemps");
            if (buffer != null) {
                var ms = new MemoryStream(buffer);
                terrainTemperatures = ms.ReadFloatArray();
            } else {
                log.Error("terrainTemps array missing. All temperature data will be reset.");
                return;
            }

            // it's fine if it's missing, means no water on map like.
            DataExposeUtility.LookByteArray(ref buffer, "terrainFlops");
            if (buffer != null) {
                var ms = new MemoryStream(buffer);
                foreach (var kv in ms.ReadMapInt2Str()) {
                    var tdef = TerrainDef.Named(kv.Value);
                    if (tdef != null) {
                        terrainFlops.Add(kv.Key, tdef);
                    } else {
                        log.Error($"Phase transition target '{kv.Value}' has no TerrainDef, dropping.");
                        log.Error("Most likely a mod with that terrain changed or vanished.");
                    }
                }
            }

            // TODO: check flop consistency. terrainFlops must only have records for topterrain that
            // has thermalprops but does not have turnsInto attribute. ideally -- ICE9 only. warn
            // if that is not the case - that means some sloppy modding.

            log.Info($"done. (from mod {modVersion} format r{dataVersion})");
            mapNeedsInitialization = false;

        }

        void LoadOldStyleData(string modVersion) {
            var log = new LogContext("LoadOldStyleData");
            string str = null;

            if (modVersion != null && modVersion != "2.6.0") {
                log.Error($"data from mod version '{modVersion}' not supported, dropping.");
                return;
            }

            Scribe_Values.Look(ref str, "temperatures");
            if (str == null) {
                log.Error("missing temperatures array. All temperature data will be reset.");
                return;
            } else {
                temperatures = DataUtility.StringToArray(str);
            }

            Scribe_Values.Look(ref str, "terrainTemperatures");
            if (str != null) {
                terrainTemperatures = DataUtility.StringToArray(str);
            } else {
                terrainTemperatures = new float[temperatures.Length];
            }
            if (modVersion == null) {
                if (temperatures != null) {
                    log.Info($" : is from pre-2.0 Celsius. Converting.");
                    temperatures = DataUtility.Transpose(temperatures, map.Size.x);
                    if (terrainTemperatures != null)
                        terrainTemperatures = DataUtility.Transpose(terrainTemperatures, map.Size.x);
                    iceConversionRequested = true;
                } else {
                    log.Info($" : looks like adding Celsius to an existing save.");
                    // not setting mapNeedsInitialization = false;
                    // actually i don't think we'll get called on a save w/o celsius section
                }
            } else {
                log.Info($"from Celsius 2.6 Requesting ice conversion.");
                iceConversionRequested = true;
            }
            mapNeedsInitialization = false;
        }
#endregion LoadSave
        public override void MapRemoved() {
            LogContext log = new LogContext("MapRemoved");
            // disable event handling
            initialized = false;
            // tell threads to exit:
            this.threadsExitRequested = true;
            this.modelHandle.Set();
            this.collectorHandle.Set();
            this.modelThread.Join();
            this.collectorThread.Join();
            TemperatureUtility.temperatureInfos.Remove(map);
            log.Info($"Map {map} removed. TemperatureInfo cache now contains {TemperatureUtility.temperatureInfos.Count} records.");
        }
        public void ThermalPropertiesInvalidated() {
            RepopulateCTProps();
        }
        public void ColorSettingsChanged() {
            overlayDrawer.SetDirty();
        }
        Color TemperatureColorForCell(int index, float vacuum = 1.0f) {
            if (Settings.UseVanillaTemperatureColors)
                return map.mapTemperature.GetCellExtraColor(index);

            float temperature = GetTemperatureForCell(index);

            // compressing green range and clamping the rest.
            //
            //  by default 21C is the optimum temp
            //  15C range centered on it is "comfortable" range.
            // "interesting" range is from about -15 to +55.
            //  the rest might as well be drawn single color.
            //
            //  HSV has Hue green hue at about 90 to 150 degrees.
            // red at 0 and blue at 240.
            //
            // temperature settings:
            // MinMapped, MinComfort, MaxComfort, MaxMapped.
            //    default  -20, +15, +25, +60
            //
            // those are mapped to  240, 150, 90 and 0 Hue resp.
            //
            // S and V are used for vacuum. transfer function
            // is Pn^5. This puts pressures <0.7 (3km alt) at below
            // 0.2 S*V.
            // Pn is pressure normalized to 0-1 atm which is what
            // the game seems to use.

            // Range into which comfort zone is mapped
            const float H_MinMapped  = 240.0f;
            const float H_MinComfort = 150.0f;
            const float H_MaxComfort =  90.0f;
            const float H_MaxMapped  =   0.0f;

            float Tn, H;

            if      (temperature < Settings.T_MinMapped) { H = H_MinMapped; }
              else if (temperature < Settings.T_MinComfort) {
                Tn = (temperature - Settings.T_MinMapped) / (Settings.T_MinComfort - Settings.T_MinMapped);
                H  = Mathf.Lerp(H_MinMapped, H_MinComfort, Tn);
            } else if (temperature < Settings.T_MaxComfort) {
                Tn = (temperature - Settings.T_MinComfort) / (Settings.T_MaxComfort - Settings.T_MinComfort);
                H  = Mathf.Lerp(H_MinComfort, H_MaxComfort, Tn);
            } else if (temperature < Settings.T_MaxMapped) {
                Tn = (temperature - Settings.T_MaxComfort) / (Settings.T_MaxMapped - Settings.T_MaxComfort);
                H  = Mathf.Lerp(H_MaxComfort, H_MaxMapped, Tn);
            } else { H = H_MaxMapped; }

            float S = Settings.T_Saturation;
            float V = Mathf.Pow(vacuum, Settings.PressureExponent);

            // HSV -> RGB
            float C = V * S;
            float X = C * (1.0f - Mathf.Abs((H / 60.0f) % 2.0f - 1.0f));
            float m = V - C;

            if (H < 60.0f)       { return new Color(C + m, X + m,  0.0f); }
            else if (H < 120.0f) { return new Color(X + m, C + m,  0.0f); }
            else if (H < 180.0f) { return new Color( 0.0f, C + m, X + m); }
            else if (H < 240.0f) { return new Color( 0.0f, X + m, C + m); }
            else if (H < 300.0f) { return new Color(X + m,  0.0f, C + m); }
            else                 { return new Color(C + m,  0.0f, X + m); }
        }

        public override void MapComponentUpdate()
        {
            if (Find.PlaySettings.showTemperatureOverlay && Find.CurrentMap == map) {
                overlayDrawer.MarkForDraw();
            }
            overlayDrawer.CellBoolDrawerUpdate();
        }

        public override void MapComponentOnGUI()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == DefOf.Celsius_SwitchTemperatureMap.MainKey && Find.MainTabsRoot.OpenTab == null)
            {
                Find.PlaySettings.showTemperatureOverlay = !Find.PlaySettings.showTemperatureOverlay;
                Event.current.Use();
            }
            if (!Find.PlaySettings.showTemperatureOverlay || !Settings.ShowTemperatureTooltip)
                return;
            int index = map.cellIndices.CellToIndex(UI.MouseCell());
            if (InBounds(index) && (!map.fogGrid.IsFogged(index) || Prefs.DevMode))
            {
                GameFont font = Text.Font;
                Text.Font = GameFont.Tiny;
                string tooltip = "Celsius_MapTempOverlay_Cell".Translate(GetTemperatureForCell(index).ToStringTemperature(Settings.TemperatureDisplayFormatString));
                if (Settings.FreezingAndMeltingEnabled)
                {
                    float terrainTemperature = terrainTemperatures[index];
                    if (!float.IsNaN(terrainTemperature))
                        tooltip += "\n" + "Celsius_MapTempOverlay_Terrain".Translate(terrainTemperature.ToStringTemperature(Settings.TemperatureDisplayFormatString));
                }

                if (Settings.DebugMode)
                    Widgets.Label(new Rect(UI.MousePositionOnUIInverted.x + 20, UI.MousePositionOnUIInverted.y + 20, 150, 100), $"{tooltip}\n{GetThermalPropertiesAt(index)}");
                else Widgets.Label(new Rect(UI.MousePositionOnUIInverted.x + 20, UI.MousePositionOnUIInverted.y + 20, 100, 40), tooltip);

                Text.Font = font;
            }
        }

        void ModelThreadMethod() {
            while (true) {
                this.modelHandle.WaitOne();
                if (this.threadsExitRequested) {
                    break;
                }
                SwapCTProps(); // get freshest info
                ApplyCannedHeat();
                UpdateTemperatures();
                Gen.Swap(ref temperatures, ref worksetTemperatures);

                this.modelHandle.Reset();
                this.modelDone = true;
            }
        }
        void CollectorThreadMethod() {
            while (true) {
                this.collectorHandle.WaitOne();
                if (this.threadsExitRequested) {
                    break;
                }
                CollectTerrainEffects();

                this.collectorHandle.Reset();
                this.collectorDone = true;
            }
        }
        // Runs the diffusion model only. via a threadpool.
        void UpdateTemperatures() {
            var stopWatch = Stopwatch.StartNew();
            int runStartTick = GenTicks.TicksGame;
            int ticksSinceLastStart = runStartTick - this.modelStartTick;
            this.modelTickEma.update(ticksSinceLastStart);
            this.modelStartTick = runStartTick;

            // Adjust environment/outdoors heat transfer since the model was
            // originally calibrated for 120 ticks per step.
            const float oldUpdateInterval = 120.0f;
            float modelStepFactor = (float)ticksSinceLastStart / oldUpdateInterval;

            float outdoorTemperature = map.mapTemperature.OutdoorTemp;

            // Main loop
            var partitioner = Partitioner.Create(0, temperatures.Length);
            Parallel.ForEach(partitioner, range => {
                for (int i = range.Item1; i < range.Item2; i++) {
                    IntVec3 cell = map.cellIndices.IndexToCell(i);
                    float temperature = temperatures[i];
                    ThermalProps cellProps = GetThermalPropertiesAt(i);

                    float heatFlow = cellProps.HeatFlow; // How quickly the system changes its temperature (capacity * conductivity)
                    float energy = temperature * heatFlow; // How much energy is added to the cell (temperature * capacity * conductivity)

                    // ignore Ody tempGrid : let it freeze/melt thinIce and remove floodWater by itself.
                    TerrainDef terrain = map.terrainGrid.TerrainAtIgnoreTemp(i);
                    ThermalProps terrainProps = null;
                    if (TemperatureUtility.thermalsCache.TryGetThermals(terrain, out terrainProps)) {
                        float terrainTemperature = terrainTemperatures[i];

                        TemperatureUtility.CalculateHeatTransferTerrain(terrainTemperature, terrainProps, modelStepFactor, ref energy, ref heatFlow);

                        float terrainTempChange = (temperature - terrainTemperature) * cellProps.HeatFlow / heatFlow;
                        terrainTemperature += terrainTempChange * terrainProps.conductivity;

                        // Make sure that terrains that push or pull heat are at least as hot or cold as the air in the cell
                        // (maybe will add a more accurate calculation later)

                        // in vanilla - only two terrains in Ody. maybe there are more in mods. but not in my set.
                        // no idea what "more accurate" can mean here.

                        if (terrain.heatPerTick > 0)
                        {
                            if (terrainTemperature < temperature)
                                terrainTemperature = temperature;
                        }
                        else if (terrain.heatPerTick < 0)
                        {
                            if (terrainTemperature > temperature)
                                terrainTemperature = temperature;
                        }
                        terrainTemperatures[i] = terrainTemperature;
                    }
                    // Diffusion & convection
                    void ProcessNeighbour(int neighbourIndex) =>
                        TemperatureUtility.CalculateHeatTransferCells(temperatures[neighbourIndex],
                            GetThermalPropertiesAt(neighbourIndex), cellProps.airflow, modelStepFactor,
                                ref energy, ref heatFlow, false);

                    if (cell.z > 0)
                        ProcessNeighbour(i - map.Size.x);
                    if (cell.z < map.Size.z - 1)
                        ProcessNeighbour(i + map.Size.x);
                    if (cell.x > 0)
                        ProcessNeighbour(i - 1);
                    if (cell.x < map.Size.x - 1)
                        ProcessNeighbour(i + 1);

                    // Thermal exchange with the environment
                    RoofDef roof = map.roofGrid.RoofAt(i);
                    TemperatureUtility.CalculateHeatTransferEnvironment(outdoorTemperature, cellProps, roof, modelStepFactor,
                                                                        ref energy, ref heatFlow);

                    // Applying heat transfer
                    float equilibriumTemp = energy / heatFlow;
                    temperature += (equilibriumTemp - temperature) * cellProps.conductivity;

                    // Terrain heat push (only when SteadyEnvironmentEffects.DoCellSteadyEffects hasn't done it yet)
                    // in vanilla there's only GenTemperature.PushHeat(c, this.map, terrain.heatPerTick * 1666.6666f)
                    // for terrain.heatPerTick > 0f && room !=  null && !(room != null && room.UsesOutdoorTemperature)
                    // so basically terrain heat push turns off for non-rooms, or rooms with !UsesOutdoorTemperature

                    // the below code mimics that for some reason and thus only has any effect for terrain.heatPerTick < 0
                    // of which I haven't seen any examples.

                    bool shouldPushHeat = terrain.heatPerTick > 0;

                    if (shouldPushHeat) {
                        Room room = cell.GetRoom(map);
                        if (room != null && !room.UsesOutdoorTemperature)
                            shouldPushHeat = false;
                    } else {
                        shouldPushHeat = terrain.heatPerTick < 0;
                    }

                    if (shouldPushHeat) {
                        temperature += terrain.heatPerTick * ticksSinceLastStart * Settings.HeatPushEffect / cellProps.heatCapacity;
                    }

                    worksetTemperatures[i] = temperature;
                }
            });

            stopWatch.Stop();
            this.modelMsecEma.update(stopWatch.ElapsedMilliseconds);
            this.modelDone = true;
        }
        // Based on vanilla formula: 0.0058 * T * [T / 10] for 0.06% of cells every tick
        static float SnowChangeAmountAt(float temperature) => -0.00058f * 0.0006f * temperature * Mathf.Clamp(temperature, 0, 10);
        // Converts temperature field into stuff happening. Also threadpooled.
        void CollectTerrainEffects() {
            var stopWatch = Stopwatch.StartNew();
            int runStartTick = GenTicks.TicksGame;
            int ticksSinceLastStart = runStartTick - this.collectorStartTick;
            this.collectorTickEma.update(ticksSinceLastStart);
            this.collectorStartTick = runStartTick;

            // Odysseus FreezeManager uses c.GetTemperature(this.map); for melting/freezing ThinIce on tempGrid
            // while we deal with ice on topGrid. hah. okay. they'll freeze/thaw in parallel for now

            // snow melt rate under rain:
            // RainRate gets lerped between 0 and 1 effectively every tick
            // We can and should update snowmeltamount here.
            // Default snowMeltRainFactor of 2.0 thus doubles snowmelt for max rain.
            float snowMeltRainFactor = map.weatherManager.RainRate * (Settings.SnowMeltRainFactor / 2.0f) + 1.0f;

            // Main loop
            var partitioner = Partitioner.Create(0, temperatures.Length);
            var parallelLoopResult = Parallel.ForEach(partitioner, range => {
                for (int i = range.Item1; i < range.Item2; i++) {
                    TerrainDef terrain = map.terrainGrid.TerrainAtIgnoreTemp(i);
                    float terrainTemperature = terrainTemperatures[i];
                    if (Settings.FreezingAndMeltingEnabled && !float.IsNaN(terrainTemperature)) {
                        if (TemperatureUtility.thermalsCache.TryGetThermals(terrain, out ThermalProps terrainProps)) {
                            if (terrainProps.MeltsAt(terrainTemperature)) {
                                terrainsChanged.Add(new TerrainChange() { at = i, dir = PTDir.Melt } );
                            } else {
                                if (terrainProps.FreezesAt(terrainTemperature)) {
                                    terrainsChanged.Add(new TerrainChange() { at = i, dir = PTDir.Freeze } );
                                }
                            }
                        }
                    }

                    IntVec3 cell = map.cellIndices.IndexToCell(i);
                    float temperature = temperatures[i];

                    // Snow melting

                    if (temperature > 0 && cell.GetSnowDepth(map) > 0) {
                        RoofDef roof = map.roofGrid.RoofAt(i);
                        float amount = SnowChangeAmountAt(temperature) * ticksSinceLastStart;
                        amount *= (roof != null ? 1.0f : snowMeltRainFactor);
                        snowsMelted.Add(new SnowMelt() { at = i, amount = amount } );
                    }

                    // Autoignition

                    // the below depends on being last in the loop.
                    if (!Settings.AutoignitionEnabled || temperature < MinIgnitionTemperature) {
                        continue;
                    }

                    // Most checks are in TryStartFireIn(); before that we also do the vacuum check.
                    // Here we just look if something in the cell has reached autoignition temperature.
                    //
                    // Now if there are several flammable things.. we just submit the sum of their
                    // flammabilities. At fire start time it's multiplied by effective vacuum
                    // and some constant. Or maybe w/o constants since typical fireSizes are
                    // 0.5-0.8 for fire arrow and typical flammabilities are 0.2-1.2 or so.
                    //
                    // and do nothing if there's already a fire.
                    //
                    // Pawn autoignition would be costly too. But very fun.

                    bool nothingToDo = false;
                    float fireSize = 0f; // sum of flammabilities of stuff that reached autoignition.
                    List<Thing> things = map.thingGrid.ThingsListAtFast(i);
                    for (int k = 0; k < things.Count; k++) {
                        if (things[k].FireBulwark) {
                            nothingToDo = true;
                            break;
                        }
                        if (things[k] is Fire fire) {
                            nothingToDo = true;
                            break;
                        }
                        if (things[k] is Pawn pawn) {
                            continue; // for now. Still don't go firefighting with a stack of prometheum.
                        }
                        // this is costly. but should be hit relatively rare. don't think it warrants another cache
                        float ignitionTemp = things[k].GetStatValue(DefOf.Celsius_IgnitionTemperature);
                        if (ignitionTemp < MinIgnitionTemperature) { // meaning "unset" / None
                            continue;
                        }
                        if (temperature > ignitionTemp) {
                            fireSize += things[k].GetStatValue(StatDefOf.Flammability);
                        }
                    }
                    if (!nothingToDo) {
                        // Treat flammable terrain as just another fuel source
                        // FIXME looks a bit wrong since we don't have ignitionTemp for terrains for some reason.
                        float terrainFlammability = 0.0f;
                        if (TemperatureUtility.thermalsCache.TryGetTerrainFlammability(terrain, out terrainFlammability)) {
                            fireSize += terrainFlammability;
                        }
                        if (fireSize > Fire.MinFireSize) {
                            firesStarted.Add(new FireStart() { at = i, size = fireSize } );
                        }  // else  existingFire.fireSize += fireSize; this pyromany commented out
                        // mainly for performance reasons since FireUtility.GetEffectiveVacuumForFire()
                        // is costly. But also because temperature in a fire is usually >> ignitionTemp
                        // and this quickly turns any fire into a deflagration.
                    }
                }
            });
            stopWatch.Stop();
            this.collectorMsecEma.update(stopWatch.ElapsedMilliseconds);
            this.collectorDone = true;
        }
        // Applies queued stuff to the game state
        void ApplyTerrainEffects() {
            var log = new LogContext("ApplyTerrainEffects");
            FireStart fs;
            while (firesStarted.TryTake(out fs)) {
                IntVec3 cell = map.cellIndices.IndexToCell(fs.at);
                float effVac = FireUtility.GetEffectiveVacuumForFire(cell, map); // woo-hoo Ody compat for autoignition!!
                float fireSize = fs.size * (1f - effVac); // and maybe scale more or maybe not.
                log.Info($"Autoignition at [{cell.x}, {cell.z}] temp={temperatures[fs.at]:F1}C fs.size={fs.size:F2} effVac={effVac:F2}");
                if (fireSize  > Fire.MinFireSize) {
                    FireUtility.TryStartFireIn(cell, map, fireSize, null);
                }
            }

            SnowMelt sm;
            while (snowsMelted.TryTake(out sm)) {
                IntVec3 cell = map.cellIndices.IndexToCell(sm.at);
                map.snowGrid.AddDepth(cell, sm.amount);
            }

            TerrainChange tc;
            while (terrainsChanged.TryTake(out tc)) {
                IntVec3 cell = map.cellIndices.IndexToCell(tc.at);
                TerrainDef terrain = cell.GetTerrain(map);
                log = new LogContext($"ApplyTerrainEffects[{cell.x}, {cell.x}] terrain={terrain}");
                TerrainDef nextTerrain = null;
                if (TemperatureUtility.thermalsCache.TryGetThermals(terrain, out ThermalProps terrainProps)) {
                    nextTerrain = terrainProps.turnsInto;
                }  else {
                    // Should be impossible since collector can't add
                    // non-thermalized terrains to the bag b/c they do lack transitionTemperature.
                    log.Warn($"non-thermalized terrain somehow ended up here, skipping.");
                    continue;
                }

                if (terrainFlops.TryGetValue(tc.at, out TerrainDef flopDef)) {
                    if (nextTerrain != null) {
                        // most probably stuck there due to modlist changes
                        log.Warn($"both turnsInto: {nextTerrain} and a record in flops: {flopDef}.");
                        terrainFlops.Remove(tc.at);
                    } else { // aha, here we go.
                        nextTerrain = flopDef;
                    }
                }

                if (nextTerrain == null) {
                    if (tc.dir == PTDir.Freeze) {
                        nextTerrain	= CelsiusIceNine;
                    } else { // no turnsInto, no flop. melt into... nothing?
                        // this is hard mod error. better make it very obvious.
                        // TODO: put up a scary message.
                        // (remember, vanilla ice does not melt any more, this should be unreachable)
                        log.Error($"melt transition target neither defined nor found.");
                        nextTerrain = CelsiusSlade;
                    }
                }

                // those below are all hard mod errors. better make them very obvious.
                // TODO: put up a scary message.
                if (nextTerrain.temporary) {  // ody flood?
                    log.Error($"transition to {nextTerrain} invalid: tdef.temporary set");
                    nextTerrain = CelsiusSlade;
                }
                if (nextTerrain.isFoundation) { // bridge ??
                    log.Error($"transition to {nextTerrain} invalid: tdef.isFoundation set");
                    nextTerrain = CelsiusSlade;
                }
                if (nextTerrain.layerable) { // constructed floor ??
                    log.Error($"transition to {nextTerrain} invalid: tdef.layerable set");
                    nextTerrain = CelsiusSlade;
                }
                // at this point terrainGrid.SetTerrain() should set topGrid only.
                //
                // It is possible to define a freezable terrain which accepts floors (that is, moves to underGrid
                // It won't freeze atm - freezer looks only at topGrid . won't unfreeze too.
                //
                // In Ody, thinIce is tempGrid and replaceableByBridge but affordance forced to {walkable, bridgeable, shallowwater}
                // So, no floors.
                //
                // replaceableByBridge works because all deep water and WaterOceanShallow is set to canFreeze=false.
                //  (although the latter has enough affordances for bridges). And walking on melt works because of that too.
                //
                // What do we do with IceNine?
                //
                // At the moment - nothing. Just walkable. And don't freeze the ocean just like in Ody.
                //
                if (tc.dir == PTDir.Freeze) {
                    // insert fun effects of pawns freezing into snotonite.
                } else {
                    // fun effects of pawns drowning in molten snot.
                    // IceNine is limited to walkable+shallow, so nothing drowns.
                    // TODO: make it messages/notifies, not just Log.

                    List<Thing> things = map.thingGrid.ThingsListAtFast(tc.at);

                    for (int i = things.Count - 1; i >= 0; i--) {
                        Thing thing = things[i];
                        if (!thing.def.destroyable)
                            continue;

                        bool nextImpassable = nextTerrain.passability == Traversability.Impassable;

                        // Drowning
                        if (thing is Pawn pawn && nextImpassable) {
                            log.Info($"{pawn.LabelShortCap} drowns in {nextTerrain}.");
                            pawn.health?.AddHediff(DefOf.Celsius_Hediff_Drown, dinfo: new DamageInfo(DefOf.Celsius_Damage_Drown, 1));
                            pawn.Corpse?.Destroy();
                            continue;
                        }

                        // Sinking
                        if (nextImpassable) {
                            log.Info($"{thing} sinks in {nextTerrain}.");
                            thing.Destroy();
                            continue;
                        }

                        // Dissolution
                        CompDissolution compDissolution = thing.TryGetComp<CompDissolution>();
                        if (compDissolution != null) {
                            log.Info($"{thing} dissolves in {nextTerrain}.");
                            compDissolution.TriggerDissolutionEvent(thing.stackCount);
                            continue;
                        }

                        // Collapse
                        TerrainAffordanceDef terrainAffordance = thing.TerrainAffordanceNeeded;
                        if (terrainAffordance != null && !nextTerrain.affordances.Contains(terrainAffordance)) {
                            if (thing is Building_Grave grave && grave.HasAnyContents) {
                                log.Info($"Grave with {grave.ContainedThing?.LabelShort} is uncovered due to melting.");
                                grave.EjectContents(); // I wonder how to reapply the Sinking and Dissolution to teh contents. TODO
                            } else {
                                log.Info($"{thing} collapses as {terrain} melts into {nextTerrain} (needs {terrainAffordance}).");
                            }
                            thing.Destroy();
                            continue;
                        }
                        // We're out of fun to impose on this thing, let it live.
                    }
                }
                //
                // snow and sand removal is handled in DoTerrainChangedEffects() called from SetTerrain()
                //
                // it also notifies PathFinderMapData via events.. so ..
                //
                // original code did
                //
                //     if (map.terrainGrid.UnderTerrainAt(index) == null)
                //          map.terrainGrid.SetUnderTerrain(cell, meltedTerrain);
                //     map.terrainGrid.RemoveTopLayer(cell, false);
                //
                //  which does RemoveTempTerrain(); RemoveFoundation(); among other things.
                //   not quite pretty.
                //
                //  we just do
                //     map.terrainGrid.SetTerrain(cell, nextTerrain);
                //
                //  which amounts to mostly same things.
                //
                map.terrainGrid.SetTerrain(cell, nextTerrain);
            }
        }
        // called from model thread just before model run to do what's written on the can
        void ApplyCannedHeat() {
            Gen.Swap(ref readCannedHeat, ref writeCannedHeat);
            foreach (CannedHeat ch in readCannedHeat) {
                temperatures[ch.at] +=  ch.energy * Settings.HeatPushEffect / GetThermalPropertiesAt(ch.at).heatCapacity;
            }
            readCannedHeat.Clear();
        }

        int modelRuns = 0;
        int collectRuns = 0;
        public override void MapComponentTick() {
            var log = new LogContext("MapComponentTick");
            var stopWatch = Stopwatch.StartNew();
            int thisTick = GenTicks.TicksGame;
            int ticksSinceLastModelStart = thisTick - this.modelStartTick;
            int ticksSinceLastCollectorStart = thisTick - this.collectorStartTick;

            if (thisTick % 180 == 0) {
                var str = $"tick {thisTick}: runs: {modelRuns}/{collectRuns} {modelMsecEma.ema:F2} / {collectorMsecEma.ema:F2} ms ";
                str += $"periods: {modelTickEma.ema:F2} / {collectorTickEma.ema:F2} ticks ";
                str += $"mct {mapComponentTickMsecEma.ema:F2} ms";
                log.Info(str);
            }
            if (thisTick % 1800 == 0) {
                TemperatureUtility.thermalsCache.LogStats();
            }

            // time to run it again
            if (!this.modelDone && ticksSinceLastModelStart > Settings.TicksPerUpdate) {
                this.modelHandle.Set();
            }
            // TODO: think of something better than just magic 4
            if (!this.collectorDone && ticksSinceLastCollectorStart > Settings.TicksPerUpdate * 4) {
                this.collectorHandle.Set();
            }

            if (this.modelDone) {
                modelRuns += 1;
                this.modelDone = false;
                // On every update clear the room temperatures and set the temperature overlay to be dirty
                roomTemperatures.Clear();
                overlayDrawer.SetDirty();
            }

            if (this.collectorDone) {
                collectRuns += 1;
                this.collectorDone = false;
                // drain our beloved concurrent bags.
                ApplyTerrainEffects();
            }
            stopWatch.Stop();
            this.mapComponentTickMsecEma.update(stopWatch.ElapsedMilliseconds);
        }

        public bool InBounds(int index) => index >= 0 && index < temperatures.Length;

        public float GetTemperatureForCell(int index) => temperatures != null ? temperatures[index] : TemperatureTuning.DefaultTemperature;

        public float GetTemperatureForCell(IntVec3 cell) => GetTemperatureForCell(map.cellIndices.CellToIndex(cell));

        public float GetRoomAverageTemperature(Room room) {
            if (room.ID == -1 || roomTemperatures == null) {
                Log.Error($"Could not get temperature for room {room?.ToString() ?? "null"}.");
                return map.mapTemperature.OutdoorTemp;
            }
            if (roomTemperatures.TryGetValue(room.ID, out float temperature)) {
                return temperature;
            }
            return roomTemperatures[room.ID] = room.Cells.Average(cell => GetTemperatureForCell(cell));
        }

        public float GetTerrainTemperature(int index) => terrainTemperatures[index];

        public ThermalProps GetThermalPropertiesAt(int index) {
            ThermalProps props = readCTProps[index];
            return props == null ? ThermalProps.Air : props;
        }

        public void PushHeat(int index, float energy) => writeCannedHeat.Add(new CannedHeat() { at = index, energy = energy });
        public void PushHeat(IntVec3 cell, float energy) => PushHeat(map.cellIndices.CellToIndex(cell), energy);
    }
}
