using UnityEngine;
using Verse;
using RimWorld;

namespace Celsius
{
    public class CelsiusMod : Mod
    {
        private static CelsiusMod _instance = null;

        public CelsiusMod(ModContentPack content) : base(content) {
            if (_instance != null) {
                LC.Error("CelsiusMod", "creating second instance of CelsiusMod");
            }
            _instance = this;
            GetSettings<Settings>();
        }

		public static CelsiusMod getModHandle() { return _instance; }

        public override void DoSettingsWindowContents(Rect rect) {
            SettingsWindow.DoWindowContents(rect, mainTabMode: false);
        }

        public override string SettingsCategory() => "Celsius";

        public override void WriteSettings() {
            LC.Spam("CelsiusMod.SettingsChanged", "WriteSettings");
            base.WriteSettings();
            Settings.Dump();
        }
    }
}
