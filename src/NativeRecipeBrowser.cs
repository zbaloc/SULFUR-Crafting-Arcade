// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 zbaloc
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.World;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Core.UI.Inventory;
using Object=UnityEngine.Object;

namespace SulfurCraftingArcade
{
    public sealed class NativeRecipeBrowser : MonoBehaviour
    {
        TMP_FontAsset font;
        Material fontMaterial;
        Image slotStyle,borderStyle,scrollTrackStyle,scrollHandleStyle;
        Color accent,slotColor,borderColor,textColor;
        RectTransform content;
        ScrollRect scroll;
        TextMeshProUGUI summary,footer;
        Action close;
        List<Match> matches=new List<Match>();
        IReadOnlyDictionary<ushort,int> stock;
        Dictionary<ushort,List<string>> locations;
        IReadOnlyDictionary<ushort,Sprite> artwork;
        string search="",fingerprint="";
        bool showUncraftable;
        RecipeFilter filter=RecipeFilter.All;
        RecipeSort sort=RecipeSort.Name;
        GameObject choicesPopup;
        string choicesOwner;
        float layoutWidth=1440f,layoutHeight=900f;
        float ListWidth=>layoutWidth-132f;
        float ViewHeight=>layoutHeight-380f;
        ushort? selection;
        Image selectedBorder;
        readonly Dictionary<Sprite,Sprite> slices=new Dictionary<Sprite,Sprite>();
        readonly HashSet<ushort> expanded=new HashSet<ushort>();
        readonly List<GroupView> groups=new List<GroupView>();
        sealed class GroupView
        {
            public OutputRecipeGroup Data;
            public RectTransform Root,Details;
            public TextMeshProUGUI Arrow;
            public float Height;
        }
        static T Field<T>(object o,string name)=>(T)AccessTools.Field(o.GetType(),name).GetValue(o);

