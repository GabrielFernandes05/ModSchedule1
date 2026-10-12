using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Core.Items.Framework;   // EItemCategory (Il2CppScheduleOne.Core.dll)
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModSchedule1.Features;

/// <summary>
/// Multiplica o limite de pilha por categoria de item e a capacidade da mixing station.
/// Itens com dado próprio por unidade (arma/pente = balas, regador = água, roupa = cor, pegador = lixo) NÃO empilham
/// além do original: o jogo guarda um valor só para a pilha inteira e não compara esse dado ao empilhar, então
/// "2x pente de 7" dividia as mesmas 7 balas e uma recarga parcial esvaziava os dois.
/// Munição soma balas: pentes iguais na barra de atalhos viram um item só (7 + 7 = 14) até o limite configurado;
/// a recarga do jogo já tira do item só as balas que faltam na arma.
/// </summary>
public sealed class BetterStacks : Feature<BetterStacks>
{
    public override string Id => "BetterStacks";
    public override string Name => "Better Stacks";
    public override string Description => "Pilhas maiores por categoria; munição soma balas num item só (7 + 7 = 14); mixer com mais capacidade.";

    // Multiplicadores por nome de EItemCategory (string, para não quebrar se o jogo renomear categorias).
    private readonly Dictionary<string, Pref<int>> _multipliers = new();
    private Pref<int> _mixCapacity = null!, _ammo = null!;
    // Valor original de cada definição (por ID): aplicar é sempre original x multiplicador, então ligar/desligar não acumula.
    private readonly Dictionary<string, int> _originals = new();
    private readonly Dictionary<string, bool> _perUnitData = new();   // ID -> instância guarda dado que a pilha compartilharia
    private readonly Dictionary<string, int> _ammoRounds = new();     // ID do pente/munição -> balas de um item novo
    private float _nextMerge;

    protected override void OnInit()
    {
        void Mult(string category, string label, int def) =>
            _multipliers[category] = Setting($"Mult_{category}", def, $"Pilha x: {label}", $"Pilha de itens '{category}' x N (1 = normal)", min: 1, max: 50);
        Mult("Product", "Produtos", 2);
        Mult("Packaging", "Embalagens", 3);
        Mult("Agriculture", "Agricultura", 2);   // era "Growing" antes do 0.4.7
        Mult("Ingredient", "Ingredientes", 10);
        Mult("Consumable", "Consumíveis", 10);
        Mult("Tools", "Ferramentas", 1);
        Mult("Furniture", "Móveis", 1);
        Mult("Equipment", "Equipamentos", 1);
        Mult("Clothing", "Roupas", 1);
        Mult("Lighting", "Iluminação", 1);
        Mult("Decoration", "Decoração", 1);
        _mixCapacity = Setting("MixingStationCapacity", 2, "Mixer: capacidade x", min: 1, max: 50);
        _ammo = Setting("AmmoPerSlot", 5, "Munição: balas por slot x", "Pentes iguais se juntam até N vezes as balas de um pente (1 = não junta)", min: 1, max: 50);

        Enabled.Changed += () => Apply();
        foreach (var m in _multipliers.Values) m.Changed += () => Apply();
    }

    public int MultiplierFor(EItemCategory category) =>
        _multipliers.TryGetValue(category.ToString(), out var e) && e.Value > 0 ? e.Value : 1;

    public override void OnSceneLoaded(string scene) => Apply();

    public override void OnUpdate()
    {
        if (Time.unscaledTime < _nextMerge) return;
        _nextMerge = Time.unscaledTime + 1f;
        if (_ammo.Value > 1 && SceneManager.GetActiveScene().name == "Main") MergeAmmo();
    }

    /// <summary>
    /// Muda o StackLimit na DEFINIÇÃO do item. A v0.1 patchava só o getter da instância, e a UI/código de merge lia o
    /// limite da definição (20) enquanto a instância dizia 40: arrastar uma pilha sobre outra travava o jogo.
    /// </summary>
    private void Apply()
    {
        try
        {
            if (!Registry.InstanceExists) { Log.Debug("Registry ainda não existe; aplica na próxima cena"); return; }
            int changed = 0;
            FindAmmo();
            foreach (var def in Registry.Instance.GetAllItems())
                if (ApplyTo(def)) changed++;
            Log.Info($"limites de pilha aplicados: {changed} itens alterados ({_originals.Count} conhecidos)");
        }
        catch (System.Exception e) { Fault("Apply", e); }
    }

    private bool ApplyTo(ItemDefinition? def)
    {
        if (def == null) return false;
        if (!_originals.TryGetValue(def.ID, out int original))
            _originals[def.ID] = original = def.StackLimit;
        bool stackable = original > 1 || !HasPerUnitData(def);   // o jogo já empilhava: ele sabe lidar
        int target = Enabled.Value && !Faulted && stackable ? original * MultiplierFor(def.Category) : original;
        if (def.StackLimit == target) return false;
        Log.Debug($"{def.Name} ({def.Category}): pilha {def.StackLimit} -> {target}");
        def.StackLimit = target;
        return true;
    }

