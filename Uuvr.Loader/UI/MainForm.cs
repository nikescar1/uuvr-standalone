using System.Diagnostics;
using Uuvr.Loader.Core;

namespace Uuvr.Loader.UI;

public class MainForm : Form
{
    private class GameEntry
    {
        public UnityGameInfo Info = null!;
        public string DisplayName = "";
        public string Source = "";
    }

    private readonly List<GameEntry> _games = new();
    private readonly LoaderSettings _settings = LoaderSettings.Load();

    private ListView _gameList = null!;
    private Panel _detailsPanel = null!;
    private Label _detailName = null!;
    private Label _detailPath = null!;
    private Label _detailUnity = null!;
    private Label _detailBackend = null!;
    private Label _detailStatus = null!;
    private ComboBox _generationCombo = null!;
    private Button _installButton = null!;
    private Button _toggleVrButton = null!;
    private Button _uninstallButton = null!;
    private Button _launchButton = null!;
    private RichTextBox _logBox = null!;

    private GameEntry? SelectedGame =>
        _gameList.SelectedIndices.Count > 0 && _gameList.SelectedIndices[0] < _games.Count
            ? _games[_gameList.SelectedIndices[0]]
            : null;

    private SplitContainer _split = null!;

    // Width the details panel is designed for; the splitter keeps it fixed.
    private const int DetailsPanelWidth = 370;

    public MainForm()
    {
        Text = $"UUVR — Universal Unity VR {LoaderVersion.Value}";
        // Scale the fixed-pixel layout with the monitor's DPI, so nothing clips at 125%/150%.
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(980, 640);
        Size = new Size(1100, 700);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.BaseFont;
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        BuildLayout();

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        Shown += async (_, _) =>
        {
            PlaceSplitter();
            await RefreshGamesAsync();
        };
    }

    // Positioning the splitter only once the form has its real size avoids the clamping and
    // drift that happens when SplitterDistance is set during construction.
    private void PlaceSplitter()
    {
        try
        {
            var available = _split.ClientSize.Width - _split.SplitterWidth;

            // Needs room for at least one pixel either side of the splitter, or there is no
            // legal distance to assign at all.
            if (available < 2) return;

            // Clamp into the range the control will accept, so this can't throw the way
            // the minimum-size properties did.
            var desired = available - DetailsPanelWidth;
            _split.SplitterDistance = Math.Max(1, Math.Min(desired, available - 1));
        }
        catch (Exception)
        {
            // Window too small to honour the requested split; whatever position the
            // SplitContainer already has is fine.
        }

        FitGameListColumns();
    }

    private bool _fittingColumns;

    // The first column absorbs all remaining width, so the header never shows an
    // unpainted strip past the last column.
    private void FitGameListColumns()
    {
        // Changing a column width can toggle the list's scrollbar, which resizes the list,
        // which lands back here. Without this guard that recurses until the stack dies.
        if (_fittingColumns) return;
        if (_gameList.Columns.Count == 0) return;

        _fittingColumns = true;
        try
        {
            var otherColumnsWidth = 0;
            for (var index = 1; index < _gameList.Columns.Count; index++)
            {
                otherColumnsWidth += _gameList.Columns[index].Width;
            }

            var width = _gameList.ClientSize.Width - otherColumnsWidth - SystemInformation.VerticalScrollBarWidth - 4;
            _gameList.Columns[0].Width = Math.Max(160, width);
        }
        catch (Exception)
        {
        }
        finally
        {
            _fittingColumns = false;
        }
    }

    private void BuildLayout()
    {
        // Bottom log.
        _logBox = new RichTextBox
        {
            Dock = DockStyle.Bottom,
            Height = 150,
            ReadOnly = true,
            BackColor = Color.FromArgb(18, 18, 22),
            ForeColor = Theme.TextDim,
            Font = Theme.MonoFont,
            BorderStyle = BorderStyle.None,
        };
        Controls.Add(_logBox);

        // Header bar.
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Panel };
        var title = Theme.MakeLabel("UUVR", font: Theme.TitleFont);
        title.Location = new Point(18, 10);
        var subtitle = Theme.MakeLabel("Standalone VR injector for Unity games — pick a game, install, play.", Theme.TextDim);
        subtitle.Location = new Point(20, 38);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var addGameButton = Theme.MakeButton("Add game…");
        addGameButton.Width = 110;
        addGameButton.Click += (_, _) => AddGameViaDialog();

        var rescanButton = Theme.MakeButton("Rescan Steam");
        rescanButton.Width = 120;
        rescanButton.Click += async (_, _) => await RefreshGamesAsync();

