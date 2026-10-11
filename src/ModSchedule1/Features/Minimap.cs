using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone.Map;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ModSchedule1.Features;

/// <summary>
/// Minimapa no HUD feito do próprio mapa do celular: mesma imagem (MapApp.BackgroundImage) e os mesmos ícones
/// (cópias dos ícones de cada POI: lojas, imóveis, entregas, outros jogadores...). Posições pela conversão do jogo
/// (MapPositionUtility). Nada de câmera extra: custo de uma imagem e algumas dezenas de ícones.
/// </summary>
public sealed class Minimap : Feature<Minimap>
{
    public override string Id => "Minimap";
    public override string Name => "Minimap";
    public override string Description => "Minimapa no canto da tela com os ícones do mapa do celular.";
    public override bool Shared => false;   // pessoal: só desenha na tela de quem usa

    private Pref<int> _size = null!, _radius = null!, _corner = null!;
    private Pref<bool> _rotate = null!;
    private Pref<float> _iconScale = null!;

    private GameObject? _root;
    private RectTransform _frame = null!, _world = null!, _offset = null!, _arrow = null!;
    private readonly Dictionary<IntPtr, (POI Poi, RectTransform Icon)> _icons = new();
    private float _nextScan;
    private bool _logged;
    private static Sprite? _circle;

    protected override void OnInit()
    {
        _size = Setting("Size", 220, "Tamanho (px)", min: 120, max: 420);
        _radius = Setting("Radius", 90, "Alcance (metros)", min: 30, max: 400);
        _corner = Setting("Corner", 0, "Canto (0 sup.dir. 1 sup.esq. 2 inf.dir. 3 inf.esq.)", min: 0, max: 3);
        _rotate = Setting("Rotate", true, "Girar com a câmera");
        _iconScale = Setting("IconScale", 0.6f, "Tamanho dos ícones", min: 0.2f, max: 1.5f);
        Enabled.Changed += () => { if (_root != null) _root.SetActive(Enabled.Value); };
    }

    public override void OnSceneLoaded(string scene)
    {
        _root = null;   // o HUD (pai) é destruído com a cena
        _icons.Clear();
    }

