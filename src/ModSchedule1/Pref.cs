using System;
using System.Globalization;
using MelonLoader;

namespace ModSchedule1;

/// <summary>Visão não genérica de um <see cref="Pref{T}"/> (menu de config e sincronização).</summary>
public interface IPref
{
    string Key { get; }                    // "<FeatureId>.<Identifier>", igual em todos os PCs
    MelonPreferences_Entry Entry { get; }
    bool Shared { get; }                   // config de jogo: o host manda nos clientes
    bool Overridden { get; }               // valor vindo do host nesta sessão
    object BoxedValue { get; }
    float Min { get; }                     // faixa do slider no menu (só int/float)
    float Max { get; }
    string Serialize();
    void SetOverride(string raw);
    void ClearOverride();
}

/// <summary>
/// Config de uma feature. O valor local fica no MelonPreferences (UserData/ModSchedule1.cfg); em multiplayer, as
/// configs Shared recebem uma sobreposição do host que vive só em memória. Assim o cfg do jogador nunca é
/// sobrescrito pelos valores do host (ML salva o cfg ao fechar o jogo).
/// </summary>
public sealed class Pref<T> : IPref
{
    public MelonPreferences_Entry<T> Entry { get; }
    MelonPreferences_Entry IPref.Entry => Entry;
    public string Key { get; }
    public bool Shared { get; }
    public bool Overridden { get; private set; }
    public float Min { get; }
    public float Max { get; }
    private T _override = default!;

    /// <summary>Disparado quando o valor efetivo muda (edição local sem override, ou override do host).</summary>
    public event Action? Changed;

    public Pref(MelonPreferences_Entry<T> entry, string featureId, bool shared, float min = float.NaN, float max = float.NaN)
    {
        Entry = entry;
        Key = featureId + "." + entry.Identifier;
        Shared = shared;
        // ponytail: faixa padrão derivada do valor padrão; quem precisa de outra passa min/max no Setting()
        float def = entry.DefaultValue is IConvertible c && typeof(T) != typeof(bool) && typeof(T) != typeof(string)
            ? Convert.ToSingle(c, CultureInfo.InvariantCulture) : 0f;
        Min = float.IsNaN(min) ? (typeof(T) == typeof(int) && def >= 1 ? 1 : 0) : min;
        Max = float.IsNaN(max) ? Math.Max(typeof(T) == typeof(int) ? 10 : 5, def * 4) : max;
        entry.OnEntryValueChanged.Subscribe((_, _) => { if (!Overridden) Changed?.Invoke(); });
    }

    public T Value
    {
        get => Overridden ? _override : Entry.Value;
        set => Entry.Value = value;   // edição local; se houver override do host, vale de novo ao sair da sessão
    }

    public object BoxedValue => Value!;

    public string Serialize() => Format(Entry.Value);

    public void SetOverride(string raw)
    {
        if (!TryParse(raw, out var v)) return;
        bool changed = !Overridden || !Equals(v, _override);
        _override = v;
        Overridden = true;
        if (changed) Changed?.Invoke();
    }

    public void ClearOverride()
    {
        if (!Overridden) return;
        bool changed = !Equals(_override, Entry.Value);
        Overridden = false;
        if (changed) Changed?.Invoke();
    }

    private static string Format(T value) => value switch
    {
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        null => "",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static bool TryParse(string raw, out T value)
    {
        object? parsed = null;
        if (typeof(T) == typeof(bool) && bool.TryParse(raw, out var b)) parsed = b;
        else if (typeof(T) == typeof(int) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) parsed = i;
        else if (typeof(T) == typeof(float) && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) parsed = f;
        else if (typeof(T) == typeof(string)) parsed = raw;
        value = parsed is T t ? t : default!;
        return parsed is T;
    }
}
