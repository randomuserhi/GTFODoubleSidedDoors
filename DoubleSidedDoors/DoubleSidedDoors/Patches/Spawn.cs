using AIGraph;
using API;
using ChainedPuzzles;
using GameData;
using HarmonyLib;
using LevelGeneration;
using Player;
using System.Text.RegularExpressions;
using UnityEngine;
using static SurvivalWave;

namespace DoubleSidedDoors {
    public sealed class LayoutConfig {
        public uint LevelLayoutID { get; set; }
        public DoorIdentifier[] Doors { get; set; } = Array.Empty<DoorIdentifier>();
        public DoorIdentifier[] AddHandle { get; set; } = Array.Empty<DoorIdentifier>();
        public BindDoorToPlayer[] BindDoorToPlayer { get; set; } = Array.Empty<BindDoorToPlayer>();
        public DoorIdentifier[] DoorLockedGraphicOverrides { get; set; } = Array.Empty<DoorIdentifier>();
        public DoorMessageOverride[] DoorMessageOverrides { get; set; } = Array.Empty<DoorMessageOverride>();
        public DoorTriggerOverride[] DoorOverrideTrigger { get; set; } = Array.Empty<DoorTriggerOverride>();
    }

    public sealed class BindDoorToPlayer {
        public int Slot { get; set; }
        public DoorIdentifier? Door { get; set; }
    }

    public sealed class DoorIdentifier {
        public int To { get; set; }
        public int From { get; set; } = -1;
        public string Text { get; set; } = "<color=red>BI-DIRECTIONAL ACCESS DISABLED</color>";
        public string State { get; set; } = "";
    }

    public sealed class DoorMessageOverride {
        public DoorIdentifier? Door { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class DoorTriggerOverride {
        public DoorIdentifier? Door { get; set; }
        public DoorIdentifier? Target { get; set; }

        public bool OpenOnTarget { get; set; } = false;
    }
}

namespace DoubleSidedDoors.Patches {
    [HarmonyPatch]
    internal static class Spawn {
        private static bool IsTargetReachable(AIG_CourseNode source, AIG_CourseNode target) {
            if (source == null || target == null) return false;
            if (source.NodeID == target.NodeID) return true;

            AIG_SearchID.IncrementSearchID();
            ushort searchID = AIG_SearchID.SearchID;
            Queue<AIG_CourseNode> queue = new Queue<AIG_CourseNode>();
            queue.Enqueue(source);

            while (queue.Count > 0) {
                AIG_CourseNode current = queue.Dequeue();
                current.m_searchID = searchID;
                foreach (AIG_CoursePortal portal in current.m_portals) {
                    LG_SecurityDoor? secDoor = portal.Gate?.SpawnedDoor?.TryCast<LG_SecurityDoor>();
                    if (secDoor != null) {
                        APILogger.Debug($"SecurityDoor {secDoor.m_serialNumber} - {secDoor.LastStatus.ToString()}");
                        if (secDoor.LastStatus != eDoorStatus.Open && secDoor.LastStatus != eDoorStatus.Opening)
                            continue;
                    }
                    AIG_CourseNode nextNode = portal.GetOppositeNode(current);
                    if (nextNode.m_searchID == searchID) continue;
                    if (nextNode.NodeID == target.NodeID) return true;
                    queue.Enqueue(nextNode);
                }
            }

            return false;
        }

