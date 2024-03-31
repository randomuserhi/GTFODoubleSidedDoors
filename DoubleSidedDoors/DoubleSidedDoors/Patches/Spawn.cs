using API;
using ChainedPuzzles;
using GameData;
using HarmonyLib;
using LevelGeneration;
using UnityEngine;

namespace DoubleSidedDoors {
    public sealed class LayoutConfig {
        public uint LevelLayoutID { get; set; }
        public DoorIdentifier[] Doors { get; set; } = Array.Empty<DoorIdentifier>();
    }

    public sealed class DoorIdentifier {
        public int To { get; set; }
        public int From { get; set; } = -1;
    }
}

namespace DoubleSidedDoors.Patches {
    [HarmonyPatch]
    internal static class Spawn {
        public static Dictionary<uint, LayoutConfig> data = new Dictionary<uint, LayoutConfig>();

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
        private static HashSet<int> reversedGates = new HashSet<int>();

        [HarmonyPatch(typeof(ElevatorRide), nameof(ElevatorRide.StartElevatorRide))]
        [HarmonyPostfix]
        private static void StartElevatorRide() {
            reversedSecDoors.Clear();
            reversedGates.Clear();
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


        [HarmonyPatch(typeof(LG_ZoneExpander), nameof(LG_ZoneExpander.GetOppositeArea))]
        [HarmonyPrefix]
        private static bool GetOppositeArea(LG_ZoneExpander __instance, ref LG_Area __result, LG_Area area) {
            if (onDoorSync) {
                LG_Gate? gate = __instance.GetGate();
                if (gate == null) return true;
                if (reversedGates.Contains(gate.GetInstanceID())) {
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

        [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.Setup))]
        [HarmonyPostfix]
        private static void SecDoor_Setup(LG_SecurityDoor __instance, LG_Gate gate) {
            if (!reverse(GetLayoutIdOfZone(gate.m_linksFrom.m_zone), gate.m_linksFrom.m_zone.Alias, gate.m_linksTo.m_zone.Alias)) return;

            Transform crossing = __instance.transform.Find("crossing");
            if (crossing == null) return;
            crossing.localRotation *= Quaternion.Euler(0, 180, 0);

            reversedSecDoors.Add(__instance.transform.GetInstanceID());
            reversedGates.Add(gate.GetInstanceID());

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
            message.m_message = $"<color=red>BI-DIRECTIONAL ACCESS DISABLED</color>";
            backInteractionMessage.SetActive(true);
            message.SetActive(true);
        }
    }
}
