using GTFO.API;
using TMPro;
using UnityEngine;
using ShuttleboxPlugin.Modules;
using TexturePainterAPI.PaintableTextures;

namespace ShuttleboxPlugin
{
    public static class Assets
    {
        public static readonly string ShuttleboxPrefabPath = "Assets/GameObject/ShuttleboxPlugin.prefab";
        public static readonly GameObject ShuttleboxPrefab = null;

        public static readonly Material ShuttleboxSharedMaterial = null;
        public static readonly string ShuttleboxPaintableMainTexPath = "Assets/Texture2D/Shuttlebox_Base.png";
        public static readonly Texture2D ShuttleboxPaintableMainTex = null;
        public static readonly string ShuttleboxPaintableMaskPath = "Assets/Texture2D/Shuttlebox_Mask.png";
        public static readonly Texture2D ShuttleboxPaintableMask = null;
        public static readonly PaintableChannelMaskedTexture ShuttleboxPaintableTexture = null;

        public static readonly string TerminalFloorPath = "ASSETS/ASSETPREFABS/COMPLEX/GENERIC/FUNCTIONMARKERS/TERMINAL_FLOOR.PREFAB";
        public static readonly TMP_FontAsset OxaniumFont = null;
        public static readonly Material OxaniumFontMaterial = null;

        static Assets()
        {
            ShuttleboxPrefab = AssetAPI.GetLoadedAsset<GameObject>(ShuttleboxPrefabPath);
            ShuttleboxSharedMaterial = ShuttleboxPrefab.transform
                .Find(Shuttlebox_Core.MainMeshPath)
                .Find(Shuttlebox_Core.MainVisualMeshSubpath)
                .GetComponent<Renderer>()
                .sharedMaterial;

            ShuttleboxPaintableMainTex = AssetAPI.GetLoadedAsset<Texture2D>(ShuttleboxPaintableMainTexPath);
            ShuttleboxPaintableMask = AssetAPI.GetLoadedAsset<Texture2D>(ShuttleboxPaintableMaskPath);

            ShuttleboxPaintableTexture = new PaintableChannelMaskedTexture(ShuttleboxPaintableMainTex);
            ShuttleboxPaintableTexture.SetMaskTexture(ShuttleboxPaintableMask);

            var term = AssetAPI.GetLoadedAsset<GameObject>(TerminalFloorPath);
            var tmp = term.transform.Find("Serial").GetComponent<TextMeshPro>();
            OxaniumFont = tmp.font;
            OxaniumFontMaterial = tmp.fontSharedMaterial;
        }
    }
}