        public static NativeRecipeBrowser Create(InventoryUI inventory,StampPurchaseStation vendor,Action close)
        {
            if(!inventory || !vendor)throw new InvalidOperationException("Church inventory UI is not loaded.");
            var purchase=Field<StampPurchaseInventory>(vendor,"purchaseInventoryPrefab");
            var prototype=purchase.PurchaseListItemPrefab.GetComponent<StampPurchaseListItem>();
            var label=Field<TextMeshProUGUI>(prototype,"label");
            var root=new GameObject("Crafting Arcade native recipe inventory",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            try
            {
                var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=500;
                var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution=new Vector2(1440,900);scaler.matchWidthOrHeight=0.5f;
                // Keep the reference layout readable while exposing the extra width/height.
                scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                var browser=root.AddComponent<NativeRecipeBrowser>();browser.close=close;
                browser.font=label.font;browser.fontMaterial=new Material(label.fontSharedMaterial);
                if(browser.fontMaterial.HasProperty("_FaceColor"))browser.fontMaterial.SetColor("_FaceColor",Color.white);
                browser.textColor=new Color(0.96f,0.91f,0.76f,1);
                browser.accent=new Color(1f,0.82f,0.38f,1);
                var item=Field<GameObject>(inventory,"inventoryItemPrefab").GetComponent<InventoryItem>();
                var visual=Resources.FindObjectsOfTypeAll<GridSlotVisual>().FirstOrDefault(v=>v && v.gameObject.scene.IsValid());
                browser.slotStyle=visual ? Field<Image>(visual,"backgroundImage") : Field<Image>(item,"containerImage");
                browser.borderStyle=visual ? Field<Image>(visual,"borderImage") : Field<Image>(prototype,"borderImage");
                browser.slotColor=visual ? Field<Color>(visual,"occupiedColor") : Field<Color>(item,"backgroundColor");
                browser.borderColor=visual ? Field<Color>(visual,"borderColor") : browser.accent;
                var nativeScrollbar=purchase.ItemScrollView.verticalScrollbar;
                if(!nativeScrollbar)nativeScrollbar=Object.FindObjectsOfType<Scrollbar>(true).FirstOrDefault(s=>s.direction==Scrollbar.Direction.BottomToTop || s.direction==Scrollbar.Direction.TopToBottom);
                if(nativeScrollbar){browser.scrollTrackStyle=nativeScrollbar.GetComponent<Image>();browser.scrollHandleStyle=nativeScrollbar.targetGraphic as Image;}
                browser.Build();return browser;
            }
            catch {Object.Destroy(root);throw;}
        }
        void Build()
        {
            var root=(RectTransform)transform;
            Canvas.ForceUpdateCanvases();
            layoutWidth=Math.Max(1440f,root.rect.width);layoutHeight=Math.Max(900f,root.rect.height);
            float leftWidth=(layoutWidth-180f)/2f,rightX=48f+leftWidth+36f,rightWidth=layoutWidth-49f-rightX;
            var backdrop=ImageAt(root,"Backdrop",0,0,layoutWidth,layoutHeight,null,new Color(0.015f,0.02f,0.015f,0.87f));
            backdrop.anchorMin=Vector2.zero;backdrop.anchorMax=Vector2.one;backdrop.offsetMin=backdrop.offsetMax=Vector2.zero;
            Text(root,"RECIPE ORACLE",48,30,layoutWidth-370f,48,34,accent);
            Button(root,"CLOSE [ESC]",layoutWidth-230f,32,180,43,close);
            var searchArea=Slot(root,"Search recipes",48,94,leftWidth,44,false);
            var input=searchArea.gameObject.AddComponent<TMP_InputField>();
            var value=Text(searchArea,"",12,5,leftWidth-28f,34,21,textColor);
            var placeholder=Text(searchArea,"Search recipes or ingredients…",12,5,leftWidth-28f,34,21,new Color(textColor.r,textColor.g,textColor.b,0.55f));
            input.textViewport=searchArea;input.textComponent=value;input.placeholder=placeholder;
            input.text=search;
            input.onValueChanged.AddListener(s=>{search=s;Render(true);});
            var option=Rect("Show uncraftable recipes",root,rightX,94,rightWidth,44);
            option.gameObject.AddComponent<Image>().color=Color.clear;
            var toggle=option.gameObject.AddComponent<Toggle>();
            var box=Slot(option,"Checkbox",0,6,32,32,false);
            var check=Text(box,"X",3,1,26,30,24,accent);check.alignment=TextAlignmentOptions.Center;
            toggle.targetGraphic=box.GetComponent<Image>();toggle.graphic=check;toggle.isOn=showUncraftable;
            check.enabled=showUncraftable;
            Text(option,"Show uncraftable recipes",46,2,rightWidth-53f,40,22,textColor);
            toggle.onValueChanged.AddListener(value=>{check.enabled=value;showUncraftable=value;Render(true);});
            Choice(root,"FILTER",48,153,leftWidth,new[]{"All items","Healing items","Other (non-HP) effects","Consumables","Equipment","Enchantments / oils","Attachments","Other items"},(int)filter,index=>{filter=(RecipeFilter)index;Render(true);});
            Choice(root,"SORT",rightX,153,rightWidth,new[]{"Name (A-Z)","Highest HP gain","Lowest HP gain","Fastest healing","Slowest healing","Most craftable items","Fewest craftable items","Most ingredient units","Fewest ingredient units"},(int)sort,index=>{sort=(RecipeSort)index;Render(true);});
            Text(root,"CRAFTABLE ITEM",65,211,370,30,18,accent);
            Text(root,"EXPAND AN ITEM TO VIEW ITS RECIPES",455,211,layoutWidth-540f,30,18,accent);
            var viewport=Rect("Recipe viewport",root,48,246,ListWidth,ViewHeight);
            viewport.gameObject.AddComponent<Image>().color=Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;
            scroll.horizontal=false;scroll.vertical=true;scroll.scrollSensitivity=45;scroll.movementType=ScrollRect.MovementType.Clamped;
            content=Rect("Long native inventory slots",viewport,0,0,ListWidth,ViewHeight);scroll.content=content;
            var track=ImageAt(root,"Recipe scrollbar",layoutWidth-69f,246,20,ViewHeight,scrollTrackStyle,new Color(0.09f,0.10f,0.075f,1));
            var slider=track.gameObject.AddComponent<Scrollbar>();slider.direction=Scrollbar.Direction.BottomToTop;
            var sliding=Rect("Sliding area",track,2,2,16,ViewHeight-4);
            var handle=ImageAt(sliding,"Native scrollbar handle",0,0,16,100,scrollHandleStyle,accent);
            handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;
            slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Image>();slider.numberOfSteps=0;
            scroll.verticalScrollbar=slider;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
            summary=Text(root,"Reading church storage…",48,layoutHeight-117f,layoutWidth-97f,28,18,accent);
            footer=Text(root,"Select a recipe for ingredient locations. Craft using the game's crafting station.",48,layoutHeight-78f,layoutWidth-97f,52,19,textColor);
        }
        void Update()
        {
            if(!content)return;
            var bounds=((RectTransform)transform).rect;
            if(Math.Abs(Math.Max(1440f,bounds.width)-layoutWidth)<1f && Math.Abs(Math.Max(900f,bounds.height)-layoutHeight)<1f)return;
            string status=summary.text;float offset=content.anchoredPosition.y;
            foreach(Transform child in transform){child.gameObject.SetActive(false);Object.Destroy(child.gameObject);}
            choicesPopup=null;selectedBorder=null;groups.Clear();
            Build();summary.text=status;Render(false);Reflow(offset);
        }
        public void SetData(List<Match> recipes,IReadOnlyDictionary<ushort,int> counts,Dictionary<ushort,List<string>> sources,IReadOnlyDictionary<ushort,Sprite> icons,string status)
        {
            matches=recipes;stock=counts;locations=sources;artwork=icons;summary.text=status;
            string stamp=string.Join(";",recipes.Select(m=>m.Recipe.Id+":"+m.Batches+":"+m.Recipe.Name))+"/"+
                string.Join(";",counts.OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value))+"/"+
                string.Join(";",sources.OrderBy(p=>p.Key).Select(p=>p.Key+":"+string.Join(",",p.Value)));
            if(stamp!=fingerprint){bool first=fingerprint.Length==0;fingerprint=stamp;Render(first);}
        }
        bool Visible(Match m)
        {
            if(!showUncraftable && m.Batches==0)return false;
            if(!RecipeOptions.InFilter(m.Recipe,filter))return false;
            return string.IsNullOrWhiteSpace(search) || m.Recipe.Name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0 || m.Recipe.Ingredients.Any(i=>i.Name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0);
        }
        void Render(bool resetScroll)
        {
            if(!content)return;
            float previous=content.anchoredPosition.y;
            foreach(Transform child in content){child.gameObject.SetActive(false);Object.Destroy(child.gameObject);}
            selectedBorder=null;
            groups.Clear();var visible=matches.Where(Visible).ToList();
            foreach(var data in OutputRecipeGroup.Build(visible,sort))
            {
                var view=new GroupView {Data=data,Root=Rect("Output group "+data.OutputId,content,0,0,ListWidth,64),Height=64};
                var header=Slot(view.Root,"Expand "+data.Name,0,0,ListWidth,64,false);
                var opacity=header.gameObject.AddComponent<CanvasGroup>();opacity.alpha=data.Recipes.Any(m=>m.Batches>0) ? 1f : 0.54f;
                view.Arrow=Text(header,"+",16,14,28,36,28,accent);
                Icon(header,data.OutputId,57,8,48);
                var item=data.Recipes[0].Recipe;
                Text(header,data.Name,122,item.HealingHp>0 ? 5 : 12,600,item.HealingHp>0 ? 31 : 40,25,textColor);
                if(item.HealingHp>0)Text(header,HealingLabel(item),122,35,630,22,17,accent);
                int ready=data.Recipes.Count(m=>m.Batches>0);
                Text(header,ready+" craftable  •  "+data.Recipes.Count+" recipe(s)",ListWidth-493f,15,469,34,19,accent);
                var click=header.gameObject.AddComponent<Button>();click.targetGraphic=header.GetComponent<Image>();click.transition=Selectable.Transition.None;
                click.onClick.AddListener(()=>ToggleGroup(view));groups.Add(view);
                if(expanded.Contains(data.OutputId))CreateDetails(view);
            }
            if(visible.Count==0)Text(content,showUncraftable ? "No recipes match your search." : "No craftable recipes. Check Show uncraftable recipes to see what is missing.",18,20,ListWidth-68f,50,23,textColor);
            Reflow(resetScroll ? 0f : previous);
            UpdateFooter();
        }
        void ToggleGroup(GroupView view)
        {
            float previous=content.anchoredPosition.y;
            if(expanded.Remove(view.Data.OutputId))
            {
                if(view.Details){view.Details.gameObject.SetActive(false);Object.Destroy(view.Details.gameObject);}
                view.Details=null;view.Height=64;view.Arrow.text="+";
            }
            else {expanded.Add(view.Data.OutputId);CreateDetails(view);}
            Reflow(previous);
        }
        void CreateDetails(GroupView view)
        {
            view.Arrow.text="-";
            view.Details=Rect("Recipes loaded on expansion",view.Root,0,72,ListWidth,1);
            float y=0;
            foreach(var match in view.Data.Recipes)
            {
                var m=match;int ingredientRows=Math.Max(1,(m.Required.Count+3)/4);
                float height=Math.Max(106,ingredientRows*65+18);
                var row=Slot(view.Details,"Recipe "+m.Recipe.Id,0,y,ListWidth,height,selection==m.Recipe.Id);
                var rowBorder=row.GetComponentsInChildren<Image>().First(i=>i.name=="Native slot border");
                if(selection==m.Recipe.Id)selectedBorder=rowBorder;
                var group=row.gameObject.AddComponent<CanvasGroup>();group.alpha=m.Batches>0 ? 1f : 0.54f;group.interactable=true;group.blocksRaycasts=true;
                var button=row.gameObject.AddComponent<Button>();button.targetGraphic=row.GetComponent<Image>();button.transition=Selectable.Transition.None;
                button.onClick.AddListener(()=>{
                    if(selectedBorder)selectedBorder.color=borderColor;
                    selection=m.Recipe.Id;selectedBorder=rowBorder;rowBorder.color=accent;UpdateFooter();
                });
                Icon(row,m.Recipe.OutputId,12,12,78);
                Text(row,m.Recipe.Name,108,13,287,35,23,textColor);
                Text(row,"×"+m.Recipe.OutputQuantity+"  •  "+(m.Recipe.HealingHp>0 ? HealingLabel(m.Recipe) : m.Recipe.Category),108,49,287,24,17,accent);
                Text(row,m.Batches>0 ? m.Batches+" available batch(es)" : "Missing ingredients",108,77,287,22,17,textColor);
                int index=0;
                foreach(var required in m.Required)
                {
                    int owned=0;if(stock!=null)stock.TryGetValue(required.Key,out owned);
                    float pitch=(ListWidth-428f)/4f;
                    float x=412+(index%4)*pitch;float iy=10+(index/4)*65;
                    var cell=Slot(row,"Ingredient "+required.Key,x,iy,pitch-6f,59,false);
                    Icon(cell,required.Key,5,7,43);
                    string name=m.Recipe.Ingredients.First(i=>i.Id==required.Key).Name;
                    Text(cell,name,55,5,pitch-68f,25,17,textColor);
                    Text(cell,owned+" / "+required.Value,55,32,pitch-68f,22,17,owned>=required.Value ? accent : new Color(0.95f,0.60f,0.46f));
                    index++;
                }
                y+=height+10;
            }
            view.Details.sizeDelta=new Vector2(ListWidth,y);view.Height=72+y;
        }
        void Reflow(float scrollOffset)
        {
            float y=0;
            foreach(var view in groups)
            {view.Root.anchoredPosition=new Vector2(0,-y);view.Root.sizeDelta=new Vector2(ListWidth,view.Height);y+=view.Height+10;}
            content.sizeDelta=new Vector2(ListWidth,Math.Max(ViewHeight,y));
            Canvas.ForceUpdateCanvases();
            float range=Math.Max(0,y-ViewHeight);
            scroll.verticalNormalizedPosition=range>0 ? 1f-Mathf.Clamp(scrollOffset,0,range)/range : 1f;
        }
        void UpdateFooter()
        {
            var chosen=selection.HasValue ? matches.FirstOrDefault(m=>m.Recipe.Id==selection.Value) : null;
            if(chosen!=null)
            {
                var places=chosen.Required.Keys.Select(id=>{
                    List<string> source;string name=chosen.Recipe.Ingredients.First(i=>i.Id==id).Name;
                    return name+": "+(locations.TryGetValue(id,out source) ? string.Join(", ",source) : "missing");});
                footer.text=string.Join("  •  ",places);
            }
        }
        void Icon(RectTransform parent,ushort id,float x,float y,float size)
        {
            Sprite sprite=null;if(artwork!=null)artwork.TryGetValue(id,out sprite);
            var image=ImageAt(parent,"Artwork "+id,x,y,size,size,null,Color.white).GetComponent<Image>();
            image.sprite=sprite;image.enabled=sprite;image.preserveAspect=true;image.raycastTarget=false;
        }
        RectTransform Slot(RectTransform parent,string name,float x,float y,float w,float h,bool selected)
        {
            var root=ImageAt(parent,name,x,y,w,h,null,Color.clear);
            var background=ImageAt(root,"Inset slot fill",1.5f,1.5f,Math.Max(1,w-3),Math.Max(1,h-3),slotStyle,slotColor);
            background.GetComponent<Image>().raycastTarget=false;
            var border=ImageAt(root,"Native slot border",0,0,w,h,borderStyle,selected ? accent : borderColor);
            border.GetComponent<Image>().raycastTarget=false;return root;
        }
        RectTransform ImageAt(RectTransform parent,string name,float x,float y,float w,float h,Image style,Color color)
        {
            var rect=Rect(name,parent,x,y,w,h);var image=rect.gameObject.AddComponent<Image>();
            if(style && style.sprite)
            {
                var sprite=style.sprite;
                if(sprite.border==Vector4.zero)
                {
                    Sprite sliced;
                    if(!slices.TryGetValue(sprite,out sliced))
                    {
                        // Preserve native corners rather than stretching them across the long row.
                        try
                        {
                            var region=sprite.textureRect;float edge=Math.Min(6f,Math.Min(region.width,region.height)*0.2f);
                            sliced=Sprite.Create(sprite.texture,region,new Vector2(0.5f,0.5f),sprite.pixelsPerUnit,0,SpriteMeshType.FullRect,new Vector4(edge,edge,edge,edge));
                        }
                        catch(UnityException){sliced=sprite;}
                        slices[sprite]=sliced;
                    }
                    sprite=sliced;
                }
                image.sprite=sprite;image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=style.pixelsPerUnitMultiplier;
            }
            // A fresh UI material allows the new viewport to apply its own mask/stencil state.
            image.color=color;return rect;
        }
        TextMeshProUGUI Text(RectTransform parent,string value,float x,float y,float w,float h,float size,Color color)
        {
            var rect=Rect(value,parent,x,y,w,h);var text=rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font=font;text.fontSharedMaterial=fontMaterial;text.color=color;text.text=value;text.raycastTarget=false;
            text.alignment=TextAlignmentOptions.MidlineLeft;text.enableWordWrapping=false;text.overflowMode=TextOverflowModes.Ellipsis;
            text.enableAutoSizing=true;text.fontSizeMax=size;text.fontSizeMin=size*0.8f;return text;
        }
        Button Button(RectTransform parent,string value,float x,float y,float w,float h,Action action)
        {
            var rect=Slot(parent,value,x,y,w,h,false);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Image>();
            button.onClick.AddListener(()=>action());var text=Text(rect,value,6,3,w-12,h-6,18,textColor);text.alignment=TextAlignmentOptions.Center;return button;
        }
        static string HealingLabel(Recipe recipe)
        {
            string value=recipe.HealingHp.ToString("0.#")+" HP/item";
            if(recipe.InstantHp>0)value+=" • "+recipe.InstantHp.ToString("0.#")+" instant HP";
            if(recipe.HealingRate>0)value+=" • "+recipe.HealingRate.ToString("0.##")+" HP/s avg";
            return value;
        }
        void Choice(RectTransform parent,string title,float x,float y,float width,string[] options,int initial,Action<int> changed)
        {
            int selected=initial;
            TextMeshProUGUI caption=null;
            var button=Button(parent,"",x,y,width,44,()=>{
                bool same=choicesPopup && choicesOwner==title;
                if(choicesPopup){Object.Destroy(choicesPopup);choicesPopup=null;}
                if(same)return;
                choicesOwner=title;
                var popup=Rect(title+" dropdown overlay",(RectTransform)transform,0,0,layoutWidth,layoutHeight);choicesPopup=popup.gameObject;
                var blocker=popup.gameObject.AddComponent<Image>();blocker.color=Color.clear;
                var dismiss=popup.gameObject.AddComponent<Button>();dismiss.transition=Selectable.Transition.None;
                dismiss.onClick.AddListener(()=>{Object.Destroy(choicesPopup);choicesPopup=null;});
                var menu=Slot(popup,title+" choices",x,y+47,width,options.Length*37+8,false);
                menu.Find("Inset slot fill").GetComponent<Image>().color=new Color(0.07f,0.075f,0.055f,0.98f);
                for(int i=0;i<options.Length;i++)
                {
                    int index=i;
                    var option=Button(menu,(index==selected ? "> " : "")+options[index],5,4+i*37,width-10,35,()=>{
                        selected=index;caption.text=title+": "+options[index]+"  v";
                        Object.Destroy(choicesPopup);choicesPopup=null;changed(index);
                    });
                    var label=option.GetComponentInChildren<TextMeshProUGUI>();label.alignment=TextAlignmentOptions.MidlineLeft;label.color=index==selected ? accent : textColor;
                }
                popup.SetAsLastSibling();
            });
            caption=button.GetComponentInChildren<TextMeshProUGUI>();caption.alignment=TextAlignmentOptions.MidlineLeft;
            caption.text=title+": "+options[selected]+"  v";
        }
        RectTransform Rect(string name,RectTransform parent,float x,float y,float w,float h)
        {
            var g=new GameObject(name,typeof(RectTransform));var rect=(RectTransform)g.transform;rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);return rect;
        }
        void OnDisable(){if(choicesPopup)Object.Destroy(choicesPopup);choicesPopup=null;}
        void OnDestroy(){foreach(var pair in slices)if(pair.Value && pair.Key!=pair.Value)Object.Destroy(pair.Value);if(fontMaterial)Object.Destroy(fontMaterial);}
    }
}
