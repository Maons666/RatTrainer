using BepInEx;
using UnityEngine.InputSystem;

namespace RatAssist
{
    [BepInPlugin("local.ratgame.mapmarkers", "Rat Game Team Markers", "1.0.1")]
    [BepInProcess("Rat Game.exe")]
    public sealed class MarkersPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            AssistRuntime runtime=AssistRuntime.Acquire(AssistFeature.Pings,Logger);
            runtime.PingKey=Config.Bind("Marker","Key",Key.G,"准心实体表面标点；Shift + 此键清除自己的标点").Value;
            runtime.PingLifetime=UnityEngine.Mathf.Clamp(Config.Bind("Marker","LifetimeSeconds",30f,"标点持续秒数，5–120").Value,5f,120f);
            runtime.MaxOwnPings=UnityEngine.Mathf.Clamp(Config.Bind("Marker","Maximum",3,"每人最多保留的标点，1–5").Value,1,5);
            Logger.LogInfo("地图标点 1.0.1 已加载。默认 G 标点，Shift+G 清除；Steam 房间中的小鼠收发双方需更新标点插件，厨师仅自己可见。");
        }
        private void OnDestroy() { if(AssistRuntime.Instance!=null) AssistRuntime.Instance.Release(AssistFeature.Pings); }
    }
}
