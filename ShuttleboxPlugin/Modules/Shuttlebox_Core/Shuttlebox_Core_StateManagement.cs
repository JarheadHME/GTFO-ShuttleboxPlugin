using AK;
using AmorLib.Networking.StateReplicators;
using GameData;
using GTFO.API;
using GTFO.API.Extensions;
using Il2CppInterop.Runtime.Attributes;
using LevelGeneration;
using Player;
using ShuttleboxPlugin.Utils;
using SNetwork;
using System.Collections.Generic;
using UnityEngine;

namespace ShuttleboxPlugin.Modules;
public partial class Shuttlebox_Core : MonoBehaviour, IStateReplicatorHolder<pShuttleboxState>
{

    // power cells' (and others ig if they have them) sound doesn't move when they get transferred
    // so try to manually update it?
    public static void UpdateSoundPlayerPositions(ItemInLevel item)
    {
        var simplesound = item.GetComponentsInChildren<SimpleSoundPlayer>();
        var cellsoundplayers = item.GetComponentsInChildren<CellSoundPlayer>(); // check both just in case ig

        foreach (var simplesoundplayer in simplesound)
            simplesoundplayer.m_sound.UpdatePosition(item.transform.position);
        foreach (var cellsoundplayer in cellsoundplayers)
            cellsoundplayer.UpdatePosition(item.transform.position);

    }

    #region OnSequencesEnded
    public void OnCloseSequenceEnded()
    {
        var state = State;
        state.state = eShuttleboxState.Closed;
        Replicator.SetStateUnsynced(state);

        if (this.InteractionType == eShuttleboxInteractionType.None)
            return;

        if (HasItem && (
            InteractionType != eShuttleboxInteractionType.ReceiveTransferClose
         && InteractionType != eShuttleboxInteractionType.Close
            )
        )
            TryExecuteItemEvents(ItemInside.GetItem(), eWardenObjectiveEventTrigger.OnMid);
        else if (InteractionType == eShuttleboxInteractionType.Summon && ItemToSummon != null)
            TryExecuteItemEvents(ItemToSummon.GetItem(), eWardenObjectiveEventTrigger.OnMid);

        if (!IsMaster) return;

        bool Reopen = true;

        switch (InteractionType)
        {
            case eShuttleboxInteractionType.Close:
                Reopen = false; 
                break;

            case eShuttleboxInteractionType.CloseConsumeRemainShut:
                Reopen = false;
                goto case eShuttleboxInteractionType.CloseConsume;
            case eShuttleboxInteractionType.CloseConsume:
                if (!HasItem)
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] Tried to consume item, but had no item to consume.");
                    break;
                }
                HideItemAtBottomOfElevator(ItemInside);
                ItemInside = null;
                state.interactedItem = default;
                break;

            case eShuttleboxInteractionType.Transfer:

                m_linkedShuttlebox.ItemInside = ItemInside;
                var linked_state = m_linkedShuttlebox.State;
                linked_state.interactedItem = ItemInside.Get_pItemData();
                m_linkedShuttlebox.Replicator.SetStateUnsynced(linked_state);

                m_linkedShuttlebox.MoveItemToShuttlebox(ItemInside);

                ItemInside = null;
                state.interactedItem = default;
                state.type = eShuttleboxInteractionType.None;
                break;

            case eShuttleboxInteractionType.ReceiveTransferClose:
                AttemptInteract(eShuttleboxInteractionType.ReceiveTransferOpen);
                return;

            case eShuttleboxInteractionType.Summon:
                if (ItemToSummon == null)
                {
                    Logger.Info($"[Shuttlebox {DebugName}] Got to end of close sequence trying to summon, but had no item to summon.");
                    break;
                }
                ItemInside = ItemToSummon;
                state.interactedItem = state.summonItem;
                state.summonItem = default;
                goto case eShuttleboxInteractionType.ReceiveTransferClose;

