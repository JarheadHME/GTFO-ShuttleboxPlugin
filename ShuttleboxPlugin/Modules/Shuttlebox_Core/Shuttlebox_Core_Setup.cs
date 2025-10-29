using System;
using System.Collections.Generic;
using UnityEngine;
using LevelGeneration;
using SNetwork;
using AmorLib.Networking.StateReplicators;
using Player;
using GameData;
using Il2CppInterop.Runtime.Attributes;
using AIGraph;
using TerminalQueryAPI;
using TexturePainterAPI.PaintableTextures;
using TMPro;

namespace ShuttleboxPlugin.Modules;
public partial class Shuttlebox_Core : MonoBehaviour
{
    public static bool IsMaster { get => SNet.IsMaster; }

    public static List<Shuttlebox_Core> s_setupShuttleboxes = new();

    [HideFromIl2Cpp]
    public StateReplicator<pShuttleboxState> Replicator { get; set; }

    internal iCarryItemInteractionTarget m_interact;
    private LG_WorldEventAnimationTrigger m_animTrigger;
    private Transform m_itemAlign;
    private Transform m_serialAlign;
    internal Shuttlebox_Core m_linkedShuttlebox;

    private LG_WorldEventAnimationTrigger m_summonTrigger;
    private LG_WorldEventAnimationTrigger m_forceOpenTrigger;
    private LG_WorldEventAnimationTrigger m_forceCloseTrigger;
    public ItemInLevel ItemInside {
        get
        {
            return this.m_itemInside;
        }
        set
        {
            if (this.m_itemInside != null)
            {
                if (IsMaster)
                    this.m_itemInside.internalSync.remove_OnSyncStateChange(OnSyncStatusChanged_action);
            }

            this.m_itemInside = value;

            if (this.m_itemInside != null)
            {
                // when there's an item, then you shouldn't be able to interact with it.
                this.m_interact.SetActive(false);

                if (IsMaster)
                {
                    this.m_itemInside.internalSync.add_OnSyncStateChange(OnSyncStatusChanged_action);
                    MoveItemToShuttlebox(m_itemInside);
                }

                if (IsOpen())
                    SetItemVisibleInteractable(m_itemInside);
                else
                    SetItemVisibleNotInteractable(m_itemInside);
            }
        }
    }
    private ItemInLevel m_itemInside;
    public bool HasItem { get => this.ItemInside != null; }

    private AIG_CourseNode m_sourceNode;

    public int ShuttleboxLinkID = -1;
    public string DebugName { get; set; } = string.Empty;

    public Dictionary<uint, ShuttleboxItemsData> ItemToEventsOnEnterDict;

    public static pShuttleboxState StartingState = default;

    private iTerminalItem m_terminalItem;

    public ItemInLevel ItemToSummon
    {
        get
        {
            var item = this.State.summonItem;
            if (item.Equals(default(pItemData)))
                return null;
            
            if (!TryGetItemInLevelFromData(item, out var iteminlevel)) return null;
            
            return iteminlevel;
        }
    }
    public bool ShouldDeregisterSummonItem = true;
    public static List<string> PingOverrideItemKeys = new List<string>();

    public LG_LightEmitterMesh LightMesh = null;
    public Shuttlebox_LightFader LightFader = null;

    private bool m_shouldCloseAtStart = false;
    private bool m_shouldHideItemToSummon = true;

    public pShuttleboxState State { get => Replicator.State; }
    public eShuttleboxState CurrState { get => State.state; }
    public eShuttleboxInteractionType InteractionType { get => State.type; }

