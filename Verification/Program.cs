using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace StationpediaCraftableProducts.Verification
{
    internal static class Program
    {
        private static readonly List<string> SearchDirectories =
            new List<string>();

        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine(
                    "Usage: StationpediaCraftableProductsPatchProbe.exe " +
                    "<StationeersDir> <Plugin.dll>");
                return 2;
            }

            string gameDir = Path.GetFullPath(args[0]);
            string pluginPath = Path.GetFullPath(args[1]);
            string managedDir = Path.Combine(gameDir, "rocketstation_Data", "Managed");
            string bepinExCore = Path.Combine(gameDir, "BepInEx", "core");

            SearchDirectories.Add(managedDir);
            SearchDirectories.Add(bepinExCore);
            SearchDirectories.Add(Path.GetDirectoryName(pluginPath));
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

            try
            {
                Assembly gameAssembly = Assembly.LoadFrom(
                    Path.Combine(managedDir, "Assembly-CSharp.dll"));
                Assembly pluginAssembly = Assembly.LoadFrom(pluginPath);

                Type universalPage = RequireType(
                    gameAssembly,
                    "Assets.Scripts.UI.UniversalPage");
                Type stationpediaPage = RequireType(
                    gameAssembly,
                    "Assets.Scripts.UI.StationpediaPage");
                Type stationpedia = RequireType(
                    gameAssembly,
                    "Assets.Scripts.UI.Stationpedia");
                Type stationpediaCategory = RequireType(
                    gameAssembly,
                    "Assets.Scripts.UI.StationpediaCategory");
                Type manufacturer = RequireType(
                    gameAssembly,
                    "Assets.Scripts.UI.SPDAManufacturer");
                Type electronicReader = RequireType(
                    gameAssembly,
                    "Assets.Scripts.Objects.Items.ElectronicReader");
                Type recipe = RequireType(gameAssembly, "Reagents.Recipe");
                Type recipeReference = RequireType(
                    gameAssembly,
                    "Assets.Scripts.Objects.Items.RecipeReference");
                Type resourceConsumer = RequireType(
                    gameAssembly,
                    "Assets.Scripts.Objects.Electrical.IResourceConsumer");

                MethodInfo changeDisplay = RequireMethod(
                    universalPage,
                    "ChangeDisplay",
                    stationpediaPage);
                RequireField(universalPage, "Content");
                RequireField(universalPage, "UsedIn");
                RequireField(universalPage, "CostToPrintContents");
                RequireField(universalPage, "CreatedCategories");
                RequireField(stationpedia, "ManufactureInsertPrefab");
                RequireField(stationpediaCategory, "Contents");
                RequireField(stationpediaCategory, "SecondContents");
                RequireField(stationpediaCategory, "Title");
                RequireField(stationpediaCategory, "CollapseImage");
                RequireField(stationpediaCategory, "VisibleImage");
                RequireMethod(stationpediaCategory, "SetActive", typeof(bool));
                RequireField(stationpedia, "ContentRectTransform");
                RequireField(stationpedia, "ScrollBarUniversal");
                RequireField(manufacturer, "PrinterNameTitle");
                RequireField(manufacturer, "ImageButton");
                RequireMethod(
                    manufacturer,
                    "SetText",
                    RequireType(gameAssembly, "Assets.Scripts.UI.StationBuildCostInsert"));
                RequireField(electronicReader, "AllRecipes");
                RequireMethod(
                    electronicReader,
                    "GetAllMyCreators",
                    RequireType(gameAssembly, "Assets.Scripts.Objects.DynamicThing"));
                RequireMethod(recipe, "ToString", recipeReference);
                RequireMethod(
                    resourceConsumer,
                    "GetResourcesUsed");

                Type patchType = pluginAssembly.GetType(
                    "StationpediaCraftableProducts.UniversalPageChangeDisplayPatch",
                    throwOnError: true);
                HarmonyMethod targetInfo =
                    HarmonyMethodExtensions.GetMergedFromType(patchType);
                MethodInfo target = AccessTools.DeclaredMethod(
                    targetInfo.declaringType,
                    targetInfo.methodName,
                    targetInfo.argumentTypes);
                if (target == null || target != changeDisplay)
                {
                    throw new InvalidOperationException(
                        "Harmony target did not resolve to " +
                        "UniversalPage.ChangeDisplay(StationpediaPage).");
                }

                MethodInfo postfix = patchType.GetMethod(
                    "AddCraftableProducts",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (postfix == null ||
                    postfix.GetCustomAttributes(typeof(HarmonyPostfix), false).Length != 1)
                {
                    throw new InvalidOperationException(
                        "Craftable-products Harmony postfix was not found.");
                }

                Type renderer = pluginAssembly.GetType(
                    "StationpediaCraftableProducts.CraftableProductsRenderer",
                    throwOnError: true);
                RequireMethod(renderer, "Render", universalPage, stationpediaPage);

                Console.WriteLine("RESOLVED " + target);
                Console.WriteLine("RESOLVED UniversalPage.CostToPrintContents");
                Console.WriteLine("RESOLVED Stationpedia.ManufactureInsertPrefab");
                Console.WriteLine("RESOLVED ElectronicReader.AllRecipes");
                Console.WriteLine("RESOLVED Recipe.ToString(RecipeReference)");
                Console.WriteLine("RESOLVED IResourceConsumer.GetResourcesUsed()");
                Console.WriteLine(
                    "PASS: reverse recipe and Stationpedia UI targets match " +
                    "the installed Stationeers assembly.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception);
                return 1;
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
            }
        }

        private static Type RequireType(Assembly assembly, string name)
        {
            Type type = assembly.GetType(name, throwOnError: false);
            if (type == null)
                throw new TypeLoadException(name);
            return type;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            params Type[] arguments)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance,
                binder: null,
                types: arguments,
                modifiers: null);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            string fileName = new AssemblyName(args.Name).Name + ".dll";
            foreach (string directory in SearchDirectories.Where(
                         value => !string.IsNullOrEmpty(value)))
            {
                string candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }

            return null;
        }
    }
}
