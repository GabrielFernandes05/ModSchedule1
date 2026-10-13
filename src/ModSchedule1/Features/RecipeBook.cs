using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.State;
using Il2CppScheduleOne.StationFramework;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Stations;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CanvasScaler = UnityEngine.UI.CanvasScaler;
using GameState = Il2CppScheduleOne.State.State;

namespace ModSchedule1.Features;

/// <summary>
/// Livro de receitas (F8): tudo o que dá para produzir, em abas. Os dados vêm do próprio jogo na hora de abrir
/// (produtos criados, ingredientes, efeitos, química, forno, caldeirão, cultivo), com os tempos já com o mod e as
/// melhorias. Abre como os menus do jogo: um State "UI sem inventário" na pilha InGame solta o mouse e trava
/// câmera, movimento e hotbar; sair da pilha (ESC, F8, morte, cutscene) devolve tudo como estava.
/// </summary>
public sealed partial class Recipes
{
    private const string UiName = "MS1_RecipeBook";
    private static readonly string[] TabNames = { "Produtos", "Misturas", "Ingredientes", "Efeitos", "Química", "Estações", "Cultivo" };
    private static int _tab;   // lembra a última aba aberta
    private static int _mixBase;   // droga base escolhida na aba Misturas
    private const int MixPage = 50;
    private const int MixBeam = 5000;

    /// <summary>Misturas de uma base, calculadas uma vez por cena (o mapa de mistura depende da seed do save).</summary>
    private sealed record MixResult(List<MixSearch.Node> Mixes, Effect[] Effects, Dictionary<string, int> Index, string[] Mixers);
    private readonly Dictionary<string, MixResult> _mixes = new();
    private Pref<int> _mixSteps = null!;

    private HotKey _key = null!;
    private GameObject? _book;
    private Canvas? _canvas;
    private Transform _list = null!;
    private ScrollRect _scroll = null!;
    private readonly List<(Image Bg, Text Label)> _tabs = new();
    private GameState? _state;

    private bool IsOpen => _book != null && _book.activeSelf;

    protected override void OnInit()
    {
        _key = new HotKey(Setting("Key", "F8", "Tecla do livro de receitas (nome do KeyCode da Unity)"), KeyCode.F8, Log);
        _mixSteps = Setting("MixSteps", 8, "Misturas: até N ingredientes", "Mais = acha misturas mais longas, mas demora mais para abrir a aba", min: 1, max: 16);
        _mixSteps.Changed += _mixes.Clear;
        Enabled.Changed += () => { if (!Enabled.Value) Close(); };
    }

    // o canvas e o State morrem com a cena
    public override void OnSceneLoaded(string scene) { _book = null; _state = null; _mixes.Clear(); }

    public override void OnUpdate()
    {
        if (!_key.Pressed) return;
        if (IsOpen) { Close(); return; }
        if (SceneManager.GetActiveScene().name != "Main" || !PlayerCamera.InstanceExists || !ProductManager.InstanceExists) return;
        // mesmas condições que o jogo usa para abrir o celular: nada de chat, pausa ou outro menu aberto
        if (GameInput.IsTyping || (PauseMenu.InstanceExists && PauseMenu.Instance.IsPaused) || PlayerCamera.Instance.ActiveUIElementCount > 0) return;
        var onFoot = Player.LocalPlayerState;
        if (onFoot == null || !onFoot.IsAcceptingInput) return;
        Open();
    }

