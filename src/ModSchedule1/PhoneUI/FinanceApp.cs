using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI;
using ModSchedule1.Features;
using UnityEngine;
using UnityEngine.UI;

namespace ModSchedule1.PhoneUI;

/// <summary>
/// App "Finanças" em abas: Banco, Lavagem, Fornecedores, Dealers, Melhorias e Histórico. Cabeçalho fixo com o
/// dinheiro em mãos e no banco; cada aba é uma página da mesma lista rolável (só a aberta é atualizada).
/// </summary>
public sealed class FinanceApp : PhoneApp
{
    public override string AppName => "ModSchedule1Finance";
    public override string IconLabel => "Finanças";
    public override string IconGlyph => "$";
    public override Color IconColor => new(0.15f, 0.5f, 0.25f);

    private static readonly string[] TabNames = { "Banco", "Lavagem", "Fornecedores", "Dealers", "Melhorias", "Histórico" };
    private const int BankTab = 0, LaundryTab = 1, SuppliersTab = 2, DealersTab = 3, UpgradesTab = 4, HistoryTab = 5;
    private static int _tab;   // lembra a última aba aberta

    private readonly List<(Image Bg, Text Label)> _tabs = new();
    private readonly List<GameObject> _pages = new();
    private readonly List<Text> _disabledNotes = new();
    private Text _summary = null!;

    // banco
    private Text _amountText = null!, _bankNote = null!;
    private float _amount;
    // lavagem
    private Transform _laundry = null!;
    private readonly List<(Business Business, Text Info, Text Ops, Text MaxLabel)> _laundryRows = new();
    private int _laundryBuiltFor = -1;
    // fornecedores
    private Transform _suppliers = null!;
    private Text _autoPayNote = null!;
    private readonly List<(Supplier Supplier, Text Info, Text PayLabel, Button Pay, Text Status)> _supplierRows = new();
    private int _suppliersBuiltFor = -1;
    // dealers
    private Transform _dealers = null!;
    private Text _collectAllLabel = null!;
    private readonly List<(Dealer Dealer, Text Info, Button Collect)> _dealerRows = new();
    private int _dealersBuiltFor = -1;
    // melhorias e histórico
    private readonly List<(Upgrades.Track Track, Text Info, Text Label, Button Button)> _upgradeRows = new();
    private Text _ledger = null!;

    private static Color Accent() => Color.Lerp(Ui.Native.Card, Ui.Native.Accent, 0.45f);

    protected override void BuildUI(GameObject container)
    {
        _tabs.Clear(); _pages.Clear(); _disabledNotes.Clear();
        _laundryRows.Clear(); _supplierRows.Clear(); _dealerRows.Clear(); _upgradeRows.Clear();
        _laundryBuiltFor = _suppliersBuiltFor = _dealersBuiltFor = -1;

        // cabeçalho: título à esquerda, saldos à direita
        var header = Ui.Child("Header", container.transform);
        Pin(header, 0, 58);
        header.AddComponent<Image>().color = Color.Lerp(Ui.Native.Background, Color.black, 0.3f);
        var title = Ui.Text(header.transform, "Finanças", 26, bold: true);
        title.color = Ui.Native.Accent;
        Fill(title, 20, 0);
        _summary = Ui.Text(header.transform, "", 17, anchor: TextAnchor.MiddleRight);
        _summary.supportRichText = true;
        Fill(_summary, 0, 20);

        // abas: largura igual, a selecionada em destaque
        var bar = Ui.Child("Tabs", container.transform);
        Pin(bar, 58, 50);
        bar.AddComponent<Image>().color = Color.Lerp(Ui.Native.Background, Color.black, 0.15f);
        var h = bar.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(10, 10, 7, 7);
        h.spacing = 6;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;
        for (int i = 0; i < TabNames.Length; i++)
        {
            int index = i;
            var (_, label, bg) = Ui.Button(bar.transform, TabNames[i], default, () => Select(index), width: 60, height: 36);
            var le = bg.GetComponent<LayoutElement>();
            le.preferredWidth = 0;
            le.flexibleWidth = 1;
            label.fontSize = 15;
            _tabs.Add((bg, label));
        }

        var list = Ui.ScrollList(container.transform, topMargin: 112);
        for (int i = 0; i < TabNames.Length; i++) _pages.Add(Page(list, i));

        BuildBank(_pages[BankTab].transform);
        BuildLaundry(_pages[LaundryTab].transform);
        BuildSuppliers(_pages[SuppliersTab].transform);
        BuildDealers(_pages[DealersTab].transform);
        BuildUpgrades(_pages[UpgradesTab].transform);
        _ledger = Ui.Text(Ui.Card(_pages[HistoryTab].transform).transform, "", 15, minHeight: 20);
        _ledger.supportRichText = true;
        Select(_tab);
    }

