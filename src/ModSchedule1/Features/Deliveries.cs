using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.UI.Phone.Delivery;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.UI;
using GameProperty = Il2CppScheduleOne.Property.Property;

namespace ModSchedule1.Features;

/// <summary>
/// App "Entregas" (DeliveryApp++): entregas em andamento, favoritos salvos e pedidos recorrentes diários.
/// O jogo já tem histórico + reordenar; todo pedido daqui passa pelo DeliveryApp.Reorder do jogo (mesmas checagens,
/// mesmo pagamento pelo banco, funciona para host e clientes).
/// </summary>
public sealed class Deliveries : Feature<Deliveries>
{
    public override string Id => "Deliveries";
    public override string Name => "DeliveryApp++";
    public override string Description => "App Entregas: favoritos, pedidos recorrentes e entregas em andamento.";
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

    protected override void OnInit()
    {
        Saved = Store.Load<Data>(File);
        PhoneApp.All.Add(new DeliveriesApp());
    }

    public void Persist() => Store.Save(File, Saved);

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

public sealed class DeliveriesApp : PhoneApp
{
    public override string AppName => "ModSchedule1Deliveries";
    public override string IconLabel => "Entregas";
    public override string IconGlyph => "E";
    public override Color IconColor => new(0.15f, 0.55f, 0.55f);
    protected override bool ShowIcon => Core.SeparateIcons;

    private Transform _list = null!;
    private Text _active = null!, _message = null!;

    protected override void BuildUI(GameObject container)
    {
        _list = Header(container, "Entregas", out _);
    }

    protected override void OnOpened() => Rebuild();

    // Só o bloco "em andamento" muda sozinho; o resto é reconstruído quando o jogador age.
    protected override void OnTick()
    {
        if (_active == null || !Deliveries.I.Active || !DeliveryManager.InstanceExists) return;
        var lines = new List<string>();
        foreach (var d in DeliveryManager.Instance.Deliveries)
        {
            if (d == null) continue;
            int t = d.GetTimeStatus();
            string status = t == -2 ? "chegou, na doca" : t == -1 ? "esperando doca livre" : $"chega em {t / 60}h{t % 60:00}";
            lines.Add($"{d.StoreName} -> {Deliveries.PropertyName(d.DestinationCode)}: {status}");
        }
        _active.text = lines.Count > 0 ? string.Join("\n", lines) : "nenhuma entrega a caminho";
    }

    private void Say(string text) { if (_message != null) _message.text = text; }

    private void Rebuild()
    {
        Ui.Clear(_list);
        _active = null!;
        if (!Deliveries.I.Active) { Ui.Text(_list, "Ative o 'DeliveryApp++' no app Mods.", 18); return; }
        _message = Ui.Text(_list, "", 15, minHeight: 22);
        _message.color = new Color(1f, 0.85f, 0.4f);

        Ui.Text(_list, "Em andamento", 18, bold: true);
        _active = Ui.Text(_list, "", 15, minHeight: 20);

        Ui.Text(_list, "Favoritos", 18, bold: true);
        var favs = Deliveries.I.Saved.Favorites;
        if (favs.Count == 0) Ui.Text(_list, "Toque em ★ num pedido do histórico para salvar aqui.", 14);
        foreach (var fav in favs.ToList())
        {
            var f = fav;
            var card = Ui.Card(_list);
            var desc = Ui.Text(card.transform, Deliveries.Describe(f), 15, minHeight: 40);
            desc.supportRichText = true;
            var row = Ui.Row(card.transform, 38);
            Ui.Button(row.transform, "Pedir agora", new Color(0.15f, 0.55f, 0.55f), () => Deliveries.I.Guard("pedir", () =>
                Say(Deliveries.I.Order(f) is { } why ? "Não pedido: " + why : "Pedido feito!")), width: 150, height: 32);
            Ui.Button(row.transform, f.RecurringHour < 0 ? "Recorrente: não" : $"Todo dia {f.RecurringHour:00}h",
                new Color(0.3f, 0.3f, 0.34f), () =>
                {
                    // ciclo: desligado -> 6h -> 12h -> 18h -> desligado
                    f.RecurringHour = f.RecurringHour switch { < 0 => 6, < 12 => 12, < 18 => 18, _ => -1 };
                    Deliveries.I.Persist();
                    Rebuild();
                }, width: 180, height: 32);
            Ui.Button(row.transform, "Remover", new Color(0.45f, 0.25f, 0.25f), () =>
            {
                favs.Remove(f);
                Deliveries.I.Persist();
                Rebuild();
            }, width: 110, height: 32);
        }

        Ui.Text(_list, "Histórico do jogo", 18, bold: true);
        if (!DeliveryManager.InstanceExists) return;
        var history = DeliveryManager.Instance.DisplayedDeliveryHistory;
        for (int i = history.Count - 1, shown = 0; i >= 0 && shown < 10; i--, shown++)
        {
            var r = history[i];
            if (r == null) continue;
            var asFav = Deliveries.FromReceipt(r.StoreName, r.DestinationCode, r.LoadingDockIndex, r.Items);
            var card = Ui.Card(_list);
            var desc = Ui.Text(card.transform, Deliveries.Describe(asFav), 15, minHeight: 40);
            desc.supportRichText = true;
            var row = Ui.Row(card.transform, 38);
            Ui.Button(row.transform, "Pedir de novo", new Color(0.15f, 0.55f, 0.55f), () => Deliveries.I.Guard("repetir", () =>
                Say(Deliveries.I.Order(asFav) is { } why ? "Não pedido: " + why : "Pedido feito!")), width: 160, height: 32);
            Ui.Button(row.transform, "★ Favoritar", new Color(0.55f, 0.45f, 0.1f), () =>
            {
                favs.Add(asFav);
                Deliveries.I.Persist();
                Rebuild();
                Say("Salvo nos favoritos.");
            }, width: 150, height: 32);
        }
        OnTick();
    }
}
