using GameData;
using LevelGeneration;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using GTFO.API.Utilities;
using MTFO.API;
using System.Text.Json;
using ShuttleboxPlugin.Utils;

namespace ShuttleboxPlugin.Modules;

public class ShuttleboxDTO
{
    public uint MainLevelLayoutID { get; set; }
    public List<ShuttleboxPlacementData> Shuttleboxes { get; set; }
}

public class ShuttleboxPlacementData
{
    public string DebugName { get; set; } = "Unnamed";
    
    // yoinked to get areaseedoffset and markerseedoffset + weights easily
    public DumbwaiterPlacementData ZonePlacement { get; set; } = new();
    // ZonePlacement.AreaSeedOffset is gonna be yoinked to be the area index when doing absolute positioning

    // This stuff pretty much copied from what amor did with DoubleSidedDoors
    public eDimensionIndex DimensionIndex { get; set; } = eDimensionIndex.Reality;
    public LG_LayerType Layer { get; set; } = LG_LayerType.MainLayer;
    public eLocalZoneIndex LocalIndex { get; set; } = eLocalZoneIndex.Zone_0;

    public bool AbsolutePosition { get; set; } = false;
    public Vector3 Position { get; set; } = Vector3.zero;
    public Vector3 Rotation { get; set; } = Vector3.zero;

    public Dictionary<string, bool> Decorations { get; set; } = new();
    public ShuttleboxColorData Colors { get; set; } = new()
    {
        MainColor = default,
        AccentColor = default,
        SerialColor = Color.white
    };

    public int LinkID { get; set; } = -1;

    public bool IsClosedAtStart { get; set; } = false;

    public List<ShuttleboxItemsData> ValidInsertItems { get; set; } = new();

    public ShuttleboxSpawnedItemData SpawnedItem { get; set; } = new();


    public GlobalZoneIndex GlobalZoneIndex
    {
        get
        {
            return new(DimensionIndex, Layer, LocalIndex);
        }
    }

}

public class ShuttleboxItemsData
{
    public uint ItemID { get; set; } = 0; // When 0, will be used to get all keys
    public eShuttleboxAction ActionOnInsert { get; set; } = eShuttleboxAction.None;
    public List<WardenObjectiveEventData> Events { get; set; } = new();
}

public class ShuttleboxSpawnedItemData
{
    public uint ItemID { get; set; } = 0;
    public bool IsAvailableAtStart { get; set; } = false;
    public bool IsWardenObjective { get; set; } = false;
    public int ChainedObjectiveIndex { get; set; } = 0;
    public bool ShowOnTerminalList { get; set; } = false;
    public uint Uses { get; set; } = 0;
}

public class ShuttleboxColorData
{
    public Color MainColor { get; set; } = default;
    public Color AccentColor { get; set; } = default;
    //public Color MainColor { get; set; } = new(0.85f, 0.42f, 0f, 0.75f);
    //public Color AccentColor { get; set; } = new(0.8f, 0.7f, 0.65f, 0.8f);
    public Color SerialColor { get; set; } = Color.white;
}

public static class ShuttleboxPlacements
{
    public static Dictionary<uint, List<ShuttleboxPlacementData>> ShuttleboxesToPlace = new();

    public const string CustomFolderName = "ShuttleboxData";
    public static string PlacementsPath = string.Empty;

    public static bool HasSetUpLiveEdit = false;

    static ShuttleboxPlacements()
    {
        JsonSettings = AmorLib.Utils.JsonSerializerUtil.CreateDefaultSettings(useLocalizedText: true, usePartialData: true, useInjectLib: true);
        JsonSettings.Converters.Add(new UnityColorHexConverter());

        JsonSettings.ReadCommentHandling = JsonCommentHandling.Skip;
        JsonSettings.PropertyNameCaseInsensitive = true;
        JsonSettings.AllowTrailingCommas = true;
    }

    public static void Init()
    {
        if (!MTFOPathAPI.HasCustomPath)
        {
            Logger.Warn("No custom path, not trying to load shuttlebox data");
            return;
        }

        PlacementsPath = Path.Join(MTFOPathAPI.CustomPath, CustomFolderName);

        Logger.Info($"Initializing path '{PlacementsPath}'");

        if (!Directory.Exists(PlacementsPath))
        {
            Directory.CreateDirectory(PlacementsPath);
            CreateTemplate(PlacementsPath);
        }
            

        ClearDict();

        var files = Directory.GetFiles(PlacementsPath, "*.json");
        foreach (var file in files)
        {
            string text = File.ReadAllText(file);
            LoadShuttleboxes(text);
        }

        if (!HasSetUpLiveEdit)
        {
            var LiveEditListener = GTFO.API.Utilities.LiveEdit.CreateListener(PlacementsPath, "*.json", true);
            LiveEditListener.FileChangedEventCooldown = 1.5f;
            LiveEditListener.FileChanged += OnLiveEditUpdate;
            HasSetUpLiveEdit = true;
        }
    }

    public static void CreateTemplate(string path)
    {
        string templatePath = Path.Combine(path, "template.json");
        if (!File.Exists(templatePath))
        {
            File.WriteAllText(templatePath, TemplateText);
        }
    }