            default:
                Logger.Warn($"[Shuttlebox '{DebugName}'] Got to end of close sequence with weird interact type: {InteractionType}");
                break;

        }

        // Commit a state change if any took place
        Replicator.SetStateUnsynced(state);

        if (Reopen)
        {
            AttemptInteract(eShuttleboxInteractionType.Open);
        }
        else // stay closed
        {
            //state.type = eShuttleboxInteractionType.None;
            state.terminalItemStatus = eFloorInventoryObjectStatus.Deactivated;
            SetOnlyLightState(eShuttleboxLightState.ShutOff, state);
        }

    }
    public void OnOpenSequenceEnded()
    {

        var state = State;
        state.state = eShuttleboxState.Open;
        state.type = eShuttleboxInteractionType.None;

        if (!HasItem && state.interactedItem.itemID_gearCRC != 0) // Supposedly doesn't have an item, but state says it does, so will force correct ig
        {
            if (!TryGetItemInsideFromState(state.interactedItem))
            {
                Logger.Error($"[Shuttlebox '{DebugName}'] CurrState had item but no ItemInside, failed to get item from state. Unsetting state's item.");
                state.interactedItem = default;
            }
        }
    
        Replicator.SetStateUnsynced(state);

        this.m_interact.SetActive(!HasItem);
        if (HasItem)
            SetItemVisibleInteractable(ItemInside);

        if (IsMaster)
        {

            // Set light to queued if there are actions queued
            if (AnyQueued())
                SetOnlyLightState(eShuttleboxLightState.Queued);
            else
                SetOnlyLightState(eShuttleboxLightState.Ready);
        }

    }

    // Event triggers
    public void OnSummonSequenceTriggered()
    {
        Logger.DebugOnly($"[Shuttlebox '{DebugName}'] Summon sequence triggered");

        if (IsMaster && ItemToSummon != null)
            this.AttemptInteract(eShuttleboxInteractionType.Summon);
    }
    public void OnOpenSequenceTriggered()
    {
        Logger.DebugOnly($"[Shuttlebox '{DebugName}'] ForceOpen sequence triggered");
        this.m_forceOpenTrigger.ResetTrigger();

        if (!IsMaster) return;

        AttemptInteract(eShuttleboxInteractionType.Open);
    }
    public void OnCloseSequenceTriggered()
    {
        Logger.DebugOnly($"[Shuttlebox '{DebugName}'] ForceClose sequence triggered");
        this.m_forceCloseTrigger.ResetTrigger();

        if (!IsMaster) return;

        AttemptInteract(eShuttleboxInteractionType.Close);
    }
    #endregion

    public void OnStateChange(pShuttleboxState oldState, pShuttleboxState state, bool isRecall)
    {
        //Logger.Info($"[Shuttlebox '{DebugName}'] OnStateChange\nHasItem: {HasItem}\nOld CurrState:\n{oldState}\n\nNew CurrState:\n{state}");

        // The only thing that needs to happen on client side is the opening and closing
        // Host will determine pretty much everything after that.
        if (IsOpen(oldState) && IsClosing(state))
        {
            this.m_animTrigger.Trigger();
            this.m_interact.SetActive(false);
            if (HasItem)
                SetItemVisibleNotInteractable(ItemInside);
        }
        if (IsClosed(oldState) && IsOpening(state))
            this.m_animTrigger.ResetTrigger();

        //LightMesh.SetColor(state.lightColor);

        LightFader.TargetColor = state.lightColor;

        // If the only thing changed in the state is the light, then functionally nothing changes
        if (state.EqualsNoLight(oldState))
            return;

        this.m_terminalItem.FloorItemStatus = state.terminalItemStatus;

        if (isRecall) // Item Correction post checkpoint
        {
            // run regardless because this should also set iteminside to null if there was none
            TryGetItemInsideFromState(state.interactedItem);

            this.m_interact.SetActive(!HasItem);

            // Rehide summon, also will re-deregister them from terminal system
            if (IsMaster && this.ItemToSummon != null)
                HideItemAtBottomOfElevator(this.ItemToSummon, this.ShouldDeregisterSummonItem);

        }


        switch (state.type)
        {
            case eShuttleboxInteractionType.Insert:
                if (!HasItem && ValidatePlayerHasItem(state.interactingPlayer, state.interactedItem, out var player, out var ItemInserted))
                {
                    //bool result = CarryItemInteractionUtils.TryInsertItem(player, state.interactedItem, this.m_interact, null);
                    bool result = AttemptRemoveItemFromInventory(player, ItemInserted);
                    if (!result)
                    {
                        Logger.Error($"[Shuttlebox '{DebugName}'] ({state.type}) Validated insert failed???");
                        return;
                    }
                    ItemInside = ItemInserted.Cast<ItemInLevel>();

                    TryExecuteItemEvents(ItemInside.GetItem(), eWardenObjectiveEventTrigger.OnStart);

                    // empty its interaction type so if a reload happens it doesn't try to insert the same item again
                    state.type = eShuttleboxInteractionType.None;
                    Replicator.SetStateUnsynced(state);

                    if (!IsMaster) break;

                    TryExecuteItemActions(ItemInserted);
                }
                else
                    Logger.Error($"[Shuttlebox '{DebugName}'] ({state.type}) Tried to insert item, but it either already had an item or validation failed.");
                break;
            case eShuttleboxInteractionType.Remove:
                if (HasItem || TryGetItemInsideFromState(oldState.interactedItem))
                    TryExecuteItemEvents(ItemInside.GetItem(), eWardenObjectiveEventTrigger.OnEnd);
                else
                    Logger.Error($"[Shuttlebox '{DebugName}'] Tried to remove item, but no item found. If this turns out to be a major issue, report this to the relevant persons.");


                ItemInside = null;
                m_interact.SetActive(true);

                // empty its interaction type so if a reload happens it doesn't try to remove the same item again
                state.type = eShuttleboxInteractionType.None;
                Replicator.SetStateUnsynced(state);

                break;

            case eShuttleboxInteractionType.SwapIn:
                ItemInLevel iteminlevel = null;
                if (!TryGetItemInLevelFromData(state.interactedItem, out iteminlevel))
                    Logger.Error($"[Shuttlebox '{DebugName}'] Swapping in item but can't find iteminlevel");

                ItemInside = iteminlevel;
                m_interact.SetActive(false);

                TryExecuteItemEvents(ItemInside.GetItem(), eWardenObjectiveEventTrigger.OnStart);

                if (!IsMaster) break;

                TryExecuteItemActions(ItemInside.GetItem());

                break;

            case eShuttleboxInteractionType.Place:
                ItemInLevel itemInLevel = null;
                if (!TryGetItemInLevelFromData(state.interactedItem, out itemInLevel))
                    Logger.Error($"[Shuttlebox '{DebugName}'] Placing item but can't find iteminlevel");

                ItemInside = itemInLevel;
                m_interact.SetActive(false);

                break;

            case eShuttleboxInteractionType.Transfer:
                if (IsMaster)
                {
                    SetItemVisibleNotInteractable(ItemInside);
                }

                m_linkedShuttlebox.AttemptInteract(eShuttleboxInteractionType.ReceiveTransferClose);
                    
                break;
            case eShuttleboxInteractionType.ReceiveTransferOpen:
                if (HasItem || TryGetItemInsideFromState(state.interactedItem))
                {
                    UpdateSoundPlayerPositions(ItemInside);
                }
                else
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] Got no item in interact '{state.type}', if this turns out to be a major issue, please report this to the relevant persons.");
                }
                break;

            case eShuttleboxInteractionType.Open:
                if (state.interactedItem.itemID_gearCRC == 0)
                    ItemInside = null;
                break;

            case eShuttleboxInteractionType.Close:
            case eShuttleboxInteractionType.CloseConsume:
            case eShuttleboxInteractionType.CloseConsumeRemainShut:
                if (HasItem && IsMaster)
                    SetItemVisibleNotInteractable(ItemInside);
                break;
        }

    }


    #region Inventory Management
    public static bool ValidatePlayerHasItem(SNetStructs.pPlayer pPlayer, pItemData itemData, out SNet_Player player, out Item item)
    {
        player = null;
        item = null;
        if (CarryItemInteractionUtils.ValidateCarryItemInsertion(pPlayer, itemData, out player, out item))
            return true;
        else
        {
            if (player == null && !pPlayer.TryGetPlayer(out player))
                return false;
            // PocketItem
            return TryGetPocketItemInLevel(itemData, player, out item);
        }
    }
    public static bool TryGetPocketItemInLevel(pItemData itemData, SNet_Player player, out Item item)
    {
        item = null;
        var backpack = PlayerBackpackManager.GetBackpack(player);
        if (backpack == null) return false;

        if (backpack.CountPocketItem(itemData.itemID_gearCRC) > 0)
        {
            foreach (PocketItem pocketItem in backpack.ItemIDToPocketItemGroup[itemData.itemID_gearCRC])
            {
                if (pocketItem.replicatorRef.Equals(itemData.replicatorRef))
                {
                    return PlayerBackpackManager.TryGetItemInLevelFromItemData(itemData, out item);
                }
            }
        }
        return false;
    }

    // not static because it drops the item inside the shuttlebox
    public bool AttemptRemoveItemFromInventory(SNet_Player player, Item item)
    {
        if (player == null) return false;
        if (item == null) return false;

        var backpack = PlayerBackpackManager.GetBackpack(player);
        if (backpack == null) return false;

        var itemData = item.Get_pItemData();
        var slot = itemData.slot;

        if (slot == InventorySlot.InPocket)
        {
            var itemID = item.ItemDataBlock.persistentID;
            if (backpack.CountPocketItem(itemID) > 0)
            {
                var pocketItem = backpack.ItemIDToPocketItemGroup[itemID][0];
                if (!pocketItem.replicatorRef.Equals(item.Get_pItemData().replicatorRef))
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] Pocket item to be removed from backpack was not the first item in that group??");
                    return false;
                }
                return backpack.RemovePocketItem(itemID);
            }
            Logger.Error($"[Shuttlebox '{DebugName}'] Player {player.NickName} didn't have pocket item {item.PublicName}???");
            return false;
        }
        else
        {
            backpack.TryGetBackpackItem(slot, out var backpackItem);
            if (backpackItem == null) return false;

            AmmoType ammotypefromslot = PlayerAmmoStorage.GetAmmoTypeFromSlot(slot);

            backpack.AmmoStorage.SetAmmo(ammotypefromslot, State.interactedItem.custom.ammo);

            PlayerAgent agent = player.PlayerAgent.TryCast<PlayerAgent>();
            if (agent == null) return false;

            PlayerBackpackManager.Current.DropItemFromBackpack(backpack, slot, ammotypefromslot, this.m_itemAlign.position, this.m_itemAlign.rotation, agent);
            return true;
        }
    }
    #endregion

    #region Events and Actions
    public void TryExecuteItemActions(Item item)
    {
        if (this.ItemToEventsOnEnterDict.TryGetValue(item.ItemDataBlock.persistentID, out var actions))
        {
            switch (actions.ActionOnInsert)
            {
                case eShuttleboxAction.Transfer:
                    AttemptInteract(eShuttleboxInteractionType.Transfer);
                    break;
                case eShuttleboxAction.Consume:
                    AttemptInteract(eShuttleboxInteractionType.CloseConsume);
                    break;
                case eShuttleboxAction.ConsumeAndRemainClosed:
                    AttemptInteract(eShuttleboxInteractionType.CloseConsumeRemainShut);
                    break;

                default:
                    break;
            }
        }
        else
        {
            //Logger.Warn($"[Shuttlebox '{DebugName}'] No actions defined for {item.PublicName}");
            SetOnlyLightState(eShuttleboxLightState.InvalidItem);
        }
    }

    public void TryExecuteItemEvents(Item item, eWardenObjectiveEventTrigger trigger)
    {
        if (this.ItemToEventsOnEnterDict.TryGetValue(item.ItemDataBlock.persistentID, out var actions))
        {
            ExecuteWardenEvents(actions.Events, trigger);
        }
    }

    public static void ExecuteWardenEvents(List<WardenObjectiveEventData> events, eWardenObjectiveEventTrigger trigger)
    {
        WardenObjectiveManager.CheckAndExecuteEventsOnTrigger(events.ToIl2Cpp(), trigger);
    }
    #endregion

    public void AttemptInteract(eShuttleboxInteractionType type, SNet_Player player = null, Item item = null)
    {
        pShuttleboxState state = State;

        //Logger.Info($"[Shuttlebox '{DebugName}'] Attempt Interact:\nType: {type}\nState:\n{state}");

        switch (type)
        {
            case eShuttleboxInteractionType.Open:
                if (!IsClosed())
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] ({type}) Tried to open when not in valid state (should be Closed, was {GetState()})");
                    return;
                }

                state.state = eShuttleboxState.Opening;
                break;

            case eShuttleboxInteractionType.Close: // pretty much same as the two below, but different light color since it's closing
                if (!IsOpen())
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] ({type}) Tried to close when not in valid state (should be Open)");
                    return;
                }
                state.state = eShuttleboxState.Closing;
                state.lightColor = LightStateToColor[eShuttleboxLightState.InvalidItem];
                break;

            case eShuttleboxInteractionType.CloseConsume: // OnCloseSequenceEnded will refer to it later
            case eShuttleboxInteractionType.CloseConsumeRemainShut:
                if (!IsOpen())
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] ({type}) Tried to close when not in valid state (should be Open)");
                    return;
                }

                state.state = eShuttleboxState.Closing;
                state.lightColor = LightStateToColor[eShuttleboxLightState.Working];
                break;

            case eShuttleboxInteractionType.Insert:
                if (!IsOpen() || HasItem)
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] ({type}) Shouldn't be able to attempt insert when not open, or not empty");
                    return;
                }

                if (player == null || item == null)
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] ({type}) Wanting to insert but either player or item is null?");
                    return;
                }

                SNetStructs.pPlayer playerStruct = new();
                playerStruct.SetPlayer(player);

                state.interactingPlayer = playerStruct;

                var data = item.Get_pItemData();
                var slot = data.slot;
                var storage = PlayerBackpackManager.LocalBackpack.AmmoStorage;

                var custom = data.custom;
                custom.ammo = storage.GetAmmoInPack(PlayerAmmoStorage.GetAmmoTypeFromSlot(slot));

                data.custom = custom;
                state.interactedItem = data;

                break;

            case eShuttleboxInteractionType.Remove:
                if (!IsOpen()) Logger.Warn($"[Shuttlebox '{DebugName}'] ({type}) Is somehow removing item when shuttlebox isn't open?");
                
                state.interactingPlayer = default;
                state.interactedItem = default;

                DeQueueAction(eShuttleboxInteractionType.Transfer);
                state.lightColor = LightStateToColor[eShuttleboxLightState.Ready];

                break;

            case eShuttleboxInteractionType.SwapIn:
                if (item == null)
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] SwapIn queued, but item to attempt to swap is null");
                    return;
                }
                if (!IsOpen() || HasItem)
                {
                    QueuedItem = item;
                    this.QueueAction(eShuttleboxInteractionType.SwapIn);
                    return;
                }

                this.DeQueueAction(type);
                QueuedItem = null;

                state.interactedItem = item.Get_pItemData();
                break;

            case eShuttleboxInteractionType.Place:
                if (item == null)
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] SwapIn queued, but item to attempt to swap is null");
                    return;
                }
                if (HasItem)
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] Tried to use Place interaction type, but there's already item inside? How'd this happen wut");
                    return;
                }

                state.interactedItem = item.Get_pItemData();
                break;

            case eShuttleboxInteractionType.Transfer:
                if (!HasItem)
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] ({type}) Tried to transfer with no item inside.");
                    return;
                }

                if (m_linkedShuttlebox.HasItem || !m_linkedShuttlebox.IsOpen())
                {
                    QueueAction(type);
                    if (IsOpen())
                        SetOnlyLightState(eShuttleboxLightState.Queued);
                    return;
                }

                DeQueueAction(type);

                goto case eShuttleboxInteractionType.CloseConsume;
            case eShuttleboxInteractionType.Summon:
                if (ItemToSummon == null)
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] Tried to summon an item when no item to summon");
                    DeQueueAction(type);
                    return;
                }

                // Also check if linked has a transfer queued, cause otherwise they can happen simultaneously
                if (!IsOpen() || HasItem)
                {
                    QueueAction(type);
                    if (IsOpen())
                        SetOnlyLightState(eShuttleboxLightState.Queued);
                    return;
                }
                    

                DeQueueAction(type);
                // All it'll do to start with is close, so just go to close.
                goto case eShuttleboxInteractionType.CloseConsume;

            case eShuttleboxInteractionType.ReceiveTransferOpen:
                // Receive should happen when opening
                goto case eShuttleboxInteractionType.Open;
            case eShuttleboxInteractionType.ReceiveTransferClose:
                state.interactedItem = m_linkedShuttlebox.State.interactedItem;
                goto case eShuttleboxInteractionType.CloseConsume; // this one instead of normal close for the right color
            default:
                // Do nothing ig lol
                break;
        }

        state.type = type;

        // If it's doing anything here then it's not deactivated (at least yet)
        state.terminalItemStatus = eFloorInventoryObjectStatus.Normal;

        Replicator.SetState(state);
    }

    public void AttemptInsert(SNet_Player player, Item item)
    {
        // Don't allow placing consumables or resource packs because it could hypothetically softlock a player
        // e.g if they placed a flashlight into the shuttlebox where it would do nothing, then picked up another flashlight
        // If they had no access to any other consumables, then there would be no way to place something you needed to place into the shuttlebox
        var itemdb = item.ItemDataBlock;
        if ( (itemdb.inventorySlot == InventorySlot.Consumable || itemdb.inventorySlot == InventorySlot.ResourcePack) &&
            !this.ItemToEventsOnEnterDict.ContainsKey(item.ItemDataBlock.persistentID))
        {
            CellSound.Post(EVENTS.BUTTONGENERICBLIPDENIED, player.PlayerAgent.TryCast<PlayerAgent>().Position);
            GuiManager.InteractionLayer.SetTimedInteractionPrompt("This item is not allowed in here!", 1.0f, ePUIMessageStyle.Default);
            return;
        }

        // Otherwise, we're all gucci, do the thing
        AttemptInteract(eShuttleboxInteractionType.Insert, player, item);
    }
    #region State generalization
    public eShuttleboxState GetState() => State.state;
    public eShuttleboxInteractionType GetInteractionType() => State.type;
    public static bool IsOpen(pShuttleboxState state)
    {
        return state.state == eShuttleboxState.Open;
    }
    public bool IsOpen() => IsOpen(State);
    public static bool IsOpening(pShuttleboxState state)
    {
        return state.state == eShuttleboxState.Opening;
    }
    public bool IsOpening() => IsOpening(State);
    public static bool IsClosed(pShuttleboxState state)
    {
        return state.state == eShuttleboxState.Closed;
    }
    public bool IsClosed() => IsClosed(State);
    public static bool IsClosing(pShuttleboxState state)
    {
        return state.state == eShuttleboxState.Closing;
    }
    public bool IsClosing() => IsClosing(State);
    #endregion

    #region ItemInside state changes
    // These both set an item to the pickup align, and change their state.
    // The names of the states (visible/interactable) are only accurate if it's a bigpickup
    public void SetItemVisibleNotInteractable(ItemInLevel item)
    {
        if (item.TryCastAtHome(out CarryItemPickup_Core _) && IsMaster)
        {
            var prev_state = item.GetCustomData();
            pItemData_Custom custom = new()
            {
                ammo = prev_state.ammo,
                byteId = prev_state.byteId,
                byteState = (byte)eCarryItemCustomState.Inserted_Visible_NotInteractable
            };

            item.GetSyncComponent().AttemptPickupInteraction(
                type: ePickupItemInteractionType.Place,
                player: null,
                custom: custom,
                position: m_itemAlign.position,
                rotation: m_itemAlign.rotation,
                node: m_sourceNode,
                droppedOnFloor: false,
                forceUpdate: true
            );
        }
        else
        {
            var interact = item.GetComponentInChildren<Interact_Base>();
            if (interact != null)
            {
                interact.SetActive(false);
            }
        }

        
    }
    public void SetItemVisibleInteractable(ItemInLevel item)
    {

        if (item.TryCastAtHome(out CarryItemPickup_Core _) && IsMaster)
        {
            var prev_state = item.GetCustomData();
            pItemData_Custom custom = new()
            {
                ammo = prev_state.ammo,
                byteId = prev_state.byteId,
                byteState = (byte)eCarryItemCustomState.Default
            };

            item.GetSyncComponent().AttemptPickupInteraction(
                type: ePickupItemInteractionType.Place,
                player: null,
                custom: custom,
                position: m_itemAlign.position,
                rotation: m_itemAlign.rotation,
                node: m_sourceNode,
                droppedOnFloor: false,
                forceUpdate: true
            );
        }
        else
        {
            var interact = item.GetComponentInChildren<Interact_Base>();
            if (interact != null)
            {
                interact.SetActive(true);
            }
        }
        
    }
    public void MoveItemToShuttlebox(ItemInLevel item)
    {
        if (!IsMaster) return;
        var prev_state = item.GetCustomData();

        item.GetSyncComponent().AttemptPickupInteraction(
            type: ePickupItemInteractionType.Place,
            player: null,
            custom: prev_state,
            position: m_itemAlign.position,
            rotation: m_itemAlign.rotation,
            node: m_sourceNode,
            droppedOnFloor: false,
            forceUpdate: true
        );

        NetworkAPI.InvokeEvent<pItemData>(NetworkEvents.ResetOverriddenQueryEvent, item.Get_pItemData());
        NetworkEvents.ResetItemQueryOverride(0U, item.Get_pItemData());
    }

    public void HideItemAtBottomOfElevator(ItemInLevel item, bool shouldDeregister = true)
    {
        if (!IsMaster) return;

        var prev = item.GetCustomData();
        pItemData_Custom custom = new()
        {
            ammo = prev.ammo,
            byteId = prev.byteId,
            byteState = (byte)eCarryItemCustomState.Inserted_Disabled
        };

        item.GetSyncComponent().AttemptPickupInteraction(
            ePickupItemInteractionType.Place, 
            player: null, 
            custom: custom, 
            position: new Vector3(0, -1000, 0), 
            node: m_sourceNode, 
            forceUpdate: true
        );

        if (shouldDeregister)
        {
            NetworkAPI.InvokeEvent<pItemData>(NetworkEvents.DeregisterTerminalItemEvent, item.Get_pItemData());
            NetworkEvents.AttemptDeregisterTerminalItem(0U, item.Get_pItemData());
        }
        else
        { // Shouldn't deregister, but still want to do some overriding stuff
            NetworkAPI.InvokeEvent<pItemData>(NetworkEvents.OverrideQueryEvent, item.Get_pItemData());
            NetworkEvents.OverrideItemQueryInformation(0U, item.Get_pItemData());
        }

    }

    [HideFromIl2Cpp]
    public List<string> GetHiddenItemQueryInfo(List<string> defaultDetails)
    {
        return [
            "----------------------------------------------------------------",
            "Stored Shuttlebox Item",
            defaultDetails[0],
            defaultDetails[1],
            defaultDetails[2],
            $"LOCATION: Inside {this.m_terminalItem.TerminalItemKey}",
            $"PING STATUS: Inaccessible",
            "----------------------------------------------------------------"
        ];
    }

    public bool TryGetItemInsideFromState(pItemData state)
    {
        if (TryGetItemInLevelFromData(state, out ItemInLevel item))
            ItemInside = item;
        else
            ItemInside = null;
        return HasItem;
    }
    public static bool TryGetItemInLevelFromData(pItemData data, out ItemInLevel iteminlevel)
    {
        iteminlevel = null;
        if (data.itemID_gearCRC == 0) return false;
        return PlayerBackpackManager.TryGetItemInLevelFromItemData(data, out Item item) && item.TryCastAtHome(out iteminlevel);
    }

    // Casting the method to an Action manually every time causes it to seemingly not be the same item
    // thus causing .remove_xxx to not actually remove the previous versions.
    // Caching it like this makes it work!
    private Il2CppSystem.Action<ePickupItemStatus, pPickupPlacement, PlayerAgent, bool> OnSyncStatusChanged_action;
    public void OnSyncStatusChanged(ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall)
    {
        if (status == ePickupItemStatus.PickedUp)
            AttemptInteract(eShuttleboxInteractionType.Remove);
        else if (status == ePickupItemStatus.PlacedInLevel)
        {
            var interact = ItemInside.GetComponentInChildren<Interact_Base>();
            if (interact != null)
            {
                interact.SetActive(IsOpen());
            }
        }
    }
    #endregion

}

