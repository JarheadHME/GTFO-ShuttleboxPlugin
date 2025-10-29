using Il2CppSystem.Runtime.Remoting.Messaging;
using LevelGeneration;
using SNetwork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ShuttleboxPlugin.Modules;
public partial class Shuttlebox_Core : MonoBehaviour
{
    public delegate bool IsInValidStateForInteractDelegate(Shuttlebox_Core shuttlebox, eShuttleboxInteractionType type);
    public Dictionary<eShuttleboxInteractionType, bool> QueueStates = new()
    {
        { eShuttleboxInteractionType.SwapIn, false },
        { eShuttleboxInteractionType.Transfer, false },
        { eShuttleboxInteractionType.Summon, false },
    };
    public static Dictionary<eShuttleboxInteractionType, IsInValidStateForInteractDelegate> ValidStateForInteract = new()
    {
        { eShuttleboxInteractionType.SwapIn, IsValidForSwap },
        { eShuttleboxInteractionType.Transfer, IsValidForTransfer },
        { eShuttleboxInteractionType.Summon, IsValidForSummon },
    };

    public Item QueuedItem = null;

    public void Awake()
    {
        this.enabled = false;
    }

    public void Start()
    {
        if (!IsMaster) 
            this.enabled = false;
    }

    public void Update()
    {
        foreach (var interactType in QueueStates.Keys)
        {
            var state = QueueStates[interactType];
            if (!state) continue;
                
            if  (ValidStateForInteract.TryGetValue(interactType, out var func))
            {
                if (func.Invoke(this, interactType)) AttemptInteract(interactType, item: QueuedItem);
                return;
            }
            else
            {
                Logger.Error($"[Shuttlebox '{DebugName}'] Has action {interactType} queued but no delegate to check if in valid state!");
                this.DeQueueAction(interactType);
            }
        }

        // if get here, then no interacts are queued, so yeet
        this.enabled = false;
    }

    #region Queue/Dequeue
    public void QueueAction(eShuttleboxInteractionType type)
    {
        if (this.QueueStates.ContainsKey(type))
            this.QueueStates[type] = true;
        else
            this.QueueStates.Add(type, true);
        this.enabled = true;
    }

    public void DeQueueAction(eShuttleboxInteractionType type)
    {
        if (this.QueueStates.ContainsKey(type))
            this.QueueStates[type] = false;
    }

    public bool IsQueued(eShuttleboxInteractionType type) => this.QueueStates.TryGetValue(type, out bool result) && result;
    public bool AnyQueued() => this.QueueStates.Values.Any(k => k);
    public bool IsOnlyQueued(eShuttleboxInteractionType type)
    {
        foreach (var kvp in this.QueueStates)
        {
            var key = kvp.Key;
            var value = kvp.Value;
            if (key == type && !value) return false;
       else if (key != type &&  value) return false;
        }
        return true;
    }
    #endregion


    public static bool IsValidForSwap(Shuttlebox_Core shuttlebox, eShuttleboxInteractionType type)
    {
        return shuttlebox.IsOpen()
            && !shuttlebox.HasItem;
    }
    public static bool IsValidForTransfer(Shuttlebox_Core shuttlebox, eShuttleboxInteractionType type)
    {
        var linked_box = shuttlebox.m_linkedShuttlebox;
        if (linked_box == null)
        {
            Logger.Error($"[Shuttlebox '{shuttlebox.DebugName}'] Somehow had transfer queued when no linked shuttlebox???");
            shuttlebox.DeQueueAction(eShuttleboxInteractionType.Transfer);
            return false;
        }

        return shuttlebox.HasItem
            && shuttlebox.IsOpen()
            && !linked_box.HasItem
            && linked_box.IsOpen()
            && !linked_box.IsQueued(eShuttleboxInteractionType.SwapIn);
    }
    public static bool IsValidForSummon(Shuttlebox_Core shuttlebox, eShuttleboxInteractionType type)
    {
        bool shouldReceiveTransfer = shuttlebox.m_linkedShuttlebox?.IsQueued(eShuttleboxInteractionType.Transfer) ?? false;
        return !shuttlebox.HasItem
            && shuttlebox.IsOpen()
            && !shouldReceiveTransfer;
    }
}