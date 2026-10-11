using System;
using System.Collections.Generic;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Shop;
using Il2CppTMPro;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace ModSchedule1.Features;

/// <summary>
/// Assalto e furto nas lojas presenciais. Com a loja aberta aparecem dois botões abaixo do "comprar":
/// "Assaltar o caixa" (dinheiro do caixa, 1x por dia por loja; armado sempre funciona) e "Furtar o carrinho"
/// (leva o carrinho sem pagar, com chance de ser visto). Reações são as nativas do jogo: o caixa chama a polícia
/// e quem viu reage como num furto de carteira descoberto (polícia, briga ou fuga).
/// </summary>
public sealed class Robbery : Feature<Robbery>
{
    public override string Id => "Robbery";
    public override string Name => "Robbery";
    public override string Description => "Assaltar o caixa ou furtar o carrinho nas lojas (botões na tela da loja).";

    private Pref<int> _min = null!, _max = null!, _unarmed = null!, _caught = null!, _stealMax = null!, _witnessRadius = null!;
    private Pref<bool> _steal = null!, _police = null!;
    private readonly Dictionary<string, int> _lastDay = new();   // "loja|ação" -> dia do jogo
    private bool _dumped;

    protected override void OnInit()
    {
        _min = Setting("MinPayout", 350, "Assalto: mínimo ($)", min: 0, max: 5000);
        _max = Setting("MaxPayout", 750, "Assalto: máximo ($)", min: 0, max: 10000);
        _unarmed = Setting("UnarmedChance", 35, "Assalto sem arma: chance de dar certo (%)", min: 0, max: 100);
        _police = Setting("CashierCallsPolice", true, "Caixa chama a polícia");
        _steal = Setting("Shoplifting", true, "Permitir furtar o carrinho");
        _caught = Setting("CaughtChance", 40, "Furto: chance de ser visto (%)", min: 0, max: 100);
        _stealMax = Setting("StealMaxValue", 400, "Furto: valor máximo do carrinho ($)", min: 10, max: 10000);
        _witnessRadius = Setting("WitnessRadius", 15, "Distância das testemunhas (m)", min: 0, max: 60);
        // OnUpdate não roda desligado: some com os botões já criados nas lojas
        Enabled.Changed += () => Guard("mostrar botões", () =>
        {
            foreach (var shop in ShopInterface.AllShops)
            {
                var parent = shop != null && shop.Cart != null && shop.Cart.buyButton != null ? shop.Cart.buyButton.transform.parent : null;
                if (parent == null) continue;
                foreach (var name in new[] { "MS1_Rob", "MS1_Steal" }) parent.Find(name)?.gameObject.SetActive(Active);
            }
        });
    }

    private static int Today => TimeManager.InstanceExists ? TimeManager.Instance.ElapsedDays : 0;
    private bool DoneToday(ShopInterface shop, string what) => _lastDay.TryGetValue(shop.ShopCode + "|" + what, out var d) && d == Today;

    // A loja abre por eventos do cenário (às vezes chamada direto no C++): olhar IsOpen é mais garantido que patch.
    public override void OnUpdate()
    {
        foreach (var shop in ShopInterface.AllShops)
        {
            if (shop == null || !shop.IsOpen || shop.Cart == null || shop.Cart.buyButton == null) continue;
            var (rob, steal) = Buttons(shop);
            rob.gameObject.SetActive(true);
            Label(rob, DoneToday(shop, "rob") ? "Caixa já assaltado hoje" : "Assaltar o caixa");
            rob.interactable = !DoneToday(shop, "rob");
            steal.gameObject.SetActive(_steal.Value);
            if (_steal.Value)
            {
                float value = shop.Cart.GetPriceSum();
                bool can = !DoneToday(shop, "steal") && value > 0 && value <= _stealMax.Value;
                Label(steal, DoneToday(shop, "steal") ? "Já furtou aqui hoje" : value > _stealMax.Value ? $"Furto até {Ui.Money(_stealMax.Value)}" : "Furtar o carrinho");
                steal.interactable = can;
            }
        }
    }