#region Structs and Enums
public struct pShuttleboxState
{
    public override string ToString()
    {
        bool gotPlayer = interactingPlayer.TryGetPlayer(out var player);
        uint id = interactedItem.itemID_gearCRC;
        ItemDataBlock item = id != 0 ? ItemDataBlock.GetBlock(id) : null;
        return $"  CurrState -> {state}\n  Type -> {type}\n  Player -> {(gotPlayer ? player.NickName : interactingPlayer.lookup)}\n  ItemInserted -> {item?.publicName}\n  Color -> {lightColor}";
    }

    public bool EqualsNoLight(pShuttleboxState other)
    {
        pShuttleboxState copy = other;
        copy.lightColor = this.lightColor;
        return copy.Equals(this);
    }

    public eShuttleboxState state { get; set; }
    public eShuttleboxInteractionType type { get; set; }
    public SNetStructs.pPlayer interactingPlayer { get; set; }
    public pItemData interactedItem { get; set; }
    public pItemData summonItem { get; set; }
    public Color lightColor { get; set; }
    public eFloorInventoryObjectStatus terminalItemStatus { get; set; }

}

public enum eShuttleboxState
{
    Open,
    Opening,
    Closed,
    Closing
}

public enum eShuttleboxInteractionType
{
    None,
    Open,
    Close,
    CloseConsume,
    CloseConsumeRemainShut,
    Insert,
    Remove,
    SwapIn,
    Transfer,
    ReceiveTransferClose,
    ReceiveTransferOpen,
    Summon,
    Place, // used for summon items
}

public enum eShuttleboxAction
{
    None,
    Transfer,
    Consume,
    ConsumeAndRemainClosed
}

#endregion