        var headerButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            Width = 260,
            Padding = new Padding(0, 15, 12, 0),
            BackColor = Color.Transparent,
        };
        headerButtons.Controls.Add(addGameButton);
        headerButtons.Controls.Add(rescanButton);
        header.Controls.Add(headerButtons);
        Controls.Add(header);
        header.BringToFront();

        // Main split: game list on the left, details on the right. FixedPanel.Panel2 keeps the
        // details panel a constant width while the game list absorbs window resizing.
        //
        // Panel1MinSize/Panel2MinSize are deliberately left alone. A fresh SplitContainer is
        // 150px wide, and assigning a minimum wider than that makes WinForms push
        // SplitterDistance out of its own legal range and throw, which crashed the loader on
        // launch. The splitter position is managed in PlaceSplitter instead.
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2,
        };
        var split = _split;
        Controls.Add(split);
        split.BringToFront();

        _gameList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            OwnerDraw = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        _gameList.Columns.Add("Game", 240);
        _gameList.Columns.Add("Unity", 90);
        _gameList.Columns.Add("Backend", 80);
        _gameList.Columns.Add("Status", 120);
        _gameList.DrawColumnHeader += (_, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(Theme.PanelLight), e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Theme.HeaderFont,
                new Point(e.Bounds.X + 6, e.Bounds.Y + 4), Theme.TextDim);
        };
        _gameList.DrawItem += (_, e) => e.DrawDefault = true;
        _gameList.DrawSubItem += (_, e) => e.DrawDefault = true;
        _gameList.SelectedIndexChanged += (_, _) => UpdateDetails();
        _gameList.DoubleClick += (_, _) => LaunchSelectedGame();
        _gameList.Resize += (_, _) => FitGameListColumns();
        split.Panel1.Controls.Add(_gameList);

        // Details panel.
        _detailsPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(16) };
        split.Panel2.Controls.Add(_detailsPanel);

        // Fixed single-line sizes with ellipsis, so long names and paths can't spill
        // over the controls positioned below them.
        _detailName = Theme.MakeLabel("Select a game", font: Theme.TitleFont);
        _detailName.Location = new Point(16, 14);
        _detailName.AutoSize = false;
        _detailName.Size = new Size(DetailsPanelWidth - 40, 30);
        _detailName.AutoEllipsis = true;
        _detailPath = Theme.MakeLabel("", Theme.TextDim);
        _detailPath.Location = new Point(17, 46);
        _detailPath.AutoSize = false;
        _detailPath.Size = new Size(DetailsPanelWidth - 40, 34);
        _detailPath.AutoEllipsis = true;
        _detailUnity = Theme.MakeLabel("");
        _detailUnity.Location = new Point(17, 84);
        _detailBackend = Theme.MakeLabel("");
        _detailBackend.Location = new Point(17, 108);
        _detailStatus = Theme.MakeLabel("");
        _detailStatus.Location = new Point(17, 132);

        var generationLabel = Theme.MakeLabel("Unity generation:", Theme.TextDim);
        generationLabel.Location = new Point(17, 166);

        _generationCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.PanelLight,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Width = 300,
            Location = new Point(17, 188),
        };
        _generationCombo.Items.AddRange(new object[]
        {
            "Auto (recommended)",
            "Force legacy (Unity 2019 or older)",
            "Force modern (Unity 2020 or newer)",
        });
        _generationCombo.SelectedIndex = 0;

        _installButton = Theme.MakeButton("Install VR mod", Theme.Accent);
        _installButton.Location = new Point(17, 232);
        _installButton.Width = 300;
        _installButton.Height = 40;
        _installButton.Click += async (_, _) => await InstallSelectedGameAsync();

        _toggleVrButton = Theme.MakeButton("Disable VR (play flat)");
        _toggleVrButton.Location = new Point(17, 280);
        _toggleVrButton.Width = 300;
        _toggleVrButton.Click += (_, _) => ToggleSelectedGameVr();

        _uninstallButton = Theme.MakeButton("Uninstall");
        _uninstallButton.Location = new Point(17, 322);
        _uninstallButton.Width = 300;
        _uninstallButton.Click += (_, _) => UninstallSelectedGame();

        _launchButton = Theme.MakeButton("Launch game");
        _launchButton.Location = new Point(17, 364);
        _launchButton.Width = 300;
        _launchButton.Click += (_, _) => LaunchSelectedGame();

        var openFolderButton = Theme.MakeButton("Open game folder");
        openFolderButton.Location = new Point(17, 406);
        openFolderButton.Width = 300;
        openFolderButton.Click += (_, _) => OpenSelectedGameFolder("");

        var openConfigButton = Theme.MakeButton("Open BepInEx folder (config + logs)");
        openConfigButton.Location = new Point(17, 448);
        openConfigButton.Width = 300;
        openConfigButton.Click += (_, _) => OpenSelectedGameFolder("BepInEx");

        var helpLabel = Theme.MakeLabel(
            "In game: F2 opens the UUVR menu, F3 toggles VR, F4 recenters.\n" +
            "You can also drag and drop a game's .exe onto this window.",
            Theme.TextDim);
        helpLabel.Location = new Point(17, 498);

        _detailsPanel.Controls.Add(_detailName);
        _detailsPanel.Controls.Add(_detailPath);
        _detailsPanel.Controls.Add(_detailUnity);
        _detailsPanel.Controls.Add(_detailBackend);
        _detailsPanel.Controls.Add(_detailStatus);
        _detailsPanel.Controls.Add(generationLabel);
        _detailsPanel.Controls.Add(_generationCombo);
        _detailsPanel.Controls.Add(_installButton);
        _detailsPanel.Controls.Add(_toggleVrButton);
        _detailsPanel.Controls.Add(_uninstallButton);
        _detailsPanel.Controls.Add(_launchButton);
        _detailsPanel.Controls.Add(openFolderButton);
        _detailsPanel.Controls.Add(openConfigButton);
        _detailsPanel.Controls.Add(helpLabel);

        UpdateDetails();
        Log($"UUVR Loader {LoaderVersion.Value}. Payload: {(Directory.Exists(ModInstaller.PayloadDir) ? ModInstaller.PayloadDir : "not found (runtimes will be downloaded on demand)")}");
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }

        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private async Task RefreshGamesAsync()
    {
        Log("Scanning for Unity games...");

        var manualPaths = _settings.ManualGamePaths.ToList();
        var entries = await Task.Run(() =>
        {
            var found = new List<GameEntry>();

            foreach (var scanned in GameScanner.ScanSteamGames())
            {
                var info = UnityGameInfo.Detect(scanned.ExePath);
                if (info == null) continue;
                found.Add(new GameEntry { Info = info, DisplayName = scanned.DisplayName, Source = scanned.Source });
            }

            foreach (var manualPath in manualPaths)
            {
                if (found.Any(entry => string.Equals(entry.Info.ExePath, manualPath, StringComparison.OrdinalIgnoreCase))) continue;
                var info = UnityGameInfo.Detect(manualPath);
                if (info == null) continue;
                found.Add(new GameEntry { Info = info, DisplayName = info.Name, Source = "Manual" });
            }

            return found;
        });

        _games.Clear();
        _games.AddRange(entries);
        RebuildGameList();
        Log($"Found {_games.Count} Unity game(s). Steam games are detected automatically; use 'Add game…' for anything else.");
    }

    private void RebuildGameList()
    {
        _gameList.BeginUpdate();
        _gameList.Items.Clear();

        foreach (var entry in _games)
        {
            var manifest = ModInstaller.ReadManifest(entry.Info);
            var item = new ListViewItem(entry.DisplayName);
            item.SubItems.Add(entry.Info.UnityVersion.Length > 0 ? entry.Info.UnityVersion : "?");
            item.SubItems.Add(entry.Info.Backend switch
            {
                UnityBackend.Il2Cpp => "IL2CPP",
                UnityBackend.Mono => "Mono",
                _ => "?",
            });
            item.SubItems.Add(manifest != null ? $"UUVR {manifest.UuvrVersion}" : "");
            if (manifest != null) item.ForeColor = Theme.Success;
            _gameList.Items.Add(item);
        }

        _gameList.EndUpdate();
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        var selected = SelectedGame;

        _installButton.Enabled = selected != null;
        _uninstallButton.Enabled = selected != null && ModInstaller.ReadManifest(selected.Info) != null;
        _launchButton.Enabled = selected != null;

        var vrEnabledState = selected != null ? ModInstaller.GetVrEnabledState(selected.Info) : null;
        _toggleVrButton.Enabled = vrEnabledState != null;
        _toggleVrButton.Text = vrEnabledState == false ? "Enable VR" : "Disable VR (play flat)";

        if (selected == null)
        {
            _detailName.Text = "Select a game";
            _detailPath.Text = "Steam games show up automatically. Use 'Add game…' or drag an .exe here for anything else.";
            _detailUnity.Text = "";
            _detailBackend.Text = "";
            _detailStatus.Text = "";
            return;
        }

        var info = selected.Info;
        var manifest = ModInstaller.ReadManifest(info);

        _detailName.Text = selected.DisplayName;
        _detailPath.Text = info.ExePath;
        _detailUnity.Text = $"Unity {(info.UnityVersion.Length > 0 ? info.UnityVersion : "version unknown")}  ({info.Generation})";
        _detailBackend.Text = $"{(info.Backend == UnityBackend.Il2Cpp ? "IL2CPP" : info.Backend == UnityBackend.Mono ? "Mono" : "Unknown backend")}, {info.Architecture}";

        if (manifest != null)
        {
            var vrState = ModInstaller.GetVrEnabledState(info);
            var stateSuffix = vrState == false ? ", VR disabled — launches flat" : "";
            _detailStatus.Text = $"UUVR {manifest.UuvrVersion} installed ({manifest.Flavor}{stateSuffix})";
            _detailStatus.ForeColor = vrState == false ? Theme.Warning : Theme.Success;
            _installButton.Text = "Reinstall / update VR mod";
        }
        else
        {
            _detailStatus.Text = "UUVR not installed";
            _detailStatus.ForeColor = Theme.TextDim;
            _installButton.Text = "Install VR mod";
        }

        if (ModInstaller.HasForeignBepInEx(info))
        {
            _detailStatus.Text += "  (existing BepInEx found)";
        }
    }

    private UnityGeneration? GenerationOverride => _generationCombo.SelectedIndex switch
    {
        1 => UnityGeneration.Legacy,
        2 => UnityGeneration.Modern,
        _ => null,
    };

    private async Task InstallSelectedGameAsync()
    {
        var selected = SelectedGame;
        if (selected == null) return;

        _installButton.Enabled = false;
        try
        {
            var info = selected.Info;
            var runtimeName = ModInstaller.GetRuntimeName(info.Backend, info.Architecture);

            if (!ModInstaller.HasForeignBepInEx(info) && !RuntimeDownloader.IsRuntimeAvailable(runtimeName))
            {
                var downloaded = await RuntimeDownloader.EnsureRuntimeAsync(runtimeName, Log);
                if (!downloaded) return;
            }

            var generationOverride = GenerationOverride;
            var result = await Task.Run(() => new ModInstaller(Log).Install(info, generationOverride));

            if (result.Success)
            {
                Log("Done. Start SteamVR (or your OpenXR runtime), then launch the game.");
            }
        }
        finally
        {
            _installButton.Enabled = true;
            RebuildGameList();
        }
    }

    private void ToggleSelectedGameVr()
    {
        var selected = SelectedGame;
        if (selected == null) return;

        var currentState = ModInstaller.GetVrEnabledState(selected.Info);
        if (currentState == null) return;

        new ModInstaller(Log).SetVrEnabled(selected.Info, currentState == false);
        UpdateDetails();
    }

    private void UninstallSelectedGame()
    {
        var selected = SelectedGame;
        if (selected == null) return;

        new ModInstaller(Log).Uninstall(selected.Info);
        RebuildGameList();
    }

    private void LaunchSelectedGame()
    {
        var selected = SelectedGame;
        if (selected == null) return;

        try
        {
            Log($"Launching {selected.DisplayName}...");
            Process.Start(new ProcessStartInfo
            {
                FileName = selected.Info.ExePath,
                WorkingDirectory = selected.Info.GameDir,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            Log($"Failed to launch: {exception.Message}");
        }
    }

    private void OpenSelectedGameFolder(string subFolder)
    {
        var selected = SelectedGame;
        if (selected == null) return;

        var path = Path.Combine(selected.Info.GameDir, subFolder);
        if (!Directory.Exists(path))
        {
            Log($"Folder doesn't exist yet: {path}");
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void AddGameViaDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select the game's executable",
            Filter = "Game executable (*.exe)|*.exe",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AddGameFromPath(dialog.FileName);
    }

    private void AddGameFromPath(string exePath)
    {
        var info = UnityGameInfo.Detect(exePath);
        if (info == null)
        {
            Log($"'{Path.GetFileName(exePath)}' doesn't look like a Unity game (no _Data folder next to it).");
            return;
        }

        if (!_settings.ManualGamePaths.Contains(info.ExePath, StringComparer.OrdinalIgnoreCase))
        {
            _settings.ManualGamePaths.Add(info.ExePath);
            _settings.Save();
        }

        if (!_games.Any(entry => string.Equals(entry.Info.ExePath, info.ExePath, StringComparison.OrdinalIgnoreCase)))
        {
            _games.Add(new GameEntry { Info = info, DisplayName = info.Name, Source = "Manual" });
            RebuildGameList();
        }

        // Select the game that was just added.
        var index = _games.FindIndex(entry => string.Equals(entry.Info.ExePath, info.ExePath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _gameList.Items[index].Selected = true;
            _gameList.EnsureVisible(index);
        }

        Log($"Added {info.Name} ({info.Backend}, Unity {(info.UnityVersion.Length > 0 ? info.UnityVersion : "unknown")}).");
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;

        foreach (var path in paths)
        {
            if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                AddGameFromPath(path);
            }
        }
    }
}
