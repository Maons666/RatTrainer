using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RatAssist
{
    public sealed class MapOverlay
    {
        private readonly AssistRuntime app;
        private Camera camera;
        private RawImage original,map,fallbackMap;
        private Canvas fallbackCanvas;
        private RectTransform layer;
        private float findAt;
        private readonly List<MapLabel> players=new List<MapLabel>(),pings=new List<MapLabel>();
        public MapOverlay(AssistRuntime runtime) { app=runtime; }
        public void Reset()
        {
            ClearLabels(); if(fallbackCanvas!=null) UnityEngine.Object.Destroy(fallbackCanvas.gameObject);
            fallbackCanvas=null; fallbackMap=null; original=map=null; camera=null; findAt=0;
        }
        private void ClearLabels()
        {
            foreach(MapLabel label in players) label.Dispose(); foreach(MapLabel label in pings) label.Dispose(); players.Clear(); pings.Clear();
            if(layer!=null) UnityEngine.Object.Destroy(layer.gameObject); layer=null;
        }
        private void Hide()
        { if(layer!=null) layer.gameObject.SetActive(false); if(fallbackCanvas!=null) fallbackCanvas.gameObject.SetActive(false); }
        private void FindMap()
        {
            if(MinimapCamera.instance==null) return;
            camera=MinimapCamera.instance.GetComponent<Camera>();
            if(camera==null||camera.targetTexture==null||app.Screen==null) return;
            if(app.Screen.TodoList!=null)
                foreach(RawImage image in app.Screen.TodoList.GetComponentsInChildren<RawImage>(true))
                    if(image.texture==camera.targetTexture) { original=image; return; }
            foreach(RawImage image in app.Screen.GetComponentsInChildren<RawImage>(true))
                if(image.texture==camera.targetTexture) { original=image; return; }
        }
        private void MakeFallback()
        {
            if(fallbackCanvas!=null) return;
            fallbackCanvas=OverlayUi.Canvas("RatAssist.PersonalMap",14000);
            Image background=OverlayUi.Image("MapPanel",fallbackCanvas.transform,new Vector2(990,820),new Color(.06f,.08f,.07f,.96f));
            Text title=OverlayUi.Text("Title",background.transform,new Vector2(900,40),app.UiFont,21,Color.white); title.rectTransform.anchoredPosition=new Vector2(0,370);
            title.text=AssistRuntime.SideOf(AssistRuntime.Local)==Side.Chef?"阵营地图 · 厨师仅显示自己":"阵营地图 · 小鼠阵营";
            RectTransform rect=OverlayUi.Rect("Map",background.transform,new Vector2(900,700)); rect.anchoredPosition=new Vector2(0,-20);
            fallbackMap=rect.gameObject.AddComponent<RawImage>(); fallbackMap.texture=camera.targetTexture; fallbackMap.raycastTarget=false;
            float aspect=(float)camera.targetTexture.width/camera.targetTexture.height;
            rect.sizeDelta=aspect>900f/700f?new Vector2(900,900/aspect):new Vector2(700*aspect,700);
        }
        public void Tick(bool visible)
        {
            if(!visible||InputController.instance==null||!InputController.instance.GetShowTodo()) { Hide(); return; }
            if(camera==null || original==null && Time.unscaledTime>=findAt) { findAt=Time.unscaledTime+1f; FindMap(); }
            if(camera==null||camera.targetTexture==null) { Hide(); return; }
            bool ownMap=AssistRuntime.SideOf(AssistRuntime.Local)==Side.Chef||original==null;
            RawImage target;
            if(ownMap)
            {
                MakeFallback(); fallbackCanvas.gameObject.SetActive(true); target=fallbackMap;
                if(original==null&&app.Screen.TodoList!=null) app.Screen.TodoList.localScale=Vector3.zero;
            }
            else { if(fallbackCanvas!=null) fallbackCanvas.gameObject.SetActive(false); target=original; }
            if(target==null||!target.gameObject.activeInHierarchy) { Hide(); return; }
            if(map!=target||layer==null)
            {
                ClearLabels(); map=target; layer=OverlayUi.Rect("RatAssist.TeamLayer",map.rectTransform,Vector2.zero);
                layer.anchorMin=Vector2.zero; layer.anchorMax=Vector2.one; layer.offsetMin=layer.offsetMax=Vector2.zero; layer.SetAsLastSibling();
                layer.gameObject.AddComponent<RectMask2D>();
            }
            layer.gameObject.SetActive(true);
            PlayerController local=AssistRuntime.Local;
            int count=0;
            if((app.Features&AssistFeature.TeamMap)!=0)
                foreach(PlayerController actor in GameManager.instance.AllPlayerControllers)
                {
                    if(!AssistRuntime.IsCurrent(actor)||!AssistRuntime.SameTeam(local,actor)) continue;
                    Vector2 at;
                    if(!Project(actor.CharacterController.transform.position,out at)) continue;
                    while(players.Count<=count) players.Add(new MapLabel(app,layer,true));
                    players[count++].Set(at,AssistRuntime.PlayerName(actor)+(actor==local?" · 我":""),layer.rect);
                }
            for(int i=count;i<players.Count;i++) players[i].Hide();
            count=0;
            if((app.Features&AssistFeature.Pings)!=0)
                foreach(PingMark mark in app.Pings.Visible())
                {
                    Vector2 at; if(!Project(mark.Position,out at)) continue;
                    while(pings.Count<=count) pings.Add(new MapLabel(app,layer,false));
                    pings[count++].Set(at,AssistRuntime.PlayerName(mark.Owner),layer.rect);
                }
            for(int i=count;i<pings.Count;i++) pings[i].Hide();
        }
        private bool Project(Vector3 world,out Vector2 at)
        {
            world.y=TeamPolicy.MapPlaneY; Vector3 viewport=camera.WorldToViewportPoint(world); Rect uv=map.uvRect;
            float u=(viewport.x-uv.x)/uv.width,v=(viewport.y-uv.y)/uv.height; at=Vector2.zero;
            if(!TeamPolicy.Finite(u)||!TeamPolicy.Finite(v)||u<0||u>1||v<0||v>1) return false;
            Rect rect=layer.rect; at=new Vector2(rect.xMin+u*rect.width,rect.yMin+v*rect.height); return true;
        }
    }
}
