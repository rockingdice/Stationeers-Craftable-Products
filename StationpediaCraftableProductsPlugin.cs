using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Assets.Scripts;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.UI;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Reagents;
using UnityEngine;
using UnityEngine.UI;

namespace StationpediaCraftableProducts
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class StationpediaCraftableProductsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "stationeers.stationpediacraftableproducts";
        public const string PluginName = "RockingDice's Stationpedia Craftable Products";
        public const string PluginVersion = "1.0.6";

        internal static ManualLogSource Log { get; private set; }
        internal static StationpediaCraftableProductsPlugin Instance { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(StationpediaCraftableProductsPlugin).Assembly);
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        internal static void ScheduleLayoutRefresh(
            UniversalPage universalPage,
            StationpediaCategory category)
        {
            if (Instance != null)
                Instance.StartCoroutine(Instance.RefreshLayoutAfterDisplay(
                    universalPage,
                    category));
        }

        private IEnumerator RefreshLayoutAfterDisplay(
            UniversalPage universalPage,
            StationpediaCategory category)
        {
            // ChangeDisplay runs before Stationpedia makes the universal page
            // visible. Rebuild on the following frames, after TMP and all
            // nested layout groups have supplied their preferred heights.
            yield return null;
            if (universalPage == null || category == null)
                yield break;
            CraftableProductsRenderer.RebuildStationpediaLayout(
                universalPage,
                category);

            yield return null;
            if (universalPage == null || category == null)
                yield break;
            CraftableProductsRenderer.RebuildStationpediaLayout(
                universalPage,
                category);
        }
    }

    [HarmonyPatch(typeof(UniversalPage), nameof(UniversalPage.ChangeDisplay))]
    internal static class UniversalPageChangeDisplayPatch
    {
        [HarmonyPostfix]
        private static void AddCraftableProducts(
            UniversalPage __instance,
            StationpediaPage page)
        {
            try
            {
                CraftableProductsRenderer.Render(__instance, page);
            }
            catch (Exception exception)
            {
                StationpediaCraftableProductsPlugin.Log?.LogError(
                    "Could not render the Craftable Products category for " +
                    (page?.Key ?? "<unknown>") + ": " + exception);
            }
        }
    }

    internal static class CraftableProductsRenderer
    {
        private const string CategoryObjectName =
            "StationpediaCraftableProducts.Category";
        private const string CategoryTitleChinese = "可制造";
        private const string CategoryTitleEnglish = "Craftable";
        private const int MaxComplexityDepth = 8;

        private static readonly Dictionary<int, List<Item>> ResourceCache =
            new Dictionary<int, List<Item>>();
        private static readonly Dictionary<int, int> ItemComplexityCache =
            new Dictionary<int, int>();

        internal static void Render(UniversalPage universalPage, StationpediaPage page)
        {
            RemovePreviousCategory(universalPage);

            if (universalPage == null || page == null || Stationpedia.Instance == null)
                return;

            Item material = Prefab.Find(page.PrefabHash) as Item;
            if (material == null || material.HideInStationpedia)
                return;

            ResourceCache.Clear();
            ItemComplexityCache.Clear();

            List<CraftableRecipeEntry> entries = BuildEntries(material);
            if (entries.Count == 0)
                return;

            entries.Sort(CraftableRecipeEntryComparer.Instance);

            if (universalPage.CostToPrintContents == null)
            {
                StationpediaCraftableProductsPlugin.Log?.LogWarning(
                    "The vanilla How To Manufacture category is unavailable.");
                return;
            }

            // Clone the live vanilla "How To Manufacture" category so its
            // contents keep the same one-column, full-width card layout.
            StationpediaCategory category = UnityEngine.Object.Instantiate(
                universalPage.CostToPrintContents,
                universalPage.Content);
            category.name = CategoryObjectName;
            DetachAndDestroyChildren(category.Contents);
            DetachAndDestroyChildren(category.SecondContents);
            category.Title.text = GetCategoryTitle();
            category.SetVisible(isVisble: true);
            // The live vanilla category can be cloned while its parent page is
            // hidden. SetActive is required to clear the copied
            // LayoutElement.ignoreLayout state; otherwise the cards render but
            // do not contribute to the ScrollRect content height.
            category.SetActive(active: true);
            category.Contents.gameObject.SetActive(true);
            if (category.SecondContents != null)
                category.SecondContents.gameObject.SetActive(true);
            if (category.CollapseImage != null && category.VisibleImage != null)
                category.CollapseImage.sprite = category.VisibleImage;

            int usedInIndex = universalPage.UsedIn.transform.GetSiblingIndex();
            category.transform.SetSiblingIndex(usedInIndex + 1);
            universalPage.CreatedCategories.Add(category);

            foreach (CraftableRecipeEntry entry in entries)
                AddCard(category, entry);

            RebuildStationpediaLayout(universalPage, category);
            StationpediaCraftableProductsPlugin.ScheduleLayoutRefresh(
                universalPage,
                category);

            StationpediaCraftableProductsPlugin.Log?.LogDebug(
                "Rendered " + entries.Count + " craftable-product entries for " +
                material.PrefabName + ".");
        }

        internal static void RebuildStationpediaLayout(
            UniversalPage universalPage,
            StationpediaCategory category)
        {
            Canvas.ForceUpdateCanvases();

            if (category.Contents != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(category.Contents);
            if (category.RectTransform != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(category.RectTransform);
            if (universalPage.Content != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(universalPage.Content);
            if (Stationpedia.Instance.ContentRectTransform != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    Stationpedia.Instance.ContentRectTransform);
            }

            Canvas.ForceUpdateCanvases();

            ScrollRect scrollRect = universalPage.Content != null
                ? universalPage.Content.GetComponentInParent<ScrollRect>()
                : null;
            if (scrollRect != null)
            {
                // UniversalPage.Content is the LayoutGroup under the viewport,
                // i.e. the real ScrollRect content object.
                scrollRect.content = universalPage.Content;
                scrollRect.vertical = true;
                if (Stationpedia.Instance.ScrollBarUniversal != null)
                {
                    scrollRect.verticalScrollbar =
                        Stationpedia.Instance.ScrollBarUniversal;
                }
                scrollRect.Rebuild(CanvasUpdate.Prelayout);
                scrollRect.Rebuild(CanvasUpdate.PostLayout);
            }
        }

        private static void DetachAndDestroyChildren(RectTransform contents)
        {
            if (contents == null)
                return;

            while (contents.childCount > 0)
            {
                Transform inheritedChild = contents.GetChild(0);
                inheritedChild.SetParent(null, worldPositionStays: false);
                UnityEngine.Object.Destroy(inheritedChild.gameObject);
            }
        }

        private static string GetCategoryTitle()
        {
            return Localization.CurrentLanguage == LanguageCode.CN
                ? CategoryTitleChinese
                : CategoryTitleEnglish;
        }

        private static void RemovePreviousCategory(UniversalPage universalPage)
        {
            if (universalPage == null || universalPage.Content == null)
                return;

            for (int index = universalPage.Content.childCount - 1; index >= 0; index--)
            {
                Transform child = universalPage.Content.GetChild(index);
                if (child == null || child.name != CategoryObjectName)
                    continue;

                StationpediaCategory category = child.GetComponent<StationpediaCategory>();
                if (category != null)
                    universalPage.CreatedCategories.Remove(category);

                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static List<CraftableRecipeEntry> BuildEntries(Item material)
        {
            List<CraftableRecipeEntry> entries = new List<CraftableRecipeEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<RecipeReference> recipes = ElectronicReader.AllRecipes;
            if (recipes == null)
                return entries;

            foreach (RecipeReference reference in recipes)
            {
                if (!TryCreateEntry(material, reference, out CraftableRecipeEntry entry))
                    continue;

                string key = entry.Product.PrefabHash + ":" +
                             entry.Creator.PrefabHash + ":" +
                             entry.Reference.Recipe.GetHashCode() + ":" +
                             (entry.Reference.Source?.PrefabHash ?? 0);
                if (seen.Add(key))
                    entries.Add(entry);
            }

            return entries;
        }

        private static bool TryCreateEntry(
            Item material,
            RecipeReference reference,
            out CraftableRecipeEntry entry)
        {
            entry = null;
            if (reference == null || reference.Creator == null ||
                reference.DynamicThing == null || reference.DynamicThing.HideInStationpedia)
            {
                return false;
            }

            if (!TryGetMaterialAmount(material, reference, out double materialAmount))
                return false;

            IngredientDifficulty difficulty = CalculateIngredientDifficulty(
                reference.Recipe,
                reference.Creator);

            entry = new CraftableRecipeEntry
            {
                Reference = reference,
                Product = reference.DynamicThing,
                Creator = reference.Creator,
                CurrentMaterialAmount = materialAmount,
                MaxAlloyRank = difficulty.MaxAlloyRank,
                AlloyIngredientCount = difficulty.AlloyIngredientCount,
                MaxIngredientComplexity = difficulty.MaxComplexity,
                TotalIngredientComplexity = difficulty.TotalComplexity,
                IngredientTypes = reference.Source != null ? 1 : reference.Recipe.CountTypes,
                TotalMaterialAmount = GetTotalMaterialAmount(reference),
                TierRank = GetTierRank(reference.DynamicThing.RecipeTier)
            };
            return true;
        }

        private static bool TryGetMaterialAmount(
            Item material,
            RecipeReference reference,
            out double amount)
        {
            amount = 0.0;

            if (reference.Source != null)
            {
                if (reference.Source.PrefabHash != material.PrefabHash)
                    return false;

                amount = 1.0;
                return true;
            }

            IResourceConsumer consumer = reference.Creator as IResourceConsumer;
            if (consumer == null || !CanMachineConsumeItem(reference.Creator, material))
                return false;

            if (material.CreatedReagentMixture == null ||
                material.CreatedReagentMixture.TotalReagents <= 0.0)
            {
                return false;
            }

            foreach (Reagent reagent in Reagent.AllReagents)
            {
                double required = reference.Recipe.Get(reagent);
                if (required <= 0.0 ||
                    material.CreatedReagentMixture.Get(reagent) <= 0.0 ||
                    !consumer.CanProcess(reagent))
                {
                    continue;
                }

                amount += required;
            }

            return amount > 0.0;
        }

        private static bool CanMachineConsumeItem(Thing creator, Item material)
        {
            foreach (Item resource in GetResources(creator))
            {
                if (resource != null && resource.PrefabHash == material.PrefabHash)
                    return true;
            }

            return false;
        }

        private static List<Item> GetResources(Thing creator)
        {
            if (creator == null)
                return EmptyItems;

            if (ResourceCache.TryGetValue(creator.PrefabHash, out List<Item> resources))
                return resources;

            resources = new List<Item>();
            if (creator is IResourceConsumer consumer)
            {
                try
                {
                    List<Item> provided = consumer.GetResourcesUsed();
                    if (provided != null)
                        resources.AddRange(provided.Where(item => item != null));
                }
                catch (Exception exception)
                {
                    StationpediaCraftableProductsPlugin.Log?.LogWarning(
                        "Could not enumerate resources for " + creator.PrefabName +
                        ": " + exception.Message);
                }
            }

            ResourceCache[creator.PrefabHash] = resources;
            return resources;
        }

        private static readonly List<Item> EmptyItems = new List<Item>();

        private static IngredientDifficulty CalculateIngredientDifficulty(
            Recipe recipe,
            Thing creator)
        {
            IngredientDifficulty result = new IngredientDifficulty();
            List<Item> resources = GetResources(creator);

            foreach (Reagent reagent in Reagent.AllReagents)
            {
                if (recipe.Get(reagent) <= 0.0)
                    continue;

                SourceDifficulty sourceDifficulty = GetEasiestSourceDifficulty(
                    reagent,
                    resources,
                    new HashSet<int>(),
                    0);
                result.MaxAlloyRank = Math.Max(
                    result.MaxAlloyRank,
                    sourceDifficulty.AlloyRank);
                if (sourceDifficulty.AlloyRank > 0)
                    result.AlloyIngredientCount++;
                result.MaxComplexity = Math.Max(
                    result.MaxComplexity,
                    sourceDifficulty.Complexity);
                result.TotalComplexity += sourceDifficulty.Complexity;
            }

            return result;
        }

        private static SourceDifficulty GetEasiestSourceDifficulty(
            Reagent reagent,
            List<Item> resources,
            HashSet<int> visiting,
            int depth)
        {
            SourceDifficulty best = SourceDifficulty.Unknown;

            foreach (Item resource in resources)
            {
                if (resource?.CreatedReagentMixture == null ||
                    resource.CreatedReagentMixture.Get(reagent) <= 0.0)
                {
                    continue;
                }

                int alloyRank = GetAlloyRank(resource);
                int complexity = GetItemComplexity(resource, visiting, depth + 1);
                SourceDifficulty candidate = new SourceDifficulty(
                    alloyRank,
                    complexity);
                if (candidate.CompareTo(best) < 0)
                    best = candidate;
            }

            return best.IsKnown ? best : new SourceDifficulty(0, 0);
        }

        private static int GetItemComplexity(
            Item item,
            HashSet<int> visiting,
            int depth)
        {
            if (item == null || depth >= MaxComplexityDepth)
                return 0;

            if (ItemComplexityCache.TryGetValue(item.PrefabHash, out int cached))
                return cached;

            if (!visiting.Add(item.PrefabHash))
                return 0;

            int alloyPenalty = GetAlloyRank(item) * 1000;
            int easiestRoute = int.MaxValue;
            List<RecipeReference> creators = ElectronicReader.GetAllMyCreators(item);

            if (creators != null)
            {
                foreach (RecipeReference creatorReference in creators)
                {
                    if (creatorReference?.Creator == null)
                        continue;

                    int routeComplexity = 1 +
                                          GetTierRank(item.RecipeTier) +
                                          creatorReference.Recipe.CountTypes * 4;

                    if (creatorReference.Source != null)
                    {
                        routeComplexity += GetItemComplexity(
                            creatorReference.Source as Item,
                            visiting,
                            depth + 1);
                    }
                    else
                    {
                        List<Item> routeResources = GetResources(creatorReference.Creator);
                        foreach (Reagent reagent in Reagent.AllReagents)
                        {
                            if (creatorReference.Recipe.Get(reagent) <= 0.0)
                                continue;

                            SourceDifficulty sourceDifficulty =
                                GetEasiestSourceDifficulty(
                                    reagent,
                                    routeResources,
                                    visiting,
                                    depth + 1);
                            routeComplexity += Math.Min(
                                sourceDifficulty.Complexity,
                                500);
                        }
                    }

                    easiestRoute = Math.Min(easiestRoute, routeComplexity);
                }
            }

            visiting.Remove(item.PrefabHash);
            int result = alloyPenalty +
                         (easiestRoute == int.MaxValue ? 0 : easiestRoute);
            ItemComplexityCache[item.PrefabHash] = result;
            return result;
        }

        private static int GetAlloyRank(Item item)
        {
            if (!(item is Ingot ingot))
                return 0;

            switch (ingot.IngotType)
            {
                case IngotType.Alloy:
                    return 1;
                case IngotType.SuperAlloy:
                    return 2;
                default:
                    return 0;
            }
        }

        private static double GetTotalMaterialAmount(RecipeReference reference)
        {
            if (reference.Source != null)
                return 1.0;

            double total = 0.0;
            foreach (Reagent reagent in Reagent.AllReagents)
                total += Math.Max(0.0, reference.Recipe.Get(reagent));
            return total;
        }

        private static int GetTierRank(MachineTier tier)
        {
            switch (tier)
            {
                case MachineTier.TierOne:
                    return 1;
                case MachineTier.TierTwo:
                    return 2;
                case MachineTier.TierThree:
                    return 3;
                case MachineTier.Max:
                    return 4;
                default:
                    return 0;
            }
        }

        private static void AddCard(
            StationpediaCategory category,
            CraftableRecipeEntry entry)
        {
            SPDAManufacturer card = UnityEngine.Object.Instantiate(
                Stationpedia.Instance.ManufactureInsertPrefab,
                category.Contents);

            string productTitle = Localization.ParseHelpText(
                "{THING:" + entry.Product.PrefabName + "}");
            card.PrinterNameTitle.text = Stationpedia.Trim(productTitle);

            string pageLink = "Thing" + entry.Product.PrefabName;
            card.ImageButton.onClick.AddListener(delegate
            {
                Stationpedia.Instance.OpenPageByKey(pageLink);
            });

            StationBuildCostInsert display = new StationBuildCostInsert
            {
                PrinterImage = entry.Product.GetThumbnail(),
                PageLink = pageLink,
                Description = BuildRequirements(entry.Reference)
            };
            card.SetText(display);
        }

        private static string BuildRequirements(RecipeReference reference)
        {
            StringBuilder text = new StringBuilder();
            text.Append(reference.Creator.ToStationpediaLink());

            MachineTier tier = reference.DynamicThing.RecipeTier;
            if (tier != MachineTier.Undefined && tier != MachineTier.Max)
            {
                text.Append(" (");
                text.Append(Localization.GetInterface(tier.ToString()));
                text.Append(')');
            }

            text.AppendLine();

            if (reference.Source != null)
            {
                text.Append("<color=yellow>1</color> x ");
                text.Append(reference.Source.ToStationpediaLink());
                text.AppendLine();
            }
            else
            {
                text.Append(reference.Recipe.ToString(reference));
            }

            return text.ToString();
        }

        private sealed class CraftableRecipeEntry
        {
            internal RecipeReference Reference;
            internal DynamicThing Product;
            internal Thing Creator;
            internal double CurrentMaterialAmount;
            internal int MaxAlloyRank;
            internal int AlloyIngredientCount;
            internal int MaxIngredientComplexity;
            internal int TotalIngredientComplexity;
            internal int IngredientTypes;
            internal double TotalMaterialAmount;
            internal int TierRank;
        }

        private sealed class CraftableRecipeEntryComparer :
            IComparer<CraftableRecipeEntry>
        {
            internal static readonly CraftableRecipeEntryComparer Instance =
                new CraftableRecipeEntryComparer();

            public int Compare(CraftableRecipeEntry left, CraftableRecipeEntry right)
            {
                if (ReferenceEquals(left, right))
                    return 0;
                if (left == null)
                    return 1;
                if (right == null)
                    return -1;

                int result = left.IngredientTypes.CompareTo(
                    right.IngredientTypes);
                if (result != 0)
                    return result;

                result = left.CurrentMaterialAmount.CompareTo(
                    right.CurrentMaterialAmount);
                if (result != 0)
                    return result;

                result = left.MaxAlloyRank.CompareTo(right.MaxAlloyRank);
                if (result != 0)
                    return result;

                result = left.AlloyIngredientCount.CompareTo(
                    right.AlloyIngredientCount);
                if (result != 0)
                    return result;

                result = left.MaxIngredientComplexity.CompareTo(
                    right.MaxIngredientComplexity);
                if (result != 0)
                    return result;

                result = left.TotalIngredientComplexity.CompareTo(
                    right.TotalIngredientComplexity);
                if (result != 0)
                    return result;

                result = left.TotalMaterialAmount.CompareTo(
                    right.TotalMaterialAmount);
                if (result != 0)
                    return result;

                result = left.TierRank.CompareTo(right.TierRank);
                if (result != 0)
                    return result;

                result = string.Compare(
                    left.Product.DisplayName,
                    right.Product.DisplayName,
                    StringComparison.Ordinal);
                if (result != 0)
                    return result;

                return string.Compare(
                    left.Creator.DisplayName,
                    right.Creator.DisplayName,
                    StringComparison.Ordinal);
            }
        }

        private struct IngredientDifficulty
        {
            internal int MaxAlloyRank;
            internal int AlloyIngredientCount;
            internal int MaxComplexity;
            internal int TotalComplexity;
        }

        private struct SourceDifficulty : IComparable<SourceDifficulty>
        {
            internal static readonly SourceDifficulty Unknown =
                new SourceDifficulty(int.MaxValue, int.MaxValue, false);

            internal readonly int AlloyRank;
            internal readonly int Complexity;
            internal readonly bool IsKnown;

            internal SourceDifficulty(int alloyRank, int complexity)
                : this(alloyRank, complexity, true)
            {
            }

            private SourceDifficulty(int alloyRank, int complexity, bool isKnown)
            {
                AlloyRank = alloyRank;
                Complexity = complexity;
                IsKnown = isKnown;
            }

            public int CompareTo(SourceDifficulty other)
            {
                if (!IsKnown)
                    return other.IsKnown ? 1 : 0;
                if (!other.IsKnown)
                    return -1;

                int result = AlloyRank.CompareTo(other.AlloyRank);
                return result != 0
                    ? result
                    : Complexity.CompareTo(other.Complexity);
            }
        }
    }
}
