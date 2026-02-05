using UnityEngine;
using Verse;
//using RimWorld;

namespace Celsius {

    static class TemperatureString {
        static string IndoorsOutdoorsText(Map map, IntVec3 mCell) {
            var room = mCell.GetRoom(map);

            if (room == null) {
                bool roomPO = false;
                for (int i = 0; i < 9; i++) {
                    IntVec3 nCell = mCell + GenAdj.AdjacentCellsAndInside[i];
                    if (nCell.InBounds(map)) {
                        Room nRoom = nCell.GetRoom(map);
                        if (nRoom == null) { continue; }
                        bool nRoomPO = nRoom.PsychologicallyOutdoors;

                        if ((!nRoomPO && !nRoom.UsesOutdoorTemperature)
                            || (!nRoomPO && (room == null || roomPO))
                            || ( nRoomPO &&  room == null))
                        {
                            room = nRoom;
                            roomPO = nRoomPO;
                            // This is vanilla logic. Though a break here might save some cycles.
                        }
                    }
                }
            }
            if (room == null) {
                Building edifice = mCell.GetEdifice(map);
                if (edifice != null) {
                    foreach (IntVec3 eCell in edifice.OccupiedRect().ExpandedBy(1).ClipInsideMap(map)) {
                        room = eCell.GetRoom(map);
                        if (room != null && !room.PsychologicallyOutdoors) {
                            break;
                        }
                    }
                }
            }
            string text;
            if (room != null && !room.PsychologicallyOutdoors) {
                if (room.OpenRoofCount == 0) {
                    text = "Indoors".Translate();
                } else {
                    text = "IndoorsUnroofed".Translate() + " (" + room.OpenRoofCount.ToStringCached() + ")";
                }
            } else {
                text = "Outdoors".Translate().CapitalizeFirst();
            }
            return text;
        }

    // Since IndoorOutdoorState is quite expensive to get, it's cached
    // longer than strings themselves.

    // OTOH temperature might change every model run, so its TTL is of
    // model run period from settings.

        static string cachedPrefix  = "";
        static int    cachedPfxTick = -1;
        static string cachedString  = "";
        static int    cachedStrTick = -1;
        static int    cachedAtIndex = -1;
        const int   cachedPrefixTTL = 60;

        public static string Get(IntVec3 mCell, TemperatureInfo tinfo) {
            Map map = Find.CurrentMap;

            if (mCell.Fogged(map) && !Settings.DebugMode) { return ""; }

            int index = map.cellIndices.CellToIndex(mCell);
            int tick = GenTicks.TicksGame;
            int ttl = Settings.TicksPerUpdate;

            if (cachedStrTick + ttl > tick) {
                if ( index == cachedAtIndex ) {
                    return cachedString;
                }
            }

            ttl = cachedPrefixTTL;

            if (cachedPfxTick + ttl < tick || index != cachedAtIndex) {
                cachedPrefix = IndoorsOutdoorsText(map, mCell);
                cachedPfxTick = tick;
            }

            string text = cachedPrefix;

            if (tinfo.IsThereVacuum() && tinfo.ZeroPressureAt(mCell)) {
                    text = "Celsius_VacuumTemperature".Translate();
            } else {
                float Tc = tinfo.GetTemperatureForCell(index);
                text = cachedPrefix + ", " + Tc.ToStringTemperature(Settings.TemperatureDisplayFormatString);
            }

            if (!tinfo.IsThereVacuum()) {
                float Tt = tinfo.GetTerrainTemperature(index);
                if (!float.IsNaN(Tt)) {
                    text += " / " + "Celsius_Terrain".Translate() + ": "
                        + Tt.ToStringTemperature(Settings.TemperatureDisplayFormatString);
                } else {
                    float Tw = map.mapTemperature.OutdoorTemp;
                    text += " / " + "Celsius_Weather".Translate() + ": "
                        + Tw.ToStringTemperature(Settings.TemperatureDisplayFormatString);
                }
            }

            cachedAtIndex = index;
            cachedStrTick = tick;
            cachedString  = text;

            return text;
        }
    }

    static class TemperatureColorMap {
        public static Color Get(float temperature, float pressure) {
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
            float V = Mathf.Pow(pressure, Settings.PressureExponent);

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
    }

    public static class MapTooltip {
        public static void Draw(TemperatureInfo tinfo) {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == DefOf.Celsius_SwitchTemperatureMap.MainKey && Find.MainTabsRoot.OpenTab == null)
            {
                Find.PlaySettings.showTemperatureOverlay = !Find.PlaySettings.showTemperatureOverlay;
                Event.current.Use();
            }
            if (!Find.PlaySettings.showTemperatureOverlay || !Settings.ShowTemperatureTooltip)
                return;
            var cell = UI.MouseCell();
            int index = tinfo.map.cellIndices.CellToIndex(cell);
            if (cell.InBounds(tinfo.map) && (!tinfo.map.fogGrid.IsFogged(index) || Prefs.DevMode)) {
                GameFont font = Text.Font;
                Text.Font = GameFont.Tiny;

                string tooltip = "Celsius_MapTempOverlay_Cell".Translate(tinfo.GetTemperatureForCell(index)
                                            .ToStringTemperature(Settings.TemperatureDisplayFormatString));
                int tt_lines = 1;

                if (tinfo.IsThereVacuum()) {
                    tooltip += $"\nPressure: {tinfo.PressureAt(cell):F2}";
                    tt_lines += 1;
                    var room = cell.GetRoomOrAdjacent(tinfo.map);
                    if (room != null) {
                        tooltip += $"\nRoomVacuum: {room.Vacuum:F2}";
                        tt_lines += 1;
                    }
                }
                if (Settings.DebugMode) {
                    tooltip += $"\n" + tinfo.GetThermalPropertiesAt(index).ToString();
                    tooltip += "\n";
                    tt_lines += 6;
                }
                float terrainTemperature = tinfo.GetTerrainTemperature(index);
                if (!float.IsNaN(terrainTemperature)) {
                    TerrainDef terrain = tinfo.map.terrainGrid.TerrainAtIgnoreTemp(index);
                    tooltip += $"\n{terrain.defName}: {terrainTemperature.ToStringTemperature(Settings.TemperatureDisplayFormatString)}";
                    tt_lines += 1;
                    if (Settings.DebugMode) {
                        if (tinfo.TryGetFlopAt(index, out TerrainDef flopDef)) {
                            tooltip += $"\nflopTo: {flopDef.defName}";
                            tt_lines += 1;
                        }
                        if (TemperatureUtility.thermalsCache.TryGetThermals(terrain, out ThermalProps terrainProps)) {
                            tooltip += $"\n" + terrainProps.ToString();
                            tt_lines += 7;
                        }
                    }
                }
                const int TinyFontLabelHeight = 16; // carefully measured.
                const int TinyFontLabelFudge  =  8; // also.
                int tooltip_h = tt_lines * TinyFontLabelHeight + TinyFontLabelFudge;
                tooltip = tooltip.Replace("; ", "\n"); // props' ToStrings have conveniently placed semicolons
                Widgets.Label(new Rect(UI.MousePositionOnUIInverted.x + 20, UI.MousePositionOnUIInverted.y + 20, 256, tooltip_h), tooltip);
                Text.Font = font;
            }
        }
    }
}
