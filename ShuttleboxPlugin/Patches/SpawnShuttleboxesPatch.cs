using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using GameData;
using LevelGeneration;
using XXHashing;
using Modules;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using ShuttleboxPlugin.Modules;
using GTFO.API;
using ShuttleboxPlugin.Utils;
using FluffyUnderware.DevTools.Extensions;

namespace ShuttleboxPlugin.Patches
{
    [HarmonyPatch]
    internal class SpawnShuttleboxesPatch
    {
        private static ExpeditionFunction TargetFunction = ExpeditionFunction.DisinfectionStation;

        // for some reason, `Build` seems to get called twice even when returning true
        // so this list is to make sure our code only runs once per zone
        public static List<int> DistributedZones = new List<int>();
        public static void ClearDistributedZones() => DistributedZones.Clear();

        [HarmonyPatch(typeof(LG_PopulateFunctionMarkersInZoneJob), nameof(LG_PopulateFunctionMarkersInZoneJob.Build))]
        [HarmonyPostfix]
        public static void PlaceShuttleboxes(LG_PopulateFunctionMarkersInZoneJob __instance, bool __result)
        {
            if (!__result) return; // gotta wait till the whole distribution process for the zone is done

            var zone = __instance.m_zone;
            // This patch still seems to run twice, so just make sure it won't run again in the same zone
            if (DistributedZones.Contains(zone.GetInstanceID())) return;

            uint mainLevelLayoutID = Builder.LevelGenExpedition.LevelLayoutData;
            List<ShuttleboxPlacementData> ShuttleboxesInLevel;
            if (!ShuttleboxPlacements.ShuttleboxesToPlace.TryGetValue(mainLevelLayoutID, out ShuttleboxesInLevel))
                return;

            var dimIndex = zone.DimensionIndex;
            var layerType = zone.m_layer.m_type;
            var zoneIndex = zone.LocalIndex;

            GlobalZoneIndex currZoneIndex = new GlobalZoneIndex(dimIndex, layerType, zoneIndex);
            List<ShuttleboxPlacementData> shuttleboxesToPlace = new List<ShuttleboxPlacementData>();
            foreach (var shuttleboxData in ShuttleboxesInLevel)
            {
                if (shuttleboxData.GlobalZoneIndex.Equals(currZoneIndex))
                    shuttleboxesToPlace.Add(shuttleboxData);
            }

            try
            {
                for (int i = 0; i < shuttleboxesToPlace.Count; i++)
                {
                    var shuttleboxData = shuttleboxesToPlace[i];
                    var placement = shuttleboxData.ZonePlacement;

                    GameObject spawnedShuttlebox = null;
                    LG_Area shuttleboxArea = null;

                    if (!shuttleboxData.AbsolutePosition) // don't use abs pos, so use markers
                    {
                        var rng = new XXHashSeed(__instance.m_rnd.Seed.SubSeed((uint)i, (uint)placement.AreaSeedOffset));

                        //var node = LG_DistributionJobUtils.GetRandomNodeFromZoneForFunction(zone, ExpeditionFunction.PowerGenerator, rng.Float(1U));
                        if (!LG_DistributionJobUtils.TryGetWeightedNodeFromZone(zone, rng.Float(1U), placement.PlacementWeights, out var node))
                        {
                            Logger.Error($"Didn't get any course node for shuttlebox {shuttleboxData.DebugName}");
                            continue;
                        }
                        if (node != null)
                        {
                            // get a new rng seed with the marker cause yayyyy
                            rng = new(rng.SubSeed((uint)placement.MarkerSeedOffset));
                            shuttleboxArea = node.m_area;
                            //var marker = shuttleboxArea.GetAndConsumeRandomMarkerSpawner(TargetFunction, rng.Float(1U));

                            bool funcspawned = false;
                            var listofmarkerprefabs = new Il2CppSystem.Collections.Generic.List<MarkerComposition>();
                            listofmarkerprefabs.Add(new MarkerComposition() { prefab = Assets.ShuttleboxPrefabPath });


                            // Trying to make a functionbuilder
                            LG_FunctionMarkerBuilder builder = new LG_FunctionMarkerBuilder(
                                node: node,
                                function: TargetFunction, // trying dis cause they're a bit bigger?
                                isWardenObjective: false,
                                wardenObjectiveChainIndex: 0
                            );

                            LG_DistributeItem distItem = new LG_DistributeItem(TargetFunction, 1, node);
                            distItem.m_markerSeedOffset = placement.MarkerSeedOffset;
                            __instance.TriggerFunctionBuilder(builder, distItem, out var marker);

                            marker.m_spawnedGO.Destroy();
                                
                            spawnedShuttlebox = marker.PickAndSpawnRandomPrefab(
                                listofmarkerprefabs,
                                1f, // totalweight
                                0f, // random value (lol)
                                marker.m_producerSource.transform,
                                TargetFunction,
                                ref funcspawned,
                                out string debugInfo
                            );
                        }
                        else
                        {
                            Logger.Error($"Didn't find m_sourceNode for Shuttlebox '{shuttleboxData.DebugName}' in zone {currZoneIndex}");
                            continue;
                        }
                    }
                    else // this is for using absolute position
                    {
                        var areaIndex = shuttleboxData.ZonePlacement.AreaSeedOffset;
                        if (areaIndex >= zone.m_areas.Count)
                        {
                            Logger.Error($"Chosen area for shuttlebox {shuttleboxData.DebugName} (Area {LG_Area.m_areaChars[areaIndex % LG_Area.m_areaChars.Length].ToUpper()} (AreaSeedOffset is value {areaIndex})) is not in zone {currZoneIndex}");
                            continue;
                        }
                        shuttleboxArea = zone.m_areas[shuttleboxData.ZonePlacement.AreaSeedOffset];
                        spawnedShuttlebox = GameObject.Instantiate(Assets.ShuttleboxPrefab, shuttleboxArea.transform);
                    }

                    if (spawnedShuttlebox != null)
                    {
                        Logger.DebugOnly($"Successfully spawned shuttlebox '{shuttleboxData.DebugName}'");

                        if (shuttleboxData.AbsolutePosition)
                        {
                            spawnedShuttlebox.transform.SetPositionAndRotation(shuttleboxData.Position, Quaternion.Euler(shuttleboxData.Rotation));
                        }
                        else
                        {
                            // these should default to the equivalent of zero (obviously) and identity
                            spawnedShuttlebox.transform.localPosition = shuttleboxData.Position;
                            spawnedShuttlebox.transform.localRotation = Quaternion.Euler(shuttleboxData.Rotation);
                        }

                        SetupShuttlebox(spawnedShuttlebox, shuttleboxArea, shuttleboxData);
                    }
                    else
                    {
                        Logger.Error("Spawned shuttlebox was null???");
                        continue;
                    }
                }
            }
            // Catching all errors because otherwise it just spams spawning shuttleboxes across the entire zone
            catch (Exception e)
            {
                Logger.Error($"Exception occured while adding shuttleboxes to zone {currZoneIndex}:\n{e}");
            }

            DistributedZones.Add(zone.GetInstanceID());
        }