    public override void OnUpdate()
    {
        if (SceneManager.GetActiveScene().name != "Main" || Player.Local == null || !MapPositionUtility.InstanceExists) return;
        if (_root == null && !Build()) return;
        _root!.SetActive(true);

        var util = MapPositionUtility.Instance;
        Vector3 pos = Player.Local.transform.position;
        Vector2 here = util.GetMapPosition(pos);
        float unitsPerMeter = (util.GetMapPosition(pos + Vector3.right) - here).magnitude;
        float size = _size.Value;
        float zoom = size / (2f * _radius.Value * Mathf.Max(unitsPerMeter, 0.0001f));   // pixels por unidade do mapa
        float yaw = PlayerCamera.InstanceExists ? PlayerCamera.Instance.transform.eulerAngles.y : Player.Local.transform.eulerAngles.y;

        Place(size);
        _world.localScale = new Vector3(zoom, zoom, 1f);
        _world.localEulerAngles = new Vector3(0, 0, _rotate.Value ? yaw : 0f);
        _offset.anchoredPosition = -here;
        _arrow.localEulerAngles = new Vector3(0, 0, _rotate.Value ? 0f : -yaw);

        if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 2f; ScanPois(); }
        float iconScale = _iconScale.Value / zoom;
        float counter = _rotate.Value ? -yaw : 0f;
        foreach (var (poi, icon) in _icons.Values)
        {
            if (poi == null || icon == null) continue;
            icon.anchoredPosition = util.GetMapPosition(poi.transform.position);
            icon.localScale = new Vector3(iconScale, iconScale, 1f);
            icon.localEulerAngles = new Vector3(0, 0, counter);   // ícone sempre em pé
        }
    }

    private void Place(float size)
    {
        int c = Mathf.Clamp(_corner.Value, 0, 3);
        var anchor = new Vector2(c == 0 || c == 2 ? 1f : 0f, c <= 1 ? 1f : 0f);
        // em cima à direita fica o saldo online do HUD: desce um pouco
        var margin = new Vector2(anchor.x > 0 ? -20f : 20f, anchor.y > 0 ? (c == 0 ? -110f : -20f) : 20f);
        _frame.anchorMin = _frame.anchorMax = _frame.pivot = anchor;
        _frame.anchoredPosition = margin;
        _frame.sizeDelta = new Vector2(size, size);
    }

    private bool Build()
    {
        if (!HUD.InstanceExists || !MapApp.InstanceExists) return false;
        var map = MapApp.Instance;
        if (map.BackgroundImage == null || map.BackgroundImage.sprite == null || map.PoIContainer == null) return false;

        _root = new GameObject("MS1_Minimap");
        _frame = _root.AddComponent<RectTransform>();
        _frame.SetParent(HUD.Instance.canvas.transform, false);
        _root.layer = HUD.Instance.canvas.gameObject.layer;
        _root.AddComponent<Canvas>();   // canvas próprio: mexer no minimapa todo frame não redesenha o HUD inteiro
        var border = _root.AddComponent<Image>();
        border.sprite = Circle();
        border.color = new Color(0f, 0f, 0f, 0.65f);
        border.raycastTarget = false;

        var mask = PhoneUI.Ui.Stretch("Mask", _root.transform);
        var maskRt = mask.GetComponent<RectTransform>();
        maskRt.offsetMin = new Vector2(4, 4);
        maskRt.offsetMax = new Vector2(-4, -4);
        var maskImage = mask.AddComponent<Image>();
        maskImage.sprite = Circle();
        maskImage.color = new Color(0.12f, 0.14f, 0.16f, 1f);   // fora do mapa
        maskImage.raycastTarget = false;
        mask.AddComponent<Mask>().showMaskGraphic = true;

        _world = Centered("World", mask.transform);
        _offset = Centered("Offset", _world);

        // imagem do mapa no mesmo sistema de coordenadas dos ícones (o PoIContainer do app)
        var bg = map.BackgroundImage.rectTransform;
        var container = map.PoIContainer;
        Vector3 center = container.InverseTransformPoint(bg.TransformPoint(bg.rect.center));
        Vector2 bgSize = Vector2.Scale(bg.rect.size, new Vector2(bg.lossyScale.x / container.lossyScale.x, bg.lossyScale.y / container.lossyScale.y));
        var mapImage = Centered("Map", _offset);
        mapImage.anchoredPosition = (Vector2)center - container.rect.center;
        mapImage.sizeDelta = bgSize;
        var img = mapImage.gameObject.AddComponent<Image>();
        img.sprite = map.BackgroundImage.sprite;
        img.raycastTarget = false;

        _arrow = Centered("Player", mask.transform);
        _arrow.sizeDelta = new Vector2(18, 18);
        var playerIcon = Player.Local.PoI != null ? Player.Local.PoI.IconContainer : null;
        if (playerIcon != null) CopyIcon(playerIcon, _arrow).localScale = Vector3.one * 0.8f;
        else
        {
            var dot = _arrow.gameObject.AddComponent<Image>();
            dot.sprite = Circle();
            dot.color = new Color(0.3f, 0.8f, 1f);
        }

        _icons.Clear();
        _nextScan = 0;
        if (!_logged)
        {
            _logged = true;
            Log.Info($"minimapa criado (mapa {bgSize.x:0}x{bgSize.y:0}, centro {mapImage.anchoredPosition}, ícone do jogador: {playerIcon != null})");
        }
        return true;
    }

    /// <summary>Ícones de todos os POIs ativos (os mesmos do app de mapa), menos o do jogador local (vai no centro).</summary>
    private void ScanPois()
    {
        var seen = new HashSet<IntPtr>();
        var local = Player.Local.PoI;
        foreach (var poi in Object.FindObjectsOfType<POI>())
        {
            if (poi == null || poi.IconContainer == null || (local != null && poi.Pointer == local.Pointer)) continue;
            if (poi.UI != null && !poi.UI.gameObject.activeSelf) continue;   // POI escondido no app também
            seen.Add(poi.Pointer);
            if (_icons.ContainsKey(poi.Pointer)) continue;
            var holder = Centered("Poi", _offset);
            CopyIcon(poi.IconContainer, holder);
            _icons[poi.Pointer] = (poi, holder);
        }
        var gone = new List<IntPtr>();
        foreach (var (key, entry) in _icons)
            if (!seen.Contains(key) || entry.Icon == null) gone.Add(key);
        foreach (var key in gone)
        {
            if (_icons[key].Icon != null) Object.Destroy(_icons[key].Icon.gameObject);
            _icons.Remove(key);
        }
        Log.Debug($"minimapa: {_icons.Count} ícones");
    }

    private static RectTransform CopyIcon(RectTransform source, RectTransform parent)
    {
        var copy = Object.Instantiate(source.gameObject, parent, false).GetComponent<RectTransform>();
        copy.anchorMin = copy.anchorMax = copy.pivot = new Vector2(0.5f, 0.5f);
        copy.anchoredPosition = Vector2.zero;
        copy.localEulerAngles = Vector3.zero;
        foreach (var g in copy.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        return copy;
    }

    private static RectTransform Centered(string name, Transform parent)
    {
        var rt = PhoneUI.Ui.Child(name, parent).GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    /// <summary>Círculo branco com borda suave (máscara e moldura), gerado uma vez.</summary>
    private static Sprite Circle()
    {
        if (_circle != null) return _circle;
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        float r = n / 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d)));
            }
        tex.Apply();
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        _circle.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return _circle;
    }
}
