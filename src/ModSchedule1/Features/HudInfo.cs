using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.UI;
using Il2CppTMPro;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ModSchedule1.Features;

/// <summary>
/// Relógio (hora e dia) e barra de XP sempre na tela. O jogo só mostra a hora no celular e o XP ao dormir. Fica dentro
/// do HUD, então some junto com ele (cutscene, morte, prisão, câmera livre). Estilo copiado do saldo online do HUD.
/// </summary>
public sealed class HudInfo : Feature<HudInfo>
{
    public override string Id => "HudInfo";
    public override string Name => "Relógio e XP na tela";
    public override string Description => "Hora, dia e barra de XP do rank sempre visíveis no canto da tela.";
    public override bool Shared => false;   // pessoal: só a tela de quem usa

    private Pref<bool> _clock = null!, _xp = null!, _24h = null!;
    private Pref<int> _corner = null!;
    private GameObject? _root;
    private TextMeshProUGUI _time = null!, _rank = null!;
    private RectTransform _barFill = null!, _bar = null!;
    private float _next;

    private static readonly string[] Days = { "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Domingo" };

    protected override void OnInit()
    {
        _clock = Setting("Clock", true, "Mostrar relógio");
        _xp = Setting("XP", true, "Mostrar barra de XP");
        _24h = Setting("Format24h", true, "Relógio 24h");
        _corner = Setting("Corner", 1, "Canto (0 sup.dir. 1 sup.esq. 2 inf.dir. 3 inf.esq.)", min: 0, max: 3);
        Enabled.Changed += () => { if (_root != null) _root.SetActive(Enabled.Value); };
    }

    public override void OnSceneLoaded(string scene) => _root = null;   // o HUD (pai) é destruído com a cena

    public override void OnUpdate()
    {
        if (Time.unscaledTime < _next || SceneManager.GetActiveScene().name != "Main") return;
        _next = Time.unscaledTime + 0.5f;
        if (!HUD.InstanceExists || !TimeManager.InstanceExists) return;
        if (_root == null) Build();
        _root!.SetActive(true);
        Place();

        var tm = TimeManager.Instance;
        _time.gameObject.SetActive(_clock.Value);
        if (_clock.Value)
        {
            int t = tm.CurrentTime;
            string clock = _24h.Value ? $"{t / 100:00}:{t % 100:00}" : TimeManager.Get12HourTime(t, true);
            _time.text = $"{clock}  <size=70%>{Days[Mathf.Clamp(tm.DayIndex, 0, 6)]}, dia {tm.ElapsedDays + 1}</size>";
        }

        bool xp = _xp.Value && LevelManager.InstanceExists;
        _rank.gameObject.SetActive(xp);
        _bar.gameObject.SetActive(xp);
        if (xp)
        {
            var lm = LevelManager.Instance;
            float progress = lm.XPToNextTier > 0 ? Mathf.Clamp01(lm.XP / lm.XPToNextTier) : 0f;
            _rank.text = $"{FullRank.GetString(lm.GetFullRank())}  <size=80%>{lm.XP} / {lm.XPToNextTier:0} XP</size>";
            _barFill.anchorMax = new Vector2(progress, 1f);
        }
    }

    private void Place()
    {
        int c = Mathf.Clamp(_corner.Value, 0, 3);
        bool right = c == 0 || c == 2, top = c <= 1;
        var rt = _root!.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
        // em cima à direita ficam o saldo online e o minimapa: desce bem
        rt.anchoredPosition = new Vector2(right ? -20f : 20f, top ? (right ? -340f : -20f) : 20f);
        var align = right ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        _time.alignment = _rank.alignment = align;
        _bar.pivot = _bar.anchorMin = new Vector2(right ? 1f : 0f, _bar.anchorMin.y);
        _bar.anchorMax = new Vector2(right ? 1f : 0f, _bar.anchorMax.y);
    }

    private void Build()
    {
        var hud = HUD.Instance;
        _root = new GameObject("MS1_HudInfo");
        var rt = _root.AddComponent<RectTransform>();
        rt.SetParent(hud.canvasRect, false);
        rt.sizeDelta = new Vector2(320, 70);
        _root.layer = hud.canvas.gameObject.layer;

        var style = hud.OnlineBalanceDisplay != null ? hud.OnlineBalanceDisplay.BalanceLabel : null;
        _time = Label("Clock", style, 1f, 0f, 34f);
        _rank = Label("Rank", style, 0.65f, -36f, 22f);

        _bar = PhoneUI.Ui.Child("XPBar", _root.transform).GetComponent<RectTransform>();
        _bar.anchorMin = _bar.anchorMax = new Vector2(0f, 1f);
        _bar.pivot = new Vector2(0f, 1f);
        _bar.anchoredPosition = new Vector2(0f, -60f);
        _bar.sizeDelta = new Vector2(220f, 6f);
        var bg = _bar.gameObject.AddComponent<Image>();
        bg.sprite = Ui.Rounded();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;
        _barFill = PhoneUI.Ui.Stretch("Fill", _bar).GetComponent<RectTransform>();
        var fill = _barFill.gameObject.AddComponent<Image>();
        fill.sprite = Ui.Rounded();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.33f, 0.9f, 0.45f, 0.95f);
        fill.raycastTarget = false;
        Log.Info("relógio e XP no HUD criados");
    }

    private TextMeshProUGUI Label(string name, TextMeshProUGUI? style, float scale, float y, float height)
    {
        var go = PhoneUI.Ui.Child(name, _root!.transform);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = new Vector2(0f, 1f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.anchoredPosition = new Vector2(0f, y);
        r.sizeDelta = new Vector2(0f, height);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (style != null)
        {
            t.font = style.font;
            t.fontSharedMaterial = style.fontSharedMaterial;
            t.color = style.color;
            t.fontSize = style.fontSize * scale;
        }
        else t.fontSize = 26f * scale;
        t.richText = true;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }
}
