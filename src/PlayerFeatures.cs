using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RatTrainer
{
    public sealed class PlayerFeatures
    {
        private readonly Plugin p;
        private RatController rat;
        private RatMovementSettings originalMovement, clone;
        private float fallHeight, interactDistance, interactRadius;
        private ChefController chef;
        private float chefAcceleration, chefCrouch, chefGrab;
        private float nextFeed;
        private bool hasPosition;
        private int positionScene;
        private ulong positionOwner;
        private Vector3 position;
        private Quaternion rotation;
        private static readonly System.Reflection.FieldInfo Exhausted = AccessTools.Field(typeof(RatController), "inSprintExhaustion");
        private static readonly System.Reflection.FieldInfo ExhaustionTimer = AccessTools.Field(typeof(RatController), "sprintExhaustionTimer");
        private static readonly System.Reflection.FieldInfo StarveTimer = AccessTools.Field(typeof(RatController), "totalStarveTimer");

        public PlayerFeatures(Plugin plugin) { p = plugin; }
        public string Bookmark { get { return hasPosition ? position.ToString("F1") : "尚未记录位置"; } }
        public string BaselineDescription
        {
            get { return originalMovement == null ? "" : "speed=" + originalMovement.MaxSpeed + ", jump=" + originalMovement.JumpStrength; }
        }

        public void Tick()
        {
            if (!p.Active) { if (rat != null || chef != null) Restore(); return; }
            PlayerController local = p.LocalPlayer;
            RatController currentRat = local as RatController;
            ChefController currentChef = local as ChefController;
            if (rat != currentRat || chef != currentChef) { Restore(); Bind(currentRat, currentChef); }
            if (!Plugin.Connected || SceneManager.GetActiveScene().handle != positionScene) hasPosition = false;
            if (currentRat != null && rat == null) Bind(currentRat, null);
            if (currentChef != null && chef == null) Bind(null, currentChef);
            if (rat != null) ApplyRat(rat);
            if (chef != null)
            {
                TrainerOptions o = p.Options;
                float multiplier = o.ChefSpeedEnabled ? o.ChefSpeed : 1f;
                chef.Acceleration = chefAcceleration * multiplier;
                chef.CrouchAcceleration = chefCrouch * multiplier;
                chef.MaxGrabDistance = o.GrabEnabled ? Mathf.Min(20f, chefGrab * o.Grab) : chefGrab;
            }
        }

        private void Bind(RatController newRat, ChefController newChef)
        {
            if (newRat != null && p.IsLocal(newRat))
            {
                rat = newRat;
                originalMovement = rat.Movement;
                clone = UnityEngine.Object.Instantiate(originalMovement);
                clone.name = originalMovement.name + " (RatTrainer local)";
                rat.Movement = clone;
                fallHeight = rat.FallDamageHeight;
                interactDistance = rat.MaxInteractDistance;
                interactRadius = rat.InteractRadius;
                nextFeed = 0f;
            }
            if (newChef != null && p.IsLocal(newChef))
            {
                chef = newChef;
                chefAcceleration = chef.Acceleration;
                chefCrouch = chef.CrouchAcceleration;
                chefGrab = chef.MaxGrabDistance;
            }
        }

        public void ApplyRat(RatController target)
        {
            if (!p.Active || !p.IsLocal(target)) return;
            TrainerOptions o = p.Options;
            if (target == rat && clone != null)
            {
                float multiplier = o.RatSpeedEnabled ? o.RatSpeed : 1f;
                clone.Acceleration = originalMovement.Acceleration * multiplier;
                clone.AirAcceleration = originalMovement.AirAcceleration * multiplier;
                clone.SprintAcceleration = originalMovement.SprintAcceleration * multiplier;
                clone.MaxSpeed = originalMovement.MaxSpeed * multiplier;
                clone.JumpStrength = originalMovement.JumpStrength * (o.JumpEnabled ? o.Jump : 1f);
                target.FallDamageHeight = o.NoFallDamage ? 100000f : fallHeight;
                target.MaxInteractDistance = o.ReachEnabled ? Mathf.Min(15f, interactDistance * o.Reach) : interactDistance;
                target.InteractRadius = o.ReachEnabled ? Mathf.Min(5f, interactRadius * o.Reach) : interactRadius;
            }
            if (o.InfiniteSprint)
            {
                target.SprintAmountVar.Value = 1f;
                Exhausted.SetValue(target, false);
                ExhaustionTimer.SetValue(target, 0f);
            }
            if (o.Invincible) target.Health = 1f;
            if (o.NoPoison) { target.IsPoisoned = false; target.PoisonTimer = 0f; }
            if (o.NoStun && target.RatStateVar.Value == RatController.RatState.Stunned)
            {
                target.StunTimer = 0f;
                target.RatStateVar.Value = RatController.RatState.Normal;
            }
            if (o.NoHunger)
            {
                StarveTimer.SetValue(target, 0f);
                if (Time.unscaledTime >= nextFeed)
                {
                    nextFeed = Time.unscaledTime + 1f;
                    if (target.HungerAmountVar.Value < 0.9f) target.ChangeHungerAmountServerRpc(1f - target.HungerAmountVar.Value);
                }
            }
        }

        public void Heal()
        {
            RatController r = p.LocalPlayer as RatController;
            if (!p.IsLocal(r)) throw new InvalidOperationException("请先进入游戏并控制一只老鼠");
            if (r.RatStateVar.Value == RatController.RatState.Dead) throw new InvalidOperationException("该老鼠已经死亡，恢复状态不会复活幽灵");
            r.Health = 1f; r.IsPoisoned = false; r.PoisonTimer = 0f; r.StunTimer = 0f;
            r.RatStateVar.Value = RatController.RatState.Normal;
            r.ChangeHungerAmountServerRpc(Mathf.Max(0f, 1f - r.HungerAmountVar.Value));
            r.SprintAmountVar.Value = 1f;
            p.Notify("已恢复当前老鼠的生命、体力和饱食度，并清除中毒与眩晕");
        }

        public void StorePosition()
        {
            PlayerController local = CheckTeleport();
            position = local.CharacterController.transform.position;
            rotation = GameCamera.instance != null ? GameCamera.instance.transform.rotation : local.transform.rotation;
            positionScene = SceneManager.GetActiveScene().handle;
            positionOwner = local.NetworkObjectId;
            hasPosition = true;
            p.Notify("位置已记录：" + Bookmark);
        }
        public void ReturnPosition()
        {
            PlayerController local = CheckTeleport();
            if (!hasPosition || positionScene != SceneManager.GetActiveScene().handle || positionOwner != local.NetworkObjectId)
                throw new InvalidOperationException("请先在当前地图、当前角色上记录位置");
            local.TeleportTo(position, rotation);
            if (GameCamera.instance != null) GameCamera.instance.HardsetPosition();
            p.Notify("已返回记录位置");
        }
        private PlayerController CheckTeleport()
        {
            PlayerController local = p.LocalPlayer;
            if (!p.Active || !p.IsLocal(local) || local.CharacterController == null) throw new InvalidOperationException("请开启总开关并进入游戏");
            RatController r = local as RatController;
            if (r != null && (r.IsHeldByChef || r.EnteredObject != null || r.RatStateVar.Value == RatController.RatState.Dead))
                throw new InvalidOperationException("被抓、藏在物品内或死亡时无法传送");
            return local;
        }

        public void Restore()
        {
            if (rat != null)
            {
                if (originalMovement != null) rat.Movement = originalMovement;
                rat.FallDamageHeight = fallHeight;
                rat.MaxInteractDistance = interactDistance;
                rat.InteractRadius = interactRadius;
            }
            if (clone != null) UnityEngine.Object.Destroy(clone);
            rat = null; clone = null; originalMovement = null;
            if (chef != null) { chef.Acceleration = chefAcceleration; chef.CrouchAcceleration = chefCrouch; chef.MaxGrabDistance = chefGrab; }
            chef = null;
        }
    }
}
