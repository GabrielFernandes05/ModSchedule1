using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.NPCs;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModSchedule1.Features;

/// <summary>
/// Armas novas sem modelos novos: clones das armas do jogo com outras estatísticas, à venda no Stan.
/// Ficam com a aparência/animação da arma de origem. Dano/alcance/dispersão são calculados no PC de quem atira.
/// Os itens são sempre registrados (mesmo desativado) para o save nunca perder armas já compradas;
/// desativar só tira da loja e o tiro automático.
/// </summary>
public sealed class MoreGuns : Feature<MoreGuns>
{
    public override string Id => "MoreGuns";
    public override string Name => "More Guns";
    public override string Description => "Submetralhadora, pistola de pente estendido, Magnum e escopeta automática no Stan.";

    private enum Kind { Pistol, Revolver, Shotgun }

    // IDs fixos: todos os PCs precisam dos mesmos (o launcher garante o mesmo DLL).
    private static readonly HashSet<string> AutoIds = new(StringComparer.OrdinalIgnoreCase) { "ms1_smg", "ms1_autoshotgun" };
    private readonly List<(ItemDefinition Item, int Price)> _forSale = new();
    private bool _listed;

    protected override void OnInit() => CustomItems.Add(Build);

    private IEnumerable<ItemDefinition> Build()
    {
        var bases = new Dictionary<Kind, (ItemDefinition Def, Equippable_RangedWeapon Weapon)>();
        foreach (var def in Registry.Instance.GetAllItems())
        {
            var eq = def?.Equippable;
            var w = eq != null ? eq.GetComponent<Equippable_RangedWeapon>() : null;
            if (w == null) continue;
            var kind = w.TryCast<Equippable_PumpShotgun>() != null ? Kind.Shotgun
                : w.TryCast<Equippable_Revolver>() != null ? Kind.Revolver : Kind.Pistol;
            Log.Info($"arma do jogo: {def!.ID} ({kind}) pente {w.MagazineSize}, dano {w.Damage}, cadência {w.FireCooldown}s, munição {w.Magazine?.ID}");
            if (!bases.ContainsKey(kind) && def.TryCast<IntegerItemDefinition>() != null) bases[kind] = (def, w);
        }

        var made = new List<ItemDefinition>();
        if (bases.TryGetValue(Kind.Pistol, out var pistol))
        {
            var smgMag = Magazine(pistol.Weapon, "ms1_smg_mag", "Pente de submetralhadora (30)", 30, 150, made);
            made.Add(Gun(pistol, "ms1_smg", "Submetralhadora", "Automática: segure o botão para atirar. Usa pente de 30.", 3500, w =>
            {
                w.MagazineSize = 30;
                w.FireCooldown = 0.09f;
                w.Damage *= 0.55f;
                w.MinSpread += 2f;
                w.MaxSpread *= 1.5f;
                w.AccuracyDropPerShot *= 0.6f;
                if (smgMag != null) w.Magazine = smgMag;
            }));
            var extMag = Magazine(pistol.Weapon, "ms1_pistol_ext_mag", "Pente estendido de pistola (17)", 17, 90, made);
            made.Add(Gun(pistol, "ms1_pistol_ext", "Pistola de pente estendido", "Mesma pistola, pente de 17.", 1400, w =>
            {
                w.MagazineSize = 17;
                if (extMag != null) w.Magazine = extMag;
            }));
        }
        if (bases.TryGetValue(Kind.Revolver, out var revolver))
            made.Add(Gun(revolver, "ms1_magnum", "Magnum .44", "Revólver pesado: muito mais dano, cadência menor. Mesma munição.", 3000, w =>
            {
                w.Damage *= 1.8f;
                w.FireCooldown *= 1.4f;
            }));
        if (bases.TryGetValue(Kind.Shotgun, out var shotgun))
            made.Add(Gun(shotgun, "ms1_autoshotgun", "Escopeta automática", "Automática: segure o botão para atirar. Mesma munição.", 5000, w =>
            {
                w.FireCooldown = Mathf.Min(w.FireCooldown, 0.3f);
            }));
        made.RemoveAll(m => m == null);
        return made;
    }

    /// <summary>Clona definição + prefab da arma e aplica as estatísticas no componente do clone.</summary>
    private ItemDefinition Gun((ItemDefinition Def, Equippable_RangedWeapon Weapon) source, string id, string name, string desc,
        int price, Action<Equippable_RangedWeapon> tune)
    {
        var def = CustomItems.Clone(source.Def, id, name);
        def.Description = desc;
        var prefab = Object.Instantiate(source.Def.Equippable.gameObject, CustomItems.PrefabHolder, false);
        prefab.name = id;
        var weapon = prefab.GetComponent<Equippable_RangedWeapon>();
        int oldMag = weapon.MagazineSize;
        tune(weapon);
        def.Equippable = weapon;
        var integer = def.TryCast<IntegerItemDefinition>();
        if (integer != null && integer.DefaultValue >= oldMag) integer.DefaultValue = weapon.MagazineSize;   // vem carregada como a original
        var storable = def.TryCast<StorableItemDefinition>();
        if (storable != null) storable.BasePurchasePrice = price;
        _forSale.Add((def, price));
        Log.Debug($"{id}: pente {weapon.MagazineSize}, dano {weapon.Damage:0.#}, cadência {weapon.FireCooldown:0.##}s");
        return def;
    }

    /// <summary>Pente novo (cópia do pente da arma de origem com outra capacidade). Só para recarga por pente.</summary>
    private StorableItemDefinition? Magazine(Equippable_RangedWeapon source, string id, string name, int rounds, int price, List<ItemDefinition> made)
    {
        if (source.Magazine == null || source.ReloadType != Equippable_RangedWeapon.EReloadType.Magazine) return null;
        var mag = CustomItems.Clone(source.Magazine, id, name);
        var integer = mag.TryCast<IntegerItemDefinition>();
        if (integer == null) return null;
        integer.DefaultValue = rounds;
        mag.BasePurchasePrice = price;
        made.Add(mag);
        _forSale.Add((mag, price));
        return mag;
    }

    public override void OnSceneLoaded(string scene) => _listed = false;

    // A loja do Stan só existe na cena Main; tenta até achar (cada cena recria a loja).
    public override void OnUpdate()
    {
        if (_listed || _forSale.Count == 0 || SceneManager.GetActiveScene().name != "Main") return;
        var stan = Object.FindObjectOfType<Stan>();
        if (stan == null || stan.ShopInterface == null) return;
        _listed = true;
        int added = 0;
        foreach (var (item, _) in _forSale)
        {
            var storable = item.TryCast<StorableItemDefinition>();
            if (storable != null && CustomItems.AddListing(stan.ShopInterface, storable, new Color(1f, 0.75f, 0.45f))) added++;
        }
        Log.Info($"{added} armas/pentes à venda no Stan");
    }

    // Automático: com o botão segurado, enfileira o próximo tiro (o jogo respeita o FireCooldown).
    [HarmonyPatch(typeof(Equippable_RangedWeapon), nameof(Equippable_RangedWeapon.Update))]
    private static class AutoFirePatch
    {
        private static void Prefix(Equippable_RangedWeapon __instance)
        {
            if (!I.Active) return;
            var item = __instance.itemInstance;
            if (item == null || !AutoIds.Contains(item.ID)) return;
            if (GameInput.GetButton(GameInput.ButtonCode.PrimaryClick) && __instance.Ammo > 0) __instance.shotQueued = true;
        }
    }
}
