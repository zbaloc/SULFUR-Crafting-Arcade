$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition ([IO.File]::ReadAllText("$PSScriptRoot\src\RecipeMatcher.cs"))
function Assert-Equal($actual, $expected, $label) {
    if ($actual -ne $expected) { throw "$label expected $expected; got $actual" }
    Write-Output "PASS $label"
}
function Ingredient($id, $quantity) {
    $i = [SulfurCraftingArcade.Ingredient]::new(); $i.Id=$id; $i.Quantity=$quantity; return $i
}
$r = [SulfurCraftingArcade.Recipe]::new()
$r.Ingredients.Add((Ingredient 1 2)); $r.Ingredients.Add((Ingredient 2 1))
$stock = [Collections.Generic.Dictionary[ushort,int]]::new()
$stock[1]=6; $stock[2]=2
$match = [SulfurCraftingArcade.RecipeMatcher]::Evaluate($r,$stock)
Assert-Equal $match.Batches 2 'Batches limited by scarcest ingredient'
Assert-Equal $match.HasAny $true 'Complete recipe has ingredients'
$stock[2]=0
$match = [SulfurCraftingArcade.RecipeMatcher]::Evaluate($r,$stock)
Assert-Equal $match.Batches 0 'Missing ingredient prevents ready state'
Assert-Equal $match.HasAny $true 'Partial recipe remains visible'
$stock.Clear()
$match = [SulfurCraftingArcade.RecipeMatcher]::Evaluate($r,$stock)
Assert-Equal $match.HasAny $false 'No ingredients hides irrelevant recipe'
$r.Ingredients.Add((Ingredient 1 3)); $stock[1]=4; $stock[2]=10
$match = [SulfurCraftingArcade.RecipeMatcher]::Evaluate($r,$stock)
Assert-Equal $match.Required[1] 5 'Repeated ingredient requirements are summed'
Assert-Equal $match.Batches 0 'Repeated ingredients cannot reuse stock'
$stock[1]=10
Assert-Equal ([SulfurCraftingArcade.RecipeMatcher]::Evaluate($r,$stock)).Batches 2 'Summed stacks yield correct batches'
$empty = [SulfurCraftingArcade.Recipe]::new()
Assert-Equal ([SulfurCraftingArcade.RecipeMatcher]::Evaluate($empty,$stock)).Batches 0 'Empty recipe never reports unlimited crafting'
$r.Ingredients.Add((Ingredient 3 0))
$rejected = $false
try { [SulfurCraftingArcade.RecipeMatcher]::Evaluate($r,$stock) | Out-Null } catch { $rejected=$true }
Assert-Equal $rejected $true 'Invalid recipe quantity is rejected'
function Group-Match($name,$output,$recipeId,$batches) {
    $recipe=[SulfurCraftingArcade.Recipe]::new();$recipe.Name=$name;$recipe.OutputId=$output;$recipe.Id=$recipeId
    $match=[SulfurCraftingArcade.Match]::new();$match.Recipe=$recipe;$match.Batches=$batches;return $match
}
$variants=[Collections.Generic.List[SulfurCraftingArcade.Match]]::new()
$variants.Add((Group-Match 'Bread' 10 2 0));$variants.Add((Group-Match 'Apple' 11 3 0))
$variants.Add((Group-Match 'Soup' 12 4 1));$variants.Add((Group-Match 'Bread' 10 1 2))
$groups=[SulfurCraftingArcade.OutputRecipeGroup]::Build($variants)
Assert-Equal $groups.Count 3 'Multiple recipes share one output group'
Assert-Equal $groups[0].Name 'Bread' 'Craftable output groups sorted alphabetically first'
Assert-Equal $groups[1].Name 'Soup' 'Second craftable group precedes uncraftable outputs'
Assert-Equal $groups[2].Name 'Apple' 'Uncraftable output groups appear last'
Assert-Equal $groups[0].Recipes.Count 2 'Bread group retains both recipe variants'
Assert-Equal $groups[0].Recipes[0].Batches 2 'Craftable variants precede incomplete variants'
$a=Group-Match 'Gentle stew' 21 21 2
$a.Recipe.ItemKind='Consumable';$a.Recipe.HealingHp=40;$a.Recipe.HealingRate=2;$a.Recipe.OutputQuantity=4
$a.Required=[Collections.Generic.Dictionary[ushort,int]]::new();$a.Required[1]=2;$a.Required[2]=3
$b=Group-Match 'Quick snack' 22 22 3
$b.Recipe.ItemKind='Consumable';$b.Recipe.HealingHp=20;$b.Recipe.HealingRate=10;$b.Recipe.OutputQuantity=1
$b.Required=[Collections.Generic.Dictionary[ushort,int]]::new();$b.Required[1]=1
$sample=[Collections.Generic.List[SulfurCraftingArcade.Match]]::new();$sample.Add($a);$sample.Add($b)
Assert-Equal ([SulfurCraftingArcade.RecipeOptions]::InFilter($a.Recipe,[SulfurCraftingArcade.RecipeFilter]::Healing)) $true 'Healing filter includes restored HP'
Assert-Equal ([SulfurCraftingArcade.RecipeOptions]::InFilter($a.Recipe,[SulfurCraftingArcade.RecipeFilter]::OtherEffects)) $false 'Healing alone does not count as other effects'
$a.Recipe.HasOtherEffects=$true
Assert-Equal ([SulfurCraftingArcade.RecipeOptions]::InFilter($a.Recipe,[SulfurCraftingArcade.RecipeFilter]::OtherEffects)) $true 'Mixed-effect healing item remains visible in other effects'
Assert-Equal ([SulfurCraftingArcade.RecipeOptions]::InFilter($a.Recipe,[SulfurCraftingArcade.RecipeFilter]::Equipment)) $false 'Equipment excludes consumables'
Assert-Equal ([SulfurCraftingArcade.OutputRecipeGroup]::Build($sample,[SulfurCraftingArcade.RecipeSort]::MostHealing))[0].OutputId 21 'Highest healing sorts by HP per item'
Assert-Equal ([SulfurCraftingArcade.OutputRecipeGroup]::Build($sample,[SulfurCraftingArcade.RecipeSort]::LeastHealing))[0].OutputId 22 'Lowest healing reverses order'
Assert-Equal ([SulfurCraftingArcade.OutputRecipeGroup]::Build($sample,[SulfurCraftingArcade.RecipeSort]::FastestHealing))[0].OutputId 22 'Fast healing sorts by HP per second'
Assert-Equal ([SulfurCraftingArcade.OutputRecipeGroup]::Build($sample,[SulfurCraftingArcade.RecipeSort]::SlowestHealing))[0].OutputId 21 'Slow healing reverses rate order'
Assert-Equal ([SulfurCraftingArcade.OutputRecipeGroup]::Build($sample,[SulfurCraftingArcade.RecipeSort]::MostCraftable))[0].OutputId 21 'Craftable quantity counts output units rather than batches'
Assert-Equal ([SulfurCraftingArcade.OutputRecipeGroup]::Build($sample,[SulfurCraftingArcade.RecipeSort]::LeastCraftable))[0].OutputId 22 'Least craftable reverses output-unit order'
Assert-Equal ([SulfurCraftingArcade.OutputRecipeGroup]::Build($sample,[SulfurCraftingArcade.RecipeSort]::MostIngredients))[0].OutputId 21 'Ingredient sort counts total units per batch'
Assert-Equal ([SulfurCraftingArcade.OutputRecipeGroup]::Build($sample,[SulfurCraftingArcade.RecipeSort]::LeastIngredients))[0].OutputId 22 'Least ingredient sort reverses unit order'
$a.Recipe.InstantHp=5
Assert-Equal ([SulfurCraftingArcade.OutputRecipeGroup]::Build($sample,[SulfurCraftingArcade.RecipeSort]::FastestHealing))[0].OutputId 21 'Instant HP is prioritized over timed regeneration'
$a.Batches=0
Assert-Equal ([SulfurCraftingArcade.OutputRecipeGroup]::Build($sample,[SulfurCraftingArcade.RecipeSort]::MostHealing))[0].OutputId 22 'Craftable group stays before incomplete group with more healing'
