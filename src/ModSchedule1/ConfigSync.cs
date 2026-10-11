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

    private const string Key = "ms1cfg", VersionKey = "ms1ver";
    private static float _next;
    private static string? _published;
    private static bool _warnedVersion;

    /// <summary>
    /// A cada 2 s (Core.OnUpdate): host publica a config de jogo no lobby; cliente lê e aplica.
    /// ponytail: polling em vez de callback LobbyDataUpdate (o jogo não instancia esse genérico no IL2CPP);
    /// GetLobbyData lê um cache local da Steam, é barato.
    /// </summary>
    public static void Tick()
    {
        if (UnityEngine.Time.unscaledTime < _next) return;
        _next = UnityEngine.Time.unscaledTime + 2f;
        try
        {
            if (!Net.InLobby) { Clear(); _published = null; return; }
            if (Net.IsHost)
            {
                Clear();
                var payload = BuildPayload();
                if (payload == _published) return;
                Net.SetLobbyData(Key, payload);
                Net.SetLobbyData(VersionKey, Core.Version);
                _published = payload;
                Log.Info($"config de jogo publicada para os jogadores ({payload.Length} bytes)");
            }
            else if (Net.IsClientOnly)
            {
                var hostVersion = Net.GetLobbyData(VersionKey);
                if (!_warnedVersion && !string.IsNullOrEmpty(hostVersion) && hostVersion != Core.Version)
                {
                    _warnedVersion = true;
                    Log.Warn($"o host usa ModSchedule1 {hostVersion} e você {Core.Version}: abram o jogo pelo launcher para atualizar");
                }
                var payload = Net.GetLobbyData(Key);
                if (string.IsNullOrEmpty(payload)) { Clear(); return; }   // host sem o mod (ou ainda publicando)
                Apply(payload!);
            }
        }
        catch (System.Exception e) { Log.Error($"sincronização: {e.Message}"); }
    }
}
