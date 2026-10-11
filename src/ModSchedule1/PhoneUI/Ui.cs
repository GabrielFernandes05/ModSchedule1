using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ModSchedule1.PhoneUI;

/// <summary>Tiny uGUI builder for phone apps (Text, Button, scroll list). Everything uses layout groups.</summary>
public static class Ui
{
    private static Font? _font;
    public static Font Font => Native.Font != null ? Native.Font : _font ??= LoadFont();

    /// <summary>
    /// Estilo capturado de um app nativo do celular (fonte, cores, fundo, botão): nossos apps ficam com a mesma cara.
    /// Sem app nativo disponível, cai no estilo escuro próprio.
    /// </summary>
    public static class Native
    {
        public static Font? Font;
        public static Color Text = Color.white;
        public static Color Background = new(0.08f, 0.09f, 0.11f, 1f);
        public static Sprite? BackgroundSprite;
        public static Button? ButtonTemplate;
        public static bool Captured;

        public static void Capture()
        {
            Captured = false;
            if (!Il2CppScheduleOne.UI.Phone.Messages.DealerManagementApp.InstanceExists) return;
            var app = Il2CppScheduleOne.UI.Phone.Messages.DealerManagementApp.Instance;
            if (app.CashLabel != null) { Font = app.CashLabel.font; Text = app.CashLabel.color; }
            ButtonTemplate = app.AssignCustomerButton;
            var bg = app.appContainer != null ? app.appContainer.GetComponentInChildren<Image>(true) : null;
            if (bg != null) { Background = bg.color; BackgroundSprite = bg.sprite; }
            Captured = true;
        }
    }

    /// <summary>Texto secundário: cor do texto nativo, mais apagada (funciona em fundo claro ou escuro).</summary>
    public static Color Muted => new(Native.Text.r, Native.Text.g, Native.Text.b, 0.65f);

    private static Font LoadFont()
    {
        // O jogo usa Unity 2022.3, em que a fonte embutida se chama LegacyRuntime.ttf (Arial.ttf ficou só como fallback).
        try { var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); if (f != null) return f; } catch { }
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    public static GameObject Stretch(string name, Transform parent)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.layer = parent.gameObject.layer;
        return go;
    }

    public static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>().SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        return go;
    }

    public static Transform? FindDescendant(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name == name) return c;
            var found = FindDescendant(c, name);
            if (found != null) return found;
        }
        return null;
    }

    public static string Money(float value) => "$" + value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    public static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            child.SetActive(false);   // some do layout já neste frame; Destroy só no fim do frame
            Object.Destroy(child);
        }
    }

    /// <summary>Linha de "card": fundo levemente mais claro com layout vertical para várias linhas de texto/botões.</summary>
    public static GameObject Card(Transform parent)
    {
        var go = Child("Card", parent);
        go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = 4;
        v.padding = new RectOffset(14, 14, 10, 10);
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return go;
    }

    public static Sprite SolidSprite(Color color)
    {
        var tex = new Texture2D(4, 4);
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
                tex.SetPixel(x, y, color);
        tex.Apply();
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
    }

    public static Text Text(Transform parent, string content, int size = 18, bool bold = false,
        TextAnchor anchor = TextAnchor.MiddleLeft, float minHeight = 28)
    {
        var go = Child("Text", parent);
        var t = go.AddComponent<Text>();
        t.font = Font;
        t.text = content;
        t.fontSize = size;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.alignment = anchor;
        t.color = Native.Text;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = minHeight;
        le.flexibleWidth = 1;
        return t;
    }

    public static (Button button, Text label, Image bg) Button(Transform parent, string label, Color color, Action onClick,
        float width = 160, float height = 40)
    {
        if (Native.ButtonTemplate != null)
        {
            // botão clonado do app nativo: mesma cor/sprite/fonte; a cor pedida vira só um leve tom
            var b = NativeUi.CloneButton(Native.ButtonTemplate, parent, "Button", onClick);
            b.GetComponent<LayoutElement>().ignoreLayout = false;
            var nle = b.GetComponent<LayoutElement>();
            nle.minWidth = nle.preferredWidth = width;
            nle.minHeight = nle.preferredHeight = height;
            var nlabel = b.GetComponentInChildren<Text>(true);
            if (nlabel != null) { nlabel.text = label; nlabel.resizeTextForBestFit = true; nlabel.resizeTextMinSize = 10; }
            return (b, nlabel!, b.GetComponent<Image>());
        }
        var go = Child("Button", parent);
        var bg = go.AddComponent<Image>();
        bg.color = color;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener((UnityEngine.Events.UnityAction)onClick);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = width;
        le.minHeight = le.preferredHeight = height;

        var txtGo = Stretch("Label", go.transform);
        var txt = txtGo.AddComponent<Text>();
        txt.font = Font;
        txt.text = label;
        txt.fontSize = 16;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        return (btn, txt, bg);
    }

    public static GameObject Row(Transform parent, float height = 48)
    {
        var go = Child("Row", parent);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 12;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
        return go;
    }

    /// <summary>Vertical scroll list filling <paramref name="parent"/> below a top margin; returns the content transform.</summary>
    public static Transform ScrollList(Transform parent, float topMargin)
    {
        var scroll = Stretch("Scroll", parent);
        var srt = scroll.GetComponent<RectTransform>();
        srt.offsetMax = new Vector2(0, -topMargin);
        var sr = scroll.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.scrollSensitivity = 30;

        var viewport = Stretch("Viewport", scroll.transform);
        viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        sr.viewport = viewport.GetComponent<RectTransform>();

        var content = Child("Content", viewport.transform);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1);
        crt.offsetMin = crt.offsetMax = Vector2.zero;
        var v = content.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8;
        v.padding = new RectOffset(20, 20, 10, 10);
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = crt;
        return content.transform;
    }
}