    private (Button Rob, Button Steal) Buttons(ShopInterface shop)
    {
        var buy = shop.Cart.buyButton;
        var parent = buy.transform.parent;
        var rob = parent.Find("MS1_Rob")?.GetComponent<Button>();
        var steal = parent.Find("MS1_Steal")?.GetComponent<Button>();
        if (rob != null && steal != null) return (rob, steal);

        NativeUi.DumpOnce(ref _dumped, parent, $"carrinho da loja {shop.ShopName} (botão comprar)", 2);
        rob = Clone(buy, "MS1_Rob", new Color(0.85f, 0.25f, 0.25f), () => Guard("assalto", () => Rob(shop)));
        steal = Clone(buy, "MS1_Steal", new Color(0.85f, 0.6f, 0.2f), () => Guard("furto", () => Steal(shop)));
        PlaceBelow(buy.GetComponent<RectTransform>(), rob.GetComponent<RectTransform>(), 0f, 0.5f);
        PlaceBelow(buy.GetComponent<RectTransform>(), steal.GetComponent<RectTransform>(), 0.5f, 1f);
        Log.Debug($"botões de assalto/furto criados em {shop.ShopName}");
        return (rob, steal);
    }

    private static Button Clone(Button buy, string name, Color tint, Action onClick)
    {
        var b = NativeUi.CloneButton(buy, buy.transform.parent, name, onClick);
        foreach (var t in b.GetComponentsInChildren<UITrigger>(true)) Object.DestroyImmediate(t);   // o original compra pelo UITrigger
        if (b.targetGraphic != null) b.targetGraphic.color = Color.Lerp(b.targetGraphic.color, tint, 0.7f);
        return b;
    }

    /// <summary>Logo abaixo do botão de comprar, ocupando a fração [from, to] da largura dele.</summary>
    private static void PlaceBelow(RectTransform target, RectTransform ours, float from, float to)
    {
        var parent = target.parent.GetComponent<RectTransform>();
        var r = target.rect;
        Vector2 bl = parent.InverseTransformPoint(target.TransformPoint(new Vector3(r.xMin, r.yMin)));
        Vector2 br = parent.InverseTransformPoint(target.TransformPoint(new Vector3(r.xMax, r.yMin)));
        float w = br.x - bl.x;
        ours.anchorMin = ours.anchorMax = new Vector2(0.5f, 0.5f);
        ours.pivot = new Vector2(0f, 1f);
        ours.anchoredPosition = new Vector2(bl.x + w * from + 2, bl.y - 6) - parent.rect.center;
        ours.sizeDelta = new Vector2(w * (to - from) - 4, Mathf.Max(30f, r.height * 0.8f));
    }

