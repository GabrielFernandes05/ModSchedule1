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
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModSchedule1.Features;

/// <summary>
/// Multiplica o limite de pilha por categoria de item e a capacidade da mixing station.
/// Itens com dado próprio por unidade (arma/pente = balas, regador = água, roupa = cor, pegador = lixo) NÃO empilham
/// além do original: o jogo guarda um valor só para a pilha inteira e não compara esse dado ao empilhar, então
/// "2x pente de 7" dividia as mesmas 7 balas e uma recarga parcial esvaziava os dois.
/// Exceção: munição (pente, tambor, cartucho) empilha por QUANTIDADE ("3x pente de 7", o scroll divide), com dois
/// furos do jogo tapados: pente parcial não empilha com cheio (o valor da pilha é um só) e, durante a recarga, o pente
/// que a arma vai consumir é separado da pilha (senão a recarga parcial tiraria balas de todos os pentes juntos).
/// </summary>
public sealed class BetterStacks : Feature<BetterStacks>
{
    public override string Id => "BetterStacks";
    public override string Name => "Better Stacks";
    public override string Description => "Pilhas maiores por categoria; pentes empilham por quantidade sem perder balas na recarga; mixer com mais capacidade.";

    // Multiplicadores por nome de EItemCategory (string, para não quebrar se o jogo renomear categorias).
    private readonly Dictionary<string, Pref<int>> _multipliers = new();
    private Pref<int> _mixCapacity = null!, _ammo = null!;
    private bool _warnedFull;
    // Valor original de cada definição (por ID): aplicar é sempre original x multiplicador, então ligar/desligar não acumula.
    private readonly Dictionary<string, int> _originals = new();
    private readonly Dictionary<string, bool> _perUnitData = new();   // ID -> instância guarda dado que a pilha compartilharia
    private readonly Dictionary<string, int> _ammoRounds = new();     // ID do pente/munição -> balas de um item novo
    private float _nextConvert;

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
        _ammo = Setting("Mult_Ammo", 5, "Pilha x: Munição (pentes)", "Pentes/cartuchos por pilha (1 = como no jogo)", min: 1, max: 50);

