using API;
using HarmonyLib;
using LevelGeneration;
using System.Text;
using UnityEngine;

namespace DoubleSidedDoors.Patches {
    [HarmonyPatch]
    internal class Spawn {
        private static string DebugObject(GameObject go, StringBuilder? sb = null, int depth = 0) {
            if (sb == null) sb = new StringBuilder();
            sb.Append('\t', depth);
            sb.Append(go.name + "\n");
            for (int i = 0; i < go.transform.childCount; ++i) {
                DebugObject(go.transform.GetChild(i).gameObject, sb, depth + 1);
            }
            return sb.ToString();
        }

        [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.Setup))]
        [HarmonyPostfix]
        private static void SecDoor_Setup(LG_SecurityDoor __instance, LG_Gate gate) {
            APILogger.Debug(DebugObject(__instance.gameObject));
            Transform cap_back = __instance.m_doorBladeCuller.transform.Find("securityDoor_8x4_tech/bottomDoor/g_securityDoor_bottomDoor_capback");
            if (cap_back == null) {
                cap_back = __instance.m_doorBladeCuller.transform.Find("securityDoor_4x4_tech (1)/rightDoor/g_securityDoor_bottomDoor_capback001");
                if (cap_back == null) return;
            }
            cap_back.gameObject.SetActive(false);

            Transform handle = __instance.m_doorBladeCuller.transform.Find("securityDoor_8x4_tech/bottomDoor/InteractionInterface");
            if (handle == null) {
                handle = __instance.m_doorBladeCuller.transform.Find("securityDoor_4x4_tech (1)/rightDoor/InteractionInterface");
                if (handle == null) return;
            }

            GameObject back_handle = UnityEngine.Object.Instantiate(handle.gameObject, handle.parent);
            back_handle.transform.localPosition = handle.localPosition + new Vector3(0, 0, -0.25f);
            back_handle.transform.localRotation = handle.localRotation * Quaternion.Euler(180, 180, 0);

            APILogger.Debug($"lock: {back_handle.GetComponent<LG_SecurityDoor_Locks>() == null}");
        }
    }
}
