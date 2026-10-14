// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 zbaloc
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.World;
using PerfectRandom.Sulfur.Core.Units;
using PerfectRandom.Sulfur.Core.UI.Inventory;
using Object = UnityEngine.Object;

namespace SulfurCraftingArcade
{
    [BepInPlugin("blake.sulfur.craftingarcade", "Crafting Arcade", "1.0.3")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal ItemDefinition PurchaseItem;
        Texture2D purchaseArtworkTexture;
        Sprite purchaseArtwork;
        internal ConfigEntry<int> Price;
        ConfigEntry<Vector3> Offset;
        ConfigEntry<float> Yaw;
        ConfigEntry<bool> AcrossRoom;
        ConfigEntry<bool> DiagnosticKey;
        ConfigEntry<bool> OnlyUnlockedRecipes;
        const string UnlockKey = "blake.sulfur.craftingarcade.unlocked.v1";
        // A distinct lock owner prevents closing the browser from releasing another UI's locks.
        const LockStatePadlock LockOwner = (LockStatePadlock)1396916545;
        Harmony harmony;
        GameManager manager;
        GameObject cabinet;
        NativeRecipeBrowser browser;
        StampPurchaseStation vendor;
        bool opened;
        float nextScan;
        List<Recipe> recipes = new List<Recipe>();
        List<Match> matches = new List<Match>();
        Dictionary<ushort, int> stock = new Dictionary<ushort, int>();
        Dictionary<ushort, List<string>> locations = new Dictionary<ushort, List<string>>();
        Dictionary<ushort, Sprite> artwork = new Dictionary<ushort, Sprite>();
        string status = "";
        static List<ItemDefinition> Offers(StampPurchaseStation station) =>
            AccessTools.Field(typeof(StampPurchaseStation), "purchasableItems").GetValue(station) as List<ItemDefinition>;

        internal bool Purchased => SulfurSaveState.active != null &&
            SulfurSaveState.active.ENDLESS_UPGRADES.ContainsKey(UnlockKey);

        void Awake()
        {
            Instance = this;
            Price = Config.Bind("Machine", "StampPrice", 10, "One-time stamp price; minimum 0.");
            Offset = Config.Bind("Machine", "VendorLocalOffset", new Vector3(2.2f, 0, 0), "Cabinet position relative to the church stamp vendor. Adjust if it overlaps furniture.");
            Yaw = Config.Bind("Machine", "RotationOffset", -25f, "Additional cabinet rotation in degrees. The release default faces toward the center carpet.");
            AcrossRoom = Config.Bind("Machine", "PlaceAcrossRoom", true, "Place inside the church beyond the LEFT front pew, facing diagonally toward the center. Disable to use VendorLocalOffset.");
            DiagnosticKey = Config.Bind("Debug", "EnableF8", false, "Allow F8 to open the viewer at church without buying it, for testing.");
            OnlyUnlockedRecipes = Config.Bind("Recipes", "OnlyUnlockedRecipes", true, "Only show recipes learned by the active save. False reveals all recipes for testing. The uncraftable checkbox never overrides this restriction.");
            harmony = new Harmony("blake.sulfur.craftingarcade");
            harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo("Crafting Arcade 1.0.3 by zbaloc loaded.");
        }

        void Update()
        {
            if (!manager) manager = Object.FindObjectOfType<GameManager>();
            if (!manager || !manager.IsPlayerAtChurch || SulfurSaveState.active == null)
            {
                Close();
                if (cabinet) Object.Destroy(cabinet);
                cabinet = null;
                vendor = null;
                return;
            }
            if (Keyboard.current != null && opened && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
            if (Keyboard.current != null && DiagnosticKey.Value && Keyboard.current.f8Key.wasPressedThisFrame) Open();
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.75f;
            try
            {
                EnsureVendor();
                if (Purchased && !cabinet && vendor) BuildCabinet();
                if (opened) Refresh();
            }
            catch (Exception e)
            {
                status = "Could not read game data. See BepInEx/LogOutput.log.";
                Logger.LogError(e);
                nextScan = Time.unscaledTime + 5f;
            }
        }

        void EnsureVendor()
        {
            if (!vendor)
                vendor = Object.FindObjectsOfType<StampPurchaseStation>().FirstOrDefault(s =>
                    Offers(s) != null && Offers(s).Any(i => i && i.unlocksCheckpointWhenBroughtToChurch && !i.isEndlessUpgrade));
            if (!vendor) return;
            if (!PurchaseItem)
            {
                PurchaseItem = ScriptableObject.CreateInstance<ItemDefinition>();
                PurchaseItem.name = "CraftingArcadePurchase";
                PurchaseItem.displayName = "Crafting Oracle";
                PurchaseItem.description = "A recipe oracle for your backpack and church chests.";
                PurchaseItem.flavor = "INSERT FAITH. RECEIVE DINNER.";
                PurchaseItem.excludeFromLocalization = true;
                PurchaseItem.basePrice = Math.Max(0, Price.Value);
                PurchaseItem.artwork = LoadPurchaseArtwork();
                PurchaseItem.inventorySize = new Vector2Int(1, 1);
                PurchaseItem.stackSize = 1;
                PurchaseItem.includedInDemo = true;
                PurchaseItem.includedInEarlyAccess = true;
                // This display-only offer never enters inventories or the game's item database.
                PurchaseItem.id = ItemId.None;
            }
            if (!Offers(vendor).Contains(PurchaseItem))
            {
                Offers(vendor).Add(PurchaseItem);
                Logger.LogInfo("Added Crafting Arcade offer to " + vendor.name);
            }
        }

        Sprite LoadPurchaseArtwork()
        {
            if(purchaseArtwork)return purchaseArtwork;
            using(var source=typeof(Plugin).Assembly.GetManifestResourceStream("SulfurCraftingArcade.CraftingOracle.png"))
            {
                if(source==null)throw new InvalidOperationException("Crafting Oracle purchase artwork is missing from the mod.");
                var bytes=new byte[checked((int)source.Length)];int offset=0;
                while(offset<bytes.Length)
                {
                    int count=source.Read(bytes,offset,bytes.Length-offset);
                    if(count==0)throw new System.IO.EndOfStreamException("Incomplete Crafting Oracle artwork.");
                    offset+=count;
                }
                purchaseArtworkTexture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                purchaseArtworkTexture.name="Crafting Oracle purchase thumbnail";
                purchaseArtworkTexture.filterMode=FilterMode.Bilinear;
                purchaseArtworkTexture.wrapMode=TextureWrapMode.Clamp;
                if(!ImageConversion.LoadImage(purchaseArtworkTexture,bytes,true))
                {Object.Destroy(purchaseArtworkTexture);purchaseArtworkTexture=null;throw new InvalidOperationException("Could not decode Crafting Oracle artwork.");}
                // Frame the complete cabinet, trimming unused transparent space and extraction specks.
                float width=purchaseArtworkTexture.width,height=purchaseArtworkTexture.height;
                purchaseArtwork=Sprite.Create(purchaseArtworkTexture,new Rect(width*0.25f,height*0.06875f,width*0.4765625f,height*0.8828125f),new Vector2(0.5f,0.5f),100f,0,SpriteMeshType.FullRect);
                purchaseArtwork.name="Crafting Oracle";
                return purchaseArtwork;
            }
        }

        internal bool Buy()
        {
            if (Purchased) return false;
            if (!manager || !manager.PlayerUnit || SulfurSaveState.active == null) return false;
            int price = Math.Max(0, Price.Value);
            var stats = manager.PlayerUnit.Stats;
            if (stats.GetStamps() < price) return false;
            // Use the game's currency API so resource displays and listeners are notified.
            stats.ModifyStamps(-price, this);
            SulfurSaveState.active.ENDLESS_UPGRADES[UnlockKey] = 1;
            SulfurSaveState.active.WORLD_RESOURCES = stats.SerializedWorldResourceData();
            SulfurSaveState.active.WORLD_RESOURCES_STASH = stats.SerializedWorldResourceStashData();
            SulfurSaveState.WriteActiveToDisk(result => Logger.LogInfo("Arcade purchase save result: " + result));
            if (vendor) AccessTools.Method(typeof(StampPurchaseStation), "PopulatePurchaseMenu").Invoke(vendor, null);
            return true;
        }

        void BuildCabinet()
        {
            Vector3 position=vendor.transform.TransformPoint(Offset.Value);
            Quaternion rotation=vendor.transform.rotation * Quaternion.Euler(0,Yaw.Value,0);
            if(AcrossRoom.Value)FindAcrossRoomPlacement(out position,out rotation);
            cabinet = new GameObject("Crafting Arcade");
            cabinet.transform.position = position;
            cabinet.transform.rotation = rotation;
            cabinet.layer = vendor.gameObject.layer;
            var collision = cabinet.AddComponent<BoxCollider>();
            collision.center = new Vector3(0, 1.01f, 0);
            collision.size = new Vector3(1.04f, 2.02f, 0.92f);
            try { cabinet.AddComponent<ArcadeCabinetVisuals>().Build(vendor, Logger); }
            catch { Object.Destroy(cabinet); cabinet = null; throw; }
            var interaction = cabinet.AddComponent<ArcadeInteraction>();
            interaction.showCustomText = true;
            interaction.customText = "Browse recipes";
            interaction.showGenericInteractIcon = true;
            interaction.shouldFollowObject = true;
            interaction.SingleUseInteractable = false;
            var aim = new GameObject("Interaction aim");
            aim.transform.SetParent(cabinet.transform, false);
            aim.transform.localPosition = new Vector3(0, 1.2f, -0.5f);
            interaction.aimPoint = aim.transform;
            var above = new GameObject("Interaction label");
            above.transform.SetParent(cabinet.transform, false);
            // The native indicator adds its own overhead height to this anchor.
            above.transform.localPosition = new Vector3(0, 0.1f, -0.5f);
            interaction.abovePoint = above.transform;
            var interactions = Object.FindObjectOfType<InteractionManager>();
            if (interactions) interactions.AddInteractable(interaction);
            Logger.LogInfo("Spawned cabinet at " + cabinet.transform.position);
        }

        void FindAcrossRoomPlacement(out Vector3 position,out Quaternion rotation)
        {
            // The church prefab is aligned to +X at its entrance and -X at the stage.
            // Walking toward the stage, the left pews lie along -Z.
            var church=Object.FindObjectsOfType<Transform>(true).Where(t=>t.name=="Church")
                .OrderBy(t=>(t.position-vendor.transform.position).sqrMagnitude).FirstOrDefault();
            if(!church || Vector3.Distance(church.position,vendor.transform.position)>24)
                throw new InvalidOperationException("Church interior anchor has not loaded yet.");
            Vector3 target=church.TransformPoint(new Vector3(-3.95f,-0.29f,-3.3f));
            Vector3 lastWorking=church.TransformPoint(new Vector3(-3.7f,-0.29f,-3.1f));
            // Restrict the search to the small interior gap beyond the LEFT front pew.
            // First try only a modest nudge, then the exact target/search used by the confirmed working build.
            var candidates=new List<Vector3>{target,lastWorking};
            foreach(float towardStage in new[]{0f,0.5f,1f,1.5f})foreach(float sideways in new[]{0f,0.5f,-0.5f})
                candidates.Add(lastWorking+church.TransformVector(new Vector3(-towardStage,0,sideways)));
            foreach(Vector3 probe in candidates)
            {
                Vector3 candidate=probe;
                RaycastHit floor;
                if(!Physics.Raycast(candidate+Vector3.up*0.65f,Vector3.down,out floor,1.3f,manager.geometryMask,QueryTriggerInteraction.Ignore))continue;
                // Main hall floor is about 0.3m below the Church anchor. Neither stage nor exterior qualify.
                if(Math.Abs(floor.point.y-target.y)>0.25f || floor.normal.y<0.95f)continue;
                candidate.y=floor.point.y+0.015f;
                Vector3 inward=church.TransformPoint(new Vector3(2f,0,0))-candidate;inward.y=0;
                Quaternion facing=Quaternion.LookRotation(-inward.normalized,Vector3.up)*Quaternion.Euler(0,Yaw.Value,0);
                if(Physics.CheckBox(candidate+Vector3.up*1.07f,new Vector3(0.56f,0.95f,0.48f),facing,manager.geometryMask,QueryTriggerInteraction.Ignore))continue;
                bool level=true;
                foreach(var offset in new[]{new Vector3(-0.54f,0,-0.47f),new Vector3(0.54f,0,-0.47f),new Vector3(-0.54f,0,0.47f),new Vector3(0.54f,0,0.47f),new Vector3(0,0,-1.1f)})
                {
                    RaycastHit edge;
                    if(!Physics.Raycast(candidate+facing*offset+Vector3.up*0.6f,Vector3.down,out edge,1.1f,manager.geometryMask,QueryTriggerInteraction.Ignore) || Math.Abs(edge.point.y-floor.point.y)>0.08f)
                    {level=false;break;}
                }
                if(!level)continue;
                if(Physics.CheckBox(candidate+inward.normalized*1.1f+Vector3.up,new Vector3(0.35f,0.85f,0.35f),Quaternion.identity,manager.geometryMask,QueryTriggerInteraction.Ignore))continue;
                position=candidate;rotation=facing;
                Logger.LogInfo("Placed inside beyond the left front pew: " + position + ", facing diagonally toward the hall center.");return;
            }
            throw new InvalidOperationException("Left front pew interior gap is obstructed or not loaded; cabinet will retry here rather than move outside.");
        }
        internal void Open()
        {
            if (opened || !manager || !manager.IsPlayerAtChurch) return;
            opened = true;
            manager.ModifyControllerLock(LockOwner, true);
            manager.ModifyCursorState(LockOwner, true);
            try
            {
                if (!browser) browser = NativeRecipeBrowser.Create(GetInventoryUI(), vendor, Close);
                browser.gameObject.SetActive(true);
                Refresh();
                Logger.LogInfo("Arcade recipe browser opened.");
            }
            catch (Exception e) { Logger.LogError(e); Close(); }
        }

        void Close()
        {
            if (!opened) return;
            opened = false;
            if (browser) browser.gameObject.SetActive(false);
            if (manager)
            {
                manager.ModifyControllerLock(LockOwner, false);
                manager.ModifyCursorState(LockOwner, false);
            }
        }

        void Refresh()
        {
            var ui = GetInventoryUI();
            if (!ui || !ui.PlayerBackpackGrid) { status = "Waiting for player inventory."; return; }
            stock.Clear(); locations.Clear(); artwork.Clear();
            Count(ui.PlayerBackpackGrid.GetSerializedContents(), "Backpack");
            var live = AccessTools.Field(typeof(InventoryUI), "StashInventoryItemGrids").GetValue(ui) as Dictionary<string, ItemGrid>;
            var saved = SulfurSaveState.active.STASH;
            var counted = new HashSet<string>();
            // Only storage registered to an accessible church chest is included.
            foreach (var stash in Object.FindObjectsOfType<PlayerStash>())
            {
                var unlock = stash.GetComponentInParent<UnlockableStorage>();
                if (unlock && !(bool)AccessTools.Field(typeof(UnlockableStorage), "value").GetValue(unlock)) continue;
                string id = stash.StashIdentifier;
                if (string.IsNullOrEmpty(id) || !counted.Add(id)) continue;
                ItemGrid grid;
                if (live != null && live.TryGetValue(id, out grid) && grid)
                    Count(grid.GetSerializedContents(), "Chest: " + id);
                else if (saved.ContainsKey(id) && saved[id] != null)
                    Count(saved[id].list, "Chest: " + id);
            }
            var db = Resources.FindObjectsOfTypeAll<RecipeDatabase>().FirstOrDefault();
            if (!db) { matches.Clear(); status = "Recipe database is not loaded yet."; return; }
            recipes = new List<Recipe>();
            foreach (var r in db.GetRawList())
            {
                if (!r.canBeCrafted || r.itemsNeeded == null || r.itemsNeeded.Count == 0) continue;
                if (OnlyUnlockedRecipes.Value && !r.IsLearned) continue;
                var output = r.createsItem.GetAsset();
                if (!output || !output.AllowedInGame) continue;
                artwork[r.createsItem.value] = output.artwork;
                var recipe = new Recipe { Id = r.id.value, OutputId = r.createsItem.value, Name = output.LocalizedDisplayName, Category = r.type.ToString(), OutputQuantity = r.quantityCreated };
                recipe.ItemKind=output.ItemType.ToString();
                float regen=0,duration=0;
                if(output.buffsOnConsume!=null)foreach(var buff in output.buffsOnConsume)
                {
                    if(buff.attributeId==EntityAttributes.Stat_HealthRegen && buff.duration>0)
                    {
                        float total=buff.totalValueOverride!=0 ? buff.totalValueOverride : buff.value*buff.duration;
                        if(total>0){regen+=total;duration=Math.Max(duration,buff.duration);}
                    }
                    else if(buff.attributeId!=EntityAttributes.Stat_MaxHealth && buff.attributeId!=EntityAttributes.Status_CurrentHealth && buff.attributeId!=EntityAttributes.Stat_HealthRegen && buff.attributeId!=EntityAttributes.Status_UnhealableDamage)
                        recipe.HasOtherEffects=true;
                }
                if(output.addStatusOnConsume!=null)foreach(var effect in output.addStatusOnConsume)
                {
                    if(effect.status==EntityAttributes.Status_CurrentHealth && effect.value>0)recipe.InstantHp+=effect.value;
                    else if(effect.status!=EntityAttributes.Status_CurrentHealth && effect.status!=EntityAttributes.Stat_MaxHealth && effect.status!=EntityAttributes.Status_UnhealableDamage)recipe.HasOtherEffects=true;
                }
                recipe.HealingHp=regen+recipe.InstantHp;recipe.HealingRate=duration>0 ? regen/duration : 0;
                recipe.HasOtherEffects |= output.resourceOnConsume!=null && output.resourceOnConsume.Any(e=>e.amount!=0);
                recipe.HasOtherEffects |= output.valueChangeOnItemConsume!=null && output.valueChangeOnItemConsume.Count>0;
                foreach (var i in r.itemsNeeded)
                {
                    var item = i.item.GetAsset();
                    if(item)artwork[i.item.value]=item.artwork;
                    recipe.Ingredients.Add(new Ingredient { Id = i.item.value, Name = item ? item.LocalizedDisplayName : i.item.ToString(), Quantity = i.quantity });
                }
                recipes.Add(recipe);
            }
            matches = recipes.Select(r => RecipeMatcher.Evaluate(r, stock)).OrderByDescending(m => m.Batches > 0).ThenBy(m => m.Recipe.Name).ToList();
            status = "Backpack + " + counted.Count + " accessible church chest(s) • " + recipes.Count + " recipes";
            if (browser) browser.SetData(matches, stock, locations, artwork, status);
        }

        static InventoryUI GetInventoryUI() => Object.FindObjectsOfType<InventoryUI>(true).FirstOrDefault();

        void Count(IEnumerable<InventoryData> contents, string location)
        {
            if (contents == null) return;
            foreach (var i in contents)
            {
                if (i == null || i.quantity <= 0) continue;
                int n; stock.TryGetValue(i.id.value, out n);
                stock[i.id.value] = checked(n + i.quantity);
                List<string> places;
                if (!locations.TryGetValue(i.id.value, out places)) locations[i.id.value] = places = new List<string>();
                places.Add(location + " ×" + i.quantity);
            }
        }

        void OnDestroy()
        {
            Close();
            if (vendor && PurchaseItem) Offers(vendor).Remove(PurchaseItem);
            if (cabinet) Object.Destroy(cabinet);
            if (browser) Object.Destroy(browser.gameObject);
            if (PurchaseItem) Object.Destroy(PurchaseItem);
            if (purchaseArtwork) Object.Destroy(purchaseArtwork);
            if (purchaseArtworkTexture) Object.Destroy(purchaseArtworkTexture);
            harmony?.UnpatchSelf();
            Instance = null;
        }
    }

    public sealed class ArcadeInteraction : Interactable
    {
        float nextRegistration;
        void Update()
        {
            if(Time.unscaledTime<nextRegistration)return;
            nextRegistration=Time.unscaledTime+0.5f;
            var manager=Object.FindObjectOfType<InteractionManager>();
            if(!manager)return;
            var all=AccessTools.Field(typeof(InteractionManager),"_allInteractables").GetValue(manager) as List<Interactable>;
            if(all!=null && !all.Contains(this))manager.AddInteractable(this);
        }
        // true tells the native manager to permanently consume/remove the interactable.
        public override bool OnInteract(Player player) { if(Plugin.Instance!=null)Plugin.Instance.Open(); return false; }
    }

    [HarmonyPatch(typeof(StampPurchaseListItem), "UpdateDisplay")]
    static class OraclePurchaseDisplayPatch
    {
        static void Postfix(StampPurchaseListItem __instance)
        {
            var plugin=Plugin.Instance;
            if(plugin==null || __instance.Item!=plugin.PurchaseItem)return;
            var label=AccessTools.Field(typeof(StampPurchaseListItem),"label").GetValue(__instance) as TMPro.TextMeshProUGUI;
            if(label)
            {
                label.text="Crafting Oracle";
                label.fontStyle &= ~(TMPro.FontStyles.UpperCase | TMPro.FontStyles.LowerCase | TMPro.FontStyles.SmallCaps);
            }
            var picture=AccessTools.Field(typeof(StampPurchaseListItem),"itemImage").GetValue(__instance) as UnityEngine.UI.Image;
            if(picture)
            {
                picture.sprite=plugin.PurchaseItem.artwork;picture.preserveAspect=true;
                // The row keeps the game's purchased checkmark/opacity; also mute the cabinet picture.
                picture.color=plugin.Purchased ? new Color(0.75f,0.75f,0.75f,1f) : Color.white;
            }
        }
    }

    [HarmonyPatch(typeof(StampPurchaseStation), "IsPurchased")]
    static class PurchasedPatch
    {
        static bool Prefix(ItemDefinition __0, ref bool __result)
        {
            if (Plugin.Instance == null || __0 != Plugin.Instance.PurchaseItem) return true;
            __result = Plugin.Instance.Purchased; return false;
        }
    }
    [HarmonyPatch(typeof(StampPurchaseListItem), "get_PurchasePrice")]
    static class PricePatch
    {
        static bool Prefix(StampPurchaseListItem __instance, ref int __result)
        {
            if (Plugin.Instance == null || __instance.Item != Plugin.Instance.PurchaseItem) return true;
            __result = Math.Max(0, Plugin.Instance.Price.Value); return false;
        }
    }
    [HarmonyPatch(typeof(StampPurchaseListItem), "DoPurchase")]
    static class BuyPatch
    {
        static bool Prefix(StampPurchaseListItem __instance)
        {
            if (Plugin.Instance == null || __instance.Item != Plugin.Instance.PurchaseItem) return true;
            Plugin.Instance.Buy(); return false;
        }
    }
    [HarmonyPatch(typeof(StampPurchaseListItem), "CanPurchase")]
    static class CanBuyPatch
    {
        static bool Prefix(StampPurchaseListItem __instance, ref bool __result)
        {
            var p = Plugin.Instance;
            if (p == null || __instance.Item != p.PurchaseItem) return true;
            var gm = Object.FindObjectOfType<GameManager>();
            __result = gm && gm.PlayerUnit && !p.Purchased && gm.PlayerUnit.Stats.GetStamps() >= Math.Max(0, p.Price.Value);
            return false;
        }
    }
}
