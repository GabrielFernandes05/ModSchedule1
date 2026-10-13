using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Property;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModSchedule1.Features;

/// <summary>
/// Melhorias compradas no app Finanças: crescimento (plantas e cogumelos), lavagem e cada mesa (forno, mixer, química,
/// caldeirão, secador, embalagem dos funcionários). Nada acelera sem comprar; nas mesas a melhoria multiplica a
/// velocidade da config do Station Speed. Níveis ficam por save (chave = semente do mundo, a mesma no host e nos clientes) em
/// UserData/ModSchedule1/upgrades.json no host, que os publica no lobby. Crescimento e lavagem rodam em TODOS os PCs,
/// então o efeito também: todos usam o nível do host. Só o host compra (paga pelo banco).
/// </summary>
public sealed class Upgrades : Feature<Upgrades>
{
    public override string Id => "Upgrades";
    public override string Name => "Melhorias";
    public override string Description => "Compre no app Finanças: crescimento, lavagem e cada mesa (forno, mixer, química...) mais rápidos.";

    public sealed class Track
    {
        public readonly string Id, Name, What;
        public readonly float[] Mult;
        public readonly int[] Price;   // Price[n] = preço para chegar ao nível n
        public Track(string id, string name, string what, float[] mult, int[] price) { Id = id; Name = name; What = what; Mult = mult; Price = price; }
        public int Max => Mult.Length - 1;
    }

    private static readonly float[] Speeds = { 1f, 3f, 6f, 10f };   // nível salvo é o índice: quem já comprou ganha o novo valor
    private static readonly int[] StationPrice = { 0, 8000, 20000, 50000 };
    public static readonly Track Growth = new("Growth", "Crescimento", "Plantas e cogumelos crescem", Speeds, new[] { 0, 15000, 40000, 90000 });
    public static readonly Track Laundering = new("Laundering", "Lavagem", "A lavagem termina", Speeds, new[] { 0, 10000, 30000, 75000 });
    public static readonly Track Oven = new("Oven", "Forno", "O forno cozinha", Speeds, StationPrice);
    public static readonly Track Mixing = new("Mixing", "Mixer", "O mixer mistura", Speeds, StationPrice);
    public static readonly Track Chemistry = new("Chemistry", "Estação de química", "A química cozinha", Speeds, StationPrice);
    public static readonly Track Cauldron = new("Cauldron", "Caldeirão", "O caldeirão cozinha", Speeds, StationPrice);
    public static readonly Track Drying = new("Drying", "Secador", "O secador seca", Speeds, StationPrice);
    public static readonly Track Packaging = new("Packaging", "Embalagem", "Funcionários embalam", Speeds, StationPrice);
    public static readonly Track[] All = { Growth, Laundering, Oven, Mixing, Chemistry, Cauldron, Drying, Packaging };

    /// <summary>Níveis de um save. Growth/Laundering são do formato da 0.13.0 (migrados para Tracks ao carregar).</summary>
    public sealed class Levels
    {
        public int Growth, Laundering;
        public Dictionary<string, int> Tracks = new();
    }

    private const string LobbyKey = "ms1upg";
    private Pref<float> _priceMult = null!;
    private Dictionary<string, Levels> _saved = new();
    private Levels _current = new();
    private int _loadedSeed = int.MinValue;
    private string? _published;
    private float _nextTick;

    protected override void OnInit()
    {
        _priceMult = Setting("PriceMultiplier", 1f, "Preço das melhorias x", min: 0.1f, max: 10f);
        _saved = Store.Load<Dictionary<string, Levels>>("upgrades");
    }

    public int Level(Track t) => Math.Clamp(_current.Tracks.TryGetValue(t.Id, out int l) ? l : 0, 0, t.Max);
    public float Multiplier(Track t) => Active ? t.Mult[Level(t)] : 1f;
    public int NextPrice(Track t) => Level(t) >= t.Max ? -1 : (int)Math.Round(t.Price[Level(t) + 1] * _priceMult.Value);
    public static bool CanBuy => Net.IsHost && SceneManager.GetActiveScene().name == "Main";

    public bool Buy(Track t)
    {
        int price = NextPrice(t);
        if (!Active || !CanBuy || price < 0 || Bank.Online < price) return false;
        MoneyManager.Instance.CreateOnlineTransaction($"Melhoria: {t.Name} nível {Level(t) + 1}", -price, 1f, "ModSchedule1");
        _current.Tracks[t.Id] = Level(t) + 1;
        _saved[GameManager.Seed.ToString()] = _current;
        Changed();
        Store.Save("upgrades", _saved);
        Log.Info($"melhoria comprada: {t.Name} nível {Level(t)} (x{t.Mult[Level(t)]}) por {Ui.Money(price)}");
        return true;
    }