    // ---------- estrutura ----------

    /// <summary>Faixa de largura total presa no topo do app (cabeçalho, barra de abas).</summary>
    private static void Pin(GameObject go, float top, float height)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -top);
        rt.sizeDelta = new Vector2(0, height);
    }

    private static void Fill(Text t, float left, float right)
    {
        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, 0);
        rt.offsetMax = new Vector2(-right, 0);
    }

    private GameObject Page(Transform list, int index)
    {
        var page = Ui.Child("Page_" + TabNames[index], list);
        var v = page.AddComponent<VerticalLayoutGroup>();
        v.spacing = 10;
        v.padding = new RectOffset(0, 0, 4, 10);
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        var note = Ui.Text(page.transform, "Desativado em Configurações > Mods.", 16, minHeight: 40);
        note.color = Ui.Muted;
        _disabledNotes.Add(note);
        return page;
    }

    private static GameObject Column(Transform parent)
    {
        var go = Ui.Child("Column", parent);
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = 10;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return go;
    }

    private static Text Note(Transform parent, string text)
    {
        var t = Ui.Text(parent, text, 14, minHeight: 22);
        t.color = Ui.Muted;
        t.supportRichText = true;
        return t;
    }

    private void Select(int index)
    {
        _tab = Mathf.Clamp(index, 0, TabNames.Length - 1);
        for (int i = 0; i < _pages.Count; i++)
        {
            bool on = i == _tab;
            _pages[i].SetActive(on);
            _tabs[i].Bg.color = on ? Color.Lerp(Ui.Native.Card, Ui.Native.Accent, 0.7f) : Color.Lerp(Ui.Native.Card, Ui.Native.Text, 0.06f);
            _tabs[i].Label.color = on ? Color.white : Ui.Muted;
            _tabs[i].Label.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
        }
        OnTick();
    }

    // ---------- construção das abas ----------

    private void BuildBank(Transform page)
    {
        var card = Ui.Card(page);
        Note(card.transform, "Valor da operação");
        _amountText = Ui.Text(card.transform, "", 30, bold: true, anchor: TextAnchor.MiddleCenter, minHeight: 46);
        var chips = Ui.Row(card.transform, 42);
        chips.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        foreach (var step in new[] { 100, 500, 1000, 5000 })
        {
            int s = step;
            Ui.Button(chips.transform, "+" + Ui.Money(s), default, () => { _amount += s; OnTick(); }, width: 104, height: 36);
        }
        Ui.Button(chips.transform, "Zerar", default, () => { _amount = 0; OnTick(); }, width: 90, height: 36);
        var max = Ui.Row(card.transform, 42);
        max.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        Ui.Button(max.transform, "Máx. depósito", default, () => { _amount = Bank.I.MaxDeposit; OnTick(); }, width: 190, height: 36);
        Ui.Button(max.transform, "Máx. saque", default, () => { _amount = Bank.Online; OnTick(); }, width: 190, height: 36);
        var act = Ui.Row(card.transform, 54);
        act.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        Ui.Button(act.transform, "Depositar", Accent(), () => Bank.I.Guard("depositar", () => { if (Bank.I.Deposit(_amount)) _amount = 0; OnTick(); }), width: 240, height: 46);
        Ui.Button(act.transform, "Sacar", Accent(), () => Bank.I.Guard("sacar", () => { if (Bank.I.Withdraw(_amount)) _amount = 0; OnTick(); }), width: 240, height: 46);
        _bankNote = Note(card.transform, "");
        _bankNote.alignment = TextAnchor.MiddleCenter;
    }

    private void BuildLaundry(Transform page)
    {
        Note(page, "Cada negócio lava até a capacidade dele por vez. O dinheiro limpo cai no banco quando termina.");
        _laundry = Column(page).transform;
    }

    private void BuildSuppliers(Transform page)
    {
        Note(page, "Paga a dívida na hora (dead drops pedidos fiado). O fornecedor confirma no Messages.");
        _suppliers = Column(page).transform;
        _autoPayNote = Note(page, "");
    }

    private void BuildDealers(Transform page)
    {
        var top = Ui.Row(page, 48);
        (_, _collectAllLabel, _) = Ui.Button(top.transform, "", Accent(), () => Dealers.I.Guard("coletar de todos", () => { Dealers.I.CollectAll(); OnTick(); }), width: 320, height: 42);
        _dealers = Column(page).transform;
    }

    private void BuildUpgrades(Transform page)
    {
        Note(page, "Nada acelera sem comprar. Só o host compra (pelo banco); vale para todos os jogadores e fica salvo neste save. " +
                   "Nas mesas, multiplica a velocidade do Station Speed.");
        foreach (var track in Upgrades.All)
        {
            var t = track;
            var card = Ui.Card(page);
            var info = Ui.Text(card.transform, "", 18, minHeight: 50);
            info.supportRichText = true;
            var row = Ui.Row(card.transform, 50);
            var (button, label, _) = Ui.Button(row.transform, "", Accent(), () => Upgrades.I.Guard("comprar melhoria", () => { Upgrades.I.Buy(t); OnTick(); }), width: 380, height: 42);
            _upgradeRows.Add((t, info, label, button));
        }
    }

    // ---------- atualização ----------

    protected override void OnOpened() => OnTick();

    protected override void OnTick()
    {
        if (!MoneyManager.InstanceExists || _pages.Count == 0) return;
        _summary.text = $"Em mãos <b>{Ui.Money(Bank.Cash)}</b>      Banco <b>{Ui.Money(Bank.Online)}</b>";
        bool enabled = _tab switch
        {
            BankTab => Bank.I.Active,
            LaundryTab => Laundry.I.Active,
            DealersTab => Dealers.I.Active,
            UpgradesTab => Upgrades.I.Active,
            _ => true,
        };
        // a nota "desativado" fica no topo da página; o resto some quando a feature está desligada
        var page = _pages[_tab].transform;
        _disabledNotes[_tab].gameObject.SetActive(!enabled);
        for (int i = 1; i < page.childCount; i++) page.GetChild(i).gameObject.SetActive(enabled);
        if (!enabled) return;

        switch (_tab)
        {
            case BankTab: TickBank(); break;
            case LaundryTab: TickLaundry(); break;
            case SuppliersTab: TickSuppliers(); break;
            case DealersTab: TickDealers(); break;
            case UpgradesTab: TickUpgrades(); break;
            case HistoryTab: TickHistory(); break;
        }
    }

    private void TickBank()
    {
        _amountText.text = Bank.I.Busy ? "processando..." : Ui.Money(_amount);
        float room = Bank.I.DepositRoom;
        _bankNote.text = room < float.MaxValue ? $"Depósito liberado esta semana: <b>{Ui.Money(room)}</b>" : "Depósito sem limite semanal";
    }

    private void TickLaundry()
    {
        var owned = Business.OwnedBusinesses;
        if (owned.Count != _laundryBuiltFor)
        {
            Ui.Clear(_laundry);
            _laundryRows.Clear();
            _laundryBuiltFor = owned.Count;
            if (owned.Count == 0) Note(_laundry, "Você ainda não tem negócios para lavar dinheiro.");
            foreach (var business in owned)
            {
                var b = business;
                var card = Ui.Card(_laundry);
                var info = Ui.Text(card.transform, "", 18);
                info.supportRichText = true;
                var ops = Note(card.transform, "");
                var row = Ui.Row(card.transform, 46);
                var (_, maxLabel, _) = Ui.Button(row.transform, "", Accent(),
                    () => Laundry.I.Guard("lavar máximo", () => Laundry.I.Launder(b, Laundry.I.MaxFor(b, keepReserve: true))), width: 260, height: 40);
                Ui.Button(row.transform, "Lavar metade", default,
                    () => Laundry.I.Guard("lavar metade", () => Laundry.I.Launder(b, Laundry.I.MaxFor(b, keepReserve: true) / 2)), width: 180, height: 40);
                _laundryRows.Add((b, info, ops, maxLabel));
            }
        }
        foreach (var (b, info, ops, maxLabel) in _laundryRows)
        {
            if (b == null) continue;
            info.text = $"<b>{b.PropertyName}</b>      {Ui.Money(b.currentLaunderTotal)} de {Ui.Money(b.LaunderCapacity)} lavando";
            var lines = new List<string>();
            foreach (var op in b.LaunderingOperations)
            {
                int left = Math.Max(0, op.completionTime_Minutes - op.minutesSinceStarted);
                lines.Add($"{Ui.Money(op.amount)}  •  falta {left / 60}h{left % 60:00}");
            }
            ops.text = lines.Count > 0 ? string.Join("\n", lines) : "nenhuma operação em andamento";
            int max = Laundry.I.MaxFor(b, keepReserve: true);
            maxLabel.text = Laundry.I.OnCooldown(b) ? "enviando..." : max >= 10 ? $"Lavar {Ui.Money(max)}" : "Sem espaço ou dinheiro";
        }
    }

    private void TickSuppliers()
    {
        var known = SupplierDebt.Known();
        if (known.Count != _suppliersBuiltFor)
        {
            Ui.Clear(_suppliers);
            _supplierRows.Clear();
            _suppliersBuiltFor = known.Count;
            if (known.Count == 0) Note(_suppliers, "Nenhum fornecedor desbloqueado ainda.");
            foreach (var supplier in known)
            {
                var s = supplier;
                var card = Ui.Card(_suppliers);
                var info = Ui.Text(card.transform, "", 18);
                info.supportRichText = true;
                var row = Ui.Row(card.transform, 46);
                Text status = null!;
                var (pay, payLabel, _) = Ui.Button(row.transform, "", Accent(),
                    () => SupplierDebt.I.Guard("pagar dívida", () => { status.text = SupplierDebt.I.PayNow(s); OnTick(); }), width: 260, height: 40);
                status = Ui.Text(row.transform, "", 14);
                status.color = Ui.Muted;
                _supplierRows.Add((s, info, payLabel, pay, status));
            }
        }
        foreach (var (s, info, payLabel, pay, _) in _supplierRows)
        {
            if (s == null) continue;
            float debt = Mathf.Floor(s.Debt);
            info.text = debt >= 1f ? $"<b>{s.FullName}</b>      deve <b>{Ui.Money(debt)}</b>" : $"<b>{s.FullName}</b>      sem dívida";
            payLabel.text = debt >= 1f ? $"Pagar {Ui.Money(debt)}" : "Nada a pagar";
            pay.interactable = debt >= 1f;
        }
        _autoPayNote.text = SupplierDebt.I.Enabled.Value
            ? "Pagamento automático: <b>ligado</b>."
            : "Pagamento automático: desligado (Configurações > Mods > Fornecedores).";
    }

    private void TickDealers()
    {
        var recruited = new List<Dealer>(Dealers.Recruited());
        if (recruited.Count != _dealersBuiltFor)
        {
            Ui.Clear(_dealers);
            _dealerRows.Clear();
            _dealersBuiltFor = recruited.Count;
            if (recruited.Count == 0) Note(_dealers, "Nenhum dealer recrutado.");
            foreach (var dealer in recruited)
            {
                var d = dealer;
                var card = Ui.Card(_dealers);
                var row = Ui.Row(card.transform, 46);
                var info = Ui.Text(row.transform, "", 18);
                info.supportRichText = true;
                Ui.Button(row.transform, "Inventário", default, () => Dealers.I.Guard("inventário do dealer", () => OpenInventory(d)), width: 150, height: 40);
                var (collect, _, _) = Ui.Button(row.transform, "Coletar", Accent(), () => Dealers.I.Guard("coletar", () => { Dealers.I.Collect(d); OnTick(); }), width: 150, height: 40);
                _dealerRows.Add((d, info, collect));
            }
        }
        foreach (var (d, info, collect) in _dealerRows)
        {
            if (d == null) continue;
            bool pending = Dealers.I.Pending(d);
            info.text = $"<b>{d.FullName}</b>      {Ui.Money(d.Cash)}{(pending ? "  (coletando...)" : "")}      <size=14>{d.GetPackagedProductAmount()} un. de produto</size>";
            collect.interactable = d.Cash > 0 && !pending;
        }
        float held = Dealers.I.TotalHeld();
        _collectAllLabel.text = held > 0 ? $"Coletar de todos ({Ui.Money(held)})" : "Nada para coletar";
    }

    /// <summary>
    /// Mesmo menu do "I need to trade some items" em pessoa (StorageMenu no inventário do dealer), aberto à distância:
    /// o menu não checa distância e as mudanças nos slots do NPC são RPCs, então valem no multiplayer. Fecha o app e o
    /// celular antes, como no fluxo em pessoa (evita o ESC fechar o celular por baixo do menu).
    /// </summary>
    private void OpenInventory(Dealer d)
    {
        if (d == null || d.Inventory == null || !StorageMenu.InstanceExists || StorageMenu.Instance.IsOpen) return;
        // Como em pessoa (Dealer.TradeItems): o dealer para enquanto o menu está aberto. Sem isso ele pode entregar uma
        // venda tirando itens dos mesmos slots que você está mexendo, e o produto duplica. Se ele já está parado
        // conversando/trocando com alguém, recusa (o jogo também só deixa um jogador por vez).
        // "Ligada" e não só "ativa": se o dealer leva um tiro ou foge, a conversa fica pausada mas continua de alguém.
        // Quem saiu do jogo (Player destruído) não segura mais o dealer.
        var talk = d.Behaviour != null ? d.Behaviour.GenericDialogueBehaviour : null;
        if (talk != null && talk.Enabled)
        {
            var holder = talk.targetPlayer;
            if (holder != null && Player.Local != null && holder.Pointer != Player.Local.Pointer)
            {
                if (HintDisplay.InstanceExists) HintDisplay.Instance.ShowHint($"{d.FullName} está ocupado com outro jogador.", 4f);
                return;
            }
        }
        Close("inventário do dealer");
        if (talk != null)
        {
            if (Player.Local != null) talk.SendTargetPlayer(Player.Local.NetworkObject);   // como o DialogueHandler em pessoa
            talk.Enable_Server();
        }
        // Ordem do jogo em pessoa: o menu abre por cima e só então o celular sai de baixo (Remove, não Pop). Fechar o
        // celular antes reativava o personagem por um instante entre as duas telas.
        bool phoneOpen = GameplayMenu.InstanceExists && GameplayMenu.Instance.IsOpen;
        StorageMenu.Instance.Open(new IItemSlotOwner(d.Inventory.Pointer), $"Inventário de {d.FullName}",
            "Coloque <color=#4CB0FF>produto embalado</color> aqui: o dealer vende para os clientes dele.",
            DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() =>
            {
                if (d == null) return;
                if (talk != null) talk.Disable_Server();
                // sobras do inventário só no host: num cliente a baixa na sobra é local e o item duplicaria
                if (Net.IsHost) d.TryMoveOverflowItems();
            })));
        if (phoneOpen) GameplayMenu.Instance.State.RemoveFromDefaultParent();
        Dealers.I.Log.Info($"inventário de {d.FullName} aberto pelo app");
    }

    private void TickUpgrades()
    {
        foreach (var (track, info, label, button) in _upgradeRows)
        {
            int level = Upgrades.I.Level(track), price = Upgrades.I.NextPrice(track);
            info.text = $"<b>{track.Name}</b>   nível {level} de {track.Max}\n<size=14>{track.What} <b>{track.Mult[level]:0.##}x</b> mais rápido</size>";
            label.text = price < 0 ? "Nível máximo" : !Upgrades.CanBuy ? "Só o host compra"
                : $"Comprar nível {level + 1} ({track.Mult[level + 1]:0.##}x) por {Ui.Money(price)}";
            button.interactable = price >= 0 && Upgrades.CanBuy && Bank.Online >= price;
        }
    }

    private void TickHistory()
    {
        var ledger = MoneyManager.Instance.ledger;
        var lines = new List<string>();
        for (int i = ledger.Count - 1; i >= 0 && lines.Count < 15; i--)
        {
            var t = ledger[i];
            string sign = t.total_Amount >= 0 ? "<color=#5BE07A>+" : "<color=#FF7A7A>-";
            lines.Add($"{sign}{Ui.Money(Math.Abs(t.total_Amount))}</color>   {t.transaction_Name}");
        }
        _ledger.text = lines.Count > 0 ? "<b>Últimas transações do banco (desta sessão)</b>\n" + string.Join("\n", lines) : "Nenhuma transação ainda.";
    }
}
