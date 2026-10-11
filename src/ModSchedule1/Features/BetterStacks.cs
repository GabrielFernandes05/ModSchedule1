using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.Core.Items.Framework;   // EItemCategory (mudou de ItemFramework em versões recentes)
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

    // Multiplicadores por nome de EItemCategory (string, para não quebrar se o jogo adicionar categorias).
    private readonly Dictionary<string, MelonPreferences_Entry<int>> _multipliers = new();
    private MelonPreferences_Entry<int> _mixCapacity = null!;
    private MelonPreferences_Entry<int> _mixSpeed = null!;
    private readonly HashSet<string> _logged = new();

    protected override void OnInit()
    {
        void Mult(string category, int def) =>
            _multipliers[category] = Setting($"Mult_{category}", def, $"Multiplicador: {category}", $"Pilha de itens '{category}' x N (1 = normal)");
        Mult("Product", 2);
        Mult("Packaging", 3);
        Mult("Growing", 2);
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
    }

    // StackLimit é chamado o tempo todo: resolve a entry da categoria uma vez só (evita ToString por chamada).
    private readonly Dictionary<EItemCategory, MelonPreferences_Entry<int>?> _byCategory = new();

    public int MultiplierFor(EItemCategory category)
    {
        if (!_byCategory.TryGetValue(category, out var e))
            _byCategory[category] = e = _multipliers.TryGetValue(category.ToString(), out var found) ? found : null;
        return e != null && e.Value > 0 ? e.Value : 1;
    }

    [HarmonyPatch(typeof(ItemInstance), nameof(ItemInstance.StackLimit), MethodType.Getter)]
    private static class StackLimitPatch
    {
        private static void Postfix(ItemInstance __instance, ref int __result)
        {
            if (!I.Active) return;
            try
            {
                int baseLimit = __result;
                __result = baseLimit * I.MultiplierFor(__instance.Category);
                if (Core.DebugLogging && I._logged.Add(__instance.Name))
                    I.Log.Debug($"{__instance.Name} ({__instance.Category}): pilha {baseLimit} -> {__result}");
            }
            catch (System.Exception e) { I.Fault("StackLimit", e); }
        }
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