    /// <summary>
    /// A instância padrão do item é só StorableItemInstance ou com qualidade (produtos comparam qualidade/embalagem ao
    /// empilhar)? Qualquer outro tipo guarda dado próprio que a pilha compartilharia.
    /// </summary>
    private bool HasPerUnitData(ItemDefinition def)
    {
        if (_perUnitData.TryGetValue(def.ID, out bool cached)) return cached;
        bool perUnit;
        try
        {
            var inst = def.GetDefaultInstance(1);
            perUnit = inst != null && inst.TryCast<QualityItemInstance>() == null
                      && inst.GetIl2CppType().FullName != "ScheduleOne.ItemFramework.StorableItemInstance";
            if (perUnit) Log.Debug($"{def.Name}: {inst!.GetIl2CppType().Name} guarda dado por unidade; pilha fica {def.StackLimit}");
        }
        catch (Exception e) { perUnit = true; Log.Debug($"{def.Name}: tipo de instância desconhecido ({e.Message}); pilha original"); }
        return _perUnitData[def.ID] = perUnit;
    }

    /// <summary>Munição de cada arma do jogo (e das do More Guns) que guarda balas no item (pente, tambor).</summary>
    private void FindAmmo()
    {
        foreach (var def in Registry.Instance.GetAllItems())
        {
            var eq = def?.Equippable;
            var weapon = eq != null ? eq.GetComponent<Equippable_RangedWeapon>() : null;
            var ammo = weapon != null && weapon.Magazine != null ? weapon.Magazine.TryCast<IntegerItemDefinition>() : null;
            if (ammo != null && ammo.DefaultValue > 0 && !_ammoRounds.ContainsKey(ammo.ID)) _ammoRounds[ammo.ID] = ammo.DefaultValue;
        }
    }

    /// <summary>
    /// Junta munição igual na barra de atalhos (é de lá que a recarga tira): pilhas antigas "2x de 7" viram um item
    /// de 14 e itens separados enchem o primeiro até o limite. Só move balas entre itens (conta em AmmoMerge).
    /// </summary>
    private void MergeAmmo()
    {
        if (!PlayerInventory.InstanceExists || _ammoRounds.Count == 0) return;
        var inv = PlayerInventory.Instance;
        // recarga em andamento guarda referência ao item de munição: não mexer nele agora
        if (inv.Equippable != null && inv.Equippable.TryCast<Equippable_RangedWeapon>() is { IsReloading: true }) return;
        var slots = inv.hotbarSlots;
        foreach (var (id, rounds) in _ammoRounds)
        {
            var found = new List<(ItemSlot Slot, IntegerItemInstance Ammo)>();
            var before = new List<int>();
            for (int i = 0; i < slots.Count; i++)
            {
                var ammo = slots[i]?.ItemInstance?.TryCast<IntegerItemInstance>();
                if (ammo == null || ammo.ID != id || ammo.Value <= 0) continue;
                found.Add((slots[i], ammo));
                before.Add(ammo.Value * ammo.Quantity);   // pilha antiga: a quantidade dividia as mesmas balas
            }
            if (found.Count == 0 || (found.Count == 1 && found[0].Ammo.Quantity == 1)) continue;
            var after = AmmoMerge.Merge(before, Math.Min(60000, rounds * _ammo.Value));   // o jogo grava balas em 16 bits
            for (int i = 0; i < found.Count; i++)
            {
                var (slot, ammo) = found[i];
                if (after[i] == 0) { slot.ClearStoredInstance(); continue; }
                if (ammo.Quantity > 1) slot.SetQuantity(1);
                if (ammo.Value != after[i]) ammo.SetValue(after[i]);
            }
            Log.Debug($"munição {id}: [{string.Join(", ", before)}] -> [{string.Join(", ", after)}]");
        }
    }

    // Produtos novos (misturas) entram no Registry no meio da partida.
    [HarmonyPatch(typeof(Registry), nameof(Registry.AddToRegistry))]
    private static class AddToRegistryPatch
    {
        private static void Postfix(ItemDefinition item) => I.Guard("AddToRegistry", () => I.ApplyTo(item));
    }

    // Awake roda antes de InitializeGridItem montar a config do funcionário (slider de quantidade usa MaxMixQuantity).
    // Velocidade da mixing station ficou no StationSpeed.
    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.Awake))]
    private static class MixingStationPatch
    {
        private static void Postfix(MixingStation __instance) => I.Guard("MixingStation.Awake", () =>
        {
            int old = __instance.MaxMixQuantity;
            __instance.MaxMixQuantity = old * System.Math.Max(1, I._mixCapacity.Value);
            I.Log.Debug($"mixing station: capacidade {old} -> {__instance.MaxMixQuantity}");
        });
    }
}
