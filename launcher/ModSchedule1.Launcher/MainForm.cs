using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModSchedule1.Launcher
{
    public class MainForm : Form
    {
        readonly Engine _engine = new Engine();

        // cabeçalho
        Label _subtitle;
        Pill _status;
        // card Jogo
        Label _gamePath, _gameVersion;
        // card Mods
        ListBox _mods;
        Manifest _manifest = new Manifest { Features = new List<FeatureInfo>() };
        Dictionary<string, bool> _enabled = new Dictionary<string, bool>();
        // atividade + rodapé
        RichTextBox _log;
        Label _hint;
        Button _play, _clean;
        LinkLabel _crashLink;

        readonly Timer _monitor = new Timer { Interval = 1500 };
        bool _busy, _ready, _wasRunning, _launching;
        DateTime _launchedAt;
        long _logPos;
        int _gameErrors;
        string _lastCrashLog;

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "ModSchedule1";
            ClientSize = new Size(980, 600);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Font(9.5f);
            using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico"))
                if (s != null) Icon = new Icon(s);

            BuildHeader();
            BuildGameCard();
            BuildToolsCard();
            BuildModsCard();
            BuildActivityCard();
            BuildFooter();
            ResumeLayout(false);

            _engine.Log = (msg, kind) => AppendLog(msg, kind);
            _monitor.Tick += (_, __) => MonitorGame();
            Shown += async (_, __) => { _monitor.Start(); await Sync(false); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        // ---------- layout ----------

        void BuildHeader()
        {
            var icon = new PictureBox { Location = new Point(24, 18), Size = new Size(52, 52), SizeMode = PictureBoxSizeMode.Zoom };
            using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico"))
                if (s != null) icon.Image = new Icon(s, 256, 256).ToBitmap();
            Controls.Add(icon);
            Controls.Add(new Label { Text = "ModSchedule1", Font = Theme.Semibold(20f), ForeColor = Theme.Text, Location = new Point(86, 14), AutoSize = true });
            _subtitle = new Label { Font = Theme.Font(9f), ForeColor = Theme.Muted, Location = new Point(89, 52), Size = new Size(620, 20) };
            Controls.Add(_subtitle);
            _status = new Pill { Location = new Point(716, 28), Size = new Size(240, 34) };
            Controls.Add(_status);
            UpdateSubtitle();
        }

        void BuildGameCard()
        {
            var card = new CardPanel("Jogo") { Location = new Point(24, 92), Size = new Size(300, 200) };
            card.Controls.Add(new Label { Text = "PASTA", Font = Theme.Font(8f, FontStyle.Bold), ForeColor = Theme.Muted, Location = new Point(16, 42), AutoSize = true });
            _gamePath = new Label { ForeColor = Theme.Text, Location = new Point(16, 60), Size = new Size(268, 36), AutoEllipsis = true };
            card.Controls.Add(_gamePath);
            card.Controls.Add(new Label { Text = "VERSÃO", Font = Theme.Font(8f, FontStyle.Bold), ForeColor = Theme.Muted, Location = new Point(16, 100), AutoSize = true });
            _gameVersion = new Label { Location = new Point(16, 118), Size = new Size(268, 20), Font = Theme.Semibold(9.5f) };
            card.Controls.Add(_gameVersion);

            var change = Theme.Btn("Alterar pasta", Theme.Button, Theme.ButtonHover);
            change.SetBounds(16, 150, 130, 34);
            change.Click += async (_, __) => await ChangeFolder();
            var recheck = Theme.Btn("Verificar de novo", Theme.Button, Theme.ButtonHover);
            recheck.SetBounds(154, 150, 130, 34);
            recheck.Click += async (_, __) => await Sync(false);
            card.Controls.Add(change);
            card.Controls.Add(recheck);
            Controls.Add(card);
        }

        void BuildToolsCard()
        {
            var card = new CardPanel("Ferramentas") { Location = new Point(24, 304), Size = new Size(300, 196) };
            Button Tool(string text, int y, Action action)
            {
                var b = Theme.Btn(text, Theme.Button, Theme.ButtonHover);
                b.SetBounds(16, y, 268, 32);
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.Padding = new Padding(8, 0, 0, 0);
                b.Click += (_, __) => action();
                card.Controls.Add(b);
                return b;
            }
            _clean = Tool("Instalação limpa", 42, async () => await CleanInstall());
            Tool("Abrir log do jogo", 80, () => OpenFile(_engine.GameLog, "Ainda não há log do jogo (abra o jogo uma vez)."));
            Tool("Abrir configurações (.cfg)", 118, () => OpenFile(_engine.ConfigFile, "A config é criada na 1ª abertura do jogo com o mod."));
            Tool("Abrir pasta do jogo", 156, () => { if (Directory.Exists(_engine.State.GameDir)) Process.Start("explorer.exe", _engine.State.GameDir); });
            Controls.Add(card);
        }

        void BuildModsCard()
        {
            var card = new CardPanel("Mods incluídos") { Location = new Point(340, 92), Size = new Size(616, 262) };
            _mods = new ListBox
            {
                Location = new Point(12, 40),
                Size = new Size(592, 212),
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Card,
                ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Scale(44),
                IntegralHeight = false,
                SelectionMode = SelectionMode.None,
            };
            _mods.DrawItem += DrawMod;
            card.Controls.Add(_mods);
            Controls.Add(card);
        }

        void BuildActivityCard()
        {
            var card = new CardPanel("Atividade") { Location = new Point(340, 366), Size = new Size(616, 134) };
            _log = new RichTextBox
            {
                Location = new Point(16, 40),
                Size = new Size(584, 84),
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Card,
                ForeColor = Theme.Muted,
                ReadOnly = true,
                Font = new Font("Consolas", 8.75f),
                ScrollBars = RichTextBoxScrollBars.Vertical,
                DetectUrls = false,
            };
            card.Controls.Add(_log);
            Controls.Add(card);
        }

        void BuildFooter()
        {
            _hint = new Label { Location = new Point(24, 522), Size = new Size(560, 22), ForeColor = Theme.Muted };
            Controls.Add(_hint);
            _crashLink = new LinkLabel
            {
                Location = new Point(24, 546), Size = new Size(560, 22), Visible = false,
                LinkColor = Theme.Red, ActiveLinkColor = Theme.Text, Text = "Abrir o log do crash",
            };
            _crashLink.LinkClicked += (_, __) => OpenFile(_lastCrashLog, "Log do crash não encontrado.");
            Controls.Add(_crashLink);
            _play = Theme.Btn("JOGAR", Theme.Accent, Theme.AccentHover, Theme.Semibold(15f));
            _play.SetBounds(716, 514, 240, 62);
            _play.FlatAppearance.BorderColor = Theme.AccentHover;
            _play.Click += async (_, __) => await Play();
            Controls.Add(_play);
        }

        int Scale(int px) => (int)Math.Round(px * DeviceDpi / 96f);

        void DrawMod(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _manifest.Features.Count) return;
            var f = _manifest.Features[e.Index];
            bool on = _enabled.TryGetValue(f.Id, out var v) ? v : f.Default;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(e.Index % 2 == 0 ? Theme.Card : Color.FromArgb(255, 0x1A, 0x20, 0x28)))
                g.FillRectangle(bg, e.Bounds);
            int h = e.Bounds.Height;
            using (var dot = new SolidBrush(on ? Theme.Green : Theme.Border))
                g.FillEllipse(dot, e.Bounds.X + Scale(8), e.Bounds.Y + h / 2 - Scale(5), Scale(10), Scale(10));
            using (var name = Theme.Semibold(9.75f))
                TextRenderer.DrawText(g, f.Name, name, new Point(e.Bounds.X + Scale(28), e.Bounds.Y + Scale(4)), on ? Theme.Text : Theme.Muted);
            using (var desc = Theme.Font(8.5f))
                TextRenderer.DrawText(g, f.Description, desc,
                    new Rectangle(e.Bounds.X + Scale(28), e.Bounds.Y + Scale(23), e.Bounds.Width - Scale(130), Scale(18)),
                    Theme.Muted, TextFormatFlags.EndEllipsis);
            using (var st = Theme.Font(8.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, on ? "ATIVO" : "DESATIVADO", st,
                    new Rectangle(e.Bounds.Right - Scale(110), e.Bounds.Y, Scale(100), h),
                    on ? Theme.Green : Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        // ---------- estado da tela ----------

        void UpdateSubtitle()
        {
            var parts = new List<string>();
            parts.Add(string.IsNullOrEmpty(_manifest.Version) ? "Mod: ?" : $"Mod v{_manifest.Version}");
            if (!string.IsNullOrEmpty(_engine.State.Sha)) parts.Add($"commit {_engine.State.Sha.Substring(0, 7)}");
            parts.Add($"MelonLoader {Engine.MelonVersion}");
            parts.Add($"Launcher {typeof(MainForm).Assembly.GetName().Version.ToString(3)}");
            _subtitle.Text = string.Join("   •   ", parts);
        }

        void RefreshGameCard()
        {
            _gamePath.Text = string.IsNullOrEmpty(_engine.State.GameDir) ? "não encontrada" : _engine.State.GameDir;
            switch (_engine.DetectFlavor())
            {
                case "il2cpp": _gameVersion.Text = "Normal (IL2CPP)  ✓"; _gameVersion.ForeColor = Theme.Green; break;
                case "mono": _gameVersion.Text = "Mono (beta alternate)  ✗ não suportada"; _gameVersion.ForeColor = Theme.Red; break;
                default: _gameVersion.Text = "não identificada"; _gameVersion.ForeColor = Theme.Red; break;
            }
        }

        void RefreshMods()
        {
            _manifest = _engine.LoadManifest();
            _enabled = _engine.FeatureStates();
            _mods.BeginUpdate();
            _mods.Items.Clear();
            foreach (var f in _manifest.Features) _mods.Items.Add(f.Name);
            _mods.EndUpdate();
            UpdateSubtitle();
        }

        void SetState(string pill, Color color, string hint, bool canPlay)
        {
            _status.Set(pill, color);
            _hint.Text = hint;
            _play.Enabled = canPlay;
            _play.BackColor = canPlay ? Theme.Accent : Theme.Button;
            _play.ForeColor = canPlay ? Theme.Text : Theme.Muted;
            _clean.Enabled = !_busy && !Engine.GameRunning();
        }

        void AppendLog(string msg, Engine.LogKind kind)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(msg, kind))); return; }
            var color = kind switch
            {
                Engine.LogKind.Ok => Theme.Green,
                Engine.LogKind.Warn => Theme.Amber,
                Engine.LogKind.Error => Theme.Red,
                Engine.LogKind.Game => Theme.Blue,
                _ => Theme.Muted,
            };
            if (_log.Lines.Length > 400) { _log.Select(0, _log.GetFirstCharIndexFromLine(100)); _log.SelectedText = ""; }
            _log.SelectionStart = _log.TextLength;
            _log.SelectionColor = color;
            _log.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\n");
            _log.ScrollToCaret();
        }

        static void OpenFile(string path, string missing)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) Process.Start("notepad.exe", $"\"{path}\"");
            else MessageBox.Show(missing, "ModSchedule1");
        }

        // ---------- ações ----------

        async Task Sync(bool clean)
        {
            if (_busy) return;
            _busy = true;
            _ready = false;
            SetState("Verificando...", Theme.Amber, "Procurando atualizações e conferindo a instalação.", false);
            try
            {
                if (!_engine.HasGame(_engine.State.GameDir))
                {
                    _engine.State.GameDir = _engine.FindGameDir();
                    if (_engine.State.GameDir != null) _engine.Write($"Jogo encontrado pela Steam: {_engine.State.GameDir}");
                    else _engine.State.GameDir = AskGameDir();
                    _engine.SaveState();
                }
                RefreshGameCard();
                if (!_engine.HasGame(_engine.State.GameDir))
                {
                    SetState("Jogo não encontrado", Theme.Red, "Clique em 'Alterar pasta' e escolha a pasta do Schedule I.", false);
                    return;
                }
                if (Engine.GameRunning())
                {
                    _engine.Write("O jogo está aberto: atualizações ficam para quando ele fechar.", Engine.LogKind.Warn);
                    RefreshMods();
                    return;   // o monitor assume o estado "jogo aberto"
                }
                if (!CheckVersion()) return;

                var launcherVersion = await Task.Run(() => _engine.UpdateRelease());
                if (launcherVersion != null && await Task.Run(() => _engine.SelfUpdate(launcherVersion)))
                {
                    Close();   // a versão nova já foi aberta e espera esta fechar
                    return;
                }
                await Task.Run(() => _engine.Install(clean));
                RefreshMods();
                _ready = true;
                _engine.Write("Tudo pronto.", Engine.LogKind.Ok);
                SetState("Pronto para jogar", Theme.Green, "Os mods ficam ativos assim que o jogo abrir. Ajustes: app Mods no celular do jogo.", true);
            }
            catch (Exception e)
            {
                _engine.Write("ERRO: " + e.Message, Engine.LogKind.Error);
                SetState("Erro", Theme.Red, "Algo deu errado. Detalhes em Atividade e em launcher.log.", _engine.MelonInstalled);
            }
            finally
            {
                _busy = false;
                _clean.Enabled = !Engine.GameRunning();
            }
        }

        /// <summary>Só a versão Normal é suportada: bloqueia e explica como trocar se estiver na Mono.</summary>
        bool CheckVersion()
        {
            var flavor = _engine.DetectFlavor();
            _engine.State.Flavor = "il2cpp";
            _engine.SaveState();
            if (flavor == "il2cpp") return true;
            if (flavor == "mono")
            {
                _engine.Write("BLOQUEADO: jogo na versão Mono.", Engine.LogKind.Error);
                SetState("Versão errada do jogo", Theme.Red, "Troque para a versão Normal na Steam e clique em 'Verificar de novo'.", false);
                MessageBox.Show(
                    "O Schedule I está na versão Mono (beta 'alternate'). O ModSchedule1 só funciona na versão Normal.\n\n" +
                    "Na Steam: botão direito em Schedule I > Propriedades > Betas > selecione 'Nenhum'.\n" +
                    "Espere o download terminar e clique em 'Verificar de novo'.",
                    "ModSchedule1 - versão errada do jogo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                _engine.Write("BLOQUEADO: não encontrei os arquivos da versão Normal (GameAssembly.dll).", Engine.LogKind.Error);
                SetState("Jogo incompleto", Theme.Red, "Na Steam: Propriedades > Arquivos instalados > Verificar integridade.", false);
            }
            return false;
        }

        string AskGameDir()
        {
            using var dlg = new FolderBrowserDialog { Description = $"Selecione a pasta do Schedule I (a que contém '{Engine.GameExe}')" };
            while (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (_engine.HasGame(dlg.SelectedPath)) return dlg.SelectedPath;
                MessageBox.Show($"Não encontrei '{Engine.GameExe}' nessa pasta.", "ModSchedule1");
            }
            return null;
        }

        async Task ChangeFolder()
        {
            var dir = AskGameDir();
            if (dir == null) return;
            _engine.State.GameDir = dir;
            _engine.SaveState();
            await Sync(false);
        }

        async Task CleanInstall()
        {
            if (Engine.GameRunning()) { MessageBox.Show("Feche o jogo antes da instalação limpa.", "ModSchedule1"); return; }
            var ok = MessageBox.Show(
                "Todos os loaders, mods e configurações na pasta do jogo serão movidos para _ModSchedule1_backup " +
                "e o ModSchedule1 será reinstalado do zero. Seus saves não são tocados.\n\nContinuar?",
                "Instalação limpa", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok == DialogResult.Yes) await Sync(true);
        }

        async Task Play()
        {
            if (!_ready || _busy) return;
            if (!CheckVersion()) { _ready = false; return; }   // trocou de branch com o launcher aberto
            _engine.Write("Abrindo o jogo pela Steam...");
            Engine.LaunchGame();
            _launching = true;
            _launchedAt = DateTime.Now;
            _crashLink.Visible = false;
            SetState("Abrindo o jogo...", Theme.Blue, "A Steam está iniciando o Schedule I.", false);
            await Task.CompletedTask;
        }

        // ---------- monitor do jogo ----------

        void MonitorGame()
        {
            bool running;
            try { running = Engine.GameRunning(); } catch { return; }

            if (running && !_wasRunning) GameStarted();
            else if (!running && _wasRunning) GameStopped();
            else if (!running && _launching && (DateTime.Now - _launchedAt).TotalSeconds > 90)
            {
                _launching = false;
                _engine.Write("O jogo não abriu em 90 s. Confira a Steam.", Engine.LogKind.Warn);
                SetState("Pronto para jogar", Theme.Green, "O jogo não abriu. Tente de novo.", _ready);
            }
            if (running) TailGameLog();
            _wasRunning = running;
        }

        void GameStarted()
        {
            _launching = false;
            _logPos = 0;
            _gameErrors = 0;
            _crashLink.Visible = false;
            _engine.Write("Jogo aberto. Mensagens do ModSchedule1 aparecem aqui.", Engine.LogKind.Game);
            SetState("Jogo em execução", Theme.Blue, "Pode deixar o launcher aberto: ele mostra erros do mod e detecta crash.", false);
        }

        async void GameStopped()
        {
            TailGameLog();
            if (_engine.LastSessionCrashed())
            {
                _lastCrashLog = _engine.SaveCrashLog();
                _engine.Write("O jogo fechou de forma inesperada (crash). Log salvo em " + _lastCrashLog, Engine.LogKind.Error);
                _crashLink.Text = "O jogo crashou. Clique para abrir o log (mande ele no chat de desenvolvimento).";
                _crashLink.Visible = true;
            }
            else _engine.Write("Jogo fechado.", Engine.LogKind.Ok);
            await Sync(false);   // aproveita para buscar atualizações para a próxima sessão
            if (_crashLink.Visible) _status.Set("Último jogo crashou", Theme.Red);
        }

        /// <summary>Lê o que o MelonLoader escreveu desde a última vez e mostra só as linhas do ModSchedule1 (sem [debug]).</summary>
        void TailGameLog()
        {
            try
            {
                var path = _engine.GameLog;
                if (!File.Exists(path)) return;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length < _logPos) _logPos = 0;   // log novo
                if (fs.Length == _logPos) return;
                fs.Seek(_logPos, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                var text = reader.ReadToEnd();
                int lastNewline = text.LastIndexOf('\n');
                if (lastNewline < 0) return;   // espera a linha terminar
                _logPos += Encoding.UTF8.GetByteCount(text.Substring(0, lastNewline + 1));
                foreach (var raw in text.Substring(0, lastNewline).Split('\n'))
                {
                    var line = raw.TrimEnd('\r');
                    if (!line.Contains("ModSchedule1") || line.Contains("[debug]")) continue;
                    bool error = line.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.Contains("Exception") || line.Contains("desativada até");
                    if (error) _gameErrors++;
                    var shown = line.Length > 15 && line[0] == '[' ? line.Substring(line.IndexOf(']') + 1).Trim() : line;
                    AppendLog(shown, error ? Engine.LogKind.Error : Engine.LogKind.Game);
                }
                if (_gameErrors > 0) _status.Set($"Jogo aberto • {_gameErrors} erro(s)", Theme.Amber);
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _monitor.Stop();
            base.OnFormClosing(e);
        }
    }
}
