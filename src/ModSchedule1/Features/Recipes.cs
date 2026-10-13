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
/// o jogo mostra só o último passo da mistura. O livro de receitas (F8) está em RecipeBook.cs.
/// </summary>
public sealed partial class Recipes : Feature<Recipes>
{
    public override string Id => "Recipes";
    public override string Name => "Recipe List";
    public override string Description => "Livro de receitas (F8) com tudo o que dá para produzir, em abas (inclusive todas as misturas possíveis com lucro), e receita completa de cada mistura no app de produtos do celular.";
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

    /// <summary>Menor cadeia de mistura até o produto (vazia = droga base ou receita desconhecida).</summary>
    public static List<Step> Chain(ProductDefinition product) =>
        AllChains().TryGetValue(product.ID, out var chain) ? chain : new List<Step>();

    /// <summary>
    /// Menor cadeia de mistura até cada produto: busca em largura a partir das drogas base. Cada StationRecipe é um passo
    /// (produto + ingrediente -> resultado); a ordem dos 2 ingredientes inverte após load/replicação, então o "produto" é
    /// o que for ProductDefinition. O jogo grava também misturas que não mudam nada (X + M -> X): não contam como receita,
    /// e produtos que só aparecem em ciclos ficam de fora. Base = não foi criado por mistura (lista vazia), mesmo que
    /// alguma mistura dê nele (mapa de mistura aleatório).
    /// </summary>
    public static Dictionary<string, List<Step>> AllChains()
    {
        var chains = new Dictionary<string, List<Step>>();
        if (!ProductManager.InstanceExists) return chains;
        var all = ProductManager.Instance.AllProducts;
        var created = new HashSet<string>();
        var made = ProductManager.Instance.createdProducts;
        for (int i = 0; made != null && i < made.Count; i++) if (made[i] != null) created.Add(made[i].ID);
        var next = new Dictionary<string, List<(string To, Step Step)>>();   // origem -> o que sai dela
        var queue = new Queue<string>();
        for (int i = 0; i < all.Count; i++)
        {
            var product = all[i];
            if (product == null) continue;
            var recipes = product.Recipes;
            for (int j = 0; recipes != null && j < recipes.Count; j++)
            {
                var r = recipes[j];
                if (r == null || r.Ingredients.Count < 2) continue;
                int srcIndex = r.Ingredients[0].Item?.TryCast<ProductDefinition>() != null ? 0 : 1;
                var src = r.Ingredients[srcIndex].Item?.TryCast<ProductDefinition>();
                var mixer = r.Ingredients[1 - srcIndex].Item;
                if (src == null || mixer == null || src.ID == product.ID) continue;
                if (!next.TryGetValue(src.ID, out var outs)) next[src.ID] = outs = new List<(string, Step)>();
                outs.Add((product.ID, new Step(src.Name, mixer.Name, product.Name)));
            }
            if (!created.Contains(product.ID) && chains.TryAdd(product.ID, new List<Step>())) queue.Enqueue(product.ID);
        }
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!next.TryGetValue(id, out var outs)) continue;
            foreach (var (to, step) in outs)
            {
                if (chains.ContainsKey(to)) continue;
                chains[to] = new List<Step>(chains[id]) { step };
                queue.Enqueue(to);
            }
        }
        return chains;
    }
}
