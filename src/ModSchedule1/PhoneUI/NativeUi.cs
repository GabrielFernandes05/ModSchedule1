using System;
using System.Text;
using Il2CppScheduleOne;
using Il2CppScheduleOne.UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ModSchedule1.PhoneUI;

/// <summary>
/// Botões/textos clonados dos próprios apps do jogo: ficam com a aparência nativa (fonte, cor, sprite).
/// Clones ficam fora do layout automático (ignoreLayout) e posicionados por âncora junto ao elemento de referência.
/// </summary>
public static class NativeUi
{
    private static readonly Log Log = new("UI");

    public static Button CloneButton(Button source, Transform parent, string name, Action onClick)
    {
        var go = Object.Instantiate(source.gameObject, parent, false);
        go.name = name;
        go.SetActive(true);
        // Tooltip/UISelectable do original se registrariam no painel de gamepad e mostrariam a dica errada.
        foreach (var t in go.GetComponentsInChildren<Tooltip>(true)) Object.Destroy(t);
        foreach (var s in go.GetComponentsInChildren<UISelectable>(true)) Object.Destroy(s);
        var button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();   // descarta os listeners persistentes do prefab
        button.onClick.AddListener((UnityEngine.Events.UnityAction)onClick);
        button.interactable = true;
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        return button;
    }

    public static void SetText(Button button, string text)
    {
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null) label.text = text;
    }

    public static Text CloneText(Text source, Transform parent, string name)
    {
        var go = Object.Instantiate(source.gameObject, parent, false);
        go.name = name;
        go.SetActive(true);
        var text = go.GetComponent<Text>();
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    /// <summary>Âncora no canto do pai (0..1) com deslocamento em pixels e tamanho fixo.</summary>
    public static void Place(Component c, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size)
    {
        var rt = c.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
    }

    /// <summary>Loga uma vez a hierarquia (com LayoutGroup/ContentSizeFitter) para ajustar posições sem ver o prefab.</summary>
    public static void DumpOnce(ref bool done, Transform root, string what, int maxDepth = 3)
    {
        if (done || !Core.DebugLogging || root == null) return;
        done = true;
        var sb = new StringBuilder($"hierarquia de {what}:\n");
        Dump(root, 0, maxDepth, sb);
        Log.Debug(sb.ToString());
    }

    private static void Dump(Transform t, int depth, int maxDepth, StringBuilder sb)
    {
        var rt = t.GetComponent<RectTransform>();
        sb.Append(new string(' ', depth * 2)).Append(t.name);
        if (rt != null) sb.Append($" size={rt.rect.width:0}x{rt.rect.height:0}");
        if (t.GetComponent<LayoutGroup>() != null) sb.Append(" [LayoutGroup]");
        if (t.GetComponent<ContentSizeFitter>() != null) sb.Append(" [Fitter]");
        if (!t.gameObject.activeSelf) sb.Append(" (inativo)");
        sb.Append('\n');
        if (depth >= maxDepth) return;
        for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1, maxDepth, sb);
    }
}