    public override void OnSceneLoaded(string scene) { _loadedSeed = int.MinValue; _published = null; }

    public override void OnUpdate()
    {
        if (Time.unscaledTime < _nextTick || SceneManager.GetActiveScene().name != "Main") return;
        _nextTick = Time.unscaledTime + 2f;
        if (Net.IsHost)
        {
            int seed = GameManager.Seed;
            if (seed != _loadedSeed)
            {
                _loadedSeed = seed;
                _current = _saved.TryGetValue(seed.ToString(), out var lv) ? lv : new Levels();
                if (_current.Growth > 0) { _current.Tracks[Growth.Id] = _current.Growth; _current.Growth = 0; }
                if (_current.Laundering > 0) { _current.Tracks[Laundering.Id] = _current.Laundering; _current.Laundering = 0; }
                Log.Info($"melhorias deste save: {Describe(_current)}");
                Changed();
            }
            string payload = Serialize(_current);
            if (Net.InLobby && payload != _published) { Net.SetLobbyData(LobbyKey, payload); _published = payload; }
        }
        else if (Net.InLobby && !Net.IsLobbyOwner)
        {
            var payload = Net.GetLobbyData(LobbyKey) ?? "";
            if (payload != Serialize(_current))
            {
                _current = Parse(payload);
                Log.Info($"melhorias do host: {Describe(_current)}");
                Changed();
            }
        }
    }

    // "Growth=1;Oven=2" (só níveis > 0, em ordem fixa: o texto serve para comparar)
    private static string Serialize(Levels l)
    {
        var parts = new List<string>();
        foreach (var t in All)
            if (l.Tracks.TryGetValue(t.Id, out int v) && v > 0) parts.Add($"{t.Id}={v}");
        return string.Join(";", parts);
    }

    private static Levels Parse(string payload)
    {
        var l = new Levels();
        foreach (var part in payload.Split(';'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && int.TryParse(kv[1], out int v) && v > 0) l.Tracks[kv[0]] = v;
        }
        return l;
    }

    private static string Describe(Levels l) => l.Tracks.Count == 0 ? "nenhuma" : Serialize(l).Replace(";", ", ");

    /// <summary>Mesas que leem a velocidade só ao serem criadas recebem o valor novo na hora.</summary>
    private static void Changed()
    {
        try { StationSpeed.I.Reapply(); } catch (Exception e) { Core.Log.Error($"melhorias nas mesas: {e.Message}"); }
    }

    // Planta: o crescimento do minuto (com luz, temperatura, água) é multiplicado. Vale também ao dormir
    // (Pot.OnTimeSkipped chama MinPass com os minutos dormidos e depois sincroniza).
    [HarmonyPatch(typeof(Plant), nameof(Plant.MinPass))]
    private static class PlantPatch
    {
        private static void Prefix(Plant __instance, out float __state) => __state = __instance.NormalizedGrowthProgress;

        private static void Postfix(Plant __instance, float __state)
        {
            float m = I.Multiplier(Growth);
            if (m <= 1.001f) return;
            float now = __instance.NormalizedGrowthProgress, gained = now - __state;
            if (gained > 0f && now < 1f) __instance.SetNormalizedGrowthProgress(now + gained * (m - 1f));
        }
    }

    [HarmonyPatch(typeof(ShroomColony), nameof(ShroomColony.OnMinPass))]
    private static class ShroomPatch
    {
        private static void Prefix(ShroomColony __instance, out float __state) => __state = __instance.GrowthProgress;

        private static void Postfix(ShroomColony __instance, float __state)
        {
            float m = I.Multiplier(Growth);
            if (m <= 1.001f) return;
            float now = __instance.GrowthProgress, gained = now - __state;
            if (gained > 0f && now < 1f) __instance.SetGrowthPercentage(now + gained * (m - 1f));
        }
    }

    // Cogumelo ao dormir: o servidor soma os minutos dormidos e depois sincroniza; contamos mais minutos.
    [HarmonyPatch(typeof(ShroomColony), nameof(ShroomColony.OnTimeSkipped))]
    private static class ShroomSkipPatch
    {
        private static void Prefix(ref int mins) => mins = Mathf.RoundToInt(mins * I.Multiplier(Growth));
    }

    // Lavagem: o tempo total da operação (1440 min no jogo) encolhe; a barra e o "falta" do jogo continuam certos.
    [HarmonyPatch(typeof(Business), nameof(Business.MinsPass))]
    private static class LaunderPatch
    {
        private static void Prefix(Business __instance)
        {
            int total = Math.Max(1, Mathf.RoundToInt(1440f / I.Multiplier(Laundering)));
            var ops = __instance.LaunderingOperations;
            for (int i = 0; i < ops.Count; i++)
                if (ops[i] != null && ops[i].completionTime_Minutes != total) ops[i].completionTime_Minutes = total;
        }
    }
}