    // just to properly ensure things get cleared nicely. almost certainly unnecessary
    public static void ClearDict()
    {
        foreach (var list in ShuttleboxesToPlace.Values) 
            list.Clear();
        ShuttleboxesToPlace.Clear();
    }

    public static void OnLiveEditUpdate(LiveEditEventArgs e)
    {
        Logger.Info("LiveEdit File Changed");
        LiveEditInit();
    }

    public static void LiveEditInit()
    {
        ClearDict();
        var files = Directory.GetFiles(PlacementsPath, "*.json");
        foreach (var file in files)
        {
            LiveEdit.TryReadFileContent(file, LoadShuttleboxes);
        }
    }

    public static readonly JsonSerializerOptions JsonSettings = null;
    public static void LoadShuttleboxes(string json)
    {
        List<ShuttleboxDTO> data = JsonSerializer.Deserialize<List<ShuttleboxDTO>>(json, JsonSettings);

        foreach (var item in data)
        {
            List<ShuttleboxPlacementData> list;
            if (!ShuttleboxesToPlace.TryGetValue(item.MainLevelLayoutID, out list))
            {
                list = new List<ShuttleboxPlacementData>();
                ShuttleboxesToPlace.Add(item.MainLevelLayoutID, list);
            }

            list.AddRange(item.Shuttleboxes);

            // Try update box colors if they're in level
            if (GameStateManager.IsInExpedition && RundownManager.Current.m_activeExpedition.LevelLayoutData == item.MainLevelLayoutID)
                DoInLevelLiveEdit(item.Shuttleboxes);
                    
        }
    }

    public static void DoInLevelLiveEdit(List<ShuttleboxPlacementData> shuttleboxes)
    {
        foreach (var boxData in shuttleboxes)
        {
            foreach (var shuttlebox in Shuttlebox_Core.s_setupShuttleboxes)
                if (shuttlebox.DebugName.Equals(boxData.DebugName))
                {
                    shuttlebox.TryChangeShuttleboxColor(boxData.Colors.MainColor, boxData.Colors.AccentColor);

                    if (boxData.AbsolutePosition)
                    {
                        shuttlebox.transform.position = boxData.Position;
                        shuttlebox.transform.rotation = Quaternion.Euler(boxData.Rotation);
                    }
                    else // marker, so local
                    {
                        shuttlebox.transform.localPosition = boxData.Position;
                        shuttlebox.transform.localRotation = Quaternion.Euler(boxData.Rotation);
                    }
                    break;
                }
        }       
    }

    #region template raw text
    public static string TemplateText = "[\n  {\n    \"MainLevelLayoutID\": 0, // LevelLayoutDatablock ID\n    \"Shuttleboxes\": [\n      {\n        \"DebugName\": \"NameOfShuttlebox\",\n\n        \"ZonePlacement\": { // DumbwaiterPlacementData\n          \"PlacementWeights\": { // ZonePlacementWeights\n            \"Start\": 0.0,\n            \"Middle\": 0.0,\n            \"End\": 0.0\n          },\n          \"AreaSeedOffset\": 0,\n          \"MarkerSeedOffset\": 0\n        },\n\n        \"DimensionIndex\": \"Reality\", // eDimensionIndex\n        \"Layer\": \"MainLayer\", // LG_LayerType\n        \"LocalIndex\": 0, // eLocalZoneIndex\n\n        \"AbsolutePosition\": false,\n        \"Position\": { // Vector3\n          \"x\": 0.0,\n          \"y\": 0.0,\n          \"z\": 0.0\n        },\n        \"Rotation\": { // Vector3\n          \"x\": 0.0,\n          \"y\": 0.0,\n          \"z\": 0.0\n        },\n\n        \"Decorations\": { // Dictionary\n          \"StraightShort\": false,\n          \"TurnShort\": false,\n          \"StraightLong\": false,\n          \"TurnLong\": false,\n          \"DoubleTurnRight\": false,\n          \"DoubleTurnLeft\": false,\n          \"Backward\": false,\n          \"Angled\": false,\n          \"Base\": false\n        },\n        \"Colors\": {\n          \"MainColor\": { // Color\n            \"r\": 0.5,\n            \"g\": 1.0,\n            \"b\": 0.0,\n            \"a\": 0.8\n          },\n          \"AccentColor\": \"#FFFF\", // Color\n          \"SerialColor\": \"#FFFFFF\" // Color\n        },\n\n        \"LinkID\": -1,\n        \"IsClosedAtStart\": false,\n\n        \"ValidInsertItems\": [\n          {\n            \"ItemID\": 0, // ItemDataBlock ID\n            \"ActionOnInsert\": \"None\", // enum - `0 = \"None\", 1 = \"Transfer\", 2 = \"Consume\", 3 = \"ConsumeAndRemainClosed\"`\n            \"Events\": [ // \n              {\n                \"Type\": 0,\n                \"Trigger\": \"OnStart\" // What fill this with is important, see \"Event Triggers\" section\n              }\n            ]\n          }\n        ],\n\n        \"SpawnedItem\": {\n          \"ItemID\": 0, // ItemDataBlock ID\n          \"IsAvailableAtStart\": false,\n          \"IsWardenObject\": false,\n          \"ChainedObjectiveIndex\": 0,\n          \"ShowOnTerminalList\": false,\n          \"Uses\": 0\n        }\n      }\n    ]\n  }\n]";
    #endregion
}