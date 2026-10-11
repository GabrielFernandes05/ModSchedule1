using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.UI.Phone.Delivery;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Money;
using ModSchedule1.PhoneUI;
using Object = UnityEngine.Object;
using UnityEngine;
using UnityEngine.UI;
using GameProperty = Il2CppScheduleOne.Property.Property;

namespace ModSchedule1.Features;

/// <summary>
/// DeliveryApp++ dentro do app Delivery nativo: ★ favoritar e "Todo dia" em cada pedido do histórico, favoritos
/// no topo como cartões nativos e pedidos recorrentes diários. Todo pedido passa pelo DeliveryApp.Reorder do jogo
/// (mesmas checagens, mesmo pagamento pelo banco, funciona para host e clientes).
/// </summary>
public sealed class Deliveries : Feature<Deliveries>
{
    public override string Id => "Deliveries";
    public override string Name => "DeliveryApp++";
    public override string Description => "Favoritos e pedidos recorrentes no app Delivery do celular.";
    public override bool Shared => false;   // pessoal: cada jogador usa a própria config

    public sealed class OrderItem { public string Id = ""; public int Qty; }
    public sealed class Favorite
    {
        public string Store = "", Property = "";
        public int Dock;                 // 0-based, como no recibo do jogo
        public List<OrderItem> Items = new();
        public int RecurringHour = -1;   // -1 = desligado; 0..23 = todo dia a partir dessa hora
        public int LastOrderedDay = -1;
    }
    public sealed class Data { public List<Favorite> Favorites = new(); }

    private const string File = "deliveries";
    public Data Saved { get; private set; } = new();

    protected override void OnInit() => Saved = Store.Load<Data>(File);

    public void Persist() => Store.Save(File, Saved);

    // ---------- integração com o app Delivery nativo ----------

    private static string Signature(string store, string property, int dock, IEnumerable<(string Id, int Qty)> items) =>
        $"{store}|{property}|{dock}|" + string.Join(",", items.Where(i => i.Qty > 0).OrderBy(i => i.Id).Select(i => $"{i.Id}x{i.Qty}"));

    public static string Signature(Favorite f) => Signature(f.Store, f.Property, f.Dock, f.Items.Select(i => (i.Id, i.Qty)));

    public static string Signature(DeliveryReceipt r)
    {
        var items = new List<(string, int)>();
        foreach (var p in r.Items) if (p != null) items.Add((p.String, p.Int));
        return Signature(r.StoreName, r.DestinationCode, r.LoadingDockIndex, items);
    }

    public Favorite? FindFavorite(DeliveryReceipt r)
    {
        var sig = Signature(r);
        return Saved.Favorites.FirstOrDefault(f => Signature(f) == sig);
    }

    private static DeliveryReceipt ToReceipt(Favorite f)
    {
        var items = new Il2CppReferenceArray<StringIntPair>(f.Items.Count);
        for (int i = 0; i < f.Items.Count; i++) items[i] = new StringIntPair(f.Items[i].Id, f.Items[i].Qty);
        return new DeliveryReceipt("", f.Store, f.Property, f.Dock, items);
    }

    private sealed class ReceiptButtons { public Button Star = null!, Repeat = null!; }
    private readonly Dictionary<IntPtr, ReceiptButtons> _buttons = new();
    private readonly List<GameObject> _favoriteCards = new();
    private bool _dumped;

    private static readonly Color StarOn = new(0.95f, 0.75f, 0.2f);

    /// <summary>
    /// "Favoritar" e "Repetir" numa linha própria no fim da lista de itens do pedido (_ItemEntryContainer: o jogo só
    /// liga/desliga as linhas de item, com layout automático; a nossa entra embaixo sem sobrepor nada).
    /// Botões clonados do "Reorder" nativo.
    /// </summary>
    private void AddButtons(DeliveryReceiptDisplay display)
    {
        var reorder = display.ReorderButton;
        var items = display._ItemEntryContainer;
        if (reorder == null || items == null || _buttons.ContainsKey(display.Pointer)) return;
        NativeUi.DumpOnce(ref _dumped, display.transform, "DeliveryReceiptDisplay", 3);
        var row = Ui.Child("MS1_FavRow", items);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        var rle = row.AddComponent<LayoutElement>();
        rle.minHeight = rle.preferredHeight = 30;
        var b = new ReceiptButtons
        {
            Star = RowButton(reorder, row.transform, "MS1_Star", 110, () => Guard("favoritar", () => ToggleFavorite(display))),
            Repeat = RowButton(reorder, row.transform, "MS1_Repeat", 140, () => Guard("recorrente", () => CycleRepeat(display))),
        };
        _buttons[display.Pointer] = b;
    }

