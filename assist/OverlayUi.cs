using UnityEngine;
using UnityEngine.UI;

namespace RatAssist
{
    public static class OverlayUi
    {
        public static RectTransform Rect(string name,Transform parent,Vector2 size)
        {
            GameObject obj=new GameObject(name,typeof(RectTransform)); RectTransform rect=obj.GetComponent<RectTransform>();
            rect.SetParent(parent,false); rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f); rect.pivot=new Vector2(.5f,.5f); rect.sizeDelta=size; return rect;
        }
        public static Canvas Canvas(string name,int order)
        {
            GameObject obj=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            UnityEngine.Object.DontDestroyOnLoad(obj);
            Canvas canvas=obj.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=order;
            CanvasScaler scale=obj.GetComponent<CanvasScaler>(); scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scale.referenceResolution=new Vector2(1920,1080); scale.matchWidthOrHeight=.5f;
            return canvas;
        }
        public static Image Image(string name,Transform parent,Vector2 size,Color color)
        { RectTransform rect=Rect(name,parent,size); Image image=rect.gameObject.AddComponent<Image>(); image.color=color; image.raycastTarget=false; return image; }
        public static Text Text(string name,Transform parent,Vector2 size,Font font,int fontSize,Color color)
        {
            RectTransform rect=Rect(name,parent,size); Text text=rect.gameObject.AddComponent<Text>(); text.font=font; text.fontSize=fontSize; text.color=color;
            text.alignment=TextAnchor.MiddleCenter; text.raycastTarget=false; text.supportRichText=false; text.horizontalOverflow=HorizontalWrapMode.Wrap; text.verticalOverflow=VerticalWrapMode.Truncate; return text;
        }
    }
    public sealed class MapLabel
    {
        public RectTransform Root;
        private readonly Text name;
        private readonly RectTransform namePlate;
        public MapLabel(AssistRuntime app,Transform parent,bool avatar)
        {
            Root=OverlayUi.Rect(avatar?"RatAssist.Player":"RatAssist.Ping",parent,new Vector2(24,24));
            if(avatar)
            {
                OverlayUi.Image("Border",Root,new Vector2(27,27),new Color(.24f,.71f,.55f,1));
                RectTransform imageRect=OverlayUi.Rect("RatAvatar",Root,new Vector2(23,23)); RawImage image=imageRect.gameObject.AddComponent<RawImage>();
                image.texture=app.Avatar; image.uvRect=new Rect(.2f,.1f,.7f,.7f); image.raycastTarget=false;
            }
            else
            {
                Image diamond=OverlayUi.Image("Point",Root,new Vector2(14,14),new Color(1,.73f,.24f,1)); diamond.rectTransform.localEulerAngles=new Vector3(0,0,45);
                Outline outline=diamond.gameObject.AddComponent<Outline>(); outline.effectColor=new Color(.1f,.1f,.1f,.8f); outline.effectDistance=new Vector2(1,-1);
            }
            Image background=OverlayUi.Image("NamePlate",Root,new Vector2(124,23),new Color(.07f,.1f,.09f,.83f)); background.rectTransform.anchoredPosition=new Vector2(78,0);
            namePlate=background.rectTransform;
            name=OverlayUi.Text("Name",background.transform,new Vector2(116,22),app.UiFont,12,Color.white); name.resizeTextForBestFit=true; name.resizeTextMinSize=10; name.resizeTextMaxSize=12;
        }
        public void Set(Vector2 at,string text,Rect bounds)
        {
            Root.anchoredPosition=at; name.text=text;
            namePlate.anchoredPosition=new Vector2(at.x>bounds.xMax-145?-78:78,Mathf.Clamp(0,bounds.yMin+12-at.y,bounds.yMax-12-at.y));
            Root.gameObject.SetActive(true);
        }
        public void Hide() { if(Root!=null) Root.gameObject.SetActive(false); }
        public void Dispose() { if(Root!=null) UnityEngine.Object.Destroy(Root.gameObject); }
    }
}