    [HideFromIl2Cpp]
    public void Setup(LG_Area chosen_area, ShuttleboxPlacementData data)
    {
        DebugName = data.DebugName;

        m_itemAlign = transform.FindChild(BigPickupAlignName);
        m_serialAlign = transform.FindChild(TMPAlignName);
        m_sourceNode = chosen_area.m_courseNode;

        SetupOnAnimationEnds();

        var interact = transform.FindChild(InteractObjName).GetComponent<LG_GenericCarryItemInteractionTarget>();
        interact.m_powerCellAlign = m_itemAlign;
        interact.m_insertType = InsertTypeEnum.value;
        interact.AttemptCarryItemInsert = (Action<SNet_Player, Item>)(AttemptInsert);
        interact.Setup(); // i don't think this actually does anything lul

        var smallItemInteract = interact.gameObject.AddComponent<Shuttlebox_AnyItemInteract>();
        smallItemInteract.AttemptInsertIntoShuttlebox += AttemptInsert;

        this.m_interact = interact.TryCast<iCarryItemInteractionTarget>();

        OnSyncStatusChanged_action = (Action<ePickupItemStatus, pPickupPlacement, PlayerAgent, bool>)OnSyncStatusChanged;

        ShuttleboxLinkID = data.LinkID;
        TryLinkToShuttlebox();

        SetupItemDict(data.ValidInsertItems);
        gameObject.name = $"Shuttlebox '{DebugName}'";

        ShuttleboxSpawnedItemData itemToSpawn = data.SpawnedItem;
        uint itemID = itemToSpawn.ItemID;
        ItemInLevel itemtosummon = null;
        if (ItemDataBlock.HasBlock(itemID))
        {
            var itemdb = ItemDataBlock.GetBlock(itemID);
            var inventorySlot = itemdb.inventorySlot;

            // this code essentially yoinked from LG_HSUActivator_Core
            LG_PickupItem pickupItem = LG_PickupItem.SpawnGenericPickupItem(Builder.s_root); // at 0,0,0
            pickupItem.SpawnNode = m_sourceNode;
            pickupItem.SetupCommon();

            switch (inventorySlot)
            {
                case InventorySlot.Consumable:
                case InventorySlot.ResourcePack:
                    if (itemToSpawn.Uses == 0)
                    {
                        Logger.Info("Tried to spawn Resource or Consumable with `Uses` set to 0");
                        break;
                    }
                    pickupItem.SetupAsConsumable(Builder.SessionSeedRandom.Range(0, int.MaxValue, "NO_TAG"), itemID);
                    var item = pickupItem.m_root.GetComponentInChildren<Item>(); // shouldn't ever be null, cause it's an item we're spawning
                    var itemData = item.Get_pItemData();
                    var uses = itemToSpawn.Uses;
                    if (inventorySlot == InventorySlot.ResourcePack)
                        uses *= 20;
                    itemData.custom.ammo = uses;
                    item.Set_pItemData(itemData);
                    break;
                case InventorySlot.ConsumableHeavy:
                case InventorySlot.InLevelCarry:
                    pickupItem.SetupBigPickupItemWithItemId(itemID, isWardenObjectiveItem: itemToSpawn.IsWardenObjective, objectiveChainIndex: itemToSpawn.ChainedObjectiveIndex);
                    break;
                case InventorySlot.InPocket:
                    pickupItem.SetupAsSmallGenericPickup(Builder.SessionSeedRandom.Range(0, int.MaxValue, "NO_TAG"), itemID, isWardenObjective: itemToSpawn.IsWardenObjective);
                    break;
                default:
                    Logger.Warn($"[Shuttlebox '{DebugName}'] Spawned Item isn't a slot I know what to do with, so can't run setup function.");
                    break;
            }
            
            itemtosummon = pickupItem.GetComponentInChildren<ItemInLevel>(true);
            m_shouldHideItemToSummon = !itemToSpawn.IsAvailableAtStart;

            ShouldDeregisterSummonItem = !itemToSpawn.ShowOnTerminalList;
        }

        DecalColor = data.Colors.SerialColor;
        SetupTerminalItem();

        TryChangeShuttleboxColor(data.Colors.MainColor, data.Colors.AccentColor);

        LightMesh = this.GetComponentInChildren<LG_LightEmitterMesh>(true);

        LightFader = gameObject.AddComponent<Shuttlebox_LightFader>();
        LightFader.LightMesh = LightMesh;

        m_shouldCloseAtStart = data.IsClosedAtStart;

        s_setupShuttleboxes.Add(this);

        var startingState = new pShuttleboxState();
        if (itemtosummon != null)
            startingState.summonItem = itemtosummon.Get_pItemData();

        Replicator = StateReplicator<pShuttleboxState>.Create((uint)s_setupShuttleboxes.Count, startingState, LifeTimeType.Session);
        Replicator.OnStateChanged += this.OnStateChange;

        Logger.DebugOnly($"Successfully set up shuttlebox '{DebugName}'");

    }