    private static Button RowButton(Button template, Transform row, string name, float width, Action onClick)
    {
        var b = NativeUi.CloneButton(template, row, name, onClick);
        var le = b.GetComponent<LayoutElement>();
        le.ignoreLayout = false;
        le.minWidth = le.preferredWidth = width;
        le.minHeight = le.preferredHeight = 28;
        foreach (var t in b.GetComponentsInChildren<Text>(true)) t.text = "";   // tira o preço clonado do Reorder
        return b;
    }

    private void Bind(DeliveryReceiptDisplay display)
    {
        if (!_buttons.TryGetValue(display.Pointer, out var b) || b.Star == null) return;
        var receipt = display.Receipt;
        b.Star.transform.parent.gameObject.SetActive(Active && receipt != null);
        b.Star.transform.parent.SetAsLastSibling();
        if (receipt == null) return;
        var fav = FindFavorite(receipt);
        NativeUi.SetText(b.Star, fav != null ? "Favorito" : "Favoritar");   // ASCII: a fonte do jogo pode não ter ★
        var starText = b.Star.GetComponentInChildren<Text>(true);
        if (starText != null) starText.color = fav != null ? StarOn : Color.white;
        NativeUi.SetText(b.Repeat, fav == null || fav.RecurringHour < 0 ? "Repetir: não" : $"Todo dia {fav.RecurringHour:00}h");
    }

    private void ToggleFavorite(DeliveryReceiptDisplay display)
    {
        var r = display.Receipt;
        if (r == null) return;
        var fav = FindFavorite(r);
        if (fav != null) Saved.Favorites.Remove(fav);
        else Saved.Favorites.Add(FromReceipt(r.StoreName, r.DestinationCode, r.LoadingDockIndex, r.Items));
        Persist();
        RefreshApp();
    }

    // ciclo: desligado -> 6h -> 12h -> 18h -> desligado (recorrente vira favorito automaticamente)
    private void CycleRepeat(DeliveryReceiptDisplay display)
    {
        var r = display.Receipt;
        if (r == null) return;
        var fav = FindFavorite(r);
        if (fav == null) Saved.Favorites.Add(fav = FromReceipt(r.StoreName, r.DestinationCode, r.LoadingDockIndex, r.Items));
        fav.RecurringHour = fav.RecurringHour switch { < 0 => 6, < 12 => 12, < 18 => 18, _ => -1 };
        Persist();
        RefreshApp();
    }

    private static void RefreshApp()
    {
        if (DeliveryApp.InstanceExists) DeliveryApp.Instance.UpdatePastDeliveries();
    }

    /// <summary>Favoritos como cartões nativos (mesmo prefab do histórico) no topo da aba de pedidos anteriores.</summary>
    private void ShowFavorites(DeliveryApp app)
    {
        foreach (var go in _favoriteCards) if (go != null) Object.Destroy(go);
        _favoriteCards.Clear();
        if (!Active) return;
        foreach (var fav in Saved.Favorites.AsEnumerable().Reverse())
        {
            var f = fav;
            if (app.GetShop(f.Store) == null || !GameProperty.OwnedProperties.ToArray().Any(p => p != null && p.PropertyCode == f.Property))
                continue;   // Set do jogo quebra com loja/propriedade inexistente
            var card = Object.Instantiate(app._deliveryReceiptPrefab, app.PastDeliveriesContainer, false);
            card.gameObject.name = "MS1_Favorite";
            card.transform.SetAsFirstSibling();
            card.Initialise();
            card.SubscribeToOnSelect(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<DeliveryReceipt>>(
                new Action<DeliveryReceipt>(_ => Guard("pedir favorito", () =>
                {
                    var why = Order(f);
                    if (why != null) Log.Warn("favorito não pedido: " + why);
                    RefreshApp();
                }))));
            var receipt = ToReceipt(f);
            float cost = app.GetDeliveryCost(receipt);
            card.Set(receipt, cost, MoneyManager.Instance.sync___get_value_onlineBalance() >= cost);
            _favoriteCards.Add(card.gameObject);
        }
        DeliveryApp.RefreshLayoutGroupsImmediateAndRecursive(app.PastDeliveriesContainer.gameObject);
    }

