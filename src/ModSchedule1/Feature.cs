using System;
using HarmonyLib;
using MelonLoader;

namespace ModSchedule1;

/// <summary>
/// One "mod" inside ModSchedule1. Each feature has its own config section, logger and Harmony patches
/// (nested [HarmonyPatch] classes). A feature that throws is isolated: it gets disabled, the others keep running.
/// Patches must check <see cref="Active"/> so toggling in-game works without unpatching.
/// </summary>
public abstract class Feature
{
    public const string ConfigFile = "UserData/ModSchedule1.cfg";

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual bool DefaultEnabled => true;
    /// <summary>Game logic that only the host may run in multiplayer (clients would desync or double-apply).</summary>
    public virtual bool HostOnly => false;

    public MelonPreferences_Category Category { get; private set; } = null!;
    public MelonPreferences_Entry<bool> EnabledEntry { get; private set; } = null!;
    public Log Log { get; private set; } = null!;
    public bool Faulted { get; private set; }

    public bool Active => EnabledEntry.Value && !Faulted && (!HostOnly || Net.IsHost);

    internal void Init(HarmonyLib.Harmony harmony)
    {
        Log = new Log(Id);
        Category = MelonPreferences.CreateCategory("ModSchedule1_" + Id, Name);
        Category.SetFilePath(ConfigFile, autoload: true, printmsg: false);
        EnabledEntry = Category.CreateEntry("Enabled", DefaultEnabled, "Ativo", Description);
        EnabledEntry.OnEntryValueChanged.Subscribe((_, now) => Log.Info(now ? "ativado" : "desativado"));
        OnInit();

        foreach (var nested in GetType().GetNestedTypes(AccessTools.all))
        {
            if (nested.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
            try
            {
                var patched = harmony.CreateClassProcessor(nested).Patch();
                Log.Debug($"patch {nested.Name} -> {string.Join(", ", patched)}");
            }
            catch (Exception e)
            {
                Fault($"patch {nested.Name} falhou (o jogo mudou?)", e);
            }
        }
        Log.Info($"carregado ({(EnabledEntry.Value ? "ativo" : "desativado")}{(HostOnly ? ", só host" : "")})");
    }

    protected MelonPreferences_Entry<T> Setting<T>(string id, T defaultValue, string name, string description = "")
        => Category.CreateEntry(id, defaultValue, name, description);

    /// <summary>Botões de ação no painel Ajustes do celular (ex.: "Limpar agora").</summary>
    public virtual System.Collections.Generic.IReadOnlyList<(string Label, Action Run)> Actions =>
        Array.Empty<(string, Action)>();

    protected virtual void OnInit() { }
    public virtual void OnSceneLoaded(string scene) { }
    public virtual void OnUpdate() { }

    /// <summary>Disables this feature for the session after an unexpected error, logging why.</summary>
    public void Fault(string what, Exception e)
    {
        Faulted = true;
        Log.Error($"{what}: {e}");
        Log.Error("feature desativada até reiniciar o jogo; as outras continuam funcionando");
    }

    /// <summary>Runs feature code from a patch or callback; an exception faults the feature instead of breaking the game.</summary>
    public void Guard(string what, Action action)
    {
        if (!Active) return;
        try { action(); }
        catch (Exception e) { Fault(what, e); }
    }
}

/// <summary>Gives Harmony patches (static) typed access to their feature: <c>BetterStacks.I.Active</c>.</summary>
public abstract class Feature<TSelf> : Feature where TSelf : Feature<TSelf>
{
    public static TSelf I { get; private set; } = null!;
    protected Feature() => I = (TSelf)this;
}
