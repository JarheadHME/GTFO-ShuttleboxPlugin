using LevelGeneration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ShuttleboxPlugin.Modules;
public class Shuttlebox_LightFader : MonoBehaviour
{
    public LG_LightEmitterMesh LightMesh;
    private Color PreviousColor = Color.white;
    private Color m_targetColor = Color.white;
    public Color TargetColor {
        get => m_targetColor;
        set
        {
            this.enabled = true;
            m_changeTime = Clock.Time;
            PreviousColor = LightMesh.m_colorCurrent;
            m_targetColor = value;
        } 
    }
    private float m_changeTime = 0f;
    public float ShiftTime = 0.10f;

    public void Update()
    {
        if (!LightMesh.m_isEnabled) LightMesh.SetEnabled(true);

        float lerp = (Clock.Time - m_changeTime) / ShiftTime;
        LightMesh.SetColor(Color.Lerp(PreviousColor, TargetColor, lerp));

        this.enabled = lerp <= 1.0f; // turn this off when it's at the target color
    }
}