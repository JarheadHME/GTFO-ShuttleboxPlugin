using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using ShuttleboxPlugin.Modules;
using Gear;

namespace ShuttleboxPlugin.Patches;
[HarmonyPatch]
public class ItemPickupPatches
{
    // Generic Prefix
    public static void TryGetItemAndShuttlebox(out (ItemInLevel, Shuttlebox_Core) __state, ItemInLevel __instance, ePickupItemStatus status, PlayerAgent player)
    {
        __state = (null, null);
        if (!SNet.IsMaster) return;
        if (__instance == null) return;

        if (status == ePickupItemStatus.PickedUp)
        {
            var thisitem = __instance.Get_pItemData();
            foreach (var shuttlebox in Shuttlebox_Core.s_setupShuttleboxes)
            {
                if (shuttlebox.HasItem && shuttlebox.ItemInside.Get_pItemData().Equals(thisitem))
                {
                    // this shuttlebox has the item we're picking up, so check if there's another item in this player's slot.
                    if (player == null)
                    {
                        Logger.Error("Item from shuttlebox getting picked up but no playeragent?");
                        return;
                    }
                    var backpack = PlayerBackpackManager.GetBackpack(player.Owner);
                    if (backpack == null)
                    {
                        Logger.Error($"Player {player.Owner.NickName} doesn't have a backpack??????");
                        return;
                    }

                    if (backpack.TryGetBackpackItem(thisitem.slot, out var backpackitem))
                    {
                        ItemInLevel iteminlevel = null;
                        if (!Shuttlebox_Core.TryGetItemInLevelFromData(backpackitem.Instance.Get_pItemData(), out iteminlevel))
                        {
                            Logger.Error("No ItemInLevel from backpack item ._.");
                            return;
                        }
                        __state = (iteminlevel, shuttlebox);
                        Logger.Info($"Dropping {iteminlevel.PublicName} for {__instance.PublicName}");
                        return;
                    }
                }
            }

        }
    }

    // Generic Postfix
    public static void AttemptSwapIntoShuttlebox((ItemInLevel, Shuttlebox_Core) __state)
    {
        if (!SNet.IsMaster) return;

        var item = __state.Item1;
        var shuttlebox = __state.Item2;

        if (item == null || shuttlebox == null) return;

        shuttlebox.AttemptInteract(eShuttleboxInteractionType.SwapIn, item: item.GetItem());
    }

    [HarmonyPatch(typeof(ConsumablePickup_Core), nameof(ConsumablePickup_Core.OnSyncStateChange))]
    internal class ConsumablePatches
    {
        public static void Prefix(out (ItemInLevel, Shuttlebox_Core) __state, ConsumablePickup_Core __instance, ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall)
        {
            var iteminlevel = __instance.TryCast<ItemInLevel>();
            TryGetItemAndShuttlebox(out __state, iteminlevel, status, player);
        }

        public static void Postfix((ItemInLevel, Shuttlebox_Core) __state, ConsumablePickup_Core __instance, ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall)
        {
            AttemptSwapIntoShuttlebox(__state);
        }
    }

    [HarmonyPatch(typeof(ResourcePackPickup), nameof(ResourcePackPickup.OnSyncStateChange))]
    internal class ResourcePackPatches
    {
        public static void Prefix(out (ItemInLevel, Shuttlebox_Core) __state, ResourcePackPickup __instance, ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall)
        {
            var iteminlevel = __instance.TryCast<ItemInLevel>();
            TryGetItemAndShuttlebox(out __state, iteminlevel, status, player);
        }

        public static void Postfix((ItemInLevel, Shuttlebox_Core) __state, ResourcePackPickup __instance, ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall)
        {
            AttemptSwapIntoShuttlebox(__state);
        }
    }
    
}