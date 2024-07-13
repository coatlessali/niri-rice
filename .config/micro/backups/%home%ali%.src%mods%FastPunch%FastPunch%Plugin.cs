using HarmonyLib;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMM;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

\[UKPlugin("tempy.fastpunch", false, true)]
public class Plugin : UKMod
{
    private static Dictionary<Punch, Traverse> allPunches = new Dictionary<Punch, Traverse>();
    private static Harmony harmony;
    
    public override void OnModLoaded()
    {
        harmony = new Harmony("tempy.fastpunch");
        harmony.PatchAll();
    }

    public override void OnModUnload()
    {
        harmony.UnpatchSelf();
    }

    public static Traverse GetPunchTraverse(Punch punch)
    {
        if (punch == null)
        {
            Debug.Log("Tried to get a traverse from a null punch");
            return null;
        }
        if (!allPunches.ContainsKey(punch))
        {
            Traverse punchTraverse = Traverse.Create(punch);
            allPunches.Add(punch, punchTraverse);
        }
        return allPunches[punch];
    }
}

[HarmonyPatch(typeof(FistControl), nameof(FistControl.ScrollArm))]
public static class Ensure_ArmNoChangieFromRed
{
    public static bool Prefix()
    {
        if (FistControl.Instance.currentPunch?.type != FistType.Heavy)
        {
            FistControl.Instance.ArmChange(1);
            FistControl.Instance.currentPunch.Invoke("Update", 0);
        }
        return false;
    }
}

[HarmonyPatch(typeof(FistControl), "Update")]
public static class Ensure_ArmChangieToBlue
{
    public static void Postfix()
    {
        if (MonoSingleton<InputManager>.Instance.InputSource.Punch.WasPerformedThisFrame && FistControl.Instance.currentPunch.type != FistType.Standard)
        {
            FistControl.Instance.ArmChange(0);
            FistControl.Instance.currentPunch.Invoke("Update", 0);
        }
    }
}

[HarmonyPatch(typeof(Punch), nameof(Punch.BlastCheck))]
public static class Ensure_KBBlast
{
    public static bool Prefix(Punch __instance)
    {
        if (MonoSingleton<InputManager>.Instance.InputSource.ChangeFist.IsPressed) // Only line of code really changed
        {
            Traverse punchTraverse = Plugin.GetPunchTraverse(__instance);
            punchTraverse.Field("holdingInput").SetValue(false);
            punchTraverse.Field("anim").GetValue<Animator>().SetTrigger("PunchBlast");
            Vector3 position = MonoSingleton<CameraController>.Instance.GetDefaultPos() + MonoSingleton<CameraController>.Instance.transform.forward * 2f;
            RaycastHit raycastHit;
            if (Physics.Raycast(MonoSingleton<CameraController>.Instance.GetDefaultPos(), MonoSingleton<CameraController>.Instance.transform.forward, out raycastHit, 2f, LayerMaskDefaults.Get(LMD.EnvironmentAndBigEnemies)))
            {
                position = raycastHit.point - CameraController.Instance.gameObject.transform.forward * 0.1f;
            }
            Object.Instantiate<GameObject>(__instance.blastWave, position, MonoSingleton<CameraController>.Instance.transform.rotation);
        }
        return false;
    }
}

[HarmonyPatch(typeof(Punch), "Update")]
public static class Inject_PunchBehavior
{
    public static bool Prefix(Punch __instance) // Yummi code that I basically copy and pasted
    {
        Traverse punchTraverse = Plugin.GetPunchTraverse(__instance);
        bool shopping = punchTraverse.Field("shopping").GetValue<bool>();
        bool holdingInput = punchTraverse.Field("holdingInput").GetValue<bool>();
        if (MonoSingleton<OptionsManager>.Instance.paused)
        {
            return false;
        }

        // The only big change here is that OR for checking whether or not the fists should punch
        if (((__instance.type == FistType.Standard && MonoSingleton<InputManager>.Instance.InputSource.Punch.WasPerformedThisFrame) || (__instance.type == FistType.Heavy && InputManager.Instance.InputSource.ChangeFist.WasPerformedThisFrame)) && __instance.ready && !shopping && FistControl.Instance.fistCooldown <= 0f && FistControl.Instance.activated && !GameStateManager.Instance.PlayerInputLocked)
        {
            float cooldownCost = punchTraverse.Field("cooldownCost").GetValue<float>();
            FistControl.Instance.weightCooldown += cooldownCost * 0.25f + FistControl.Instance.weightCooldown * cooldownCost * 0.1f;
            FistControl.Instance.fistCooldown += FistControl.Instance.weightCooldown;
            __instance.Invoke("PunchStart", 0);
            punchTraverse.Field("holdingInput").SetValue(true);
        }
        if (holdingInput && MonoSingleton<InputManager>.Instance.InputSource.Punch.WasCanceledThisFrame)
        {
            punchTraverse.Field("holdingInput").SetValue(false);
        }
        float layerWeight = __instance.anim.GetLayerWeight(1);
        if (shopping && layerWeight < 1f)
        {
            __instance.anim.SetLayerWeight(1, Mathf.MoveTowards(layerWeight, 1f, Time.deltaTime / 10f + 5f * Time.deltaTime * (1f - layerWeight)));
        }
        else if (!shopping && layerWeight > 0f)
        {
            __instance.anim.SetLayerWeight(1, Mathf.MoveTowards(layerWeight, 0f, Time.deltaTime / 10f + 5f * Time.deltaTime * layerWeight));
        }
        if (!MonoSingleton<InputManager>.Instance.PerformingCheatMenuCombo() && MonoSingleton<InputManager>.Instance.InputSource.Fire1.WasPerformedThisFrame && shopping)
        {
            __instance.anim.SetTrigger("ShopTap");
        }
        if (punchTraverse.Field("returnToOrigRot").GetValue<bool>())
        {
            __instance.transform.parent.localRotation = Quaternion.RotateTowards(__instance.transform.parent.localRotation, Quaternion.identity, (Quaternion.Angle(__instance.transform.parent.localRotation, Quaternion.identity) * 5f + 5f) * Time.deltaTime * 5f);
            if (__instance.transform.parent.localRotation == Quaternion.identity)
            {
                punchTraverse.Field("returnToOrigRot").SetValue(false);
            }
        }
        if (FistControl.Instance.shopping && !shopping)
        {
            __instance.ShopMode();
        }
        else if (!FistControl.Instance.shopping && shopping)
        {
            __instance.StopShop();
        }
        if (__instance.holding && __instance.heldItem)
        {
            if (!__instance.heldItem.noHoldingAnimation && FistControl.Instance.forceNoHold <= 0)
            {
                __instance.anim.SetBool("SemiHolding", false);
                __instance.anim.SetBool("Holding", true);
                return false;
            }
            __instance.anim.SetBool("SemiHolding", true);
        }

        return false;
    }
}
