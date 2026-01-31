using System;
using RimWorld;
using Verse;
using UnityEngine;
using HarmonyLib;


/// Find.WindowStack.Add(ms);

namespace Celsius {

    public class TweakWindow :  RimWorld.MainTabWindow
    {

        Dialog_ModSettings dms;
        public TweakWindow() {
            //forcePause = true;
            dms = new Dialog_ModSettings(CelsiusMod.getModHandle());
        }

        public override void DoWindowContents(Rect inRect) {
            dms.DoWindowContents(inRect);
        }

        public override void PreOpen() {
            base.PreOpen();
            //switch to the map view if the world view is active
            //Find.World.renderer.wantedMode = RimWorld.Planet.WorldRenderMode.None;

            windowRect.width  = 512;
            windowRect.height = UI.screenHeight - 35;
            windowRect.y = (float)(UI.screenHeight - 35) - windowRect.height;
        }
    }

    [HarmonyPatch(typeof(MainButtonsRoot))]
    [HarmonyPatch("MainButtonsOnGUI")]
    static public class MainButtonsRoot_MainButtonsOnGUI
    {
        private static KeyBindingDef kb;

        public static bool Prepare() {
            kb = DefDatabase<KeyBindingDef>.GetNamedSilentFail("Celsius_Tweak");
            return kb != null;
        }
        static void Postfix() {
            if (kb.KeyDownEvent) {
                MainButtonDef mb = DefDatabase<MainButtonDef>.GetNamedSilentFail("CelsiusTweak");
                if ( mb != null ) {
                    Find.MainTabsRoot.SetCurrentTab(mb, true);
                }
            }
        }
    }
}