        // FromElevatorDirectionFix
        [HarmonyPatch(typeof(SurvivalWave), nameof(SurvivalWave.GetScoredSpawnPoint_FromElevator))]
        [HarmonyPrefix]
        private static bool GetScoredSpawnPoint_FromElevator(SurvivalWave __instance, ref ScoredSpawnPoint __result) {
            AIG_CourseNode startCourseNode = __instance.m_courseNode.m_dimension.GetStartCourseNode();
            AIG_CourseNode? courseNode = null;

            // find first reachable player
            foreach (PlayerAgent player in PlayerManager.PlayerAgentsInLevel) {
                if (IsTargetReachable(startCourseNode, player.m_courseNode)) {
                    courseNode = player.m_courseNode;
                    break;
                }
            }
            if (courseNode == null) return true;

            Vector3 normalized = (startCourseNode.Position - courseNode.Position).normalized;
            normalized.y = 0f;
            Il2CppSystem.Collections.Generic.List<ScoredSpawnPoint> availableSpawnPointsBetweenElevatorAndNode = __instance.GetAvailableSpawnPointsBetweenElevatorAndNode(courseNode);
            ScoredSpawnPoint scoredSpawnPoint = new ScoredSpawnPoint {
                totalCost = float.MinValue
            };
            Vector3 position = courseNode.Position;
            float num = 1f;
            float num2 = 4f - num;
            for (int i = 0; i < availableSpawnPointsBetweenElevatorAndNode.Count; i++) {
                ScoredSpawnPoint scoredSpawnPoint2 = availableSpawnPointsBetweenElevatorAndNode[i];
                Vector3 vector = scoredSpawnPoint2.firstCoursePortal.Position - position;
                vector.y = 0f;
                vector.Normalize();
                scoredSpawnPoint2.m_dir = vector;
                scoredSpawnPoint2.totalCost = Mathf.Clamp01(Vector3.Dot(vector, normalized));
                if (scoredSpawnPoint2.pathHeat > num - 0.01f) {
                    scoredSpawnPoint2.totalCost += 1f + (1f - Mathf.Clamp(scoredSpawnPoint2.pathHeat - num, 0f, num2) / num2);
                }
                if (scoredSpawnPoint == null) {
                    scoredSpawnPoint = scoredSpawnPoint2;
                } else if (scoredSpawnPoint2.totalCost > scoredSpawnPoint.totalCost) {
                    scoredSpawnPoint = scoredSpawnPoint2;
                }
            }
            if (scoredSpawnPoint.courseNode == null) {
                scoredSpawnPoint.courseNode = courseNode;
            }
            __result = scoredSpawnPoint;

            return false;
        }

        public static Dictionary<uint, LayoutConfig> data = new Dictionary<uint, LayoutConfig>();
        public static Dictionary<int, Dictionary<int, LG_SecurityDoor>> doors = new Dictionary<int, Dictionary<int, LG_SecurityDoor>>();

        [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.Setup))]
        [HarmonyPostfix]
        private static void SecurityDoor_Setup(LG_SecurityDoor __instance, LG_Gate gate) {
            int fromAlias = gate.m_linksFrom.m_zone.Alias;
            int toAlias = gate.m_linksTo.m_zone.Alias;
            if (!doors.ContainsKey(fromAlias)) doors.Add(fromAlias, new Dictionary<int, LG_SecurityDoor>());
            doors[fromAlias].Add(toAlias, __instance);
        }

        private static uint GetLayoutIdOfZone(LG_Zone zone) {
            Dimension dimension = zone.Dimension;
            if (dimension.IsMainDimension) {
                LG_LayerType type = zone.Layer.m_type;
                ExpeditionInTierData activeExpedition = RundownManager.ActiveExpedition;
                return type switch {
                    LG_LayerType.MainLayer => activeExpedition.LevelLayoutData,
                    LG_LayerType.SecondaryLayer => activeExpedition.SecondaryLayout,
                    LG_LayerType.ThirdLayer => activeExpedition.ThirdLayout,
                    _ => throw new NotSupportedException($"LayerType: {type} is not supported!"),
                };
            }
            return dimension.DimensionData.LevelLayoutData;
        }

        private static bool reverse(uint layerId, int fromAlias, int toAlias) {
            bool result = false;
            if (data.ContainsKey(layerId)) {
                result = data[layerId].Doors.Any((d) => d.To == toAlias && (d.From == -1 || d.From == fromAlias));
            }
            APILogger.Debug($"layerId: {layerId}, fromAlias: {fromAlias}, toAlias: {toAlias} -> {(result ? "reversed" : "not reversed")}");
            return result;
        }

        private static bool doubleHandle(uint layerId, int fromAlias, int toAlias) {
            bool result = false;
            if (data.ContainsKey(layerId)) {
                result = data[layerId].AddHandle.Any((d) => d.To == toAlias && (d.From == -1 || d.From == fromAlias));
            }
            APILogger.Debug($"layerId: {layerId}, fromAlias: {fromAlias}, toAlias: {toAlias} -> {(result ? "doubleHandle" : "not doubleHandle")}");
            return result;
        }

