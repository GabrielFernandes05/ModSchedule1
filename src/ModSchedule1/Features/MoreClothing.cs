using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Avatar;
using Il2CppScheduleOne.Clothing;
using Il2CppScheduleOne.Core.Avatar;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI.Shop;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModSchedule1.Features;

/// <summary>
/// Roupas novas sem modelos novos: o jogo desenha roupa a partir de peças da biblioteca de avatar (AvatarObject).
/// Várias peças só existem em NPCs (policiais, padre, funcionários...). Para cada peça "só de NPC" criamos um item à
/// venda que aponta para ela. Renderiza em todos os PCs (a peça é do jogo); item sincroniza por ID (fixo: ms1_cloth_*).
/// </summary>
public sealed class MoreClothing : Feature<MoreClothing>
{
    public override string Id => "MoreClothing";
    public override string Name => "More Clothing";
    public override string Description => "Roupas e acessórios que só os NPCs usavam, à venda na loja de roupas.";

    private Pref<int> _price = null!;
    private readonly List<ClothingDefinition> _forSale = new();
    private bool _listed;

    // Lugar no corpo pelo nome da peça (a peça não diz o slot). Ordem importa: termos mais específicos primeiro.
    private static readonly (string Word, EClothingSlot Slot)[] SlotWords =
    {
        ("hazmat", EClothingSlot.Outerwear), ("gasmask", EClothingSlot.Eyes), ("respirator", EClothingSlot.Eyes),
        ("goggles", EClothingSlot.Eyes), ("glasses", EClothingSlot.Eyes), ("mask", EClothingSlot.Eyes),
        ("helmet", EClothingSlot.Head), ("beanie", EClothingSlot.Head), ("beret", EClothingSlot.Head),
        ("cap", EClothingSlot.Head), ("hat", EClothingSlot.Head),
        ("hoodie", EClothingSlot.Outerwear), ("jacket", EClothingSlot.Outerwear), ("coat", EClothingSlot.Outerwear),
        ("vest", EClothingSlot.Outerwear), ("gown", EClothingSlot.Outerwear), ("robe", EClothingSlot.Outerwear),
        ("apron", EClothingSlot.Outerwear), ("suit", EClothingSlot.Outerwear),
        ("shirt", EClothingSlot.Top), ("tee", EClothingSlot.Top), ("polo", EClothingSlot.Top), ("uniform", EClothingSlot.Top),
        ("blouse", EClothingSlot.Top), ("top", EClothingSlot.Top),
        ("pants", EClothingSlot.Bottom), ("jeans", EClothingSlot.Bottom), ("trousers", EClothingSlot.Bottom),
        ("shorts", EClothingSlot.Bottom), ("skirt", EClothingSlot.Bottom),
        ("belt", EClothingSlot.Waist), ("gloves", EClothingSlot.Hands),
        ("boots", EClothingSlot.Feet), ("shoes", EClothingSlot.Feet), ("sneakers", EClothingSlot.Feet),
        ("necklace", EClothingSlot.Neck), ("chain", EClothingSlot.Neck), ("scarf", EClothingSlot.Neck), ("tie", EClothingSlot.Neck),
        ("watch", EClothingSlot.Wrist), ("bracelet", EClothingSlot.Wrist),
    };

    protected override void OnInit()
    {
        _price = Setting("Price", 200, "Preço de cada peça ($)", min: 10, max: 2000);
        CustomItems.Add(Build);
    }

    private static EClothingSlot? SlotFor(string id)
    {
        var lower = id.ToLowerInvariant();
        foreach (var (word, slot) in SlotWords) if (lower.Contains(word)) return slot;
        return null;
    }

    private static string Pretty(string id) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' ').Replace('-', ' ').Trim().ToLowerInvariant());

    private IEnumerable<ItemDefinition> Build()
    {
        // peças que já têm item de roupa (não duplicar) e um "doador" por slot (ícone e configurações base)
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var donors = new Dictionary<EClothingSlot, ClothingDefinition>();
        ClothingDefinition? anyDonor = null;
        foreach (var def in Registry.Instance.GetAllItems())
        {
            var c = def?.TryCast<ClothingDefinition>();
            if (c == null) continue;
            if (c.ClothingAvatarObject != null) used.Add(c.ClothingAvatarObject.Id);
            if (!donors.ContainsKey(c.Slot)) donors[c.Slot] = c;
            anyDonor ??= c;
            Log.Debug($"roupa do jogo: {c.ID} | {c.Name} | {c.Slot} | colorível={c.Colorable} | peça={c.ClothingAvatarObject?.Id}");
        }
        if (anyDonor == null) { Log.Warn("nenhuma roupa do jogo encontrada"); yield break; }

        int skipped = 0;
        foreach (var obj in AvatarObjectLibrary.GetAllAvatarObjects())
        {
            if (obj == null || obj.PrimaryType != AvatarObject.EType.Worn || string.IsNullOrEmpty(obj.Id) || used.Contains(obj.Id)) continue;
            var slot = SlotFor(obj.Id);
            if (slot == null) { skipped++; Log.Debug($"peça só de NPC sem lugar conhecido (ignorada): {obj.Id}"); continue; }
            var donor = donors.TryGetValue(slot.Value, out var d) ? d : anyDonor;
            var item = CustomItems.Clone(donor, "ms1_cloth_" + obj.Id.ToLowerInvariant(), Pretty(obj.Id));
            item.Description = "Peça que antes só os NPCs usavam.";
            item.Slot = slot.Value;
            item.ClothingAvatarObject = obj;
            item.Colorable = obj.PropertyCollection != null && obj.PropertyCollection.GetFirstColorProperty() != null;
            item.DefaultColor = EClothingColor.White;
            item.BasePurchasePrice = _price.Value;
            _forSale.Add(item);
            Log.Info($"roupa nova: {item.Name} ({slot.Value}{(item.Colorable ? ", colorível" : "")})");
            yield return item;
        }
        if (skipped > 0) Log.Info($"{skipped} peças só de NPC ficaram de fora (lugar no corpo desconhecido; ver log debug)");
    }

    public override void OnSceneLoaded(string scene) => _listed = false;

    // Vende na loja de roupas com mais itens (a principal). Lojas só existem na cena Main.
    public override void OnUpdate()
    {
        if (_listed || _forSale.Count == 0 || SceneManager.GetActiveScene().name != "Main") return;
        ShopInterface? best = null;
        foreach (var shop in ShopInterface.AllShops)
            if (shop != null && shop.TryCast<ClothingShopInterface>() != null && (best == null || shop.Listings.Count > best.Listings.Count))
                best = shop;
        if (best == null) return;
        _listed = true;
        int added = _forSale.Count(item => CustomItems.AddListing(best, item));
        Log.Info($"{added} roupas novas à venda em {best.ShopName}");
    }
}