    private void Open()
    {
        if (_book == null) Build();
        if (_state == null)
        {
            // o jogo cria States assim nas mesas (BrickPress.Use); ESC (prioridade 3, acima da pausa) tira da pilha
            _state = new GameState(UiName, new IStateMachine(SceneState.Current.InGame.Pointer), StateProperties.EPreset.UINoInventory);
            _state.EnableAutoExitListener(3);
            _state.add_OnRemovedFromStack(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(OnClosed)));
            // algo empilhado por cima (pausa, dormir no multiplayer...) esconde o livro até voltar a ser o estado ativo
            _state.add_OnStateActivate(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => SetVisible(true))));
            _state.add_OnStateDeactivate(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => SetVisible(false))));
        }
        _state.PushToDefaultParent();
        PlayerCamera.Instance.AddActiveUIElement(UiName);   // bloqueia interagir, soco, lanterna e celular
        _book!.SetActive(true);
        Show(_tab);
    }

    private void Close()
    {
        if (!IsOpen) return;
        _state?.RemoveFromDefaultParent();   // dispara OnClosed
        if (IsOpen) OnClosed();              // o State já tinha saído da pilha
    }

    // Chamado pelo jogo quando o State sai da pilha por qualquer motivo. Sem Guard: precisa rodar com a feature desligada.
    private void OnClosed()
    {
        try
        {
            if (PlayerCamera.InstanceExists) PlayerCamera.Instance.RemoveActiveUIElement(UiName);
            if (_book != null) _book.SetActive(false);
        }
        catch (Exception e) { Log.Error($"fechar livro de receitas: {e}"); }
    }

    private void SetVisible(bool on)
    {
        try { if (_canvas != null) _canvas.enabled = on; }
        catch (Exception e) { Log.Error($"livro de receitas: {e}"); }
    }

    // ---------- estrutura ----------

    private void Build()
    {
        _tabs.Clear();
        _book = new GameObject(UiName) { layer = 5 };
        _canvas = _book.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        var scaler = _book.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;   // pela altura: o painel de 900 cabe até em monitor 32:9
        _book.AddComponent<GraphicRaycaster>();
        Ui.Stretch("Dim", _book.transform).AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        var panel = Ui.Child("Panel", _book.transform);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(1300, 900);
        Ui.Native.Style(panel.AddComponent<Image>(), Ui.Native.Background);

        var header = Ui.Child("Header", panel.transform);
        Ui.Pin(header, 0, 64);
        var title = Ui.Text(header.transform, "Livro de receitas", 30, bold: true);
        title.color = Ui.Native.Accent;
        Ui.Fill(title, 24, 300);
        var hint = Ui.Text(header.transform, $"{_key.Name} ou ESC fecha", 16, anchor: TextAnchor.MiddleRight);
        hint.color = Ui.Muted;
        Ui.Fill(hint, 300, 76);
        var (_, _, closeBg) = Ui.Button(header.transform, "X", default, Close, 44, 44);
        var crt = closeBg.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = new Vector2(-16f, 0f);
        crt.sizeDelta = new Vector2(44f, 44f);

        // abas de largura igual, a selecionada em destaque (como o app Finanças)
        var bar = Ui.Child("Tabs", panel.transform);
        Ui.Pin(bar, 64, 56);
        bar.AddComponent<Image>().color = Color.Lerp(Ui.Native.Background, Color.black, 0.15f);
        var h = bar.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(14, 14, 8, 8);
        h.spacing = 8;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;
        for (int i = 0; i < TabNames.Length; i++)
        {
            int index = i;
            var (_, label, bg) = Ui.Button(bar.transform, TabNames[i], default, () => Show(index), width: 60, height: 40);
            var le = bg.GetComponent<LayoutElement>();
            le.preferredWidth = 0;
            le.flexibleWidth = 1;
            label.fontSize = 18;
            _tabs.Add((bg, label));
        }

        _list = Ui.ScrollList(panel.transform, topMargin: 124);
        _scroll = _list.parent.parent.GetComponent<ScrollRect>();   // Content -> Viewport -> Scroll
        _scroll.scrollSensitivity = 40;
        _book.SetActive(false);
    }

    private void Show(int index)
    {
        _tab = Mathf.Clamp(index, 0, TabNames.Length - 1);
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool on = i == _tab;
            _tabs[i].Bg.color = on ? Color.Lerp(Ui.Native.Card, Ui.Native.Accent, 0.7f) : Color.Lerp(Ui.Native.Card, Ui.Native.Text, 0.06f);
            _tabs[i].Label.color = on ? Color.white : Ui.Muted;
            _tabs[i].Label.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
        }
        Ui.Clear(_list);
        // uma aba quebrada (o jogo mudou?) não derruba a feature nem as outras abas
        try
        {
            switch (_tab)
            {
                case 0: BuildProducts(); break;
                case 1: BuildMixes(); break;
                case 2: BuildMixers(); break;
                case 3: BuildEffects(); break;
                case 4: BuildChemistry(); break;
                case 5: BuildStations(); break;
                default: BuildGrowing(); break;
            }
        }
        catch (Exception e)
        {
            Log.Error($"aba {TabNames[_tab]}: {e}");
            Note("Não deu para montar esta aba (veja o log).");
        }
        _scroll.verticalNormalizedPosition = 1f;
    }

    private void Note(string text)
    {
        var t = Ui.Text(_list, text, 16, minHeight: 22);
        t.color = Ui.Muted;
    }

    /// <summary>Card com ícone, título e um destaque à direita (preço, tempo...); devolve onde pôr as linhas.</summary>
    private Transform Entry(Sprite? icon, string title, string right = "")
    {
        var card = Ui.Card(_list).transform;
        var row = Ui.Row(card, 44);
        if (icon != null)
        {
            var img = Ui.Child("Icon", row.transform).AddComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = 44;
        }
        Ui.Text(row.transform, title, 20, bold: true);
        if (right != "")
        {
            var r = Ui.Text(row.transform, right, 18, anchor: TextAnchor.MiddleRight);
            r.color = Ui.Native.Accent;
            r.GetComponent<LayoutElement>().flexibleWidth = 0;
        }
        return card;
    }

    private static void Line(Transform card, string text) => Ui.Text(card, text, 17, minHeight: 22);

    // ---------- abas ----------

    private void BuildProducts()
    {
        var pm = ProductManager.Instance;
        var known = new HashSet<string>();
        var discovered = ProductManager.DiscoveredProducts;
        for (int i = 0; i < discovered.Count; i++) if (discovered[i] != null) known.Add(discovered[i].ID);
        var all = new List<ProductDefinition>();
        for (int i = 0; i < pm.AllProducts.Count; i++) if (pm.AllProducts[i] != null) all.Add(pm.AllProducts[i]);

        var chains = AllChains();
        Note($"{known.Count} de {all.Count} produtos descobertos. Receita = caminho mais curto de mistura desde a droga base.");
        foreach (var p in all.OrderBy(p => !known.Contains(p.ID)).ThenBy(p => (int)p.DrugType).ThenBy(p => p.Name))
        {
            bool found = known.Contains(p.ID);
            var card = Entry(p.Icon, found ? p.Name : $"<color=#8a8a8a>{p.Name}  (não descoberto)</color>",
                $"{DrugName(p.DrugType)} · {Ui.Money(p.MarketValue)}");
            Line(card, "Efeitos: " + Effects(p.Properties));
            Line(card, !chains.TryGetValue(p.ID, out var chain) ? "Receita desconhecida."
                : chain.Count == 0 ? "Produto base: vem do cultivo ou das estações (veja as outras abas)."
                : string.Join("\n", chain.Select((s, i) => $"{i + 1}. {s.From} + <b>{s.Mixer}</b> -> {s.To}")));
            if (found) Line(card, $"Seu preço de venda: {Ui.Money(pm.GetPrice(p))}");
        }
    }

    private void BuildMixes()
    {
        var pm = ProductManager.Instance;
        var created = new Dictionary<string, ProductDefinition>();
        var made = pm.createdProducts;
        for (int i = 0; made != null && i < made.Count; i++) if (made[i] != null) created[made[i].ID] = made[i];
        var bases = new List<ProductDefinition>();
        for (int i = 0; i < pm.AllProducts.Count; i++)
            if (pm.AllProducts[i] != null && !created.ContainsKey(pm.AllProducts[i].ID)) bases.Add(pm.AllProducts[i]);
        bases = bases.OrderBy(p => (int)p.DrugType).ThenBy(p => p.Name).ToList();
        if (bases.Count == 0) { Note("Produtos ainda não carregaram."); return; }
        _mixBase = Mathf.Clamp(_mixBase, 0, bases.Count - 1);

        var row = Ui.Row(_list, 44);
        for (int i = 0; i < bases.Count; i++)
        {
            int index = i;
            var (_, label, bg) = Ui.Button(row.transform, bases[i].Name, default, () => { _mixBase = index; Show(_tab); }, 60, 40);
            var le = bg.GetComponent<LayoutElement>();
            le.preferredWidth = 0;
            le.flexibleWidth = 1;
            if (i == _mixBase) { bg.color = Color.Lerp(Ui.Native.Card, Ui.Native.Accent, 0.7f); label.color = Color.white; }
        }

        var b = bases[_mixBase];
        if (!_mixes.TryGetValue(b.ID, out var r)) _mixes[b.ID] = r = SearchMixes(b);
        // o que você já criou aparece com o nome que deu (mesma droga e mesmos efeitos = mesmo produto no jogo)
        var names = new Dictionary<ulong, string>();
        foreach (var p in created.Values)
        {
            if (p.DrugType != b.DrugType || p.Properties == null) continue;
            var ids = new List<int>();
            for (int i = 0; i < p.Properties.Count; i++)
                if (p.Properties[i] != null && r.Index.TryGetValue(p.Properties[i].ID, out var idx)) ids.Add(idx);
            names.TryAdd(MixSearch.Mask(ids), p.Name);
        }
        Note($"{r.Mixes.Count:N0} misturas diferentes de {b.Name} com até {_mixSteps.Value} ingredientes, da que mais lucra para a que menos. "
             + $"Lucro = valor de mercado - ingredientes (sem o custo de {b.Name}). Receita = a mais barata para chegar nesses efeitos.");
        MixCards(r, b, names, 0);
    }

    /// <summary>Uma página de cards e, se sobrar, um botão que troca a si mesmo pela próxima página.</summary>
    private void MixCards(MixResult r, ProductDefinition b, Dictionary<ulong, string> names, int from)
    {
        int to = Math.Min(r.Mixes.Count, from + MixPage);
        for (int i = from; i < to; i++)
        {
            var n = r.Mixes[i];
            var card = Entry(b.Icon, string.Join(", ", n.Effects.Select(e => EffectLabel(r.Effects[e]))), $"Lucro {Ui.Money(n.Profit)}");
            Line(card, $"{i + 1}. {b.Name} + " + string.Join(" + ", n.Mixers().Select(m => $"<b>{r.Mixers[m]}</b>")));
            float gain = n.Profit - b.MarketValue;
            Line(card, $"Valor {Ui.Money(n.Value)} · ingredientes {Ui.Money(n.Cost)} · {(gain >= 0 ? "+" : "-")}{Ui.Money(Math.Abs(gain))} sobre vender {b.Name} puro"
                       + (names.TryGetValue(MixSearch.Mask(n.Effects), out var name) ? $" · já criado: <b>{name}</b>" : ""));
        }
        if (to >= r.Mixes.Count) return;
        GameObject? more = null;
        more = Ui.Button(_list, $"Mostrar mais ({r.Mixes.Count - to:N0} restantes)", default, () =>
        {
            more!.SetActive(false);
            UnityEngine.Object.Destroy(more);
            MixCards(r, b, names, to);
        }, 400, 44).bg.gameObject;
    }

    /// <summary>Copia o mapa de mistura da droga para arrays e roda <see cref="MixSearch"/>.</summary>
    private MixResult SearchMixes(ProductDefinition b)
    {
        var pm = ProductManager.Instance;
        var map = pm.GetMixerMap(b.DrugType);
        var effects = new List<Effect>();
        var index = new Dictionary<string, int>();
        int Idx(Effect e)
        {
            if (!index.TryGetValue(e.ID, out var i)) { index[e.ID] = i = effects.Count; effects.Add(e); }
            return i;
        }
        for (int i = 0; i < map.Effects.Count; i++) if (map.Effects[i]?.Property != null) Idx(map.Effects[i].Property);
        var mixers = new List<PropertyItemDefinition>();
        var valid = pm.ValidMixIngredients;
        for (int i = 0; i < valid.Count; i++)
            if (valid[i]?.Properties != null && valid[i].Properties.Count > 0 && valid[i].Properties[0] != null) mixers.Add(valid[i]);
        var adds = mixers.Select(m => Idx(m.Properties[0])).ToArray();
        var start = new List<int>();
        for (int i = 0; b.Properties != null && i < b.Properties.Count; i++) if (b.Properties[i] != null) start.Add(Idx(b.Properties[i]));
        if (effects.Count > 63) throw new InvalidOperationException($"{effects.Count} efeitos: a busca só cabe até 63");

        // mesma conta do EffectMixCalculator: o ingrediente empurra cada efeito no mapa (girado pela seed se o save
        // usa mapas aleatórios); onde o efeito cair em outro, ele vira esse outro
        var rotation = GameManager.InstanceExists && GameManager.Instance.Settings.UseRandomizedMixMaps
            ? Quaternion.Euler(0f, 0f, GameManager.Seed % 360) : Quaternion.identity;
        var react = new int[effects.Count, mixers.Count];
        for (int e = 0; e < effects.Count; e++) for (int m = 0; m < mixers.Count; m++) react[e, m] = -1;
        for (int m = 0; m < mixers.Count; m++)
        {
            var fx = mixers[m].Properties[0];
            Vector2 push = rotation * (Vector3)(fx.MixDirection * fx.MixMagnitude);
            for (int i = 0; i < map.Effects.Count; i++)
            {
                var at = map.Effects[i];
                if (at?.Property == null) continue;
                var hit = map.GetEffectAtPoint(at.Position + push)?.Property;
                if (hit != null) react[index[at.Property.ID], m] = index[hit.ID];
            }
        }

        // valor de um produto misturado: ProductManager.CalculateProductValue sobre o preço base da droga padrão
        float basePrice = b.DrugType switch
        {
            EDrugType.Marijuana => pm.DefaultWeed.BasePrice,
            EDrugType.Methamphetamine => pm.DefaultMeth.BasePrice,
            EDrugType.Cocaine => pm.DefaultCocaine.BasePrice,
            EDrugType.Shrooms => pm.DefaultShroom.BasePrice,
            _ => b.BasePrice,
        };
        var change = effects.Select(e => (float)e.ValueChange + basePrice * e.AddBaseValueMultiple).ToArray();
        var mult = effects.Select(e => e.ValueMultiplier).ToArray();
        int Value(int[] fx)
        {
            float v = basePrice, k = 1f;
            foreach (var e in fx) { v += change[e]; k *= mult[e]; }
            return Mathf.RoundToInt(v * k);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var mixes = MixSearch.Search(start.ToArray(), react, adds, mixers.Select(m => m.BasePurchasePrice).ToArray(), Value, _mixSteps.Value, MixBeam);
        Log.Info($"misturas de {b.Name}: {mixes.Count} em {watch.ElapsedMilliseconds} ms");
        return new MixResult(mixes, effects.ToArray(), index, mixers.Select(m => m.Name).ToArray());
    }

    private void BuildMixers()
    {
        var mixers = ProductManager.Instance.ValidMixIngredients;
        foreach (var (station, def) in Stations<MixingStation>())
            Note($"{def.Name}: até {station.MaxMixQuantity * BetterStacks.I.MixCapacity} por vez, {Dur(Scaled(station.MixTimePerItem, StationSpeed.I.MixingSpeed))} por unidade.");
        Note("Cada ingrediente adiciona um efeito e pode transformar os efeitos que o produto já tem (até 8).");
        for (int i = 0; i < mixers.Count; i++)
        {
            var m = mixers[i];
            if (m == null) continue;
            var card = Entry(m.Icon, m.Name, Ui.Money(m.BasePurchasePrice));
            var effect = m.Properties != null && m.Properties.Count > 0 ? m.Properties[0] : null;
            if (effect != null) Line(card, $"Adiciona: {EffectLabel(effect)} (nível {effect.Tier})");
            if (m.RequiresLevelToPurchase) Line(card, $"Libera no rank {FullRank.GetString(m.RequiredRank)}");
        }
    }

    private void BuildEffects()
    {
        if (!PropertyUtility.InstanceExists) { Note("Lista de efeitos ainda não carregou."); return; }
        // quem dá cada efeito direto (o resto só aparece transformando outros efeitos na mistura)
        var givenBy = new Dictionary<string, string>();
        var mixers = ProductManager.Instance.ValidMixIngredients;
        for (int i = 0; i < mixers.Count; i++)
            if (mixers[i]?.Properties != null && mixers[i].Properties.Count > 0 && mixers[i].Properties[0] != null)
                givenBy[mixers[i].Properties[0].ID] = mixers[i].Name;

        var all = new List<Effect>();
        var props = PropertyUtility.Instance.AllProperties;
        for (int i = 0; i < props.Count; i++) if (props[i] != null) all.Add(props[i]);
        Note("Valor: quanto o efeito soma ou multiplica no preço do produto.");
        foreach (var e in all.OrderBy(e => e.Tier).ThenBy(e => e.Name))
        {
            var card = Entry(null, EffectLabel(e), $"Nível {e.Tier}");
            var value = new List<string>();
            if (Math.Abs(e.AddBaseValueMultiple) > 0.001f) value.Add($"{e.AddBaseValueMultiple * 100:+0;-0}% do preço base");
            if (Math.Abs(e.ValueMultiplier - 1f) > 0.001f) value.Add($"x{e.ValueMultiplier:0.##}");
            if (e.ValueChange != 0) value.Add($"{(e.ValueChange > 0 ? "+" : "-")}{Ui.Money(Math.Abs(e.ValueChange))}");
            Line(card, $"Valor: {(value.Count > 0 ? string.Join(", ", value) : "nenhum")} · vício {e.Addictiveness:0.##}"
                       + (givenBy.TryGetValue(e.ID, out var mixer) ? $" · vem de <b>{mixer}</b>" : ""));
            if (!string.IsNullOrWhiteSpace(e.Description)) Line(card, $"<color=#a8a8a8>{e.Description}</color>");
        }
    }

    private void BuildChemistry()
    {
        // a interface da estação é um singleton da cena; StationSpeed já escalou CookTime_Mins
        if (!ChemistryStationInterface.InstanceExists) { Note("Receitas de química ainda não carregaram."); return; }
        var recipes = ChemistryStationInterface.Instance.Recipes;
        Note("Estação de química. Tempos já com a velocidade do mod e as melhorias.");
        for (int i = 0; i < recipes.Count; i++)
        {
            var r = recipes[i];
            if (r == null || r.Product?.Item == null) continue;
            var card = Entry(r.Product.Item.Icon, r.Unlocked ? r.RecipeTitle : $"<color=#8a8a8a>{r.RecipeTitle}  (bloqueada)</color>", Dur(r.CookTime_Mins));
            var parts = new List<string>();
            for (int j = 0; j < r.Ingredients.Count; j++)
            {
                var ing = r.Ingredients[j];
                if (ing == null) continue;
                var names = new List<string>();
                for (int k = 0; k < ing.Items.Count; k++) if (ing.Items[k] != null) names.Add(ing.Items[k].Name);
                parts.Add($"{ing.Quantity}x {string.Join(" ou ", names)}");
            }
            Line(card, $"{string.Join(" + ", parts)} -> <b>{r.Product.Quantity}x {r.Product.Item.Name}</b>");
            if (r.CookTemperature > 0f) Line(card, $"Temperatura: {r.CookTemperature:0}° (±{r.CookTemperatureTolerance:0})");
        }
    }

    private void BuildStations()
    {
        Note("Tempos já com a velocidade do mod e as melhorias.");
        // forno: cada item cozinhável carrega um CookableModule (entrada = o próprio item)
        var items = Registry.Instance.GetAllItems();
        for (int i = 0; i < items.Count; i++)
        {
            var s = items[i]?.TryCast<StorableItemDefinition>();
            var modules = s?.StationItem?.Modules;
            if (modules == null) continue;
            for (int j = 0; j < modules.Count; j++)
            {
                var cook = modules[j]?.TryCast<CookableModule>();
                if (cook?.Product == null) continue;
                var card = Entry(cook.Product.Icon, $"{s!.Name} -> {cook.ProductQuantity}x {cook.Product.Name}",
                    Dur(Scaled(cook.CookTime, StationSpeed.I.OvenSpeed)));
                Line(card, $"Forno de laboratório ({(cook.CookType == CookableModule.ECookableType.Liquid ? "líquido" : "sólido")})");
            }
        }
        // ponytail: 20 folhas -> 10 base e 720 min por nível são consts do jogo (ler const pelo interop não é seguro)
        foreach (var (cauldron, def) in Stations<Cauldron>())
        {
            var output = cauldron.CocaineBaseDefinition;
            var card = Entry(output != null ? output.Icon : def.Icon,
                $"20x {ItemName("cocaleaf", "Folha de coca")} + 1x {ItemName("gasoline", "Gasolina")} -> 10x {(output != null ? output.Name : "base de cocaína")}",
                Dur(Scaled(cauldron.CookTime, StationSpeed.I.CauldronSpeed)));
            Line(card, $"{def.Name}. A qualidade sai igual à da pior folha usada.");
        }
        foreach (var (rack, def) in Stations<DryingRack>())
        {
            var card = Entry(def.Icon, def.Name, $"+1 qualidade a cada {Dur(Scaled(720, StationSpeed.I.DryingSpeed))}");
            Line(card, $"Até {rack.ItemCapacity} itens: maconha, cogumelos e folha de coca (sem embalar).");
        }
        foreach (var (press, def) in Stations<BrickPress>())
        {
            var brick = press.BrickPackaging;
            if (brick == null) continue;
            var card = Entry(brick.Icon, def.Name, $"{brick.Quantity} -> 1 tijolo");
            Line(card, $"{brick.Quantity} unidades do mesmo produto, sem embalar, viram 1 {brick.Name}.");
        }
    }

    private void BuildGrowing()
    {
        float growth = Upgrades.I.Multiplier(Upgrades.Growth);
        Note("Rendimento base num vaso comum, sem aditivos. Tempos já com a melhoria de crescimento.");
        var items = Registry.Instance.GetAllItems();
        for (int i = 0; i < items.Count; i++)
        {
            var seed = items[i]?.TryCast<SeedDefinition>();
            var plant = seed?.PlantPrefab;
            if (plant == null) continue;
            var harvest = plant.TryCast<WeedPlant>()?.BranchPrefab ?? plant.TryCast<CocaPlant>()?.Harvestable;
            var card = Entry(seed!.Icon, seed.Name, Ui.Money(seed.BasePurchasePrice));
            Line(card, $"Cresce em {Dur(Scaled(plant.GrowthTime * 60, growth))}"
                       + (harvest?.Product != null ? $" · rende ~{plant.BaseYieldQuantity * harvest.ProductQuantity}x {harvest.Product.Name}" : ""));
        }

        string grain = "saco de grãos";
        foreach (var (station, _) in Stations<MushroomSpawnStation>())
            if (station._grainBagDefinition != null) { grain = station._grainBagDefinition.Name; break; }
        for (int i = 0; i < items.Count; i++)
        {
            var syringe = items[i]?.TryCast<SporeSyringeDefinition>();
            var spawn = syringe?.SpawnDefinition;
            if (spawn == null) continue;
            var card = Entry(syringe!.Icon, syringe.Name, Ui.Money(syringe.BasePurchasePrice));
            Line(card, $"+ {grain} na estação de esporos -> {spawn.Name}");
            var colony = spawn.ColonyPrefab;
            if (colony != null && spawn.Shroom != null)
                Line(card, $"Na cama de cogumelos: cresce em {Dur(Scaled(colony._growTime * 60, growth))} · rende ~{colony.BaseShroomYield}x {spawn.Shroom.Name}. Só cresce em lugar frio.");
        }
    }

    // ---------- utilitários ----------

    /// <summary>Componentes dos prefabs das estações compráveis (lidos sem precisar de uma estação colocada).</summary>
    private static IEnumerable<(T Station, BuildableItemDefinition Def)> Stations<T>() where T : Component
    {
        var items = Registry.Instance.GetAllItems();
        for (int i = 0; i < items.Count; i++)
        {
            var def = items[i]?.TryCast<BuildableItemDefinition>();
            var built = def?.BuiltItem;
            var c = built != null ? built.GetComponent<T>() : null;
            if (c != null) yield return (c, def!);
        }
    }

    private static string ItemName(string id, string fallback)
    {
        var item = Registry.GetItem(id);
        return item != null ? item.Name : fallback;
    }

    private static string Effects(Il2CppSystem.Collections.Generic.List<Effect>? list)
    {
        if (list == null || list.Count == 0) return "nenhum";
        var names = new List<string>();
        for (int i = 0; i < list.Count; i++) if (list[i] != null) names.Add(EffectLabel(list[i]));
        return string.Join(", ", names);
    }

    private static string EffectLabel(Effect e) => $"<color=#{ColorUtility.ToHtmlStringRGB(e.LabelColor)}>{e.Name}</color>";

    private static string DrugName(EDrugType t) => t switch
    {
        EDrugType.Marijuana => "Maconha",
        EDrugType.Methamphetamine => "Metanfetamina",
        EDrugType.Cocaine => "Cocaína",
        EDrugType.Shrooms => "Cogumelo",
        _ => t.ToString(),
    };

    private static int Scaled(int minutes, float speed) => Math.Max(1, Mathf.RoundToInt(minutes / Math.Max(0.01f, speed)));

    private static string Dur(int mins) =>
        mins < 60 ? $"{mins} min" : mins % 60 == 0 ? $"{mins / 60}h" : $"{mins / 60}h {mins % 60}min";
}