        private static bool graphic(uint layerId, int fromAlias, int toAlias) {
            bool result = false;
            if (data.ContainsKey(layerId)) {
                result = data[layerId].DoorLockedGraphicOverrides.Any((d) => d.To == toAlias && (d.From == -1 || d.From == fromAlias));
            }
            APILogger.Debug($"layerId: {layerId}, fromAlias: {fromAlias}, toAlias: {toAlias} -> {(result ? "override handle light" : "leave handle light")}");
            return result;
        }

        private static bool overrideMessage(uint layerId, int fromAlias, int toAlias) {
            bool result = false;
            if (data.ContainsKey(layerId)) {
                result = data[layerId].DoorMessageOverrides.Any((d) => d.Door != null && d.Door.To == toAlias && (d.Door.From == -1 || d.Door.From == fromAlias));
            }
            APILogger.Debug($"layerId: {layerId}, fromAlias: {fromAlias}, toAlias: {toAlias} -> {(result ? "override message" : "not overridden")}");
            return result;
        }

        private static bool overrideTrigger(uint layerId, int fromAlias, int toAlias) {
            bool result = false;
            if (data.ContainsKey(layerId)) {
                result = data[layerId].DoorOverrideTrigger.Any((d) => d.Door != null && d.Door.To == toAlias && (d.Door.From == -1 || d.Door.From == fromAlias));
            }
            APILogger.Debug($"layerId: {layerId}, fromAlias: {fromAlias}, toAlias: {toAlias} -> {(result ? "override trigger" : "not trigger")}");
            return result;
        }

        private static bool overrideTriggerOG(uint layerId, int fromAlias, int toAlias) {
            bool result = false;
            if (data.ContainsKey(layerId)) {
                result = data[layerId].DoorOverrideTrigger.Any((d) => d.Target != null && d.Target.To == toAlias && (d.Target.From == -1 || d.Target.From == fromAlias));
            }
            APILogger.Debug($"layerId: {layerId}, fromAlias: {fromAlias}, toAlias: {toAlias} -> {(result ? "OG trigger" : "not OG trigger")}");
            return result;
        }

