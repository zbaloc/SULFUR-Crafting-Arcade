// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 zbaloc
using System;
using System.Collections.Generic;
using System.Linq;

namespace SulfurCraftingArcade
{
    public sealed class Ingredient
    {
        public ushort Id;
        public string Name;
        public int Quantity;
    }
    public sealed class Recipe
    {
        public ushort Id;
        public ushort OutputId;
        public string Name, Category;
        public int OutputQuantity;
        public string ItemKind;
        public float HealingHp, HealingRate, InstantHp;
        public bool HasOtherEffects;
        public List<Ingredient> Ingredients = new List<Ingredient>();
    }
    public sealed class Match
    {
        public Recipe Recipe;
        public int Batches;
        public bool HasAny;
        public Dictionary<ushort, int> Required;
    }
    public sealed class OutputRecipeGroup
    {
        public ushort OutputId;
        public string Name;
        public List<Match> Recipes;
        public static List<OutputRecipeGroup> Build(IEnumerable<Match> recipes)
        {return Build(recipes,RecipeSort.Name);}
        public static List<OutputRecipeGroup> Build(IEnumerable<Match> recipes,RecipeSort sort)
        {
            return recipes.GroupBy(m=>m.Recipe.OutputId)
                .Select(g=>new OutputRecipeGroup {OutputId=g.Key,Name=g.First().Recipe.Name,
                    Recipes=g.OrderByDescending(m=>m.Batches>0).ThenBy(m=>RecipeOptions.SortKey(m,sort)).ThenBy(m=>m.Recipe.Id).ToList()})
                .OrderByDescending(g=>g.Recipes.Any(m=>m.Batches>0))
                .ThenBy(g=>RecipeOptions.SortKey(g.Recipes[0],sort)).ThenBy(g=>g.Name).ToList();
        }
    }
    public enum RecipeFilter {All,Healing,OtherEffects,Consumables,Equipment,Enchantments,Attachments,OtherItems}
    public enum RecipeSort {Name,MostHealing,LeastHealing,FastestHealing,SlowestHealing,MostCraftable,LeastCraftable,MostIngredients,LeastIngredients}
    public static class RecipeOptions
    {
        public static bool InFilter(Recipe r,RecipeFilter filter)
        {
            switch(filter)
            {
                case RecipeFilter.Healing:return r.HealingHp>0;
                case RecipeFilter.OtherEffects:return r.HasOtherEffects;
                case RecipeFilter.Consumables:return r.ItemKind=="Consumable";
                case RecipeFilter.Equipment:return r.ItemKind=="Weapon" || r.ItemKind=="Armor" || r.ItemKind=="Gadget";
                case RecipeFilter.Enchantments:return r.ItemKind=="Enchantment" || r.ItemKind=="EnchantmentOil";
                case RecipeFilter.Attachments:return r.ItemKind=="Attachment";
                case RecipeFilter.OtherItems:return r.ItemKind!="Consumable" && r.ItemKind!="Weapon" && r.ItemKind!="Armor" && r.ItemKind!="Gadget" && r.ItemKind!="Enchantment" && r.ItemKind!="EnchantmentOil" && r.ItemKind!="Attachment";
                default:return true;
            }
        }
        public static double SortKey(Match m,RecipeSort sort)
        {
            double speed=m.Recipe.InstantHp>0 ? 1000000000d+m.Recipe.InstantHp : m.Recipe.HealingRate;
            switch(sort)
            {
                case RecipeSort.MostHealing:return -m.Recipe.HealingHp;
                case RecipeSort.LeastHealing:return m.Recipe.HealingHp;
                case RecipeSort.FastestHealing:return -speed;
                case RecipeSort.SlowestHealing:return speed;
                case RecipeSort.MostCraftable:return -(double)m.Batches*m.Recipe.OutputQuantity;
                case RecipeSort.LeastCraftable:return (double)m.Batches*m.Recipe.OutputQuantity;
                case RecipeSort.MostIngredients:return -m.Required.Values.Sum(v=>(double)v);
                case RecipeSort.LeastIngredients:return m.Required.Values.Sum(v=>(double)v);
                default:return 0;
            }
        }
    }
    public static class RecipeMatcher
    {
        public static Match Evaluate(Recipe recipe, IReadOnlyDictionary<ushort, int> stock)
        {
            var required = new Dictionary<ushort, int>();
            foreach (var i in recipe.Ingredients)
            {
                if (i.Quantity <= 0) throw new ArgumentException("Ingredient quantities must be positive.");
                int old;
                required.TryGetValue(i.Id, out old);
                required[i.Id] = checked(old + i.Quantity);
            }
            int batches = required.Count == 0 ? 0 : int.MaxValue;
            bool any = false;
            foreach (var i in required)
            {
                int owned;
                stock.TryGetValue(i.Key, out owned);
                owned = Math.Max(0, owned);
                any |= owned > 0;
                batches = Math.Min(batches, owned / i.Value);
            }
            return new Match { Recipe = recipe, Batches = batches, HasAny = any, Required = required };
        }
    }
}
