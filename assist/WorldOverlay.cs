using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RatAssist
{
    public sealed class WorldOverlay
    {
        private readonly AssistRuntime app;
        private Canvas canvas;
        private Text toast;
        private string message;
        private float toastUntil;
        private readonly List<MapLabel> labels=new List<MapLabel>();
        public WorldOverlay(AssistRuntime runtime) { app=runtime; }
        public void Notify(string text) { message=text; toastUntil=Time.unscaledTime+2.5f; }
        private void Ensure()
        {
            if(canvas!=null) return;
            canvas=OverlayUi.Canvas("RatAssist.WorldMarkers",15000);
            toast=OverlayUi.Text("Hint",canvas.transform,new Vector2(800,42),app.UiFont,18,new Color(1,.84f,.48f,1));
            toast.rectTransform.anchorMin=toast.rectTransform.anchorMax=new Vector2(.5f,.78f);
            Outline outline=toast.gameObject.AddComponent<Outline>(); outline.effectColor=new Color(0,0,0,.9f); outline.effectDistance=new Vector2(1,-1);
        }
        public void Tick(bool visible)
        {
            if(!visible) { if(canvas!=null) canvas.gameObject.SetActive(false); return; }
            Ensure(); canvas.gameObject.SetActive(true); toast.text=Time.unscaledTime<toastUntil?message:"";
            Camera camera=GameCamera.instance==null?null:GameCamera.instance.Cam;
            int index=0; Rect rect=((RectTransform)canvas.transform).rect;
            if(camera!=null)
                foreach(PingMark mark in app.Pings.Visible())
                {
                    Vector3 projected=camera.WorldToViewportPoint(mark.Position);
                    if(projected.z<=0||projected.x<0||projected.x>1||projected.y<0||projected.y>1) continue;
                    while(labels.Count<=index) labels.Add(new MapLabel(app,canvas.transform,false));
                    labels[index++].Set(new Vector2(rect.xMin+projected.x*rect.width,rect.yMin+projected.y*rect.height),AssistRuntime.PlayerName(mark.Owner),rect);
                }
            for(int i=index;i<labels.Count;i++) labels[i].Hide();
        }
        public void Reset() { foreach(MapLabel label in labels) label.Dispose(); labels.Clear(); toastUntil=0; if(canvas!=null) canvas.gameObject.SetActive(false); }
        public void Dispose() { Reset(); if(canvas!=null) UnityEngine.Object.Destroy(canvas.gameObject); canvas=null; }
    }
}