    private static void Label(Button b, string text)
    {
        var tmp = b.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            if (tmp.text == text) return;
            tmp.text = text;
            tmp.enableWordWrapping = false;
            if (!tmp.enableAutoSizing) { tmp.fontSizeMax = tmp.fontSize; tmp.fontSizeMin = 10; tmp.enableAutoSizing = true; }
        }
        else NativeUi.SetText(b, text);
    }

    private void Rob(ShopInterface shop)
    {
        if (DoneToday(shop, "rob")) return;
        var player = Player.Local;
        var cashier = Cashier(player);
        if (cashier == null) { Notify("Não tem ninguém no caixa para assaltar."); return; }
        _lastDay[shop.ShopCode + "|rob"] = Today;
        shop.Close();

        bool armed = HasWeapon();
        if (!armed && Random.Range(0, 100) >= _unarmed.Value)
        {
            Notify($"{cashier.FirstName} não se intimidou: sem arma não deu. Fuja!");
            Log.Info($"assalto sem arma falhou em {shop.ShopName}");
            React(cashier, player);
            return;
        }
        int amount = Random.Range(Mathf.Min(_min.Value, _max.Value), Mathf.Max(_min.Value, _max.Value) + 1);
        MoneyManager.Instance.ChangeCashBalance(amount, true, true);
        Notify($"Você levou {Ui.Money(amount)} do caixa de {shop.ShopName}{(armed ? "" : " (sem arma, na sorte)")}. Fuja!");
        Log.Info($"assalto em {shop.ShopName}: {Ui.Money(amount)} (armado={armed})");
        React(cashier, player);
    }

    private void Steal(ShopInterface shop)
    {
        var cart = shop.Cart;
        float value = cart.GetPriceSum();
        if (DoneToday(shop, "steal") || value <= 0 || value > _stealMax.Value) return;
        if (!shop.WillCartFit()) { Notify("O carrinho não cabe no inventário."); return; }
        var player = Player.Local;
        _lastDay[shop.ShopCode + "|steal"] = Today;

        foreach (var listing in cart.cartDictionary.Keys)
            if (!listing.IsUnlimitedStock) listing.RemoveStock(cart.cartDictionary[listing]);
        shop.HandoverItems();
        cart.ClearCart();
        shop.Close();

        bool seen = Random.Range(0, 100) < _caught.Value;
        Log.Info($"furto em {shop.ShopName}: {Ui.Money(value)} em itens (visto={seen})");
        if (!seen) { Notify($"Furto limpo: {Ui.Money(value)} em itens."); return; }
        Notify($"Você foi visto furtando {Ui.Money(value)}! Fuja!");
        var cashier = Cashier(player);
        if (cashier != null) React(cashier, player);
    }

    /// <summary>NPC acordado mais perto do jogador (até 6 m): quem atende a loja.</summary>
    private static NPC? Cashier(Player player)
    {
        NPC? best = null;
        float bestDist = 6f;
        foreach (var npc in NPCManager.NPCRegistry)
        {
            if (npc == null || !npc.IsConscious) continue;
            float d = Vector3.Distance(npc.transform.position, player.transform.position);
            if (d < bestDist) { bestDist = d; best = npc; }
        }
        return best;
    }

    /// <summary>
    /// Caixa chama a polícia (como o jogo faz com crimes vistos); quem mais estiver perto e enxergar o jogador reage
    /// como a um furto de carteira descoberto (policiais perseguem, civis chamam a polícia, brigam ou fogem).
    /// </summary>
    private void React(NPC cashier, Player player)
    {
        if (_police.Value)
        {
            cashier.Actions.SetCallPoliceBehaviourCrime(new Theft());
            cashier.Actions.CallPolice_Networked(player.NetworkObject);
        }
        int witnesses = 0;
        Vector3 eye = player.transform.position + Vector3.up * 1.5f;
        foreach (var npc in NPCManager.NPCRegistry)
        {
            if (npc == null || npc.Pointer == cashier.Pointer || !npc.IsConscious) continue;
            if (Vector3.Distance(npc.transform.position, player.transform.position) > _witnessRadius.Value) continue;
            var cone = npc.Awareness != null ? npc.Awareness.VisionCone : null;
            if (cone == null || !cone.IsPointWithinSight(eye, false, null)) continue;
            try { npc.Responses.PlayerFailedPickpocket(player); witnesses++; }
            catch (Exception e) { Log.Debug($"reação de {npc.FirstName}: {e.Message}"); }
        }
        Log.Info($"polícia chamada={_police.Value}, testemunhas={witnesses}");
    }

    private static bool HasWeapon()
    {
        if (!PlayerInventory.InstanceExists) return false;
        foreach (var slot in PlayerInventory.Instance.hotbarSlots)
        {
            var eq = slot?.ItemInstance?.Definition?.Equippable;
            if (eq != null && (eq.GetComponent<Equippable_RangedWeapon>() != null || eq.GetComponent<Equippable_MeleeWeapon>() != null)) return true;
        }
        return false;
    }

    private void Notify(string text)
    {
        if (HintDisplay.InstanceExists) HintDisplay.Instance.ShowHint(text, 6f);
        Log.Debug(text);
    }
}
