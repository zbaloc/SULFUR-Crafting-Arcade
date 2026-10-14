// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 zbaloc
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.World;
using PerfectRandom.Sulfur.Core.UI.Inventory;
using Object = UnityEngine.Object;

namespace SulfurCraftingArcade
{
    // Geometry and paint are generated locally; shaders/fonts come from loaded game assets.
    public sealed class ArcadeCabinetVisuals : MonoBehaviour
    {
        readonly List<Object> owned = new List<Object>();
        Material source;
        Material cream, olive, dark, red, screen;
        TMP_FontAsset font;

        public void Build(StampPurchaseStation vendor, ManualLogSource log)
        {
            var chest = Object.FindObjectsOfType<StashChest>();
            var renderers = chest.SelectMany(c => c.GetComponentsInChildren<MeshRenderer>(true))
                .Concat(vendor.GetComponentsInChildren<MeshRenderer>(true))
                .Concat(Object.FindObjectsOfType<MeshRenderer>());
            source = renderers.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m && m.shader &&
                m.shader.isSupported && !m.shader.name.Contains("Hidden/") &&
                !m.shader.name.Contains("UI/") && !m.shader.name.Contains("TextMeshPro") &&
                Enumerable.Range(0, m.shader.GetPropertyCount()).Any(i =>
                    m.shader.GetPropertyType(i) == ShaderPropertyType.Texture && IsAlbedo(m.shader.GetPropertyName(i))));
            if (!source) throw new InvalidOperationException("No supported church surface material found. Cabinet not rendered with a fallback/error shader.");
            log.LogInfo("Cabinet uses church material '" + source.name + "', shader '" + source.shader.name + "'.");
            cream = Paint("ivory enamel", new Color(0.76f, 0.72f, 0.55f));
            olive = Paint("olive enamel", new Color(0.34f, 0.37f, 0.24f));
            dark = Paint("charcoal trim", new Color(0.075f, 0.085f, 0.06f));
            red = Paint("oxide red", new Color(0.58f, 0.15f, 0.09f));
            screen = Paint("phosphor CRT", new Color(0.23f, 0.45f, 0.29f));
            foreach (var property in new[] { "_EmissionColor", "_EmissiveColor" })
                if (screen.HasProperty(property)) screen.SetColor(property, new Color(0.10f, 0.24f, 0.11f));
            var purchase=(StampPurchaseInventory)AccessTools.Field(typeof(StampPurchaseStation),"purchaseInventoryPrefab").GetValue(vendor);
            var row=purchase ? purchase.PurchaseListItemPrefab.GetComponent<StampPurchaseListItem>() : null;
            var label=row ? AccessTools.Field(typeof(StampPurchaseListItem),"label").GetValue(row) as TextMeshProUGUI : null;
            font=label ? label.font : Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f=>f && f.material && f.material.shader.isSupported);
            log.LogInfo("Cabinet signage uses " + (font ? font.name : "no available font"));

