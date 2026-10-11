using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.StationFramework;
using HarmonyLib;
using Il2CppScheduleOne.UI.Phone.ProductManagerApp;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.UI;

namespace ModSchedule1.Features;

/// <summary>
/// Receita COMPLETA (desde a droga base) no painel de detalhes do app nativo de produtos do celular;
/// o jogo mostra só o último passo da mistura.
/// </summary>
public sealed class Recipes : Feature<Recipes>
{
    public override string Id => "Recipes";
    public override string Name => "Recipe List";
    public override string Description => "Receita completa de cada mistura no app de produtos do celular.";
    public override bool Shared => false;   // pessoal: cada jogador usa a própria config

    private Text? _chainText;
    private bool _dumped;

    /// <summary>Texto com a cadeia completa logo abaixo das receitas de 1 passo que o painel nativo já mostra.</summary>
    private void ShowChain(ProductAppDetailPanel panel)
    {
        var product = panel.ActiveProduct;
        if (_chainText == null)
        {
            if (panel.PropertyLabels.Length == 0 || panel.RecipeEntries.Length == 0) return;
            var lastRecipe = panel.RecipeEntries[panel.RecipeEntries.Length - 1].transform;
            NativeUi.DumpOnce(ref _dumped, lastRecipe.parent, "ProductAppDetailPanel (receitas)");
            _chainText = NativeUi.CloneText(panel.PropertyLabels[0], lastRecipe.parent, "MS1_RecipeChain");
            _chainText.transform.SetSiblingIndex(lastRecipe.GetSiblingIndex() + 1);
            _chainText.supportRichText = true;
        }
        var chain = product != null ? Chain(product) : new List<Step>();
        bool show = Active && chain.Count > 1;   // 1 passo o jogo já mostra
        _chainText.gameObject.SetActive(show);
        if (!show) return;
        _chainText.text = "<b>Receita completa</b>\n" +
                          string.Join("\n", chain.Select((s, i) => $"{i + 1}. {s.From} + {s.Mixer} -> {s.To}"));
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Container.GetComponent<RectTransform>());
    }

    [HarmonyPatch(typeof(ProductAppDetailPanel), nameof(ProductAppDetailPanel.SetActiveProduct))]
    private static class DetailPanelPatch
    {
        private static void Postfix(ProductAppDetailPanel __instance) => I.Guard("ProductAppDetailPanel", () => I.ShowChain(__instance));
    }

    public readonly record struct Step(string From, string Mixer, string To);

    /// <summary>
    /// Menor cadeia de mistura até o produto. Cada StationRecipe é um passo (produto + ingrediente → resultado);
    /// a ordem dos 2 ingredientes inverte após load/replicação, então o "produto" é o que for ProductDefinition.
    /// Misturas podem formar ciclo (resultado que já existia): visited corta.
    /// </summary>
    public static List<Step> Chain(ProductDefinition product, HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>();
        var recipes = product.Recipes;
        if (recipes == null || recipes.Count == 0 || !visited.Add(product.ID)) return new List<Step>();
        List<Step>? best = null;
        for (int i = 0; i < recipes.Count; i++)
        {
            var r = recipes[i];
            if (r == null || r.Ingredients.Count < 2) continue;
            int srcIndex = r.Ingredients[0].Item?.TryCast<ProductDefinition>() != null ? 0 : 1;
            var src = r.Ingredients[srcIndex].Item?.TryCast<ProductDefinition>();
            var mixer = r.Ingredients[1 - srcIndex].Item;
            if (src == null || mixer == null) continue;
            var chain = Chain(src, new HashSet<string>(visited));
            chain.Add(new Step(src.Name, mixer.Name, product.Name));
            if (best == null || chain.Count < best.Count) best = chain;
        }
        return best ?? new List<Step>();
    }
}
