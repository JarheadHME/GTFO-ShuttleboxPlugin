using Player;
using SNetwork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Localization;
using LevelGeneration;
using ShuttleboxPlugin.Utils;
using Agents;
using Il2CppInterop.Runtime.Attributes;

namespace ShuttleboxPlugin.Modules
{
    internal class Shuttlebox_AnyItemInteract : CustomInteractAPI.Components.Interact_Timed
    {
        [HideFromIl2Cpp]
        public event Action<SNet_Player, Item> AttemptInsertIntoShuttlebox;

        public override string InteractionMessage { get; set; }
        public uint InsertTextID = 865U;

        private static InputAction m_upAction = InputAction.TerminalUp;
        private static InputAction m_downAction = InputAction.TerminalDown;

        public void Awake()
        {
            this.OnInteractionTriggered_action = (Action<PlayerAgent>)OnInteractTriggered;
            this.OnInteractionTriggered += this.OnInteractionTriggered_action;
        }

        protected override void Update()
        {
            base.Update();

            if (!this.IsSelected) return;

            if (this.m_interactTargetAgent != null)
            {
                if (base.PlayerCheckInput(this.m_interactTargetAgent))
                    this.PlayerDoInteract(this.m_interactTargetAgent);
            }

            if (m_pocketItemMode)
            {
                if (InputMapper.GetButtonDown.Invoke(m_downAction))
                    m_pocketSlotIndex++;
                if (InputMapper.GetButtonDown.Invoke(m_upAction))
                    m_pocketSlotIndex--;
            }
            

        }

        public static string GetPocketUseMessage(bool multipleItems = true)
        {
            string outstr = "";

            outstr += Text.Format(827U, [ InputMapper.GetBindingName(InputAction.Use) ] );

            if (multipleItems)
                outstr += $"\nPress {InputMapper.GetBindingName(m_upAction)} or {InputMapper.GetBindingName(m_downAction)}\nto change selected item.";


            return outstr;
        }

        public override void PlayerSetSelected(bool selected, PlayerAgent agent)
        {
            base.PlayerSetSelected(selected, agent);
            this.m_interactTargetAgent = selected ? agent : null;

            //m_pocketSlotIndex = 0;

            this.OnTimerUpdate(0f);

            if (m_pocketItemMode)
            {
                GuiManager.InteractionLayer.SetInteractPrompt(this.InteractionMessage, GetPocketUseMessage(this.m_pocketHasMoreThanOne));
            }
        }

        public override bool PlayerCanInteract(PlayerAgent source)
        {
            if (!base.PlayerCanInteract(source)) return false;

            var wieldedslot = source.Inventory.WieldedSlot;
            if (wieldedslot == InventorySlot.ConsumableHeavy
             || wieldedslot == InventorySlot.InLevelCarry) return false;

            if (PlayerHasItemToSend(source, out Item item))
            {
                this.InteractionMessage = Text.Format(InsertTextID, item.PublicName);

                var prev_item = m_lastItem;
                m_lastItem = item;

                if (prev_item != null && !prev_item.PublicName.Equals(m_lastItem.PublicName))
                {
                    this.OnSelectedChange(false, source, forceUpdate: true);
                }
                return true;
            }
            return false;
        }

        private Item m_lastItem = null;
        private bool m_pocketItemMode;
        private uint m_pocketSlotIndex = 0;
        private bool m_pocketHasMoreThanOne = false;
        public bool PlayerHasItemToSend(PlayerAgent player, out Item item)
        {
            item = null;
            //Logger.Info($"player wielding slot {player.Inventory.WieldedSlot}");
            if (player.Inventory.WieldedSlot == InventorySlot.Consumable
             || player.Inventory.WieldedSlot == InventorySlot.ResourcePack)
            {
                item = player.Inventory.WieldedItem;
                m_pocketItemMode = false;
                return true;
            }
            else // Try get pocket item
            {
                var backpack = PlayerBackpackManager.GetBackpack(player.Owner);

                List<uint> keys = new List<uint>();
                foreach (var kvp in backpack.ItemIDToPocketItemGroup)
                {
                    if (kvp.Value.Count > 0)
                        keys.Add(kvp.Key);
                }

                if (keys.Count == 0) return false;

                var i = (int)m_pocketSlotIndex % keys.Count;
                i = i < 0 ? i + keys.Count : i;
                var key = keys[i];

                var pocketItem = backpack.ItemIDToPocketItemGroup[key][0];

                pItemData fillerData = new pItemData();
                fillerData.itemID_gearCRC = key;
                fillerData.slot = InventorySlot.InPocket;
                fillerData.replicatorRef = pocketItem.replicatorRef;

                if (PlayerBackpackManager.TryGetItemInLevelFromItemData(fillerData, out item))
                {
                    m_pocketItemMode = true;
                    m_pocketHasMoreThanOne = keys.Count > 1;
                    return true;
                }
                else
                {
                    Logger.Error($"Failed to get pocket item from pocket: {key}");
                }


                //foreach (var key in KeysInMap)
                //{
                //    // functionally same code as PlayerBackpack.HasPocketItem
                //    if (backpack.CountPocketItem(key) > 0) // using the actual thing + TryGetValue was erroring for some reason
                //    {
                //        // could maybe theoretically store the itempickup from the progressionobjectivesmanager
                //        // but i wanna make absolutely sure that i'm getting *that specific item*'s iteminlevel from the inventory
                //        var pocketItem = backpack.ItemIDToPocketItemGroup[key][0];
                //        pItemData fillerData = new pItemData();
                //        fillerData.itemID_gearCRC = key;
                //        fillerData.slot = InventorySlot.InPocket;
                //        fillerData.replicatorRef = pocketItem.replicatorRef;

                //        if (PlayerBackpackManager.TryGetItemInLevelFromItemData(fillerData, out item))
                //        {
                //            return true;
                //        }
                //        else
                //        {
                //            Logger.Error($"Failed to get key item from pocket: {key}");
                //        }
                //    }
                //}
            }

            return false;
        }

        public Action<PlayerAgent> OnInteractionTriggered_action;
        public void OnInteractTriggered(PlayerAgent agent)
        {
            AttemptInsertIntoShuttlebox?.Invoke(agent.Owner, m_lastItem);
        }
    }
}
