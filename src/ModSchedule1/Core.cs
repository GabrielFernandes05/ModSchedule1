using System;
using System.Collections.Generic;
using MelonLoader;
using ModSchedule1.Features;
using ModSchedule1.PhoneUI;

[assembly: MelonInfo(typeof(ModSchedule1.Core), "ModSchedule1", ModSchedule1.Core.Version, "ModSchedule1")]
[assembly: MelonGame("TVGS", "Schedule I")]
// Patches são aplicados por feature (Feature.Init), cada um isolado; nada de PatchAll automático.
[assembly: HarmonyDontPatchAll]

namespace ModSchedule1;

public sealed class Core : MelonMod
{
    public const string Version = "0.13.1";

    /// <summary>Every feature of the megamod. Adding a mod = adding a line here (and in release/features.json).</summary>
    public static readonly List<Feature> Features = new()
    {
        new BetterStacks(),
        new NoMoreTrash(),
        new CompassDistances(),
        new WarehouseAlwaysOpen(),
        new PlantsAlwaysGrow(),
        new EmployeesAlwaysWork(),
        new AbsorbentSoil(),
        new StationSpeed(),
        new BetterEmployees(),
        new AutoClearDeals(),
        new Laundry(),
        new Bank(),
        new Dealers(),
        new Recipes(),
        new Deliveries(),
        new EmployeeFacilities(),
        new FastActions(),
        new MoreGuns(),
        new MoreClothing(),
        new Minimap(),
        new Robbery(),
        new TimeWarp(),
        new CustomerMarkers(),
        new SupplierDebt(),
        new FastDeliveries(),
        new Upgrades(),
        new DealerTexts(),
        new HudInfo(),
    };

    private static MelonPreferences_Entry<bool> _debug = null!;

    public static bool DebugLogging => _debug?.Value ?? true;
    public static Log Log { get; } = new("Core");

    public static void SetDebugLogging(bool on)
    {
        _debug.Value = on;
        MelonPreferences.Save();
    }

    public override void OnInitializeMelon()
    {
        var core = MelonPreferences.CreateCategory("ModSchedule1_Core", "ModSchedule1");
        core.SetFilePath(Feature.ConfigFile, autoload: true, printmsg: false);
        _debug = core.CreateEntry("DebugLogging", true, "Logs detalhados", "Escreve logs [debug] no MelonLoader/Latest.log");


        Log.Info($"ModSchedule1 v{Version} iniciando ({Features.Count} features, debug={DebugLogging})");
        foreach (var f in Features)
        {
            try { f.Init(HarmonyInstance); }
            catch (Exception e) { f.Fault("init", e); }
        }
        try
        {
            HarmonyInstance.CreateClassProcessor(typeof(PhoneApp.HomeScreenStartPatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(ModsSettingsTab.AwakePatch)).Patch();
            Log.Debug($"apps de celular registrados: {PhoneApp.All.Count}");
        }
        catch (Exception e) { Log.Error($"apps de celular desativados, patch da HomeScreen falhou: {e}"); }

        WriteSession("running");
        MelonPreferences.Save();   // grava o cfg com todos os valores padrão para o launcher e para edição manual
    }

    // O launcher lê isto quando o jogo fecha: "running" = não passou por OnApplicationQuit (crash/kill).
    public override void OnApplicationQuit() => WriteSession("exit");

    private static void WriteSession(string state)
    {
        try
        {
            var dir = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "ModSchedule1");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "session.txt"), $"{state} {Version} {DateTime.Now:O}");
        }
        catch (Exception e) { Log.Warn($"session.txt: {e.Message}"); }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        CustomItems.OnSceneLoaded();
        Log.Debug($"cena carregada: {sceneName} (host={SafeIsHost()})");
        foreach (var f in Features)
        {
            if (f.Faulted) continue;
            try { f.OnSceneLoaded(sceneName); }
            catch (Exception e) { f.Fault($"OnSceneLoaded({sceneName})", e); }
        }
    }

    public override void OnUpdate()
    {
        EndOfDay.Restore();   // rede de segurança: o relógio nunca fica preso em 3:59 entre frames
        PhoneApp.TickOpenApps();
        ModsSettingsTab.Tick();
        ConfigSync.Tick();
        foreach (var f in Features)
        {
            if (!f.Active) continue;
            try { f.OnUpdate(); }
            catch (Exception e) { f.Fault("OnUpdate", e); }
        }
    }

    private static string SafeIsHost()
    {
        try { return Net.IsHost.ToString(); } catch { return "?"; }
    }
}
