using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.StationFramework;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.UI;

namespace ModSchedule1.Features;

/// <summary>
/// App "Receitas": todos os produtos descobertos com preço, efeitos, vício e a receita COMPLETA desde a droga base
/// (o app nativo mostra só o último passo). Ordena por preço, passos ou nome; favoritos são os mesmos do jogo.
/// </summary>
public sealed class Recipes : Feature<Recipes>
{
    public override string Id => "Recipes";
    public override string Name => "Recipe List";
    public override string Description => "App Receitas: receita completa de cada produto, preços e efeitos.";

    protected override void OnInit() => PhoneApp.All.Add(new RecipesApp());

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

public sealed class RecipesApp : PhoneApp
{
    public override string AppName => "ModSchedule1Recipes";
    public override string IconLabel => "Receitas";
    public override string IconGlyph => "R";
    public override Color IconColor => new(0.55f, 0.25f, 0.6f);
    protected override bool ShowIcon => Core.SeparateIcons;

    private enum Sort { Price, Steps, Name }
    private Sort _sort = Sort.Price;
    private bool _onlyFavs;
    private Transform _list = null!;
    private Text _status = null!;

    protected override void BuildUI(GameObject container)
    {
        var root = Header(container, "Receitas", out _);
        var bar = Ui.Row(root, 44);
        Ui.Button(bar.transform, "Preço", new Color(0.3f, 0.3f, 0.34f), () => { _sort = Sort.Price; Rebuild(); }, width: 110, height: 36);
        Ui.Button(bar.transform, "Passos", new Color(0.3f, 0.3f, 0.34f), () => { _sort = Sort.Steps; Rebuild(); }, width: 110, height: 36);
        Ui.Button(bar.transform, "Nome", new Color(0.3f, 0.3f, 0.34f), () => { _sort = Sort.Name; Rebuild(); }, width: 110, height: 36);
        Ui.Button(bar.transform, "Favoritos", new Color(0.55f, 0.45f, 0.1f), () => { _onlyFavs = !_onlyFavs; Rebuild(); }, width: 130, height: 36);
        _status = Ui.Text(root, "", 14, minHeight: 22);
        _status.color = new Color(0.7f, 0.7f, 0.7f);
        _list = Ui.Child("Products", root).transform;
        var v = _list.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
    }

    // Reconstrói ao abrir: em multiplayer receitas e preços chegam alguns segundos depois de entrar.
    protected override void OnOpened() => Rebuild();

    private void Rebuild()
    {
        Ui.Clear(_list);
        if (!Recipes.I.Active) { _status.text = "Ative o 'Recipe List' no app Mods."; return; }
        Recipes.I.Guard("Receitas", () =>
        {
            var pm = ProductManager.Instance;
            var favs = ProductManager.FavouritedProducts;
            var products = new List<(ProductDefinition P, float Price, List<Recipes.Step> Chain, bool Fav)>();
            var discovered = ProductManager.DiscoveredProducts;
            for (int i = 0; i < discovered.Count; i++)
            {
                var p = discovered[i];
                if (p == null) continue;
                bool fav = favs.Contains(p);
                if (_onlyFavs && !fav) continue;
                products.Add((p, pm.GetPrice(p), Recipes.Chain(p), fav));
            }
            products = _sort switch
            {
                Sort.Price => products.OrderByDescending(x => x.Price).ToList(),
                Sort.Steps => products.OrderBy(x => x.Chain.Count).ThenByDescending(x => x.Price).ToList(),
                _ => products.OrderBy(x => x.P.Name).ToList(),
            };
            _status.text = $"{products.Count} produto(s)  -  ordenado por {_sort switch { Sort.Price => "preço", Sort.Steps => "passos", _ => "nome" }}" +
                           (_onlyFavs ? "  -  só favoritos" : "");
            foreach (var (p, price, chain, fav) in products) Card(p, price, chain, fav);
        });
    }

    private void Card(ProductDefinition p, float price, List<Recipes.Step> chain, bool fav)
    {
        var card = Ui.Card(_list);
        var top = Ui.Row(card.transform, 34);
        var title = Ui.Text(top.transform, $"<b>{p.Name}</b>   {p.DrugType}", 18);
        title.supportRichText = true;
        var product = p;
        Ui.Button(top.transform, fav ? "★ favorito" : "☆ favoritar", fav ? new Color(0.55f, 0.45f, 0.1f) : new Color(0.3f, 0.3f, 0.34f), () =>
            Recipes.I.Guard("favoritar", () =>
            {
                ProductManager.Instance.SetProductFavourited(product.ID, !fav);   // mesmo favorito do app nativo (RPC)
                Rebuild();
            }), width: 140, height: 30);

        var effects = new List<string>();
        var props = p.Properties;
        for (int i = 0; i < props.Count; i++)
            if (props[i] != null) effects.Add($"<color=#{ColorUtility.ToHtmlStringRGB(props[i].LabelColor)}>{props[i].Name}</color>");
        var info = Ui.Text(card.transform,
            $"Seu preço: <b>{Ui.Money(price)}</b>   Sugerido: {Ui.Money(p.MarketValue)}   Vício: {p.GetAddictiveness():P0}\n" +
            (effects.Count > 0 ? string.Join(", ", effects) : "sem efeitos"), 15, minHeight: 40);
        info.supportRichText = true;

        string recipe = chain.Count == 0
            ? "Produto base (sem mistura)"
            : string.Join("\n", chain.Select((s, i) => $"{i + 1}. {s.From} + <b>{s.Mixer}</b>  ->  {s.To}"));
        var steps = Ui.Text(card.transform, recipe, 14, minHeight: 20);
        steps.supportRichText = true;
        steps.color = new Color(0.85f, 0.85f, 0.85f);
    }
}
