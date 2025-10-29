using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;

namespace ShuttleboxPlugin.Patches
{
    [HarmonyPatch]
    internal class ElevatorPatch
    {
        public static event Action OnStopElevator;
        [HarmonyPatch(typeof(GS_StopElevatorRide), nameof(GS_StopElevatorRide.Enter))]
        [HarmonyPostfix]
        public static void OnStopElevatorAPI()
        {
            OnStopElevator?.Invoke();
        }

    }
}
