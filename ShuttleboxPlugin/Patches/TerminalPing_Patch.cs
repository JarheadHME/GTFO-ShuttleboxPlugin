using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using LevelGeneration;
using ShuttleboxPlugin.Modules;

namespace ShuttleboxPlugin.Patches
{
    [HarmonyPatch]
    internal class TerminalPing_Patch
    {
        [HarmonyPatch(typeof(LG_TERM_Ping), nameof(LG_TERM_Ping.Setup))]
        [HarmonyPrefix]
        public static void CancelIsInSameZone(iTerminalItem target, string itemKey, string currentZoneName, bool repeatedPing, ref bool inSameZone)
        {
            if (Shuttlebox_Core.PingOverrideItemKeys.Contains(itemKey))
            {
                inSameZone = false;
            }
        }
    }
}