        Enabled.Changed += () => { Apply(); RefreshAmmoLabels(); };
        _ammo.Changed += () => Apply();
        foreach (var m in _multipliers.Values) m.Changed += () => Apply();
    }

    public int MultiplierFor(EItemCategory category) =>
        _multipliers.TryGetValue(category.ToString(), out var e) && e.Value > 0 ? e.Value : 1;

    public override void OnSceneLoaded(string scene) => Apply();

    public override void OnUpdate()
    {
        if (_ammoRounds.Count == 0 || SceneManager.GetActiveScene().name != "Main" || !PlayerInventory.InstanceExists) return;
        var inv = PlayerInventory.Instance;
        var weapon = inv.Equippable != null ? inv.Equippable.TryCast<Equippable_RangedWeapon>() : null;
        if (weapon != null && weapon.IsReloading)
        {
            if (weapon.Magazine != null && _ammoRounds.ContainsKey(weapon.Magazine.ID)) SplitForReload(inv, weapon.Magazine.ID);
            return;   // nada de converter itens durante a recarga
        }
        if (Time.unscaledTime < _nextConvert) return;
        _nextConvert = Time.unscaledTime + 1f;
        ConvertMerged(inv);
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
        bool ammo = _ammoRounds.ContainsKey(def.ID);
        bool stackable = ammo || original > 1 || !HasPerUnitData(def);   // o jogo já empilhava: ele sabe lidar
        int mult = ammo ? Math.Max(1, _ammo.Value) : MultiplierFor(def.Category);
        int target = Enabled.Value && !Faulted && stackable ? original * mult : original;
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
    /// A recarga usa o PRIMEIRO slot da barra com o pente da arma. Se ele for uma pilha, deixamos nele só um pente
    /// (o mesmo objeto, que a recarga em andamento referencia) e o resto vai para um slot livre ou para outra pilha
    /// igual. Só acontece durante a recarga, então não mexe no inventário que o jogador arrumou.
    /// </summary>
    private void SplitForReload(PlayerInventory inv, string id)
    {
        var slots = inv.hotbarSlots;
        ItemSlot? first = null, free = null, freeBefore = null, sameStack = null;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null) continue;
            if (slot.ItemInstance == null)
            {
                if (slot.IsLocked || slot.IsAddLocked) continue;
                if (first == null) freeBefore ??= slot; else free ??= slot;   // a recarga pega o primeiro slot com o pente
                continue;
            }
            if (slot.ItemInstance.ID != id) continue;
            if (first == null) { first = slot; continue; }
            var other = slot.ItemInstance.TryCast<IntegerItemInstance>();
            var head = first.ItemInstance.TryCast<IntegerItemInstance>();
            if (sameStack == null && other != null && head != null && other.Value == head.Value) sameStack = slot;
        }
        if (first == null || first.Quantity <= 1) return;
        int rest = first.Quantity - 1;
        var single = first.ItemInstance;   // a recarga em andamento referencia este objeto: ele tem que virar o pente único
        var restItem = single.GetCopy(rest);
        if (sameStack != null && sameStack.GetCapacityForItem(restItem) >= rest) { sameStack.ChangeQuantity(rest); first.SetQuantity(1); }
        else if (free != null) { free.SetStoredItem(restItem); first.SetQuantity(1); }
        else if (freeBefore != null)
        {
            // só há espaço antes da pilha: o pente único vai para lá (vira o primeiro) e o resto fica onde estava
            first.SetStoredItem(restItem);
            single.SetQuantity(1);
            freeBefore.SetStoredItem(single);
        }
        else
        {
            if (!_warnedFull) { _warnedFull = true; Log.Warn("barra cheia: recarga direto da pilha de pentes (o jogo pode descontar balas da pilha toda)"); }
            return;
        }
        Log.Debug($"recarga: 1 pente de {id} separado da pilha ({rest} ficaram no outro slot)");
    }

    /// <summary>A 0.12.1 juntava balas num item só (ex.: 35): volta a ser pilha de pentes (5x7) quando couber.</summary>
    private void ConvertMerged(PlayerInventory inv)
    {
        var slots = inv.hotbarSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            var ammo = slots[i]?.ItemInstance?.TryCast<IntegerItemInstance>();
            if (ammo == null || ammo.Quantity != 1 || !_ammoRounds.TryGetValue(ammo.ID, out int full) || ammo.Value <= full) continue;
            int mags = ammo.Value / full, rem = ammo.Value % full;
            if (mags > ammo.StackLimit) continue;   // não cabe numa pilha: fica como está (funciona igual na recarga)
            ItemSlot? free = null;
            if (rem > 0)
            {
                for (int j = 0; j < slots.Count && free == null; j++)
                    if (slots[j] != null && slots[j].ItemInstance == null && !slots[j].IsLocked && !slots[j].IsAddLocked) free = slots[j];
                if (free == null) continue;
                var partial = ammo.GetCopy(1).TryCast<IntegerItemInstance>()!;
                partial.Value = rem;
                free.SetStoredItem(partial);
            }
            Log.Info($"munição {ammo.ID}: item de {ammo.Value} balas virou {mags}x{full}{(rem > 0 ? $" + 1x{rem}" : "")}");
            ammo.SetValue(full);
            slots[i].SetQuantity(mags);
        }
    }

    // Pente parcial não empilha com cheio: a pilha tem um valor só, então juntar 3 + 7 viraria 2x7 (ou 2x3).
    [HarmonyPatch(typeof(ItemInstance), nameof(ItemInstance.CanStackWith))]
    private static class AmmoStackPatch
    {
        private static void Postfix(ItemInstance __instance, ItemInstance other, ref bool __result)
        {
            if (!__result || other == null || !I._ammoRounds.ContainsKey(__instance.ID)) return;
            var a = __instance.TryCast<IntegerItemInstance>();
            var b = other.TryCast<IntegerItemInstance>();
            if (a != null && b != null && a.Value != b.Value) __result = false;
        }
    }

    // ---------- exibição da munição ----------
    // O slot do jogo mostra o valor do item ("7") e a quantidade ("2x") um por cima do outro. Para munição mostramos
    // o total de balas (14) e escondemos o "Nx"; o scroll continua dividindo por pente (a pilha é por quantidade).

    private bool IsAmmo(IntegerItemInstance? inst) => inst != null && Enabled.Value && !Faulted && _ammoRounds.ContainsKey(inst.ID);

    /// <summary>Redesenha os slots de munição na tela (o jogo só redesenha quando o item muda).</summary>
    private void RefreshAmmoLabels()
    {
        try
        {
            foreach (var ui in UnityEngine.Object.FindObjectsOfType<IntegerItemUI>())
                if (ui != null && ui.integerItemInstance != null && _ammoRounds.ContainsKey(ui.integerItemInstance.ID)) ui.UpdateUI();
        }
        catch (Exception e) { Log.Debug($"redesenhar munição: {e.Message}"); }
    }

    // Todo desenho de item num slot (inventário, barra, baús, estações, cópia arrastada) passa por aqui (virtual).
    // Usa o argumento: durante a divisão o jogo passa o que fica no slot de origem ou o que está sendo arrastado.
    [HarmonyPatch(typeof(ItemUI), nameof(ItemUI.SetDisplayedQuantity))]
    private static class AmmoLabelPatch
    {
        private static void Postfix(ItemUI __instance, int quantity)
        {
            if (quantity <= 0) return;
            var ui = __instance.TryCast<IntegerItemUI>();
            if (ui == null || ui.ValueLabel == null || !I.IsAmmo(ui.integerItemInstance)) return;
            ui.ValueLabel.text = (ui.integerItemInstance.Value * quantity).ToString();
            if (__instance.QuantityLabel != null) __instance.QuantityLabel.text = string.Empty;
        }
    }

    private static int _lastDragAmount = -1;
    private static string? _valuePath;
    private static bool _valuePathLogged;

    /// <summary>Caminho "a/b/c" de <paramref name="target"/> a partir de <paramref name="root"/>; null se não for filho.</summary>
    private static string? RelativePath(Transform root, Transform target)
    {
        var names = new List<string>();
        for (var t = target; t != null; t = t.parent)
        {
            if (t == root) { names.Reverse(); return string.Join("/", names); }
            names.Add(t.name);
        }
        return null;
    }

    // Divisão com o scroll: o jogo reescreve "Nx" no ícone arrastado (SetDraggedAmount pode estar inlinado no Update,
    // então corrigimos no LateUpdate, depois dele e antes de desenhar).
    [HarmonyPatch(typeof(ItemUIManager), nameof(ItemUIManager.LateUpdate))]
    private static class AmmoDragPatch
    {
        private static void Postfix(ItemUIManager __instance)
        {
            if (__instance.draggedSlot == null || __instance.tempIcon == null || !__instance.customDragAmount) { _lastDragAmount = -1; return; }
            int amount = __instance.draggedAmount;
            if (amount == _lastDragAmount) return;
            var ui = __instance.draggedSlot.ItemUI != null ? __instance.draggedSlot.ItemUI.TryCast<IntegerItemUI>() : null;
            if (ui == null || ui.ValueLabel == null || ui.IconImg == null || !I.IsAmmo(ui.integerItemInstance)) return;
            _lastDragAmount = amount;
            _valuePath ??= RelativePath(ui.IconImg.transform, ui.ValueLabel.transform);
            if (!_valuePathLogged)
            {
                _valuePathLogged = true;
                I.Log.Debug(_valuePath == null ? "rótulo de balas fora do ícone: a cópia arrastada mantém o \"Nx\"" : $"rótulo de balas no ícone: '{_valuePath}'");
            }
            if (string.IsNullOrEmpty(_valuePath)) return;   // sem o rótulo de balas na cópia: deixa o "Nx" para o jogador ver quanto divide
            var label = __instance.tempIcon.Find(_valuePath)?.GetComponent<UnityEngine.UI.Text>();
            if (label == null) return;
            label.text = (ui.integerItemInstance.Value * amount).ToString();
            var quantity = __instance.tempIcon.Find("Quantity");
            if (quantity != null) quantity.gameObject.SetActive(false);
        }
    }

    // A dica do item mostra só nome/descrição: com o "Nx" escondido, dizemos quantos pentes formam o total.
    [HarmonyPatch(typeof(ItemInfoContent), nameof(ItemInfoContent.Initialize), new[] { typeof(ItemInstance) })]
    private static class AmmoInfoPatch
    {
        private static void Postfix(ItemInfoContent __instance, ItemInstance instance)
        {
            var ammo = instance?.TryCast<IntegerItemInstance>();
            if (!I.IsAmmo(ammo) || ammo!.Quantity <= 1 || __instance.DescriptionLabel == null) return;
            __instance.DescriptionLabel.text += $"\n<b>{ammo.Quantity} x {ammo.Value} balas</b> ({ammo.Quantity * ammo.Value} no total). Scroll divide por pente.";
        }
    }

    // ---------- juntar pilhas ----------
    // Ao soltar uma pilha sobre outra, o jogo move UMA unidade por vez (cada uma com RPC e redesenho do slot). Com
    // pilhas 10x maiores isso vira centenas de RPCs e redesenhos num frame: o "lag ao stackar". Movemos tudo menos
    // a última unidade de uma vez; o laço do jogo move a última e segue o fluxo normal (onItemMoved, limpeza).
    private static bool _loggedMerge;

    private static void PreMerge(ItemUIManager m, ItemSlotUI? hovered)
    {
        if (!I.Active || m.isDraggingCash) return;
        var drag = m.draggedSlot;
        if (drag == null || hovered == null || hovered.Pointer == drag.Pointer || !m.CanDragFromSlot(drag)) return;
        ItemSlot? from = drag.assignedSlot, to = hovered.assignedSlot;
        if (from?.ItemInstance == null || to?.ItemInstance == null || to.IsLocked || to.IsAddLocked
            || !to.DoesItemMatchHardFilters(from.ItemInstance) || !to.ItemInstance.CanStackWith(from.ItemInstance, false)) return;
        int n = Math.Min(to.ItemInstance.StackLimit - to.Quantity, m.draggedAmount) - 1;
        if (n <= 0) return;
        to.ChangeQuantity(n);
        from.ChangeQuantity(-n);
        m.draggedAmount -= n;
        if (!_loggedMerge) { _loggedMerge = true; I.Log.Debug($"pilhas juntadas de uma vez ({n + 1} unidades)"); }
    }

    // Caminho do mouse: o Update solta o arrasto quando nenhum botão está pressionado (mesma condição do jogo).
    // Update é mensagem da Unity (nunca inlinada); o EndDrag sozinho pode estar inlinado em alguns chamadores.
    [HarmonyPatch(typeof(ItemUIManager), nameof(ItemUIManager.Update))]
    private static class MergeOnReleasePatch
    {
        private static void Prefix(ItemUIManager __instance)
        {
            if (__instance.draggedSlot == null || !__instance.DraggingEnabled || GameInput.GetCurrentInputDeviceIsGamepad()) return;
            if (GameInput.GetButton(GameInput.ButtonCode.PrimaryClick) || GameInput.GetButton(GameInput.ButtonCode.SecondaryClick)
                || GameInput.GetButton(GameInput.ButtonCode.TertiaryClick)) return;
            I.Guard("juntar pilhas", () => PreMerge(__instance, __instance.GetHoveredItemSlot()));
        }
    }

    // Demais caminhos (controle, desligar arrasto): se o Update já juntou, aqui não sobra nada a fazer.
    [HarmonyPatch(typeof(ItemUIManager), nameof(ItemUIManager.EndDrag))]
    private static class MergeOnEndDragPatch
    {
        private static void Prefix(ItemUIManager __instance) => I.Guard("juntar pilhas", () => PreMerge(__instance, __instance.HoveredSlot));
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
