using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ModSchedule1.Launcher
{
    [DataContract]
    public class State
    {
        [DataMember(Name = "gameDir")] public string GameDir;
        [DataMember(Name = "flavor")] public string Flavor;
        [DataMember(Name = "sha")] public string Sha;
    }

    [DataContract]
    public class Manifest
    {
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "features")] public List<FeatureInfo> Features;
    }

    [DataContract]
    public class FeatureInfo
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "description")] public string Description;
        [DataMember(Name = "default")] public bool Default;
    }

    public static class Json
    {
        public static T Read<T>(string path) where T : class, new()
        {
            try { return File.Exists(path) ? Parse<T>(File.ReadAllText(path)) ?? new T() : new T(); }
            catch { return new T(); }
        }

        public static T Parse<T>(string text) where T : class
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
        }

        public static void Write<T>(string path, T value)
        {
            using var ms = new MemoryStream();
            new DataContractJsonSerializer(typeof(T)).WriteObject(ms, value);
            File.WriteAllBytes(path, ms.ToArray());
        }
    }

    /// <summary>
    /// Tudo que o launcher faz fora da tela: achar o jogo, baixar do GitHub, instalar o MelonLoader escondido,
    /// manter só o nosso mod na pasta Mods e fazer backup (nunca apaga nada do jogador).
    /// </summary>
    public class Engine
    {
        public const string Repo = "GabrielFernandes05/ModSchedule1";
        public const string Branch = "master";
        public const string MelonVersion = "0.7.3";
        public const string SteamAppId = "3164500";
        public const string GameExe = "Schedule I.exe";
        public const string GameProcess = "Schedule I";
        public const string ModDll = "ModSchedule1.dll";

        public static readonly string Root = Environment.GetEnvironmentVariable("MODSCHEDULE1_ROOT")   // testes
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModSchedule1");
        public static readonly string Cache = Path.Combine(Root, "release");
        static readonly string StatePath = Path.Combine(Root, "state.json");
        public static readonly string LogPath = Path.Combine(Root, "launcher.log");
        public static readonly string CrashDir = Path.Combine(Root, "crash-logs");

        // Tudo que loaders/mods de terceiros deixam na pasta do jogo: vai para backup na instalação limpa.
        static readonly string[] ForeignItems =
        {
            "MelonLoader", "Mods", "Plugins", "UserLibs", "UserData", "BepInEx",
            "version.dll", "winhttp.dll", "dobby.dll", "doorstop_config.ini", ".doorstop_version", "NOTICE.txt",
        };

        public State State;
        public Action<string, LogKind> Log = (_, __) => { };
        string _backupDir;

        public enum LogKind { Info, Ok, Warn, Error, Game }

        public Engine()
        {
            Directory.CreateDirectory(Root);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            State = Json.Read<State>(StatePath);
        }

        public void SaveState() => Json.Write(StatePath, State);

        public void Write(string msg, LogKind kind = LogKind.Info)
        {
            try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}"); } catch { }
            Log(msg, kind);
        }

        // ---------- jogo ----------

        public bool HasGame(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, GameExe));

        public string FindGameDir()
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrEmpty(steam)) return null;
            var libs = new List<string> { steam };
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            foreach (var lib in libs)
            {
                var dir = Path.Combine(lib, "steamapps", "common", "Schedule I");
                if (HasGame(dir)) return dir;
            }
            return null;
        }

        /// <summary>Normal traz GameAssembly.dll; Mono traz Managed\Assembly-CSharp.dll. Se sobrar os dois, vale o mais novo.</summary>
        public string DetectFlavor()
        {
            if (string.IsNullOrEmpty(State.GameDir)) return null;
            var il2cpp = new FileInfo(Path.Combine(State.GameDir, "GameAssembly.dll"));
            var mono = new FileInfo(Path.Combine(State.GameDir, "Schedule I_Data", "Managed", "Assembly-CSharp.dll"));
            if (il2cpp.Exists && mono.Exists) return mono.LastWriteTime > il2cpp.LastWriteTime ? "mono" : "il2cpp";
            if (il2cpp.Exists) return "il2cpp";
            if (mono.Exists) return "mono";
            return null;
        }

        public static bool GameRunning() => Process.GetProcessesByName(GameProcess).Length > 0;

        public static void LaunchGame() => Process.Start("steam://rungameid/" + SteamAppId);

        // ---------- GitHub ----------

        static WebClient Client()
        {
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "ModSchedule1-Launcher";
            return wc;
        }

        public string RemoteSha()
        {
            using var wc = Client();
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github.sha";
            return wc.DownloadString($"https://api.github.com/repos/{Repo}/commits/{Branch}").Trim();
        }

        static string RawUrl(string sha, string relPath) => $"https://raw.githubusercontent.com/{Repo}/{sha}/{relPath}";

        static void Download(string url, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            using (var wc = Client()) wc.DownloadFile(url, dest + ".part");
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(dest + ".part", dest);
        }

        string CacheFile(string relPath) => Path.Combine(Cache, relPath.Replace("release/", "").Replace('/', Path.DirectorySeparatorChar));

        string GetRepoFile(string sha, string relPath)
        {
            var dest = CacheFile(relPath);
            if (File.Exists(dest)) return dest;
            Download(RawUrl(sha, relPath), dest);
            Write($"  baixado: {relPath}");
            return dest;
        }

        /// <summary>Baixa a versão do master. Devolve a versão do launcher publicada (para auto-atualização) ou null.</summary>
        public string UpdateRelease()
        {
            string sha;
            try { sha = RemoteSha(); }
            catch (Exception e)
            {
                Write($"Sem acesso ao GitHub ({e.Message}). Usando a versão já baixada.", LogKind.Warn);
                return null;
            }
            if (sha != State.Sha)
            {
                Write($"Nova versão no GitHub: {sha.Substring(0, 7)}");
                if (Directory.Exists(Cache)) Directory.Delete(Cache, true);   // cache nosso, sempre rebaixável
                State.Sha = sha;
            }
            else Write($"ModSchedule1 já está na última versão ({sha.Substring(0, 7)})", LogKind.Ok);

            GetRepoFile(sha, "release/features.json");
            try { GetRepoFile(sha, $"release/{State.Flavor}/{ModDll}"); }
            catch { Write($"Ainda não há {ModDll} publicado para a versão '{State.Flavor}'.", LogKind.Warn); }
            SaveState();
            try { return File.ReadAllText(GetRepoFile(sha, "release/launcher/version.txt")).Trim(); }
            catch { return null; }
        }

        /// <summary>
        /// Troca o .exe em execução pelo novo: o Windows deixa renomear um exe aberto, então o atual vira .old,
        /// o novo entra no lugar e é reaberto. O .old é apagado na próxima execução.
        /// </summary>
        public bool SelfUpdate(string remoteVersion)
        {
            var current = typeof(Engine).Assembly.GetName().Version;
            if (!Version.TryParse(remoteVersion, out var remote) || remote <= current) return false;
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            try
            {
                Write($"Atualizando o launcher {current} -> {remote}...");
                Download(RawUrl(State.Sha, "release/launcher/ModSchedule1.exe"), exe + ".new");
                if (File.Exists(exe + ".old")) File.Delete(exe + ".old");
                File.Move(exe, exe + ".old");
                File.Move(exe + ".new", exe);
                Process.Start(exe, "--updated");
                return true;
            }
            catch (Exception e)
            {
                Write($"Não deu para atualizar o launcher ({e.Message}). Continuando com o atual.", LogKind.Warn);
                return false;
            }
        }

        // ---------- backup ----------

        void Backup(string path)
        {
            _backupDir ??= Path.Combine(State.GameDir, "_ModSchedule1_backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var rel = path.Substring(State.GameDir.Length).TrimStart('\\', '/');
            var dest = Path.Combine(_backupDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            if (Directory.Exists(path)) Directory.Move(path, dest);
            else File.Move(path, dest);
            Write($"  backup: {rel}");
        }

        // ---------- MelonLoader ----------

        string MelonMarker => Path.Combine(State.GameDir, "MelonLoader", "modschedule1-melon.txt");

        public bool MelonInstalled => File.Exists(MelonMarker);

        bool MelonUpToDate =>
            File.Exists(MelonMarker) && File.ReadAllText(MelonMarker).Trim() == MelonVersion &&
            File.Exists(Path.Combine(State.GameDir, "version.dll"));

        void InstallMelon()
        {
            var zip = Path.Combine(Root, $"MelonLoader-{MelonVersion}.x64.zip");
            if (!File.Exists(zip))
            {
                Write($"Baixando MelonLoader {MelonVersion}...");
                Download($"https://github.com/LavaGang/MelonLoader/releases/download/v{MelonVersion}/MelonLoader.x64.zip", zip);
            }
            var old = Path.Combine(State.GameDir, "MelonLoader");
            if (Directory.Exists(old)) Backup(old);   // não mistura arquivos de versões diferentes do loader
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries)
                {
                    var dest = Path.GetFullPath(Path.Combine(State.GameDir, entry.FullName));
                    if (!dest.StartsWith(Path.GetFullPath(State.GameDir), StringComparison.OrdinalIgnoreCase)) continue;   // zip slip
                    if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                }
            File.WriteAllText(MelonMarker, MelonVersion);
            WriteLoaderConfig();
            Write($"MelonLoader {MelonVersion} instalado. A 1ª abertura do jogo demora alguns minutos.", LogKind.Ok);
        }

        // Esconde o console e a splash do MelonLoader: para quem joga, o loader fica invisível.
        void WriteLoaderConfig()
        {
            var dir = Path.Combine(State.GameDir, "UserData");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Loader.cfg"),
                "[loader]\ndebug_mode = false\ndisable_start_screen = true\n\n[console]\nhide_console = true\nhide_warnings = false\n\n[logs]\nmax_logs = 10\n");
        }

        // ---------- mods ----------

        void SyncMods()
        {
            foreach (var folder in new[] { "Mods", "Plugins" })
            {
                var dir = Path.Combine(State.GameDir, folder);
                Directory.CreateDirectory(dir);
                foreach (var entry in Directory.GetFileSystemEntries(dir))
                {
                    if (Path.GetFileName(entry).Equals(ModDll, StringComparison.OrdinalIgnoreCase)) continue;
                    Write($"Mod de fora do ModSchedule1 encontrado: {Path.GetFileName(entry)}", LogKind.Warn);
                    Backup(entry);
                }
            }
            var src = CacheFile($"release/{State.Flavor}/{ModDll}");
            var dst = Path.Combine(State.GameDir, "Mods", ModDll);
            if (!File.Exists(src))
            {
                Write($"{ModDll} indisponível: o jogo abre só com o MelonLoader.", LogKind.Warn);
                return;
            }
            if (!File.Exists(dst) || !File.ReadAllBytes(src).SequenceEqual(File.ReadAllBytes(dst)))
            {
                File.Copy(src, dst, true);
                Write($"{ModDll} atualizado na pasta Mods", LogKind.Ok);
            }
        }

        public void CleanInstall()
        {
            _backupDir = null;
            Write("Instalação limpa: movendo loaders e mods existentes para backup...");
            foreach (var name in ForeignItems)
            {
                var p = Path.Combine(State.GameDir, name);
                if (File.Exists(p) || Directory.Exists(p)) Backup(p);
            }
            InstallMelon();
            SyncMods();
            if (_backupDir != null) Write($"Backup em: {_backupDir}");
        }

        /// <summary>Instala/atualiza o necessário. Primeira vez (sem marcador nosso) = instalação limpa.</summary>
        public void Install(bool clean)
        {
            _backupDir = null;
            if (clean || !MelonInstalled) { CleanInstall(); return; }
            if (!MelonUpToDate) InstallMelon();
            SyncMods();
        }

        // ---------- leitura de estado do jogo ----------

        public Manifest LoadManifest()
        {
            var m = Json.Read<Manifest>(Path.Combine(Cache, "features.json"));
            m.Features ??= new List<FeatureInfo>();
            return m;
        }

        /// <summary>Enabled de cada feature no cfg do MelonPreferences (UserData\ModSchedule1.cfg, TOML).</summary>
        public Dictionary<string, bool> FeatureStates()
        {
            var states = new Dictionary<string, bool>();
            var cfg = Path.Combine(State.GameDir ?? "", "UserData", "ModSchedule1.cfg");
            if (!File.Exists(cfg)) return states;
            string section = null;
            foreach (var line in File.ReadAllLines(cfg))
            {
                var s = Regex.Match(line, @"^\s*\[(.+)\]\s*$");
                if (s.Success) { section = s.Groups[1].Value.Replace("ModSchedule1_", ""); continue; }
                var e = Regex.Match(line, @"^\s*Enabled\s*=\s*(true|false)");
                if (section != null && e.Success) states[section] = e.Groups[1].Value == "true";
            }
            return states;
        }

        public string GameLog => Path.Combine(State.GameDir ?? "", "MelonLoader", "Latest.log");
        public string ConfigFile => Path.Combine(State.GameDir ?? "", "UserData", "ModSchedule1.cfg");

        /// <summary>"running" quando o jogo não passou por OnApplicationQuit (crash ou processo morto).</summary>
        public bool LastSessionCrashed()
        {
            var f = Path.Combine(State.GameDir ?? "", "UserData", "ModSchedule1", "session.txt");
            return File.Exists(f) && File.ReadAllText(f).StartsWith("running");
        }

        public string SaveCrashLog()
        {
            if (!File.Exists(GameLog)) return null;
            Directory.CreateDirectory(CrashDir);
            var dest = Path.Combine(CrashDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            using (var src = new FileStream(GameLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = File.Create(dest)) src.CopyTo(dst);
            return dest;
        }
    }
}