    public static string MainMeshPath = "Decorations/Shuttlebox_Mesh";
    public static string MainVisualMeshSubpath = "g_prop_machine_dumbwaitershute_01";
    public static string HatchVisualMeshSubpath = "prop_machine_dumbwaitershute_hatch/g_prop_machine_dumbwaitershute_hatch";
    private PaintableChannelMaskedTexture PaintedTexture = null;
    public void TryChangeShuttleboxColor(Color color1, Color color2)
    {

        var parentGO = this.transform.Find(MainMeshPath);
        var mainRenderer = parentGO.Find(MainVisualMeshSubpath).GetComponent<Renderer>();
        var hatchRenderer = parentGO.Find(HatchVisualMeshSubpath).GetComponent<Renderer>();
        var mat = mainRenderer.material;

        if (color1 == default && color2 == default)
        {
            mainRenderer.sharedMaterial = Assets.ShuttleboxSharedMaterial;
            hatchRenderer.sharedMaterial = Assets.ShuttleboxSharedMaterial;
            return;
        }

        if (this.PaintedTexture == null)
        {
            this.PaintedTexture = new PaintableChannelMaskedTexture(mat.mainTexture.TryCast<Texture2D>());
            this.PaintedTexture.SetMainTexture(Assets.ShuttleboxPaintableMainTex);
            this.PaintedTexture.SetMaskTexture(Assets.ShuttleboxPaintableMask);
        }
        this.PaintedTexture.SetTintColor(color1, color2);

        mat.mainTexture = this.PaintedTexture.CurrentTexture;

        mat = hatchRenderer.material;
        mat.mainTexture = this.PaintedTexture.CurrentTexture;
        PaintedTexture.CreateCopy();
    }

    private void TryLinkToShuttlebox()
    {
        if (ShuttleboxLinkID < 0) return;

        if (m_linkedShuttlebox != null) // idk how this could possibly happen but just in case ig
        { 
            Logger.Warn($"Shuttlebox '{DebugName}' already has a linked shuttlebox when trying to link?");
            return;
        }

        foreach (Shuttlebox_Core shuttlebox in s_setupShuttleboxes)
        {
            if (shuttlebox.ShuttleboxLinkID == ShuttleboxLinkID)
            {
                if (shuttlebox.m_linkedShuttlebox != null)
                {
                    Logger.Error($"Shuttlebox '{DebugName}' tried to link to shuttlebox '{shuttlebox.DebugName}', but that shuttlebox is already linked to one.");
                    return;
                }
                LinkToShuttlebox(shuttlebox);
                shuttlebox.LinkToShuttlebox(this);
                break;
            }
        }
    }

