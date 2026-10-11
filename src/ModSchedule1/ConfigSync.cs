using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ModSchedule1;

/// <summary>
/// Config de jogo igual para todos no multiplayer: o host publica as Prefs Shared ("Feature.Id=valor" por linha) e
/// os clientes aplicam como sobreposição em memória (o cfg deles não muda). O transporte fica em <see cref="Net"/>.
/// </summary>
public static class ConfigSync
{
    public const int Version = 1;
    private static readonly Log Log = new("Sync");
    private static string? _lastApplied;

    public static IEnumerable<IPref> SharedPrefs => Core.Features.SelectMany(f => f.Prefs).Where(p => p.Shared);

    public static string BuildPayload()
    {
        var sb = new StringBuilder().Append("v=").Append(Version).Append('\n');
        foreach (var p in SharedPrefs)
            sb.Append(p.Key).Append('=').Append(p.Serialize().Replace("\\", "\\\\").Replace("\n", "\\n")).Append('\n');
        return sb.ToString();
    }

    /// <summary>Cliente: aplica a config do host. Ignora chaves desconhecidas (host com outra versão do mod).</summary>
    public static void Apply(string payload)
    {
        if (payload == _lastApplied) return;
        _lastApplied = payload;
        var byKey = SharedPrefs.ToDictionary(p => p.Key);
        int applied = 0, unknown = 0;
        foreach (var line in payload.Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line.Substring(0, eq);
            if (key == "v") { if (line.Substring(eq + 1) != Version.ToString()) Log.Warn("host usa outro formato de config; atualizem o mod"); continue; }
            var value = line.Substring(eq + 1).Replace("\\n", "\n").Replace("\\\\", "\\");
            if (byKey.TryGetValue(key, out var pref)) { pref.SetOverride(value); applied++; }
            else unknown++;
        }
        Log.Info($"config do host aplicada ({applied} valores{(unknown > 0 ? $", {unknown} desconhecidos: versões diferentes do mod?" : "")})");
    }

    /// <summary>Volta para a config local (saiu da sessão ou virou host/singleplayer).</summary>
    public static void Clear()
    {
        if (_lastApplied == null) return;
        _lastApplied = null;
        foreach (var p in SharedPrefs) p.ClearOverride();
        Log.Info("usando a config local");
    }

    public static bool Active => _lastApplied != null;
}
