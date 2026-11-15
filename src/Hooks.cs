using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RatTrainer
{
    public static class Hooks
    {
        private static readonly System.Reflection.MethodInfo TimerPausedGetter = AccessTools.PropertyGetter(typeof(Interactable), "TimerPaused");
        private static Plugin P { get { return Plugin.Instance; } }
        public static void Install(Harmony harmony, Plugin plugin)
        {
            Patch(harmony, plugin, typeof(RatController), "Update", "RatBefore", "RatAfter");
            Patch(harmony, plugin, typeof(RatController), "FixedUpdate", "RatFixed", null);
            Patch(harmony, plugin, typeof(RatController), "KillMeClientRpc", "AllowKill", null);
            Patch(harmony, plugin, typeof(RatController), "PoisonMeClientRpc", "AllowPoison", null);
            Patch(harmony, plugin, typeof(RatController), "StunClientRpc", "AllowStun", null);
            Patch(harmony, plugin, typeof(RatController), "FallDamageServerRpc", "AllowFall", null);
            Patch(harmony, plugin, typeof(RatController), "GetCurrentDeathZone", null, "DeathZoneAfter");
            Patch(harmony, plugin, typeof(ChefBot), "Update", "BotBefore", null);
            Patch(harmony, plugin, typeof(ChefBot), "get_MaxHearingDistance", null, "HearingAfter");
            Patch(harmony, plugin, typeof(ChefBot), "RatIsVisible", null, "VisibleAfter");
            Patch(harmony, plugin, typeof(GameDifficultySettings), "get_Current", null, "DifficultyAfter");
            Patch(harmony, plugin, typeof(Interactable), "LateUpdate", "InteractionBefore", null);
            Patch(harmony, plugin, typeof(GameManager), "HatIsUnlocked", null, "HatAfter");
            Patch(harmony, plugin, typeof(GameCamera), "UpdateCursorState", "CursorBefore", null);
            Patch(harmony, plugin, typeof(InputController), "GetMove", "InputVector", null);
            Patch(harmony, plugin, typeof(InputController), "GetLook", "InputVector", null);
            Patch(harmony, plugin, typeof(InputController), "GetEmoteDirection", "InputVector", null);
            foreach (string name in new[] { "GetPauseTriggered", "GetPauseHeld", "GetCrouchTriggered", "GetProneTriggered", "GetTauntTriggered", "GetShowTodo", "ToggleFlashlightTriggered", "GetSprintStarted", "GetSprint", "GetSprintEnded", "GetEmoteTriggered", "GetJumpStarted", "GetJumpEnded", "GetInteractionStartTriggered", "GetInteractionEndTriggered", "GetMouseButtonPressed", "GetMouseButtonDown", "SubmitTriggered", "CancelTriggered" })
                Patch(harmony, plugin, typeof(InputController), name, "InputBool", null);
            try
            {
                harmony.Patch(AccessTools.Method(typeof(ChefBot), "TryObserveRats"), transpiler: new HarmonyMethod(typeof(Hooks), "HearingTranspiler"));
                plugin.PatchCount++;
            }
            catch (Exception e) { plugin.PatchErrors.Add("ChefBot.TryObserveRats: " + e.Message); }
        }
        private static void Patch(Harmony h, Plugin p, Type type, string name, string before, string after)
        {
            try
            {
                System.Reflection.MethodInfo method = AccessTools.Method(type, name);
                if (method == null) throw new MissingMethodException(type.Name, name);
                h.Patch(method, before == null ? null : new HarmonyMethod(typeof(Hooks), before), after == null ? null : new HarmonyMethod(typeof(Hooks), after));
                p.PatchCount++;
            }
            catch (Exception e) { p.PatchErrors.Add(type.Name + "." + name + ": " + e.GetBaseException().Message); }
        }
        private static bool Local(RatController rat) { return P != null && P.Active && P.IsLocal(rat); }
        public static void RatBefore(RatController __instance, out bool __state)
        {
            __state = __instance.RatStateVar.Value != RatController.RatState.Dead;
            if (P != null) P.Players.ApplyRat(__instance);
        }
        public static void RatAfter(RatController __instance, bool __state)
        {
            if (!Local(__instance)) return;
            if (__state && P.Options.Invincible && __instance.RatStateVar.Value == RatController.RatState.Dead)
                __instance.RatStateVar.Value = RatController.RatState.Normal;
            P.Players.ApplyRat(__instance);
        }
        public static void RatFixed(RatController __instance) { if (P != null) P.Players.ApplyRat(__instance); }
        public static bool AllowKill(RatController __instance) { return !(Local(__instance) && P.Options.Invincible); }
        public static bool AllowPoison(RatController __instance) { return !(Local(__instance) && P.Options.NoPoison); }
        public static bool AllowStun(RatController __instance) { return !(Local(__instance) && P.Options.NoStun); }
        public static bool AllowFall(RatController __instance) { return !(Local(__instance) && P.Options.NoFallDamage); }
        public static void DeathZoneAfter(RatController __instance, ref DeathZone __result)
        {
            if (Local(__instance) && P.Options.Invincible) __result = null;
        }
        public static bool BotBefore(ChefBot __instance) { return P == null || P.World.BeforeBotUpdate(__instance); }
        public static void HearingAfter(ref float __result)
        {
            if (P != null && Plugin.IsHost && P.Bridge.EffectiveGlobal.AiTuning) __result *= P.Bridge.EffectiveGlobal.AiHearing;
        }
        public static float ScaleHearing(float original)
        {
            return P != null && Plugin.IsHost && P.Bridge.EffectiveGlobal.AiTuning ? original * P.Bridge.EffectiveGlobal.AiHearing : original;
        }
        public static IEnumerable<CodeInstruction> HearingTranspiler(IEnumerable<CodeInstruction> source)
        {
            int matched = 0;
            foreach (CodeInstruction instruction in source)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float && (float)instruction.operand == 5f)
                {
                    matched++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Hooks), "ScaleHearing"));
                }
            }
            if (matched != 1) throw new InvalidOperationException("听觉距离代码与已验证版本不匹配");
        }
        public static void VisibleAfter(RatController rc, ref bool __result)
        {
            if (!__result || P == null || !Plugin.IsHost || !P.Bridge.EffectiveGlobal.AiTuning) return;
            WorldOptions o = P.Bridge.EffectiveGlobal;
            if (o.AiVisionRange <= 0f || UnityEngine.Random.value > o.AiSpotChance) __result = false;
        }
        public static void DifficultyAfter(ref GameDifficultySettings __result)
        {
            if (P != null) __result = P.World.Difficulty(__result);
        }
        public static void InteractionBefore(Interactable __instance, ref float ___interactionTimer)
        {
            if (P == null || !Plugin.IsHost || !__instance.IsTimingVar.Value || (bool)TimerPausedGetter.Invoke(__instance, null)) return;
            WorldOptions o = P.Bridge.ForPlayer(__instance.InteractingPlayer);
            if (o.FastInteraction) ___interactionTimer += Time.deltaTime * (o.InteractionSpeed - 1f);
        }
        public static void HatAfter(string hatName, ref bool __result)
        {
            if (hatName != "musketeer" && P != null && P.Active && P.Options.PreviewAllHats) __result = true;
        }
        public static bool CursorBefore()
        {
            if (P == null || !P.MenuOpen) return true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true; return false;
        }
        public static bool InputVector(ref Vector2 __result)
        {
            if (P == null || !P.MenuOpen) return true;
            __result = Vector2.zero; return false;
        }
        public static bool InputBool(ref bool __result)
        {
            if (P == null || !P.MenuOpen) return true;
            __result = false; return false;
        }
    }
}
