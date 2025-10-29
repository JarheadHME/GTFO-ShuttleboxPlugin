using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gear;
using HarmonyLib;
using ShuttleboxPlugin.Modules;
using UnityEngine;

namespace ShuttleboxPlugin.Patches
{
    [HarmonyPatch]
    internal class CarryItemPatch
    {

        // Save the actual type of the object, as we temporarily override it to check for the shuttlebox type. 
        private static eCarryItemInsertTargetType m_prevType;

        [HarmonyPatch(typeof(CarryItemEquippableFirstPerson), nameof(CarryItemEquippableFirstPerson.OnWield))]
        [HarmonyPostfix]
        public static void OnWield(CarryItemEquippableFirstPerson __instance)
        {
            m_prevType = __instance.m_insertCheckType;
        }

        [HarmonyPatch(typeof(CarryItemEquippableFirstPerson), nameof(CarryItemEquippableFirstPerson.Update))]
        [HarmonyPostfix]
        public static void AllowInteractWithShuttlebox(CarryItemEquippableFirstPerson __instance)
        {
            // There will be a frame of delay between actually looking at the prompt and being able to use it, but it's a single frame, who's gonna care
            
            // The game skips checking if there are interact prompts if the item isn't supposed to have them
            if (__instance.m_insertCheckType == eCarryItemInsertTargetType.None) PerformRaycast(__instance);

            if (__instance.m_rayHit.collider == null) 
            { __instance.m_insertCheckType = m_prevType; return; }
            iCarryItemInteractionTarget componentInParent = __instance.m_rayHit.collider.GetComponentInParent<iCarryItemInteractionTarget>();

            if (componentInParent == null || componentInParent.InsertType != InsertTypeEnum.value) 
            { __instance.m_insertCheckType = m_prevType; return; }
                
            __instance.m_insertCheckType = InsertTypeEnum.value;
        }

        public static void PerformRaycast(CarryItemEquippableFirstPerson __instance)
        {
            // the last update when dropping a turbine has a null owner
            if (__instance.Owner == null) return;

            Physics.Raycast(__instance.Owner.FPSCamera.Position, __instance.Owner.FPSCamera.Forward, out var rayhit, 2.4f, LayerManager.MASK_APPLY_CARRY_ITEM);
            __instance.m_rayHit = rayhit;
        }
    }
}
