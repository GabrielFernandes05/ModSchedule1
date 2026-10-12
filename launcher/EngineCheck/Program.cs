using System;
using System.IO;
using System.Linq;
using ModSchedule1.Launcher;

static class Check
{
    static int failures;
    static void Assert(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

    static int Main()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ms1-check-" + Guid.NewGuid().ToString("N")[..8]);
        var root = Path.Combine(tmp, "root");
        var game = Path.Combine(tmp, "Schedule I");
        Environment.SetEnvironmentVariable("MODSCHEDULE1_ROOT", root);
        Directory.CreateDirectory(Path.Combine(game, "Mods"));
        Directory.CreateDirectory(Path.Combine(game, "UserData"));
        File.WriteAllText(Path.Combine(game, "Schedule I.exe"), "");
        File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "");
        File.WriteAllText(Path.Combine(game, "version.dll"), "old loader");
        File.WriteAllText(Path.Combine(game, "Mods", "OtherMod.dll"), "foreign");
        File.WriteAllText(Path.Combine(game, "UserData", "MelonPreferences.cfg"), "old prefs");

        var e = new Engine();
        e.Log = (m, k) => Console.WriteLine("    [" + k + "] " + m);
        e.State.GameDir = game;
        e.State.Flavor = "il2cpp";
        Assert(e.DetectFlavor() == "il2cpp", "detecta versão Normal");

        // DLL e manifest "baixados" no cache (sem rede para o GitHub)
        Directory.CreateDirectory(Path.Combine(Engine.Cache, "il2cpp"));
        File.WriteAllText(Path.Combine(Engine.Cache, "il2cpp", Engine.ModDll), "our mod v1");
        File.WriteAllText(Path.Combine(Engine.Cache, "features.json"),
            "{\"version\":\"9.9.9\",\"features\":[{\"id\":\"A\",\"name\":\"Alpha\",\"description\":\"d\",\"default\":true}]}");

        Console.WriteLine("1ª instalação (limpa):");
        e.Install(false);
        var backups = Directory.GetDirectories(Path.Combine(game, "_ModSchedule1_backup"));
        Assert(backups.Length == 1, "um backup criado");
        var b = backups[0];
        Assert(File.ReadAllText(Path.Combine(b, "version.dll")) == "old loader", "loader antigo no backup");
        Assert(File.Exists(Path.Combine(b, "Mods", "OtherMod.dll")), "mod de terceiro no backup (não apagado)");
        Assert(File.ReadAllText(Path.Combine(b, "UserData", "MelonPreferences.cfg")) == "old prefs", "config antiga no backup");
        Assert(File.Exists(Path.Combine(game, "version.dll")) && File.ReadAllText(Path.Combine(game, "version.dll")) != "old loader", "version.dll do MelonLoader novo");
        Assert(File.ReadAllText(Path.Combine(game, "MelonLoader", "modschedule1-melon.txt")) == Engine.MelonVersion, "marcador do MelonLoader");
        Assert(File.ReadAllText(Path.Combine(game, "UserData", "Loader.cfg")).Contains("hide_console = true"), "Loader.cfg escondendo console");
        Assert(File.ReadAllText(Path.Combine(game, "Mods", Engine.ModDll)) == "our mod v1", "nosso DLL instalado");
        Assert(Directory.GetFiles(Path.Combine(game, "Mods")).Length == 1, "só nosso DLL em Mods");

        Console.WriteLine("2ª execução (nada muda):");
        e.Install(false);
        Assert(Directory.GetDirectories(Path.Combine(game, "_ModSchedule1_backup")).Length == 1, "sem backup novo");

        Console.WriteLine("mod novo de terceiro + DLL atualizado:");
        File.WriteAllText(Path.Combine(game, "Mods", "Another.dll"), "x");
        File.WriteAllText(Path.Combine(Engine.Cache, "il2cpp", Engine.ModDll), "our mod v2");
        System.Threading.Thread.Sleep(1100);   // backup usa timestamp em segundos
        e.Install(false);
        Assert(!File.Exists(Path.Combine(game, "Mods", "Another.dll")), "terceiro removido de Mods");
        Assert(Directory.GetFiles(Path.Combine(game, "_ModSchedule1_backup"), "Another.dll", SearchOption.AllDirectories).Length == 1, "...e guardado no backup");
        Assert(File.ReadAllText(Path.Combine(game, "Mods", Engine.ModDll)) == "our mod v2", "DLL atualizado");

        Console.WriteLine("leitura de estado:");
        File.WriteAllText(Path.Combine(game, "UserData", "ModSchedule1.cfg"), "[ModSchedule1_A]\nEnabled = false\n\n[ModSchedule1_B]\n# x\nEnabled = true\n");
        var st = e.FeatureStates();
        Assert(st["A"] == false && st["B"] == true, "Enabled lido do cfg");
        Assert(e.LoadManifest().Features.Single().Name == "Alpha" && e.LoadManifest().Version == "9.9.9", "features.json lido");
        Directory.CreateDirectory(Path.Combine(game, "UserData", "ModSchedule1"));
        File.WriteAllText(Path.Combine(game, "UserData", "ModSchedule1", "session.txt"), "running 0.6.0 x");
        Assert(e.LastSessionCrashed(), "sessão 'running' = crash");
        File.WriteAllText(Path.Combine(game, "UserData", "ModSchedule1", "session.txt"), "exit 0.6.0 x");
        Assert(!e.LastSessionCrashed(), "sessão 'exit' = fechou normal");
        Directory.CreateDirectory(Path.Combine(game, "Schedule I_Data", "Managed"));
        File.WriteAllText(Path.Combine(game, "Schedule I_Data", "Managed", "Assembly-CSharp.dll"), "");
        File.SetLastWriteTime(Path.Combine(game, "Schedule I_Data", "Managed", "Assembly-CSharp.dll"), DateTime.Now.AddHours(1));
        Assert(e.DetectFlavor() == "mono", "detecta Mono quando os arquivos Mono são os mais novos");

        Directory.Delete(tmp, true);

        Console.WriteLine(failures == 0 ? "TUDO OK" : $"{failures} FALHA(S)");
        return failures == 0 ? 0 : 1;
    }
}