        public static Dictionary<string, string> DecorationLookup = new()
        {
            { "StraightShort",   "Shuttlebox_Variant_01_Straight_Short" },
            { "TurnShort",       "Shuttlebox_Variant_02_Turn_Short" },
            { "StraightLong",    "Shuttlebox_Variant_03_Straight_Long" },
            { "TurnLong",        "Shuttlebox_Variant_04_Turn_Long" },
            { "DoubleTurnRight", "Shuttlebox_Variant_05_DoubleTurn_Right" },
            { "DoubleTurnLeft",  "Shuttlebox_Variant_06_DoubleTurn_Left" },
            { "Backward",        "Shuttlebox_Variant_07_Backward" },
            { "Angled",          "Shuttlebox_Variant_08_Angled" },
            { "Base",            "Shuttlebox_Variant_09_Base" },
        };

        public static string DecorationRootPath = "Decorations/Shuttlebox_Decor_Variants";

        public static void SetupShuttlebox(GameObject shuttlebox, LG_Area area, ShuttleboxPlacementData shuttleboxData)
        {
            // Set up the selected decoration objects
            var transform = shuttlebox.transform;
            var decorationRoot = transform.Find(DecorationRootPath);
            foreach (var kvp in shuttleboxData.Decorations)
            {
                if (!kvp.Value) continue;
                if (DecorationLookup.TryGetValue(kvp.Key, out var decorationName))
                {
                    decorationRoot.Find(decorationName).gameObject.SetActive(true);
                }
                else
                {
                    Logger.Error($"Undefined decoration type: {kvp.Key}");
                }
            }

            var comp = shuttlebox.AddComponent<Shuttlebox_Core>();
            comp.Setup(area, shuttleboxData);

        }
    }
    
}
