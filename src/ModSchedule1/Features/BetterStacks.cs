using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Core.Items.Framework;   // EItemCategory (Il2CppScheduleOne.Core.dll)
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using MelonLoader;

namespace ModSchedule1.Features;

/// <summary>Multiplica o limite de pilha por categoria de item e a capacidade/velocidade da mixing station.</summary>
public sealed class BetterStacks : Feature<BetterStacks>
{
    public override string Id => "BetterStacks";
    public override string Name => "Better Stacks";
    public override string Description => "Pilhas maiores por categoria de item e mixing station maior/mais rápida.";

    // Multiplicadores por nome de EItemCategory (string, para não quebrar se o jogo renomear categorias).
    private readonly Dictionary<string, MelonPreferences_Entry<int>> _multipliers = new();
    private MelonPreferences_Entry<int> _mixCapacity = null!;
    private MelonPreferences_Entry<int> _mixSpeed = null!;
    // Valor original de cada definição (por ID): aplicar é sempre original x multiplicador, então ligar/desligar não acumula.
    private readonly Dictionary<string, int> _originals = new();

    protected override void OnInit()
    {
        void Mult(string category, int def) =>
            _multipliers[category] = Setting($"Mult_{category}", def, $"Multiplicador: {category}", $"Pilha de itens '{category}' x N (1 = normal)");
        Mult("Product", 2);
        Mult("Packaging", 3);
        Mult("Agriculture", 2);   // era "Growing" antes do 0.4.7
        Mult("Ingredient", 10);
        Mult("Consumable", 10);
        Mult("Tools", 1);
        Mult("Furniture", 1);
        Mult("Equipment", 1);
        Mult("Clothing", 1);
        Mult("Lighting", 1);
        Mult("Decoration", 1);
        _mixCapacity = Setting("MixingStationCapacity", 2, "Mixing station: capacidade x N");
        _mixSpeed = Setting("MixingStationSpeed", 2, "Mixing station: velocidade x N");

        EnabledEntry.OnEntryValueChanged.Subscribe((_, _) => Apply());
        foreach (var m in _multipliers.Values) m.OnEntryValueChanged.Subscribe((_, _) => Apply());
    }

    public int MultiplierFor(EItemCategory category) =>
        _multipliers.TryGetValue(category.ToString(), out var e) && e.Value > 0 ? e.Value : 1;

    public override void OnSceneLoaded(string scene) => Apply();

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
        int target = EnabledEntry.Value && !Faulted ? original * MultiplierFor(def.Category) : original;
        if (def.StackLimit == target) return false;
        Log.Debug($"{def.Name} ({def.Category}): pilha {def.StackLimit} -> {target}");
        def.StackLimit = target;
        return true;
    }

    // Produtos novos (misturas) entram no Registry no meio da partida.
    [HarmonyPatch(typeof(Registry), nameof(Registry.AddToRegistry))]
    private static class AddToRegistryPatch
    {
        private static void Postfix(ItemDefinition item) => I.Guard("AddToRegistry", () => I.ApplyTo(item));
    }

    // ponytail: aplicado quando a estação nasce; mudar a config vale para estações carregadas depois.
    [HarmonyPatch(typeof(MixingStation), "Start")]
    private static class MixingStationPatch
    {
        private static void Prefix(MixingStation __instance)
        {
            I.Guard("MixingStation.Start", () =>
            {
                int oldQty = __instance.MaxMixQuantity, oldTime = __instance.MixTimePerItem;
                __instance.MaxMixQuantity = oldQty * System.Math.Max(1, I._mixCapacity.Value);
                __instance.MixTimePerItem = System.Math.Max(1, oldTime / System.Math.Max(1, I._mixSpeed.Value));
                I.Log.Debug($"mixing station: qtd {oldQty}->{__instance.MaxMixQuantity}, tempo/item {oldTime}->{__instance.MixTimePerItem}");
            });
        }
    }
}