        [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.OnSyncDoorStatusChange))]
        [HarmonyPrefix]
        private static void OnSyncDoorStatusChangeOG(LG_SecurityDoor __instance, pDoorState state) {
            uint layer = GetLayoutIdOfZone(__instance.Gate.m_linksFrom.m_zone);
            int fromAlias = __instance.Gate.m_linksFrom.m_zone.Alias;
            int toAlias = __instance.Gate.m_linksTo.m_zone.Alias;
            if (!overrideTriggerOG(layer, fromAlias, toAlias)) return;

            DoorTriggerOverride structure = data[layer].DoorOverrideTrigger.First((d) => d.Target != null && d.Target.To == toAlias && (d.Target.From == -1 || d.Target.From == fromAlias));
            if (!structure.OpenOnTarget) {
                if (state.status != eDoorStatus.ChainedPuzzleActivated) return;

                if (structure.Door != null) {
                    if (structure.Door.From != fromAlias || structure.Door.To != toAlias) {
                        if (doors.ContainsKey(structure.Door.From)) {
                            if (doors[structure.Door.From].ContainsKey(structure.Door.To)) {
                                LG_SecurityDoor linkedDoor = doors[structure.Door.From][structure.Door.To];

                                linkedDoor.m_sync.AttemptDoorInteraction(eDoorInteractionType.ActivateChainedPuzzle);

                                __instance.m_locks.add_OnChainedPuzzleSolved((Action)(() => {
                                    linkedDoor.m_locks.ChainedPuzzleToSolve.AttemptInteract(eChainedPuzzleInteraction.Solve);
                                    pDoorState state = new pDoorState() {
                                        status = eDoorStatus.Unlocked
                                    };
                                    linkedDoor.m_graphics.OnDoorState(state, false);
                                    linkedDoor.m_anim.OnDoorState(state, false);
                                    linkedDoor.m_locks.OnDoorState(state, false);
                                    linkedDoor.m_sync.Cast<LG_Door_Sync>().AttemptDoorInteraction(eDoorInteractionType.Unlock, 0, 0, default, null);
                                }));
                            }
                        }
                    }
                }
            } else {
                if (state.status != eDoorStatus.Open && state.status != eDoorStatus.Unlocked && state.status != eDoorStatus.ChainedPuzzleActivated) return;

                if (structure.Door != null) {
                    if (structure.Door.From != fromAlias || structure.Door.To != toAlias) {
                        if (doors.ContainsKey(structure.Door.From)) {
                            if (doors[structure.Door.From].ContainsKey(structure.Door.To)) {
                                LG_SecurityDoor linkedDoor = doors[structure.Door.From][structure.Door.To];

                                if (state.status == eDoorStatus.Open) {
                                    linkedDoor.m_sync.AttemptDoorInteraction(eDoorInteractionType.Open);
                                } else if (state.status == eDoorStatus.Unlocked) {
                                    pDoorState unlockState = new pDoorState() {
                                        status = eDoorStatus.Unlocked
                                    };
                                    linkedDoor.m_graphics.OnDoorState(unlockState, false);
                                    linkedDoor.m_anim.OnDoorState(unlockState, false);
                                } else if (state.status == eDoorStatus.ChainedPuzzleActivated) {
                                    pDoorState unlockState = new pDoorState() {
                                        status = eDoorStatus.ChainedPuzzleActivated
                                    };
                                    linkedDoor.m_graphics.OnDoorState(unlockState, false);
                                    linkedDoor.m_anim.OnDoorState(unlockState, false);
                                }
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(LG_Door_Graphics), nameof(LG_Door_Graphics.OnDoorState))]
        [HarmonyPrefix]
        private static void Graphic_OnDoorState(LG_Door_Graphics __instance, ref pDoorState state) {
            LG_SecurityDoor? door = __instance.m_core.TryCast<LG_SecurityDoor>();
            if (door == null) return;
            uint layer = GetLayoutIdOfZone(door.Gate.m_linksFrom.m_zone);
            int fromAlias = door.Gate.m_linksFrom.m_zone.Alias;
            int toAlias = door.Gate.m_linksTo.m_zone.Alias;
            if (!graphic(layer, fromAlias, toAlias)) return;
            if (state.status != eDoorStatus.Closed_LockedWithChainedPuzzle &&
                state.status != eDoorStatus.Closed_LockedWithChainedPuzzle_Alarm &&
                state.status != eDoorStatus.Closed_LockedWithNoKey &&
                state.status != eDoorStatus.Closed_LockedWithPowerGenerator &&
                state.status != eDoorStatus.Closed_LockedWithBulkheadDC &&
                state.status != eDoorStatus.Closed_LockedWithKeyItem) return;

            DoorIdentifier structure = data[layer].DoorLockedGraphicOverrides.First((d) => d.To == toAlias && (d.From == -1 || d.From == fromAlias));
            if (structure.State != string.Empty && Enum.TryParse<eDoorStatus>(structure.State, true, out eDoorStatus result)) {
                state.status = result;
                if (result == eDoorStatus.Destroyed) {
                    // Note(randomuserhi): Cam instead patch setup and add key to m_graphicalModeLookup with correct objects to hide => See GraphicalModes and LG_Door_Graphics.m_graphicalModeLookup
                    Transform? scanActive = door.m_doorBladeCuller.transform.Find("securityDoor_8x4_tech/bottomDoor/Security_Display_ScanActive");
                    if (scanActive == null) {
                        door.m_doorBladeCuller.transform.Find("securityDoor_4x4_tech/rightDoor/Security_Display_ScanActive");
                    }
                    if (scanActive != null) {
                        scanActive.gameObject.SetActive(false);
                    }
                    Transform? locked = door.m_doorBladeCuller.transform.Find("securityDoor_8x4_tech/bottomDoor/Security_Display_Locked");
                    if (locked == null) {
                        door.m_doorBladeCuller.transform.Find("securityDoor_4x4_tech/rightDoor/Security_Display_Locked");
                    }
                    if (locked != null) {
                        locked.gameObject.SetActive(false);
                    }
                    Transform? lockedAlarm = door.m_doorBladeCuller.transform.Find("securityDoor_8x4_tech/bottomDoor/Security_Display_LockedAlarm");
                    if (lockedAlarm == null) {
                        door.m_doorBladeCuller.transform.Find("securityDoor_4x4_tech/rightDoor/Security_Display_LockedAlarm");
                    }
                    if (lockedAlarm != null) {
                        lockedAlarm.gameObject.SetActive(false);
                    }
                    Transform? unlocked = door.m_doorBladeCuller.transform.Find("securityDoor_8x4_tech/bottomDoor/Security_Display_UnLocked");
                    if (unlocked == null) {
                        door.m_doorBladeCuller.transform.Find("securityDoor_4x4_tech/rightDoor/Security_Display_UnLocked");
                    }
                    if (unlocked != null) {
                        unlocked.gameObject.SetActive(false);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.OnSyncDoorStatusChange))]
        [HarmonyPrefix]
        private static bool OnSyncDoorStatusChange(LG_SecurityDoor __instance, pDoorState state) {
            uint layer = GetLayoutIdOfZone(__instance.Gate.m_linksFrom.m_zone);
            int fromAlias = __instance.Gate.m_linksFrom.m_zone.Alias;
            int toAlias = __instance.Gate.m_linksTo.m_zone.Alias;
            if (!overrideTrigger(layer, fromAlias, toAlias)) return true;
            if (state.status != eDoorStatus.ChainedPuzzleActivated) return true;

            DoorTriggerOverride structure = data[layer].DoorOverrideTrigger.First((d) => d.Door != null && d.Door.To == toAlias && (d.Door.From == -1 || d.Door.From == fromAlias));

            if (structure.Target != null) {
                if (structure.Target.From != fromAlias || structure.Target.To != toAlias) {
                    if (doors.ContainsKey(structure.Target.From)) {
                        if (doors[structure.Target.From].ContainsKey(structure.Target.To)) {
                            __instance.m_graphics.OnDoorState(state, false);
                            __instance.m_anim.OnDoorState(state, false);
                            LG_SecurityDoor_Locks locks = __instance.m_locks.Cast<LG_SecurityDoor_Locks>();
                            locks.m_intUseKeyItem.SetActive(active: false);
                            locks.m_intOpenDoor.SetActive(active: false);
                            locks.m_intHack.SetActive(active: false);
                            locks.m_intCustomMessage.SetActive(active: false);

                            return false;
                        }
                    }
                }
            }

            return true;
        }

        [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.OnSyncDoorStatusChange))]
        [HarmonyPrefix]
        private static void OnSyncDoorStatusChange_Reverse(LG_SecurityDoor __instance, pDoorState state) {
            uint layer = GetLayoutIdOfZone(__instance.Gate.m_linksFrom.m_zone);
            int fromAlias = __instance.Gate.m_linksFrom.m_zone.Alias;
            int toAlias = __instance.Gate.m_linksTo.m_zone.Alias;
            if (!reverse(layer, fromAlias, toAlias)) return;
            if (state.status != eDoorStatus.Open) return;

            int id = __instance.Gate.GetInstanceID();
            if (reversedGates.ContainsKey(id)) {
                reversedGates[id].SetActive(false);
            }
        }

        [HarmonyPatch(typeof(LG_Door_Sync), nameof(LG_Door_Sync.AttemptInteract))]
        [HarmonyPrefix]
        private static void AttemptInteract(LG_Door_Sync __instance, pDoorInteraction interaction) {
            LG_SecurityDoor? door = __instance.m_core.TryCast<LG_SecurityDoor>();
            if (door == null) return;

            uint layer = GetLayoutIdOfZone(door.Gate.m_linksFrom.m_zone);
            int fromAlias = door.Gate.m_linksFrom.m_zone.Alias;
            int toAlias = door.Gate.m_linksTo.m_zone.Alias;
            if (!overrideTrigger(layer, fromAlias, toAlias)) return;
            if (interaction.type != eDoorInteractionType.ActivateChainedPuzzle) return;

            DoorTriggerOverride structure = data[layer].DoorOverrideTrigger.First((d) => d.Door != null && d.Door.To == toAlias && (d.Door.From == -1 || d.Door.From == fromAlias));
            if (structure.Target != null) {
                if (structure.Target.From != fromAlias || structure.Target.To != toAlias) {
                    if (doors.ContainsKey(structure.Target.From)) {
                        if (doors[structure.Target.From].ContainsKey(structure.Target.To)) {
                            LG_SecurityDoor target = doors[structure.Target.From][structure.Target.To];
                            target.m_sync.AttemptDoorInteraction(eDoorInteractionType.ActivateChainedPuzzle);
                        }
                    }
                }
            }

            return;
        }

        /*private static string DebugObject(GameObject go, StringBuilder? sb = null, int depth = 0) {
            if (sb == null) sb = new StringBuilder();
            sb.Append('\t', depth);
            sb.Append(go.name + "\n");
            for (int i = 0; i < go.transform.childCount; ++i) {
                DebugObject(go.transform.GetChild(i).gameObject, sb, depth + 1);
            }
            return sb.ToString();
        }*/

        private static HashSet<int> reversedSecDoors = new HashSet<int>();
        private static Dictionary<int, GameObject> reversedGates = new Dictionary<int, GameObject>();
        private static Dictionary<int, LG_Area> reversedGenericTerminalItem = new Dictionary<int, LG_Area>();

        [HarmonyPatch(typeof(ElevatorRide), nameof(ElevatorRide.StartElevatorRide))]
        [HarmonyPostfix]
        private static void StartElevatorRide() {
            reversedSecDoors.Clear();
            reversedGates.Clear();
            reversedGenericTerminalItem.Clear();
            doors.Clear();
            boundDoors.Clear();
        }

        [HarmonyPatch(typeof(ChainedPuzzleManager), nameof(ChainedPuzzleManager.CreatePuzzleInstance), new Type[] {
            typeof(ChainedPuzzleDataBlock),
            typeof(LG_Area),
            typeof(LG_Area),
            typeof(Vector3),
            typeof(Transform),
            typeof(bool)
        })]
        [HarmonyPrefix]
        private static void CreateChainedPuzzle(ChainedPuzzleDataBlock data, ref LG_Area sourceArea, ref LG_Area targetArea, Vector3 sourcePos, Transform parent, bool overrideUseStaticBioscanPoints) {
            if (reversedSecDoors.Contains(parent.GetInstanceID())) {
                LG_Area temp = sourceArea;
                sourceArea = targetArea;
                targetArea = temp;
            }
        }

        private static bool onDoorSync = false;
        [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.OnSyncDoorStatusChange))]
        [HarmonyPrefix]
        private static void Prefix_OnSyncDoorStatusChange() {
            onDoorSync = true;
        }
        [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.OnSyncDoorStatusChange))]
        [HarmonyPostfix]
        private static void Postfix_OnSyncDoorStatusChange() {
            onDoorSync = false;
        }

        [HarmonyPatch(typeof(LG_SecurityDoor_Locks), nameof(LG_SecurityDoor_Locks.OnDoorState))]
        [HarmonyPostfix]
        private static void Postfix_OnDoorState(LG_SecurityDoor_Locks __instance, pDoorState state) {
            switch (state.status) {
            case eDoorStatus.Unlocked: return;
            }

            _overrideText(__instance);
        }

        [HarmonyPatch(typeof(LG_SecurityDoor_Locks), nameof(LG_SecurityDoor_Locks.SetupAsLockedNoKey))]
        [HarmonyPostfix]
        private static void Postfix_OnDoorState(LG_SecurityDoor_Locks __instance) {
            _overrideText(__instance);
        }

        private static void _overrideText(LG_SecurityDoor_Locks __instance) {
            uint layer = GetLayoutIdOfZone(__instance.m_door.Gate.m_linksFrom.m_zone);
            int fromAlias = __instance.m_door.Gate.m_linksFrom.m_zone.Alias;
            int toAlias = __instance.m_door.Gate.m_linksTo.m_zone.Alias;
            if (!overrideMessage(layer, fromAlias, toAlias)) return;

            DoorMessageOverride structure = data[layer].DoorMessageOverrides.First((d) => d.Door != null && d.Door.To == toAlias && (d.Door.From == -1 || d.Door.From == fromAlias));
            string str = Regex.Replace(structure.Message, @"\[DOOR\(\s*(\d+)\s*,\s*(\d+)\s*\)\]", m => {
                int from = int.Parse(m.Groups[1].Value);
                int to = int.Parse(m.Groups[2].Value);
                if (doors.ContainsKey(from)) {
                    if (doors[from].ContainsKey(to)) {
                        return $"SEC_DOOR_{doors[from][to].m_serialNumber}";
                    }
                }
                return $"DEBUG_FROM({from})_TO({to})";
            });

            __instance.m_intOpenDoor.InteractionMessage = str;
            __instance.m_intUseKeyItem.InteractionMessage = str;
            __instance.m_intHack.InteractionMessage = str;
            __instance.m_intCustomMessage.m_message = str;
        }


        [HarmonyPatch(typeof(LG_ZoneExpander), nameof(LG_ZoneExpander.GetOppositeArea))]
        [HarmonyPrefix]
        private static bool GetOppositeArea(LG_ZoneExpander __instance, ref LG_Area __result, LG_Area area) {
            if (onDoorSync) {
                LG_Gate? gate = __instance.GetGate();
                if (gate == null) return true;
                if (reversedGates.ContainsKey(gate.GetInstanceID())) {
                    __result = area;
                    return false;
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.SetNavInfo))]
        [HarmonyPrefix]
        private static bool SecDoor_NavInfo(LG_SecurityDoor __instance, string infoFwd, string infoBwd, Il2CppSystem.Collections.Generic.List<string> infoFwdClean, Il2CppSystem.Collections.Generic.List<string> infoBwdClean) {
            if (!reverse(GetLayoutIdOfZone(__instance.Gate.m_linksFrom.m_zone), __instance.Gate.m_linksFrom.m_zone.Alias, __instance.Gate.m_linksTo.m_zone.Alias)) return true;

            __instance.m_graphics.SetNavInfoFwd(infoBwd);
            __instance.m_graphics.SetNavInfoBwd(infoFwd);
            __instance.m_terminalNavInfoForward = infoBwdClean;
            __instance.m_terminalNavInfoBackward = infoFwdClean;

            return false;
        }

        private static Dictionary<int, HashSet<LG_SecurityDoor>> boundDoors = new Dictionary<int, HashSet<LG_SecurityDoor>>();
        [HarmonyPatch(typeof(WardenObjectiveManager), nameof(WardenObjectiveManager.CheckExpeditionFailed))]
        [HarmonyPostfix]
        private static void CheckExpeditionFailed(WardenObjectiveManager __instance, ref bool __result) {
            if (__result == true) return;

            for (int i = 0; i < PlayerManager.PlayerAgentsInLevel.Count; i++) {
                PlayerAgent playerAgent = PlayerManager.PlayerAgentsInLevel[i];
                if (!playerAgent.Alive && boundDoors.ContainsKey(playerAgent.PlayerSlotIndex)) {
                    bool allOpen = true;
                    foreach (LG_SecurityDoor door in boundDoors[playerAgent.PlayerSlotIndex]) {
                        APILogger.Debug($"{playerAgent.PlayerSlotIndex} -> {door.Gate.m_linksTo.m_zone.Alias} {door.LastStatus}");
                        if (door.LastStatus != eDoorStatus.Open) {
                            allOpen = false;
                            break;
                        }
                    }
                    if (!allOpen) {
                        APILogger.Debug($"{playerAgent.PlayerSlotIndex} -> end run");
                        __result = true;
                        return;
                    }
                }
            }
        }

        private static bool bind(uint layerId, int fromAlias, int toAlias, out List<int> slots) {
            slots = new List<int>();
            bool result = false;
            if (data.ContainsKey(layerId)) {
                foreach (BindDoorToPlayer bind in data[layerId].BindDoorToPlayer) {
                    DoorIdentifier? d = bind.Door;
                    if (d != null && d.To == toAlias && (d.From == -1 || d.From == fromAlias)) {
                        slots.Add(bind.Slot);
                        result = true;
                    }
                }
            }
            APILogger.Debug($"layerId: {layerId}, fromAlias: {fromAlias}, toAlias: {toAlias} -> {(result ? "bound" : "not bound")}");
            return result;
        }

        [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.Setup))]
        [HarmonyPostfix]
        private static void SecDoor_Setup(LG_SecurityDoor __instance, LG_Gate gate) {
            if (bind(GetLayoutIdOfZone(gate.m_linksFrom.m_zone), gate.m_linksFrom.m_zone.Alias, gate.m_linksTo.m_zone.Alias, out List<int> slots)) {
                foreach (int slot in slots) {
                    if (!boundDoors.ContainsKey(slot)) boundDoors.Add(slot, new HashSet<LG_SecurityDoor>());
                    boundDoors[slot].Add(__instance);
                }
            }

            bool flip = reverse(GetLayoutIdOfZone(gate.m_linksFrom.m_zone), gate.m_linksFrom.m_zone.Alias, gate.m_linksTo.m_zone.Alias);
            bool dHandle = doubleHandle(GetLayoutIdOfZone(gate.m_linksFrom.m_zone), gate.m_linksFrom.m_zone.Alias, gate.m_linksTo.m_zone.Alias);

            if (!flip && !dHandle) return;

            Transform crossing = __instance.transform.Find("crossing");
            if (crossing == null) return;
            if (flip) crossing.localRotation *= Quaternion.Euler(0, 180, 0);

            Transform capBack = __instance.m_doorBladeCuller.transform.Find("securityDoor_8x4_tech/bottomDoor/g_securityDoor_bottomDoor_capback");
            if (capBack == null) {
                capBack = __instance.m_doorBladeCuller.transform.Find("securityDoor_4x4_tech (1)/rightDoor/g_securityDoor_bottomDoor_capback001");
                if (capBack == null) return;
            }
            capBack.gameObject.SetActive(false);

            Transform handle = __instance.m_doorBladeCuller.transform.Find("securityDoor_8x4_tech/bottomDoor/InteractionInterface");
            bool size4x4 = false;
            if (handle == null) {
                handle = __instance.m_doorBladeCuller.transform.Find("securityDoor_4x4_tech (1)/rightDoor/InteractionInterface");
                size4x4 = true;
                if (handle == null) return;
            }

            GameObject backHandle = UnityEngine.Object.Instantiate(handle.gameObject, handle.parent);
            backHandle.transform.localRotation = handle.localRotation * Quaternion.Euler(180, 180, 0);
            if (size4x4) {
                backHandle.transform.localPosition = handle.localPosition + new Vector3(0, 0.28f, 0);
            } else {
                backHandle.transform.localPosition = handle.localPosition + new Vector3(0, 0, -0.25f);
            }

            Transform interactMessage = __instance.transform.Find("crossing/Interaction_Message");
            if (interactMessage == null) return;
            GameObject backInteractionMessage = UnityEngine.Object.Instantiate(interactMessage.gameObject, interactMessage.parent);
            backInteractionMessage.transform.position = backHandle.transform.position;
            Interact_MessageOnScreen message = backInteractionMessage.GetComponent<Interact_MessageOnScreen>();
            message.MessageType = eMessageOnScreenType.InteractionPrompt;
            message.m_message = data[GetLayoutIdOfZone(gate.m_linksFrom.m_zone)].Doors.First((d) => d.To == gate.m_linksTo.m_zone.Alias && (d.From == -1 || d.From == gate.m_linksFrom.m_zone.Alias)).Text;
            backInteractionMessage.SetActive(true);
            message.SetActive(true);

            reversedSecDoors.Add(__instance.transform.GetInstanceID());
            reversedGates.Add(gate.GetInstanceID(), backInteractionMessage);
            reversedGenericTerminalItem.Add(__instance.m_terminalItem.Cast<LG_GenericTerminalItem>().GetInstanceID(), gate.m_linksTo);
        }

        [HarmonyPatch(typeof(LG_GenericTerminalItem), nameof(LG_GenericTerminalItem.SpawnNode), MethodType.Setter)]
        [HarmonyPostfix]
        private static void Postfix_Set_SpawnNode(LG_GenericTerminalItem __instance) {
            int instance = __instance.GetInstanceID();
            if (reversedGenericTerminalItem.ContainsKey(instance)) {
                LG_Area area = reversedGenericTerminalItem[instance];
                __instance.FloorItemLocation = area.m_zone.NavInfo.GetFormattedText(LG_NavInfoFormat.Full_And_Number_With_Underscore);
                __instance.m_spawnNode = area.m_courseNode;
            }
        }
    }
}
