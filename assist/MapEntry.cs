using BepInEx;

namespace RatAssist
{
    [BepInPlugin("local.ratgame.teammap", "Rat Game Team Map", "1.0.0")]
    [BepInProcess("Rat Game.exe")]
    public sealed class TeamMapPlugin : BaseUnityPlugin
    {
        private void Awake()
        { AssistRuntime.Acquire(AssistFeature.TeamMap,Logger); Logger.LogInfo("阵营实时地图已加载。按原游戏 Tab 显示地图；小鼠仅显示小鼠阵营，厨师只显示自己，使用 X/Z 平面位置。"); }
        private void OnDestroy() { if(AssistRuntime.Instance!=null) AssistRuntime.Instance.Release(AssistFeature.TeamMap); }
    }
}