            // Sloping screen, projecting control deck and thick side cheeks form the cabinet silhouette.
            var outline = new[] { new Vector2(-0.43f,0), new Vector2(-0.43f,0.80f),
                new Vector2(-0.62f,0.91f), new Vector2(-0.62f,1.04f),
                new Vector2(-0.31f,1.54f), new Vector2(-0.49f,1.65f),
                new Vector2(-0.49f,1.97f), new Vector2(0.39f,1.97f), new Vector2(0.39f,0) };
            Profile("Left ivory cheek", outline, -0.51f, -0.40f, cream);
            Profile("Right ivory cheek", outline, 0.40f, 0.51f, cream);
            Box("Lower body", new Vector3(0,0.42f,-0.015f), new Vector3(0.80f,0.84f,0.79f), olive);
            Box("Back housing", new Vector3(0,1.37f,0.24f), new Vector3(0.80f,1.06f,0.28f), olive);
            Box("Solid cabinet lid",new Vector3(0,1.98f,-0.05f),new Vector3(1.04f,0.075f,0.92f),cream);
            Box("Lid front trim",new Vector3(0,1.951f,-0.515f),new Vector3(1.04f,0.025f,0.025f),dark);
            Box("Heavy plinth", new Vector3(0,0.045f,-0.02f), new Vector3(1.05f,0.09f,0.85f), dark);
            Box("Marquee shadow", new Vector3(0,1.79f,-0.435f), new Vector3(0.83f,0.32f,0.10f), dark);
            Box("Cream marquee", new Vector3(0,1.79f,-0.493f), new Vector3(0.75f,0.24f,0.025f), cream);
            const float screenTilt=31f;
            var screenCenter=new Vector3(0,1.30f,-0.459f);
            var screenRotation=Quaternion.Euler(screenTilt,0,0);
            Box("CRT surround", screenCenter, new Vector3(0.79f,0.55f,0.10f), dark, screenTilt);
            Box("CRT screen", screenCenter+screenRotation*new Vector3(0,0,-0.063f), new Vector3(0.66f,0.41f,0.025f), screen, screenTilt);
            Box("Control deck", new Vector3(0,0.985f,-0.40f), new Vector3(0.80f,0.09f,0.44f), cream);
            Box("Control lip", new Vector3(0,0.95f,-0.625f), new Vector3(0.83f,0.13f,0.04f), dark);
            Cylinder("Joystick socket", new Vector3(-0.20f,1.045f,-0.42f), new Vector3(0.13f,0.012f,0.13f), dark);
            Cylinder("Joystick stem", new Vector3(-0.20f,1.11f,-0.42f), new Vector3(0.035f,0.065f,0.035f), dark);
            Primitive("Joystick ball", PrimitiveType.Sphere, new Vector3(-0.20f,1.19f,-0.42f), Vector3.one*0.11f, red);
            Cylinder("Red button rim", new Vector3(0.18f,1.045f,-0.43f), new Vector3(0.15f,0.012f,0.15f), dark);
            Cylinder("Red button", new Vector3(0.18f,1.065f,-0.43f), new Vector3(0.12f,0.012f,0.12f), red);
            Cylinder("Ivory button", new Vector3(0.32f,1.065f,-0.42f), new Vector3(0.09f,0.012f,0.09f), cream);
            Box("Coin plate", new Vector3(0.22f,0.59f,-0.423f), new Vector3(0.18f,0.21f,0.028f), dark);
            Box("Coin slot", new Vector3(0.22f,0.63f,-0.442f), new Vector3(0.095f,0.013f,0.012f), cream);
            for (int i=0;i<5;i++) Box("Speaker vent",new Vector3(-0.20f,0.52f+i*0.027f,-0.424f),new Vector3(0.22f,0.01f,0.015f),dark);
            Box("Red service stripe",new Vector3(0,0.22f,-0.427f),new Vector3(0.77f,0.052f,0.012f),red);
            Sign("RECIPE ORACLE",new Vector3(0,1.80f,-0.512f),0.68f,0.13f,Color.black);
            Sign("CHURCH PROPERTY",new Vector3(0,0.31f,-0.447f),0.65f,0.07f,new Color(0.82f,0.78f,0.59f));
            Sign("READY?",screenCenter+screenRotation*new Vector3(0,0,-0.081f),0.50f,0.13f,new Color(0.75f,0.91f,0.58f),screenTilt);
            foreach(float x in new[]{-0.35f,0.35f}) foreach(float y in new[]{1.69f,1.90f})
                Primitive("Marquee screw",PrimitiveType.Sphere,new Vector3(x,y,-0.514f),new Vector3(0.023f,0.023f,0.009f),dark);
        }

        static bool IsAlbedo(string name)
        {
            string n=name.ToLowerInvariant();
            return n=="_maintex" || n.Contains("basemap") || n.Contains("albedo") || n.Contains("diffuse") || n.Contains("basecolor") ||
                new[]{"_backtexture","_downtexture","_fallbacktexture","_forwardtexture","_lefttexture","_righttexture","_uptexture"}.Contains(n);
        }
        Material Paint(string name, Color color)
        {
            var mat=new Material(source) { name="Arcade " + name };
            owned.Add(mat);
            bool tinted=false;
            string primary=new[]{"_Tint","_BaseColor","_Color","_TintColor","_MainColor"}.FirstOrDefault(mat.HasProperty);
            foreach(string property in new[]{"_Tint","_BaseColor","_Color","_TintColor","_MainColor"})
                if(mat.HasProperty(property)) { mat.SetColor(property,property==primary ? color : Color.white); tinted=true; }
            foreach(string property in new[]{"_BackColor","_DownColor","_FallbackColor","_ForwardColor","_LeftColor","_RightColor","_UpColor"})
                if(mat.HasProperty(property)) mat.SetColor(property,Color.white*(property=="_DownColor" ? 0.72f : property=="_UpColor" ? 1f : 0.88f));
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false) { name="Arcade smooth enamel",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
            owned.Add(texture);
            Color surface=tinted ? Color.white : color;
            texture.SetPixels(new[]{surface,surface,surface,surface}); texture.Apply();
            foreach(string property in new[]{"_BumpMap","_DetailNormalMap","_DetailAlbedoMap","_DetailMask"})
                if(mat.HasProperty(property))mat.SetTexture(property,null);
            mat.DisableKeyword("_NORMALMAP");mat.DisableKeyword("_DETAIL_MULX2");
            for(int i=0;i<mat.shader.GetPropertyCount();i++)
            {
                string property=mat.shader.GetPropertyName(i);
                if(mat.shader.GetPropertyType(i)==ShaderPropertyType.Texture && IsAlbedo(property))
                { mat.SetTexture(property,texture); mat.SetTextureScale(property,Vector2.one); mat.SetTextureOffset(property,Vector2.zero); }
            }
            return mat;
        }
        void Box(string n,Vector3 p,Vector3 s,Material m,float tilt=0)
        { var g=Primitive(n,PrimitiveType.Cube,p,s,m);g.transform.localRotation=Quaternion.Euler(tilt,0,0); }
        void Cylinder(string n,Vector3 p,Vector3 s,Material m) { Primitive(n,PrimitiveType.Cylinder,p,s,m); }
        GameObject Primitive(string n,PrimitiveType type,Vector3 p,Vector3 s,Material m)
        {
            var g=GameObject.CreatePrimitive(type);g.name=n;g.transform.SetParent(transform,false);
            g.transform.localPosition=p;g.transform.localScale=s;g.layer=gameObject.layer;
            Object.Destroy(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=m;return g;
        }
        void Profile(string name,Vector2[] points,float left,float right,Material material)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
            Action<Vector3,Vector3,Vector3> tri=(a,b,c)=> { int start=vertices.Count; vertices.AddRange(new[]{a,b,c});triangles.AddRange(new[]{start,start+2,start+1});foreach(var v in new[]{a,b,c})uv.Add(new Vector2(v.z,v.y)*0.7f); };
            // Triangulate each side with a central point inside this star-shaped profile.
            var center=new Vector2(0.12f,0.97f);
            for(int i=0;i<points.Length;i++)
            {
                var a=points[i];var b=points[(i+1)%points.Length];
                tri(new Vector3(left,center.y,center.x),new Vector3(left,a.y,a.x),new Vector3(left,b.y,b.x));
                tri(new Vector3(right,center.y,center.x),new Vector3(right,b.y,b.x),new Vector3(right,a.y,a.x));
                tri(new Vector3(left,a.y,a.x),new Vector3(right,a.y,a.x),new Vector3(right,b.y,b.x));
                tri(new Vector3(left,a.y,a.x),new Vector3(right,b.y,b.x),new Vector3(left,b.y,b.x));
            }
            var mesh=new Mesh {name="Arcade faceted side"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);
            var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(transform,false);g.layer=gameObject.layer;
            g.GetComponent<MeshFilter>().sharedMesh=mesh;g.GetComponent<MeshRenderer>().sharedMaterial=material;
        }
        void Sign(string value,Vector3 pos,float width,float height,Color color,float tilt=0)
        {
            if(!font)return;
            var g=new GameObject(value);g.transform.SetParent(transform,false);g.transform.localPosition=pos;g.transform.localRotation=Quaternion.Euler(tilt,0,0);
            var text=g.AddComponent<TextMeshPro>();text.font=font;text.fontSharedMaterial=font.material;text.text=value;
            text.rectTransform.sizeDelta=new Vector2(width,height);text.alignment=TextAlignmentOptions.Center;
            text.enableWordWrapping=false;text.overflowMode=TextOverflowModes.Overflow;
            text.enableAutoSizing=true;text.fontSizeMin=0.5f;text.fontSizeMax=12f;text.color=color;
            text.fontStyle=FontStyles.Bold;text.characterSpacing=1.5f;
            text.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
        }
        void OnDestroy() { foreach(var asset in owned)if(asset)Object.Destroy(asset); }
    }
}