    [HarmonyPatch(typeof(DeliveryReceiptDisplay), nameof(DeliveryReceiptDisplay.Initialise))]
    private static class InitialisePatch
    {
        private static void Postfix(DeliveryReceiptDisplay __instance) => I.Guard("DeliveryReceiptDisplay.Initialise", () => I.AddButtons(__instance));
    }

    [HarmonyPatch(typeof(DeliveryReceiptDisplay), nameof(DeliveryReceiptDisplay.Set))]
    private static class SetPatch
    {
        private static void Postfix(DeliveryReceiptDisplay __instance) => I.Guard("DeliveryReceiptDisplay.Set", () => I.Bind(__instance));
    }

    [HarmonyPatch(typeof(DeliveryApp), nameof(DeliveryApp.UpdatePastDeliveries))]
    private static class PastDeliveriesPatch
    {
        private static void Postfix(DeliveryApp __instance) => I.Guard("DeliveryApp.UpdatePastDeliveries", () => I.ShowFavorites(__instance));
    }

    public static Favorite FromReceipt(string store, string property, int dock, Il2CppReferenceArray<StringIntPair> items)
    {
        var f = new Favorite { Store = store, Property = property, Dock = dock };
        foreach (var p in items) if (p != null && p.Int > 0) f.Items.Add(new OrderItem { Id = p.String, Qty = p.Int });
        return f;
    }

    public static string Describe(Favorite f)
    {
        string items = string.Join(", ", f.Items.Select(i => $"{i.Qty}x {Registry.GetItem(i.Id)?.Name ?? i.Id}"));
        return $"<b>{f.Store}</b> -> {PropertyName(f.Property)} (doca {f.Dock + 1})\n{items}";
    }

    public static string PropertyName(string code)
    {
        foreach (var p in GameProperty.OwnedProperties) if (p != null && p.PropertyCode == code) return p.PropertyName;
        return code + " (não é sua)";
    }

    /// <summary>Faz o pedido pelo caminho do jogo. Devolve null se deu certo, ou o motivo de não ter pedido.</summary>
    public string? Order(Favorite f)
    {
        if (!Active) return "mod desativado";
        GameProperty? property = null;
        foreach (var p in GameProperty.OwnedProperties) if (p != null && p.PropertyCode == f.Property) property = p;
        if (property == null) return "a propriedade de destino não é sua neste save";
        if (f.Dock < 0 || f.Dock >= property.LoadingDockCount) return "doca de entrega inexistente";
        if (!DeliveryApp.InstanceExists) return "abra o app Delivery do jogo uma vez";
        var app = DeliveryApp.Instance;
        var shop = app.GetShop(f.Store);
        if (shop == null) return $"loja '{f.Store}' indisponível";

        var items = new Il2CppReferenceArray<StringIntPair>(f.Items.Count);
        for (int i = 0; i < f.Items.Count; i++) items[i] = new StringIntPair(f.Items[i].Id, f.Items[i].Qty);
        var receipt = new DeliveryReceipt("", f.Store, f.Property, f.Dock, items);   // ID vazio = novo recibo no histórico
        shop.ResetCart();   // Reorder do jogo não limpa o que já estava no carrinho
        if (!app.CanReorder(receipt, out string reason)) return reason;
        app.Reorder(receipt);
        Log.Info($"pedido feito: {f.Store} -> {f.Property}");
        return null;
    }

    // Recorrentes: checa por polling (dormir pulando a hora ainda dispara no dia).
    private float _nextCheck;
    public override void OnUpdate()
    {
        if (Time.unscaledTime < _nextCheck || !TimeManager.InstanceExists) return;
        _nextCheck = Time.unscaledTime + 5f;
        var tm = TimeManager.Instance;
        bool changed = false;
        foreach (var f in Saved.Favorites)
        {
            if (f.RecurringHour < 0 || f.LastOrderedDay >= tm.ElapsedDays || tm.CurrentTime < f.RecurringHour * 100) continue;
            f.LastOrderedDay = tm.ElapsedDays;   // tenta uma vez por dia, mesmo se falhar (sem spam)
            changed = true;
            var why = Order(f);
            if (why != null) Log.Warn($"pedido recorrente de {f.Store} não feito: {why}");
        }
        if (changed) Persist();
    }
}
