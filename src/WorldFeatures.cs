using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace RatTrainer
{
    public sealed class WorldFeatures
    {
        private sealed class BotOriginal
        {
            public float Speed, Crouch, Spot;
            public bool Frozen, WasStopped;
            public Dictionary<VisionCone, float> Vision = new Dictionary<VisionCone, float>();
        }
        private readonly Plugin p;
        private readonly Dictionary<ChefBot, BotOriginal> bots = new Dictionary<ChefBot, BotOriginal>();
        private readonly Dictionary<GunInteractable, int> ammo = new Dictionary<GunInteractable, int>();
        private readonly Dictionary<GameDifficultySettings, GameDifficultySettings> difficultyClones = new Dictionary<GameDifficultySettings, GameDifficultySettings>();
        private readonly List<NetworkObject> spawned = new List<NetworkObject>();
        private float cleanupAt;
        public WorldFeatures(Plugin plugin) { p = plugin; }

        public void Tick()
        {
            if (!Plugin.IsHost) { if (bots.Count != 0 || ammo.Count != 0) Restore(); return; }
            HashSet<GunInteractable> active = new HashSet<GunInteractable>();
            if (GameManager.instance != null)
            {
                foreach (ChefController chef in GameManager.instance.ChefControllers)
                {
                    if (chef == null || chef.HeldObject == null || !p.Bridge.ForPlayer(chef).InfiniteAmmo) continue;
                    GunInteractable gun = chef.HeldObject.GetComponent<GunInteractable>();
                    if (gun == null || !gun.IsSpawned) continue;
                    active.Add(gun);
                    if (!ammo.ContainsKey(gun)) ammo[gun] = gun.BulletCountVar.Value;
                    if (gun.BulletCountVar.Value < 20) gun.BulletCountVar.Value = 99;
                }
            }
            foreach (GunInteractable gun in ammo.Keys.ToArray())
            {
                if (gun != null && active.Contains(gun)) continue;
                if (gun != null && gun.IsSpawned) gun.BulletCountVar.Value = Math.Max(0, ammo[gun]);
                ammo.Remove(gun);
            }
            if (Time.unscaledTime >= cleanupAt)
            {
                cleanupAt = Time.unscaledTime + 3f;
                foreach (ChefBot bot in bots.Keys.ToArray()) if (bot == null) bots.Remove(bot);
                spawned.RemoveAll(delegate(NetworkObject obj) { return obj == null || !obj.IsSpawned; });
            }
        }

        public bool BeforeBotUpdate(ChefBot bot)
        {
            if (!Plugin.IsHost || bot == null || bot.Controller == null || !bot.Controller.IsBotControlled()) return true;
            WorldOptions o = p.Bridge.EffectiveGlobal;
            BotOriginal original;
            if (!bots.TryGetValue(bot, out original))
            {
                if (!o.AiTuning && !o.FreezeAi) return true;
                original = new BotOriginal { Speed = bot.NormalSpeed, Crouch = bot.CrouchSpeed, Spot = bot.RatSpotChance };
                foreach (VisionCone cone in bot.VisionCones) if (cone != null) original.Vision[cone] = cone.FarPlane;
                bots.Add(bot, original);
            }
            bot.NormalSpeed = original.Speed * (o.AiTuning ? o.AiSpeed : 1f);
            bot.CrouchSpeed = original.Crouch * (o.AiTuning ? o.AiSpeed : 1f);
            bot.RatSpotChance = o.AiTuning ? o.AiSpotChance : original.Spot;
            foreach (KeyValuePair<VisionCone, float> pair in original.Vision)
                if (pair.Key != null) pair.Key.FarPlane = o.AiTuning ? Mathf.Max(pair.Key.NearPlane + 0.01f, o.AiVisionRange) : pair.Value;
            if (o.FreezeAi)
            {
                if (!original.Frozen)
                {
                    original.WasStopped = bot.NavAgent.enabled && bot.NavAgent.isOnNavMesh && bot.NavAgent.isStopped;
                    bot.Controller.BotTargetPosition = bot.Controller.CharacterController.transform.position;
                    bot.Controller.Velocity = Vector3.zero;
                    original.Frozen = true;
                }
                if (bot.NavAgent.enabled && bot.NavAgent.isOnNavMesh) bot.NavAgent.isStopped = true;
                return false;
            }
            if (original.Frozen)
            {
                if (bot.NavAgent.enabled && bot.NavAgent.isOnNavMesh) bot.NavAgent.isStopped = original.WasStopped;
                original.Frozen = false;
            }
            return true;
        }

        public GameDifficultySettings Difficulty(GameDifficultySettings original)
        {
            WorldOptions o = p.Bridge.EffectiveGlobal;
            if (original == null || !o.TaskTuning) return original;
            GameDifficultySettings clone;
            if (!difficultyClones.TryGetValue(original, out clone))
            {
                clone = UnityEngine.Object.Instantiate(original);
                clone.name = original.name + " (RatTrainer)";
                difficultyClones.Add(original, clone);
            }
            float scale = o.TaskScale;
            clone.ToiletClogAmount = Mathf.Max(1, Mathf.CeilToInt(original.ToiletClogAmount * scale));
            clone.PushBottleTime = Mathf.Max(0.1f, original.PushBottleTime * scale);
            clone.SinkOverflowTime = Mathf.Max(0.1f, original.SinkOverflowTime * scale);
            // Computer puzzles are generated once at round start; retain the generated count.
            clone.ComputerPuzzleCount = original.ComputerPuzzleCount;
            clone.BeerTapOpenTime = Mathf.Max(0.1f, original.BeerTapOpenTime * scale);
            clone.BellRingAmount = Mathf.Max(1, Mathf.CeilToInt(original.BellRingAmount * scale));
            clone.ExtinguishCandleCount = Mathf.Max(1, Mathf.CeilToInt(original.ExtinguishCandleCount * scale));
            clone.AlarmPresses = Mathf.Max(1, Mathf.CeilToInt(original.AlarmPresses * scale));
            clone.GasLeakPercentage = Mathf.Clamp(original.GasLeakPercentage * scale, 0.05f, 0.99f);
            return clone;
        }

        public string Spawn(ulong sender, string category, int count)
        {
            if (!Plugin.IsHost || GameManager.instance == null || GameManager.instance.CurrentGameState.Value != GameState.InGame)
                throw new InvalidOperationException("物品只能在已开始的对局中生成");
            count = Limits.Int(count, 1, 10);
            PlayerController player = GameManager.instance.GetControlledPlayerControllerForClient(sender);
            if (player == null) throw new InvalidOperationException("找不到请求玩家的角色");
            spawned.RemoveAll(delegate(NetworkObject obj) { return obj == null || !obj.IsSpawned; });
            if (spawned.Count + count > 50) throw new InvalidOperationException("本局修改器生成的物品已达到 50 个上限");
            List<GameObject> candidates = new List<GameObject>();
            foreach (GameObject prefab in PrefabManager.instance.AllPrefabs)
            {
                if (prefab == null || prefab.GetComponent<NetworkObject>() == null) continue;
                bool matches = category == "food" ? prefab.GetComponent<Spawnable>() != null && prefab.GetComponent<RatEatable>() != null :
                    category == "bullet" ? prefab.GetComponent<BulletInteractable>() != null :
                    category == "gun" ? prefab.GetComponent<GunInteractable>() != null :
                    category == "item" && prefab.GetComponent<SpawnableItem>() != null;
                if (matches) candidates.Add(prefab);
            }
            if (candidates.Count == 0) throw new InvalidOperationException("未找到可联网生成的此类物品");
            Vector3 origin = player.CharacterController.transform.position;
            Vector3 forward = player.CharacterController.transform.forward;
            for (int i = 0; i < count; i++)
            {
                Vector3 at = origin + forward * (0.8f + i % 3 * 0.3f) + Vector3.right * (i / 3 * 0.3f) + Vector3.up * 0.5f;
                GameObject obj = UnityEngine.Object.Instantiate(candidates[UnityEngine.Random.Range(0, candidates.Count)], at, Quaternion.identity);
                NetworkObject network = obj.GetComponent<NetworkObject>();
                try { network.Spawn(true); spawned.Add(network); }
                catch { UnityEngine.Object.Destroy(obj); throw; }
            }
            return "已在角色附近生成 " + count + " 个物品";
        }

        public void Restore()
        {
            foreach (KeyValuePair<ChefBot, BotOriginal> pair in bots)
            {
                ChefBot bot = pair.Key;
                if (bot == null) continue;
                bot.NormalSpeed = pair.Value.Speed; bot.CrouchSpeed = pair.Value.Crouch; bot.RatSpotChance = pair.Value.Spot;
                foreach (KeyValuePair<VisionCone, float> cone in pair.Value.Vision) if (cone.Key != null) cone.Key.FarPlane = cone.Value;
                if (pair.Value.Frozen && bot.NavAgent.enabled && bot.NavAgent.isOnNavMesh) bot.NavAgent.isStopped = pair.Value.WasStopped;
            }
            bots.Clear();
            if (Plugin.IsHost) foreach (KeyValuePair<GunInteractable, int> pair in ammo)
                if (pair.Key != null && pair.Key.IsSpawned) pair.Key.BulletCountVar.Value = Math.Max(0, pair.Value);
            ammo.Clear();
            foreach (GameDifficultySettings clone in difficultyClones.Values) if (clone != null) UnityEngine.Object.Destroy(clone);
            difficultyClones.Clear();
        }
    }
}