    private void SetupTerminalItem()
    {
        this.m_terminalItem = transform.FindChild(TerminalPingAlignName).GetComponent<LG_GenericTerminalItem>().Cast<iTerminalItem>();
        this.m_terminalItem.SpawnNode = this.m_sourceNode;
        
        string termKey = $"SHUTTLEBOX_{SerialGenerator.GetUniqueSerialNo()}";
        this.m_terminalItem.Setup(termKey);

        QueryableAPI.RegisterQueryableItem(termKey, GetQueryInfo);

        SetupTerminalIDDecal(termKey);
    }
    [HideFromIl2Cpp]
    private List<string> GetQueryInfo(List<string> defaultDetails)
    {
        List<string> queryInfo =
        [
             "----------------------------------------------------------------",
             "SHUTTLEBOX"
        ];

        if (!State.summonItem.Equals(default(pItemData)))
            queryInfo.Add($"STORED ITEM: {TryGetTerminalNameFromItemInLevel(ItemToSummon)}");

        if (m_linkedShuttlebox != null)
            queryInfo.Add($"LINKED SHUTTLEBOX: {m_linkedShuttlebox.m_terminalItem.TerminalItemKey}");
             
        queryInfo.AddRange([
            "----------------------------------------------------------------",
            defaultDetails[1], // ID: SHUTTLEBOX_XXX
            $"STATUS: {(State.terminalItemStatus != eFloorInventoryObjectStatus.Deactivated ? "ACTIVE" : "INACTIVE")}",
            defaultDetails[3], // LOCATION: ZONE_XXX
            defaultDetails[4]  // PING STATUS: XXX
        ]);
        return queryInfo;
    }

    public Color DecalColor = default;
    private void SetupTerminalIDDecal(string termKey)
    {
        var go = this.m_serialAlign.gameObject;
        var tmp = go.GetComponent<TextMeshPro>();
        if (tmp == null)
            tmp = go.AddComponent<TextMeshPro>();

        tmp.horizontalAlignment = HorizontalAlignmentOptions.Center;
        tmp.autoSizeTextContainer = true;
        tmp.font = Assets.OxaniumFont;
        tmp.fontSharedMaterial = Assets.OxaniumFontMaterial;
        tmp.color = DecalColor;
        tmp.SetText(termKey);
    }

    private static string TryGetTerminalNameFromItemInLevel(ItemInLevel item)
    {
        if (item == null) return "NONE";

        var comp = item.GetComponentInChildren<iTerminalItem>();
        if (comp == null) return item.ItemDataBlock.publicName;

        return comp.TerminalItemKey;
    }

    [HideFromIl2Cpp]
    private void LinkToShuttlebox(Shuttlebox_Core shuttlebox)
    {
        m_linkedShuttlebox = shuttlebox;
    }

    [HideFromIl2Cpp]
    private void SetupItemDict(List<ShuttleboxItemsData> data)
    {
        if (ItemToEventsOnEnterDict == null) ItemToEventsOnEnterDict = new();

        foreach (var item in data)
        {
            ItemToEventsOnEnterDict.Add(item.ItemID, item);
        }
    }
    // Set up the key part if allowed
    private void FinishSetupItemDict()
    {
        if (ItemToEventsOnEnterDict.TryGetValue(0U, out var item))
        {
            foreach (uint keyID in KeyItemTracker.KeyIdsInLevel)
            {
                ShuttleboxItemsData newData = new()
                {
                    ItemID = keyID,
                    ActionOnInsert = item.ActionOnInsert,
                    Events = item.Events
                }; // Probably unnecessary cause i'm pretty sure ItemID isn't used after setup
                // but, just in case

                if (!ItemToEventsOnEnterDict.TryAdd(keyID, newData))
                {
                    Logger.Error($"[Shuttlebox '{DebugName}'] Tried to register a key item (id {keyID}) for shuttlebox actions, but the item id was already registered. Why manually register what's supposed to be a keycard item?");
                }
            }
        }

        ItemToEventsOnEnterDict.Remove(0U);
    }

