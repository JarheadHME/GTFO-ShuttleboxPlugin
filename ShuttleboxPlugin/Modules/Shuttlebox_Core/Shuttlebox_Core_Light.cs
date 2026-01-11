using AmorLib.Networking.StateReplicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ShuttleboxPlugin.Modules;
public partial class Shuttlebox_Core : MonoBehaviour, IStateReplicatorHolder<pShuttleboxState>
{
    public Dictionary<eShuttleboxLightState, Color> LightStateToColor = new()
    {
        { eShuttleboxLightState.ShutOff, Color.black },
        { eShuttleboxLightState.Ready, new Color(0.0f, 0.32f, 0.32f) },
        { eShuttleboxLightState.Working, new Color(0.08f, 0.30f, 0.08f) },
        { eShuttleboxLightState.Queued, new Color(0.36f, 0.32f, 0.12f) },
        { eShuttleboxLightState.InvalidItem, new Color(0.36f, 0.09f, 0.09f) }
    };

    public void SetOnlyLightState(eShuttleboxLightState lightState, pShuttleboxState state)
        => SetOnlyLightState(LightStateToColor[lightState], state);

    public void SetOnlyLightState(eShuttleboxLightState lightState)
        => SetOnlyLightState(lightState, Replicator.State);
    public void SetOnlyLightState(Color color, pShuttleboxState state)
    {
        if (!IsMaster) return;

        //state.type = eShuttleboxInteractionType.None;
        state.lightColor = color;

        Replicator.SetState(state);
    }
    public void SetOnlyLightState(Color color) => SetOnlyLightState(color, State);

}

public enum eShuttleboxLightState
{
    ShutOff,
    Ready,
    Working,
    Queued,
    InvalidItem,
}
