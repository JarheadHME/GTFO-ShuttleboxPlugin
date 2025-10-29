using GTFO.API;
using LevelGeneration;
using Player;
using ShuttleboxPlugin.Modules;
using TerminalQueryAPI;

namespace ShuttleboxPlugin.Utils
{
    public static class NetworkEvents
    {
        public static void RegisterAllEvents()
        {
            NetworkAPI.RegisterEvent<pItemData>(DeregisterTerminalItemEvent, AttemptDeregisterTerminalItem);
            NetworkAPI.RegisterEvent<pItemData>(OverrideQueryEvent, OverrideItemQueryInformation);
            NetworkAPI.RegisterEvent<pItemData>(ResetOverriddenQueryEvent, ResetItemQueryOverride);
        }

        // This event is for Deregistering items that are no longer considered in the level from the terminal system
        public static readonly string DeregisterTerminalItemEvent = "JarheadHME.Shuttlebox.DeregisterHiddenTermItems";
        public static void AttemptDeregisterTerminalItem(ulong sender, pItemData itemData)
        {
            if (!GameStateManager.IsInExpedition)
            {
                Logger.Error("Trying to deregister an item when not in a level");
                return;
            }

            if (PlayerBackpackManager.TryGetItemInLevelFromItemData(itemData, out var item))
            {
                var termItem = item.GetComponentInChildren<iTerminalItem>();
                if (termItem != null)
                {
                    LG_LevelInteractionManager.DeregisterTerminalItem(termItem);
                }
            }
            else
            {
                Logger.Error("Tried to deregister terminal item, but didn't get item from pItemData");
            }
        }

        // This event overrides the query for an item that the shuttlebox can summon
        public static readonly string OverrideQueryEvent = "JarheadHME.Shuttlebox.SummonItemOverrideQuery";
        public static void OverrideItemQueryInformation(ulong sender, pItemData itemData)
        {
            if (!GameStateManager.IsInExpedition)
            {
                Logger.Error("Trying to override item query when not in a level");
                return;
            }

            if (PlayerBackpackManager.TryGetItemInLevelFromItemData(itemData, out var item))
            {
                var termItem = item.GetComponentInChildren<iTerminalItem>();
                if (termItem != null)
                    foreach (var shuttlebox in Shuttlebox_Core.s_setupShuttleboxes)
                        if (shuttlebox.ItemToSummon.Get_pItemData().Equals(item.Get_pItemData()))
                        {
                            QueryableAPI.ModifyTempQueryableItem(termItem, shuttlebox.GetHiddenItemQueryInfo);
                            if (!Shuttlebox_Core.PingOverrideItemKeys.Contains(termItem.TerminalItemKey))
                                Shuttlebox_Core.PingOverrideItemKeys.Add(termItem.TerminalItemKey);
                            break;
                        }
            }
            else
            {
                Logger.Error("Tried to override item query, but didn't get item from pItemData");
            }
        }

        // This event resets the overridden query of an item that a shuttlebox will summon
        public static readonly string ResetOverriddenQueryEvent = "JarheadHME.Shuttlebox.SummonItemResetQuery";
        public static void ResetItemQueryOverride(ulong sender, pItemData itemData)
        {
            if (!GameStateManager.IsInExpedition)
            {
                Logger.Error("Trying to reset overriden item query when not in a level");
                return;
            }

            if (PlayerBackpackManager.TryGetItemInLevelFromItemData(itemData, out var item))
            {
                var termItem = item.GetComponentInChildren<iTerminalItem>();
                if (termItem != null)
                {
                    QueryableAPI.DeregisterTempQueryableItem(termItem);
                    Shuttlebox_Core.PingOverrideItemKeys.Remove(termItem.TerminalItemKey);
                }
                    
            }
            else
            {
                Logger.Error("Tried to reset overridden item query, but didn't get item from pItemData");
            }
        }

    }
}