    public void SetupOnAnimationEnds()
    {
        m_animTrigger = transform.FindChild(OpenCloseName).GetComponent<LG_WorldEventAnimationTrigger>();

        var closing_sequencer = m_animTrigger.m_animationsOnTrigger[0];
        closing_sequencer.OnSequenceDone += (Action)OnCloseSequenceEnded;

        var opening_sequencer = m_animTrigger.m_animationsOnReset[0];
        opening_sequencer.OnSequenceDone += (Action)OnOpenSequenceEnded;

        // Summon anim
        this.m_summonTrigger = transform.FindChild(SummonEventTargetName).GetComponent<LG_WorldEventAnimationTrigger>();

        m_summonTrigger.m_animationsOnTrigger[0].OnSequenceDone += (Action)OnSummonSequenceTriggered;
        m_summonTrigger.gameObject.name = $"{m_summonTrigger.gameObject.name}_{DebugName}";

        // ForceOpen anim
        this.m_forceOpenTrigger = transform.FindChild(ForceOpenEventTargetName).GetComponent<LG_WorldEventAnimationTrigger>();
        m_forceOpenTrigger.m_animationsOnTrigger[0].OnSequenceDone += (Action)OnOpenSequenceTriggered;
        m_forceOpenTrigger.gameObject.name = $"{m_forceOpenTrigger.gameObject.name}_{DebugName}";

        // ForceClose anim
        this.m_forceCloseTrigger = transform.FindChild(ForceCloseEventTargetName).GetComponent<LG_WorldEventAnimationTrigger>();
        m_forceCloseTrigger.m_animationsOnTrigger[0].OnSequenceDone += (Action)OnCloseSequenceTriggered;
        m_forceCloseTrigger.gameObject.name = $"{m_forceCloseTrigger.gameObject.name}_{DebugName}";

    }

    #region Static methods for event apis
    public static void OnLevelCleanup()
    {
        // Clear setup shuttleboxes
        if (s_setupShuttleboxes == null)
            s_setupShuttleboxes = new();
        s_setupShuttleboxes.Clear();

        PingOverrideItemKeys.Clear();
    }
    public static void FinishSetupSummonItems()
    {
        if (IsMaster)
            return;
        foreach (var box in s_setupShuttleboxes)
        {
            var item = box.ItemToSummon;
            if (item != null) {
                if (box.m_shouldHideItemToSummon)
                    box.HideItemAtBottomOfElevator(item, box.ShouldDeregisterSummonItem);
                else
                {
                    box.AttemptInteract(eShuttleboxInteractionType.Place, item: item);
                    
                    var state = box.State;
                    state.summonItem = default;
                    box.Replicator.SetStateUnsynced(state);
                }
            }
        }
    }

    public static void ActivateLights()
    {
        foreach (var shuttlebox in s_setupShuttleboxes)
        {
            shuttlebox.SetOnlyLightState(eShuttleboxLightState.Ready);
        }
    }
    public static void CloseShuttleboxesAtStart()
    {
        if (!IsMaster) return;

        foreach (var box in s_setupShuttleboxes)
            if (box.m_shouldCloseAtStart)
                box.AttemptInteract(eShuttleboxInteractionType.Close);
    }
    public static void FinishSetupAllItemDicts()
    {
        foreach (var box in s_setupShuttleboxes)
            box.FinishSetupItemDict();
    }
    #endregion

    // these are just the names in the prefab, used for getting the gameobjects and their components
    private const string BigPickupAlignName = "Shuttlebox_ItemAlign";
    private const string TMPAlignName = "TMP Align";
    private const string TerminalPingAlignName = "Shuttlebox_TerminalPingAlign";
    private const string InteractObjName = "Shuttlebox_LG_GenericCarry";
    private const string OpenCloseName = "WorldEvents/EVT_ShuttleboxPlugin_OpenClose";
    public const string SummonEventTargetName = "WorldEvents/EVT_ShuttleboxPlugin_SetPickupAlign";
    public const string ForceCloseEventTargetName = "WorldEvents/EVT_ShuttleboxPlugin_ForceClose";
    public const string ForceOpenEventTargetName = "WorldEvents/EVT_ShuttleboxPlugin_ForceOpen";



}
