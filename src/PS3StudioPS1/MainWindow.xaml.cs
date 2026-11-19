using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PS2ClassicsPackageStudio;

public partial class MainWindow : Window
{
    private const int MaxDiscs = 5;
    private readonly ObservableCollection<DiscImage> _discImages = [];
    private readonly string _settingsPath;
    private CancellationTokenSource? _buildCancel;
    private CancellationTokenSource? _verificationCancel;
    private bool _closeWhenDone;
    private bool _isBuilding;
    private readonly StringBuilder _pendingLog = new();
    private bool _hasManualOutputName;
    private string? _audioPath;
    private bool _usingBatchFolder;
    private bool _updatingDestination;
    private string? _singleOutputPath;
    private string? _batchOutputFolder;
    private readonly string _artworkTemp = Path.Combine(Path.GetTempPath(), "PS3StudioArtwork-" + Guid.NewGuid().ToString("N"));
    private Dictionary<string, string> _foundArtwork = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _artworkCancel;
    private bool _checkingArtwork;
    private int _profileCheckGeneration;
    private int _protectionCheckGeneration;
    private int _manualCheckGeneration;
    private string? _previewManual;
    private string? _temporaryPreview;
    private void SetArtworkIndicator(string state)
    {
        if (ArtworkCheckDot is null || ArtworkCheckLabel is null) return;
        string color = state == "found" ? "#43D6A0" : state == "not found" ? "#FF676B" : "#8999AE";
        ArtworkCheckDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
        ArtworkCheckLabel.Text = "Artwork: " + state;
    }
    private void UpdateDiscIndicator()
    {
        if (DiscCheckDot is null || DiscCheckLabel is null) return;
        bool has = _discImages.Count > 0;
        bool valid = has && _discImages.All(d => File.Exists(d.Path) && (!IsPs1 || d.FormatLabel != "CUE" || CueValidation.Check(d.Path) is null));
        DiscCheckDot.Fill = (Brush)new BrushConverter().ConvertFromString(!has ? "#8999AE" : valid ? "#43D6A0" : "#FF676B")!;
        DiscCheckLabel.Text = !has ? "Disc: waiting" : valid ? "Disc: files found" : "Disc: missing files";
        UpdateAudioIndicator();
    }
    private void UpdateAudioIndicator()
    {
        if (AudioCheckDot is null || AudioCheckLabel is null) return;
        string status = "waiting";
        if (!IsPs1) status = "PS1 only";
        else if (_discImages.Count > 0)
        {
            var states = _discImages.Select(d => d.FormatLabel == "CUE" ? CueValidation.InspectAudio(d.Path) : CueAudioStatus.Unknown).ToArray();
            status = states.Contains(CueAudioStatus.Missing) ? "missing files" : states.Contains(CueAudioStatus.Unknown) ? "unknown" : states.Contains(CueAudioStatus.Present) ? "files found" : "no tracks";
        }
        string color = status == "files found" ? "#43D6A0" : status == "missing files" ? "#FF676B" : "#8999AE";
        AudioCheckDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
        AudioCheckLabel.Text = "Disc audio: " + status;
    }
    private void ManualPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) StartManualLibraryCheck();
    }
    private void ManualLibraryPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        SaveSettings();
        StartManualLibraryCheck();
    }
    private void BrowseManualLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose your PS2 manual library" };
        if (Directory.Exists(ManualLibraryPathBox.Text)) dialog.InitialDirectory = ManualLibraryPathBox.Text;
        if (dialog.ShowDialog(this) == true) ManualLibraryPathBox.Text = dialog.FolderName;
    }
    private void OnlineManualCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) StartManualLibraryCheck();
    }
    private void DiscManualSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) StartManualLibraryCheck();
    }

    private void SetManualStatus(string status, string color, string detail)
    {
        ManualCheckLabel.Text = "Manual: " + status;
        ManualCheckDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
        ManualHintText.Text = detail;
        PreviewManualButton.IsEnabled = _previewManual != null;
    }

    private static void DeletePreview(string? path)
    {
        if (path == null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async void StartManualLibraryCheck()
    {
        if (!IsLoaded || PreviewManualButton is null) return;
        int generation = ++_manualCheckGeneration;
        _previewManual = null;
        DeletePreview(_temporaryPreview);
        _temporaryPreview = null;
        string? image = (DiscList.SelectedItem as DiscImage)?.Path;
        string selected = ManualPathBox.Text.Trim();
        bool ps1 = IsPs1, online = OnlineManualCheck.IsChecked == true;
        string library = ManualLibraryPathBox.Text.Trim();
        if (image == null && selected.Length == 0)
        {
            SetManualStatus("waiting", "#8999AE", "Select a disc or choose a manual to preview.");
            return;
        }

        SetManualStatus("checking", "#8999AE", "Checking the manual for PC viewing…");
        string? temporary = null;
        try
        {
            await Task.Delay(250);
            if (generation != _manualCheckGeneration) return;
            string? path = await Task.Run(async () =>
            {
                string? found = selected.Length != 0 ? selected
                    : ps1 ? ManualValidation.FindPs1(image!) : Ps2ManualLibrary.Find(image!, library);
                if (found == null && !ps1) found = await OnlinePs2Manual.FindAsync(image!, online);
                if (found == null) return null;
                if (!File.Exists(found)) throw new FileNotFoundException("The selected manual could not be found.");
                string extension = Path.GetExtension(found).ToLowerInvariant();
                if (extension is not (".pdf" or ".zip" or ".cbz")) throw new InvalidDataException("Choose a PDF or a ZIP/CBZ containing page images.");
                if (extension == ".pdf")
                {
                    if (new FileInfo(found).Length > 128L * 1024 * 1024) throw new InvalidDataException("PDF exceeds 128 MiB.");
                    temporary = Path.Combine(Path.GetTempPath(), "PS3Studio-preview-" + Guid.NewGuid().ToString("N") + ".zip");
                    PdfManual.ConvertToArchive(found, temporary);
                    found = temporary;
                }
                if (ManualValidation.Check(found, false) is { } error) throw new InvalidDataException(error);
                return found;
            });
            if (generation != _manualCheckGeneration) return;
            _previewManual = path;
            _temporaryPreview = temporary;
            temporary = null;
            SetManualStatus(path == null ? "not found" : "ready", path == null ? "#FF676B" : "#43D6A0",
                path == null ? "No manual found. You can still create the package." : "Ready to read on this PC.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (generation == _manualCheckGeneration) SetManualStatus("unavailable", "#FF676B", ex.Message);
        }
        finally { DeletePreview(temporary); }
    }

    private void OpenManualPreview(object sender, RoutedEventArgs e)
    {
        if (_previewManual == null) return;
        try { PS4Studio.ManualViewer.Show(this, _previewManual); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StudioDialog.Show(this, ex.Message, "Could not open manual preview");
        }
    }

    private bool IsPs1 => SystemChoice?.SelectedIndex == 0;
    private bool _previousPs1;
    private string _ps1Engine = Ps1Engine.Bundled, _ps2Engine = "";
    private bool MultiDiscMode => BuildModeCombo?.SelectedIndex == 1;
    private bool QueueMode => !MultiDiscMode && _discImages.Count > 1;

    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        Loaded += (_, _) =>
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var brush = (LinearGradientBrush)HeaderFrame.FindResource("HeaderGlowBrush");
            if (brush.RelativeTransform is RotateTransform movement)
            {
                movement.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(8))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                });
            }
        };
        DiscList.ItemsSource = _discImages;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PS2ClassicsPackageStudio", "settings.json");

        var defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PS1 and PS2 Classics Packages");
        OutputPathBox.Text = Path.Combine(defaultDir, "PS2 Game.pkg");
        ManualLibraryPathBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PS2 Manual Library");
        LoadSettings();
        _ps2Engine = EnginePathBox.Text;
        _previousPs1 = false;
        var launchFiles = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToArray();
        if (launchFiles.Length > 0) Loaded += (_, _) => AddFiles(launchFiles);
        UpdateSummary();
        AppendLog(ClassicsBackend.IsToolFolder(EnginePathBox.Text)
            ? "PS2 engine ready. Add a PS2 DVD ISO to begin."
            : "Select your extracted PS2 Classics toolkit or a pop-fe2 command-line engine.");
    }

    private void LoadSettings()
    {
        EnginePathBox.Text = ClassicsBackend.Bundled;
        try
        {
            if (File.Exists(_settingsPath))
            {
                var saved = JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(_settingsPath));
                if (saved?.ManualLibraryPath is { Length: > 0 } manualFolder) ManualLibraryPathBox.Text = manualFolder;
                _ps1Engine = saved?.Ps1EnginePath is { Length: > 0 } savedPs1 && Ps1Engine.IsSupported(savedPs1) ? savedPs1 : Ps1Engine.Bundled;
                if (saved?.EnginePath is { } previous && (ClassicsBackend.IsToolFolder(previous) || File.Exists(previous)))
                {
                    EnginePathBox.Text = previous;
                    return;
                }
            }
            EnginePathBox.Text = ClassicsBackend.Bundled;
        }
        catch (Exception ex)
        {
            AppendLog($"Settings could not be loaded: {ex.Message}");
        }
    }

    private void UseBuiltInEngine_Click(object sender, RoutedEventArgs e)
    {
        EnginePathBox.Text = IsPs1 ? Ps1Engine.Bundled : ClassicsBackend.Bundled;
    }

    private void SaveSettings()
    {
        try
        {
            var folder = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(folder);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new StudioSettings { EnginePath = IsPs1 ? _ps2Engine : EnginePathBox.Text, Ps1EnginePath = IsPs1 ? EnginePathBox.Text : _ps1Engine, ManualLibraryPath = ManualLibraryPathBox.Text }));
        }
        catch (Exception ex)
        {
            AppendLog($"Could not save the engine preference: {ex.Message}");
        }
    }

    private void AddDiscs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose PS1 or PS2 disc images",
            Filter = "Disc images (*.cue;*.bin;*.img;*.ccd;*.iso;*.chd)|*.cue;*.bin;*.img;*.ccd;*.iso;*.chd|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        AddFiles(dialog.FileNames);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        if (_isBuilding) return;
        var selectedPaths=paths.ToArray();
        if (_discImages.Count==0 && selectedPaths.Length>0)
        {
            int? detected=DiscSystemDetector.Detect(selectedPaths[0]);
            if (detected.HasValue && SystemChoice.SelectedIndex!=detected.Value) { SystemChoice.SelectedIndex=detected.Value; AppendLog(detected.Value==0 ? "Detected a PS1 disc; selected the PS1 engine." : "Detected a PS2 disc; selected the PS2 engine."); }
            else if (!detected.HasValue) AppendLog("Disc system could not be identified automatically; check the Game System selection.");
        }
        var kit = !IsPs1 && ClassicsBackend.IsToolFolder(EnginePathBox.Text);
        foreach (var path in selectedPaths)
        {
            if (MultiDiscMode && _discImages.Count >= MaxDiscs)
            {
                StudioDialog.Show(this, "PS3 Classics packages support up to five disc images.", "Disc limit reached", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            }

            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) || _discImages.Any(d => string.Equals(d.Path, fullPath, StringComparison.OrdinalIgnoreCase))) continue;
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            var detected=DiscSystemDetector.Detect(fullPath);
            if (detected.HasValue && (detected.Value==0)!=IsPs1) { AppendLog($"Skipped {Path.GetFileName(fullPath)}: its game system differs from the current queue."); continue; }
            if (IsPs1 && extension == ".bin" && File.Exists(Path.ChangeExtension(fullPath,".cue"))) { fullPath=Path.ChangeExtension(fullPath,".cue"); extension=".cue"; }
            if (_discImages.Any(d => string.Equals(d.Path, fullPath, StringComparison.OrdinalIgnoreCase))) continue;
            if (IsPs1 ? extension is not ".cue" and not ".bin" and not ".img" and not ".ccd" : extension is not ".iso" and not ".cue" and not ".chd")
            {
                AppendLog($"Skipped unsupported disc image: {Path.GetFileName(fullPath)}");
                continue;
            }

            if (kit && extension != ".iso")
            {
                AppendLog("The Classics toolkit accepts DVD ISO images. Select pop-fe2 for CUE or CHD images.");
                continue;
            }
            if (IsPs1 && extension == ".cue")
            {
                var cueReport = CueValidation.Inspect(fullPath);
                if (cueReport.Error is not null) AppendLog($"Disc check: {Path.GetFileName(fullPath)} — {cueReport.Error}");
                else AppendLog($"Disc check: {Path.GetFileName(fullPath)} — {cueReport.TrackCount} track(s), all listed files found.");
                string? discId = DiscSystemDetector.ReadPs1Serial(fullPath);
                if (discId is not null)
                {
                    string region = discId[..4] switch { "SLUS" or "SCUS" => "North America", "SLES" or "SCES" => "Europe", "SLPS" or "SCPS" or "SLPM" => "Japan", _ => "region unknown" };
                    AppendLog($"Disc ID: {discId} · {region} (estimated from disc ID).");
                }
                string sbi = Path.ChangeExtension(fullPath, ".sbi");
                if (File.Exists(sbi)) AppendLog($"Disc check: found {Path.GetFileName(sbi)}. The bundled PS1 engine uses its own protection database; it does not import this SBI file.");
            }
            _discImages.Add(new DiscImage(fullPath));
        }

        if (DiscList.SelectedIndex < 0 && _discImages.Count > 0) DiscList.SelectedIndex = 0;
        if (!_hasManualOutputName && _discImages.Count > 0 && !QueueMode && !_usingBatchFolder)
        {
            var baseName = Path.GetFileNameWithoutExtension(_discImages[0].Path);
            OutputPathBox.Text = Path.Combine(Path.GetDirectoryName(OutputPathBox.Text) ?? "", SafeFileName(baseName) + ".pkg");
            _hasManualOutputName = false;
        }
        RefreshDiscOrder();
        UpdateSummary();
        StartManualLibraryCheck();
        StartArtworkCheck();
        RefreshPs2ProfileStatus();
    }

    private void RemoveDisc_Click(object sender, RoutedEventArgs e)
    {
        if (DiscList.SelectedItem is not DiscImage selected) return;
        int removedIndex = DiscList.SelectedIndex;
        _discImages.Remove(selected);
        RefreshDiscOrder();
        UpdateSummary();
        if (_discImages.Count > 0)
        {
            DiscList.SelectedIndex = Math.Min(removedIndex, _discImages.Count - 1);
            DiscList.ScrollIntoView(DiscList.SelectedItem);
        }
        StartArtworkCheck();
        StartManualLibraryCheck();
        RefreshPs2ProfileStatus();
    }

    private async void VerifyDisc_Click(object sender, RoutedEventArgs e)
    {
        if (_verificationCancel is not null) { _verificationCancel.Cancel(); return; }
        if (_discImages.Count == 0 || _isBuilding) return;
        bool ps1 = IsPs1;
        string[] paths = _discImages.Select(d => d.Path).ToArray();
        using var cancel = new CancellationTokenSource();
        _verificationCancel = cancel;
        VerifyDiscButton.Content = "Cancel check";
        UpdateSummary();
        AppendLog(ps1 ? "PS1 verification started using the offline Redump catalog dated 2026-11-18. Click Cancel check to stop." : "PS2 ISO verification started using the offline Redump catalog dated 2026-06-15. Click Cancel check to stop.");
        try
        {
            foreach (string path in paths)
            {
                cancel.Token.ThrowIfCancellationRequested();
                AppendLog($"Checking {Path.GetFileName(path)}...");
                var progress = new Progress<int>(p => { if (_verificationCancel == cancel) VerifyDiscButton.Content = $"Cancel {p}%"; });
                try
                {
                    var result = await Task.Run(() => ps1 ? Ps1DiscVerifier.VerifyAsync(path, progress, cancel.Token) : Ps2DiscVerifier.VerifyAsync(path, progress, cancel.Token), cancel.Token);
                    AppendLog($"ISO {result.Status}: {Path.GetFileName(path)} — {result.Detail}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                { AppendLog($"ISO check unavailable: {Path.GetFileName(path)} — {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { AppendLog("ISO verification cancelled."); }
        finally { _verificationCancel = null; VerifyDiscButton.Content = "Verify ISO"; UpdateSummary(); }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        var current = DiscList.SelectedIndex;
        var target = current + delta;
        if (current < 0 || target < 0 || target >= _discImages.Count) return;
        _discImages.Move(current, target);
        DiscList.SelectedIndex = target;
        RefreshDiscOrder();
        if (MultiDiscMode) StartManualLibraryCheck();
        if (MultiDiscMode) StartArtworkCheck();
    }

    private void RefreshDiscOrder()
    {
        for (var i = 0; i < _discImages.Count; i++) _discImages[i].Number = $"D{i + 1}";
        DiscList.Items.Refresh();
        DiscCountText.Text = MultiDiscMode ? $"{_discImages.Count} / {MaxDiscs} discs" : $"{_discImages.Count} games";
        var totalBytes = _discImages.Sum(d => d.Length);
        ImageSizeText.Text = _discImages.Count == 0
            ? "No disc images selected"
            : $"Total source size · {FormatBytes(totalBytes)}";
        if (IsPs1 && MultiDiscMode && DiscNamesOutOfOrder(_discImages.Select(d => d.Path))) ImageSizeText.Text += " · Check disc order";
    }
    private static bool DiscNamesOutOfOrder(IEnumerable<string> paths)
    {
        int? previous = null;
        foreach (string disc in paths)
        {
            var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(disc), @"(?i)\b(?:disc|disk|cd)\s*[-_. ]*\s*(\d+)\b");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int number)) { previous = null; continue; }
            if (previous.HasValue && number <= previous.Value) return true;
            previous = number;
        }
        return false;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        if (QueueMode)
        {
            var directory = new OpenFolderDialog { Title = "Choose a folder for the completed packages" };
            if (Directory.Exists(OutputPathBox.Text)) directory.InitialDirectory = OutputPathBox.Text;
            if (directory.ShowDialog(this) == true) OutputPathBox.Text = directory.FolderName;
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Save PS3 Classics package",
            Filter = "PS3 package (*.pkg)|*.pkg",
            DefaultExt = ".pkg",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = Path.GetFileName(OutputPathBox.Text)
        };
        var folder = Path.GetDirectoryName(OutputPathBox.Text);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(this) == true)
        {
            OutputPathBox.Text = dialog.FileName;
            _hasManualOutputName = true;
        }
    }

    private void BrowseKit_Click(object sender, RoutedEventArgs e)
    {
        if (IsPs1) { StudioDialog.Show(this, "PS1 packages need the pop-fe command-line engine. Use CLI file to select pop-fe.exe or pop-fe.py.", "PS1 engine"); return; }
        var dialog = new OpenFolderDialog { Title = "Choose your extracted PS2 Classics GUI folder" };
        if (dialog.ShowDialog(this) != true) return;
        if (!ClassicsBackend.IsToolFolder(dialog.FolderName))
        {
            StudioDialog.Show(this, "Choose the folder containing ps2classic.exe, scetool, exdata and the PS2U10000 template.", "Toolkit incomplete");
            return;
        }
        EnginePathBox.Text = dialog.FolderName;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        string version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.4.2";
        string credits = $"""
            PS1 and PS2 Classics Package Studio {version}

            Create PlayStation 3 PS1 and PS2 Classics packages from disc images.

            CREDITS

            PlayStation 2, PlayStation 3 and PS2 Classics · Sony Computer Entertainment / Sony Interactive Entertainment
            PS2 Classics GUI · Aldo Vargas (aldostools)
            ps2classic · ps3dev-net team
            PS2-FPKG · Jabu, feature and workflow reference
            pop-fe and pop-fe2 · Ronnie Sahlberg and contributors
            PS3 package writer · PSL1GHT Development Team
            Package block processing · OpenSSL SHA-1, integrated with the Windows thread pool
            make_npdata · Hykem and contributors
            Compatibility profiles · ManaGunZ project and contributors
            Widescreen patches · PCSX2 patch authors and contributors
            Disc verification database · Redump contributors; PS2 snapshot 2026-06-15, PS1 snapshot 2026-11-18
            Catalog snapshot source · tsitokely / ps2-iso-renamer
            Advanced PS2 CONFIG reference · ps3classics/ps2 and PS3 Developer Wiki contributors
            PS2 manual archive · Kirkland's Manual Labor / Internet Archive
            Manual ID references · Ronnie Sahlberg and pop-fe2 contributors
            PDF renderer · PDFium and its contributors
            xdelta3 · Joshua MacDonald and contributors

            See ThirdParty.zip for licenses, source and additional acknowledgments.
            """;
        StudioDialog.Show(this, credits, "About PS1 and PS2 Classics Package Studio");
    }

    private void PreviewXmb_Click(object sender, RoutedEventArgs e)
    {
        string title = GameTitleBox.Text.Trim();
        if (title.Length == 0 && _discImages.Count > 0) title = Path.GetFileNameWithoutExtension(_discImages[0].Path);
        string cover = CoverPathBox.Text.Trim();
        if (cover.Length == 0 && _discImages.Count > 0 && _foundArtwork.TryGetValue(_discImages[0].Path, out var found)) cover = found;
        XmbPreview.Show(this, title, cover, BackdropPathBox.Text.Trim(), IsPs1, SelectedAudioMode());
    }

    private void BrowsePs1Config_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose a PS1 emulator config", Filter = "PS1 configs (*.ps3config;*.bin)|*.ps3config;*.bin|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) Ps1ConfigPathBox.Text = dialog.FileName;
    }

    private void BrowsePs1Patch_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose a PS1 patch", Filter = "PS1 patches (*.ppf;*.xdelta)|*.ppf;*.xdelta" };
        if (dialog.ShowDialog(this) == true) Ps1PatchPathBox.Text = dialog.FileName;
    }
    private void OpenPs2Compatibility_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://www.psdevwiki.com/ps3/PS2_Classics_Emulator_Compatibility_List") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { StudioDialog.Show(this, "Could not open the compatibility list: " + ex.Message, "Compatibility reports"); }
    }

    private void BrowseEngine_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = IsPs1 ? "Select the PS1 pop-fe command-line engine" : "Select the PS2 Classics command-line engine",
            Filter = IsPs1 ? "PS1 pop-fe engine|pop-fe.py;pop-fe.exe|Python script (*.py)|*.py|Application (*.exe)|*.exe" : "PS2 Classics engine (pop-fe2 CLI)|pop-fe2.py;pop-fe2.exe;pop-fe2-cli.exe|Python script (*.py)|*.py|Command-line application (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) EnginePathBox.Text = dialog.FileName;
    }

    private void SystemChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || EnginePathBox is null) return;
        if (_previousPs1) _ps1Engine=EnginePathBox.Text; else _ps2Engine=EnginePathBox.Text;
        _previousPs1=IsPs1;
        EnginePathBox.Text=IsPs1 ? _ps1Engine : _ps2Engine;
        UpdateSummary();
        StartArtworkCheck();
        StartManualLibraryCheck();
        RefreshPs2ProfileStatus();
    }
    private void BrowseCover_Click(object sender, RoutedEventArgs e) => BrowseArtwork(CoverPathBox);
    private void BrowseBackdrop_Click(object sender, RoutedEventArgs e) => BrowseArtwork(BackdropPathBox);
    private void BrowseManual_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose a manual to preview on this PC", Filter = "Manuals (*.pdf;*.zip;*.cbz)|*.pdf;*.zip;*.cbz", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) ManualPathBox.Text = dialog.FileName;
    }
    private void BrowsePs2Config_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose a PS2 compatibility profile", Filter = "CONFIG files (*.config;*.bin)|*.config;*.bin|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) Ps2ConfigPathBox.Text = dialog.FileName;
    }
    private async void RefreshPs2ProfileStatus()
    {
        if (!IsLoaded || Ps2ProfileStatusText is null) return;
        RefreshPs1Protection();
        int generation = ++_profileCheckGeneration;
        if (IsPs1 || _discImages.Count == 0) { Ps2ProfileStatusText.Text = "Add a PS2 game to check bundled profiles."; return; }
        if (_discImages.Count > 1 && !MultiDiscMode) { Ps2ProfileStatusText.Text = "PS2 profiles are selected separately for each queued game; see build activity for results."; return; }
        string image = _discImages[0].Path, engine = EnginePathBox.Text.Trim();
        if (!ClassicsBackend.IsToolFolder(engine)) { Ps2ProfileStatusText.Text = "Profile lookup is available with the built-in PS2 engine or toolkit."; return; }
        Ps2ProfileStatusText.Text = "Checking PS2 compatibility profiles…";
        try {
            var result = await Task.Run(() => { using var stream = File.OpenRead(image); string serial = DiscMetadata.ReadSerial(stream); return (serial, profiles: ClassicsBackend.FindProfiles(engine, serial)); });
            if (generation != _profileCheckGeneration) return;
            Ps2ProfileStatusText.Text = result.profiles.Length > 0 ? $"{result.serial}: profiles available ({string.Join(", ", result.profiles)}). Automatic selects {result.profiles[0]}." : $"{result.serial}: no matching profile found. The game may still work.";
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) {
            if (generation == _profileCheckGeneration) Ps2ProfileStatusText.Text = "Could not check PS2 profiles for this image.";
        }
    }
    private async void RefreshPs1Protection()
    {
        if (Ps1ProtectionText is null) return;
        int generation = ++_protectionCheckGeneration;
        Ps1ProtectionDot.Fill = (Brush)new BrushConverter().ConvertFromString("#8999AE")!;
        if (!IsPs1 || _discImages.Count == 0) { Ps1ProtectionText.Text = "Protection: waiting"; return; }
        if (EnginePathBox.Text.Trim() != Ps1Engine.Bundled) { Ps1ProtectionText.Text = "Protection: external engine not checked"; return; }
        string[] paths = _discImages.Select(d => d.Path).ToArray();
        Ps1ProtectionText.Text = "Protection: checking";
        try
        {
            var results = await Task.Run(() => paths.Select(Ps1Protection.Check).ToArray());
            if (generation != _protectionCheckGeneration) return;
            int known = results.Count(r => r.Recognized), unknown = results.Count(r => r.Serial is null), sbi = results.Count(r => r.SbiPresent);
            Ps1ProtectionText.Text = known > 0 ? $"Protection: {known} ID(s) recognized by bundled engine" : unknown > 0 ? "Protection: disc ID unavailable" : "Protection: no IDs listed in bundled database";
            if (sbi > 0) Ps1ProtectionText.Text += " · SBI not imported";
            if (known > 0) Ps1ProtectionDot.Fill = (Brush)new BrushConverter().ConvertFromString("#31BDF1")!;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { if (generation == _protectionCheckGeneration) Ps1ProtectionText.Text = "Protection: check unavailable"; }
    }
    private void ShowArtworkPreview(string? path, string caption)
    {
        if (ArtworkPreviewFrame is null) return;
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { ArtworkPreviewImage.Source = null; ArtworkPreviewFrame.Visibility = Visibility.Collapsed; return; }
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.UriSource = new Uri(Path.GetFullPath(path)); image.DecodePixelWidth = 300; image.EndInit(); image.Freeze();
            ArtworkPreviewImage.Source = image; ArtworkPreviewCaption.Text = caption; ArtworkPreviewFrame.Visibility = Visibility.Visible;
        } catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or FormatException) { ArtworkPreviewImage.Source = null; ArtworkPreviewFrame.Visibility = Visibility.Collapsed; }
    }
    private void ArtworkOptionChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) StartArtworkCheck();
    }

    private async void StartArtworkCheck()
    {
        if (!IsLoaded || ArtworkStatusText is null || AutoPs2ArtworkCheck is null || CoverPathBox is null) return;
        _artworkCancel?.Cancel();
        var cancellation = new CancellationTokenSource();
        _artworkCancel = cancellation;
        _foundArtwork = new(StringComparer.OrdinalIgnoreCase);
        _checkingArtwork = false;
        if (!string.IsNullOrWhiteSpace(CoverPathBox.Text)) { bool exists = File.Exists(CoverPathBox.Text.Trim()); ArtworkStatusText.Text = exists ? "Using your selected icon for the package." : "Selected icon file was not found."; SetArtworkIndicator(exists ? "found" : "not found"); ShowArtworkPreview(CoverPathBox.Text.Trim(), "Your selected artwork"); UpdateSummary(); return; }
        ShowArtworkPreview(null, "");
        if (AutoPs2ArtworkCheck.IsChecked != true) { ArtworkStatusText.Text = "Automatic cover lookup is off."; SetArtworkIndicator("off"); UpdateSummary(); return; }
        if (_discImages.Count == 0) { ArtworkStatusText.Text = "Add a game to check for cover artwork."; SetArtworkIndicator("waiting"); UpdateSummary(); return; }
        if (!IsPs1 && !ClassicsBackend.IsToolFolder(EnginePathBox.Text)) { ArtworkStatusText.Text = "Automatic PS2 cover lookup requires the built-in PS2 engine or a compatible toolkit."; SetArtworkIndicator("unavailable"); UpdateSummary(); return; }
        bool ps1 = IsPs1;
        bool threeDimensional = ArtworkStyleChoice?.SelectedIndex == 1;
        string[] images = (MultiDiscMode ? _discImages.Take(1) : _discImages).Select(d => d.Path).ToArray();
        _checkingArtwork = true;
        SetArtworkIndicator("checking");
        ArtworkStatusText.Text = $"Checking cover artwork for {images.Length} game(s)…";
        UpdateSummary();
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int unavailable = 0, missing = 0;
        try
        {
            foreach (string image in images)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                string? serial;
                try {
                    serial = await Task.Run(() => {
                        if (ps1) return DiscSystemDetector.ReadPs1Serial(image);
                        using var file = File.OpenRead(image);
                        return DiscMetadata.ReadSerial(file);
                    }, cancellation.Token);
                } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) {
                    AppendLog($"Artwork: could not identify {Path.GetFileName(image)}: {e.Message}"); unavailable++; continue;
                }
                if (serial is null) { AppendLog($"Artwork: could not identify {Path.GetFileName(image)}."); unavailable++; continue; }
                string folder = Path.Combine(_artworkTemp, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string lookupMessage = "";
                string? path = await Ps2CoverLookup.DownloadAsync(serial, folder, line => { lookupMessage = line; AppendLog(line); }, cancellation.Token, ps1, threeDimensional);
                if (path is not null)
                {
                    try {
                        using var stream = File.OpenRead(path);
                        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        if (decoder.Frames.Count == 0) throw new InvalidDataException("Image is empty.");
                        found[image] = path;
                        AppendLog($"Artwork found: {Path.GetFileName(image)} ({serial}).");
                    } catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or FormatException) { unavailable++; AppendLog($"Artwork could not be read for {Path.GetFileName(image)}: {e.Message}"); }
                }
                else { unavailable++; if (lookupMessage.StartsWith("No cover found", StringComparison.OrdinalIgnoreCase)) missing++; AppendLog($"Artwork not found or unavailable: {Path.GetFileName(image)} ({serial})."); }
            }
            if (_artworkCancel != cancellation) return;
            _foundArtwork = found;
            SetArtworkIndicator(found.Count == images.Length ? "found" : missing > 0 ? "not found" : "unavailable");
            if (images.Length == 1 && found.TryGetValue(images[0], out var preview)) ShowArtworkPreview(preview, "Artwork found for this game");
            ArtworkStatusText.Text = images.Length == 1
                ? found.Count == 1 ? "Cover artwork found and ready to use." : "No cover artwork found or lookup unavailable. The engine or template will choose the artwork."
                : $"Cover artwork found for {found.Count} of {images.Length} games; {unavailable} not found or unavailable. See build activity for details.";
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            if (_artworkCancel != cancellation) return;
            ArtworkStatusText.Text = "Artwork check could not be completed. Default artwork will be used.";
            SetArtworkIndicator("unavailable");
            AppendLog($"Artwork check: {e.Message}");
        }
        finally
        {
            if (_artworkCancel == cancellation) { _checkingArtwork = false; UpdateSummary(); }
        }
    }
    private void BrowseArtwork(TextBox target)
    {
        var dialog=new OpenFileDialog { Title="Choose artwork", Filter="Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists=true };
        if(dialog.ShowDialog(this)==true) target.Text=dialog.FileName;
    }

    private void BrowseAudio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose XMB focus audio",
            Filter = "WAV audio (*.wav)|*.wav|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _audioPath = dialog.FileName;
            AudioFileText.Text = Path.GetFileName(_audioPath);
            AudioFileText.ToolTip = _audioPath;
        }
    }

    private void AudioModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || AudioBrowseButton is null || AudioFileText is null) return;
        var mode = SelectedAudioMode();
        AudioBrowseButton.IsEnabled = mode == "Custom WAV";
        if (mode == "Automatic") AudioFileText.Text = "Uses catalog audio when available";
        else if (mode == "No sound") AudioFileText.Text = "No focus sound will be added";
        else if (string.IsNullOrEmpty(_audioPath)) AudioFileText.Text = "Choose a WAV file";
        else AudioFileText.Text = Path.GetFileName(_audioPath);
    }

    private void OutputPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && !_updatingDestination) _hasManualOutputName = true;
        UpdateSummary();
    }

    private void EnginePathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        SaveSettings();
        UpdateSummary();
        StartArtworkCheck();
        RefreshPs2ProfileStatus();
    }

    private void BuildModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) { RefreshDiscOrder(); UpdateSummary(); StartArtworkCheck(); StartManualLibraryCheck(); RefreshPs2ProfileStatus(); }
    }

    private void UpdateDestinationMode()
    {
        if (OutputPathBox is null || OutputLabel is null || BuildModeCombo is null || _updatingDestination) return;
        bool batch = QueueMode;
        if (batch != _usingBatchFolder)
        {
            string current = OutputPathBox.Text.Trim();
            _updatingDestination = true;
            _usingBatchFolder = batch;
            if (batch)
            {
                _singleOutputPath = current;
                OutputPathBox.Text = _batchOutputFolder ?? Path.GetDirectoryName(current) ?? "";
            }
            else
            {
                _batchOutputFolder = current;
                var name = _discImages.Count > 0 ? SafeFileName(Path.GetFileNameWithoutExtension(_discImages[0].Path)) + ".pkg" : "PS2 Game.pkg";
                OutputPathBox.Text = _singleOutputPath ?? Path.Combine(current, name);
            }
            _updatingDestination = false;
        }
        OutputLabel.Text = batch ? "OUTPUT FOLDER" : "PACKAGE FILE";
        OutputPathBox.ToolTip = batch ? "Folder for the completed packages" : "Full output path ending in .pkg";
    }

    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        if (_isBuilding) return;
        if (!TryGetBuildInputs(out var engine, out var outputFile, out var outputFolder, out var error))
        {
            StudioDialog.Show(this, error, "Cannot create package", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool ps1=IsPs1; bool kit = !ps1 && ClassicsBackend.IsToolFolder(engine); bool widescreen = kit && WidescreenCheck.IsChecked == true;
        bool autoArtwork = AutoPs2ArtworkCheck.IsChecked == true;
        bool queue = QueueMode;
        string cover=CoverPathBox.Text.Trim(), backdrop=BackdropPathBox.Text.Trim(), customConfig=Ps2ConfigPathBox.Text.Trim(), ps1Config=Ps1ConfigPathBox.Text.Trim(), ps1Patch=Ps1PatchPathBox.Text.Trim(); bool newEmu=Ps1NewEmuCheck.IsChecked==true, swap=!queue && MultiDiscMode && SwapDiscCheck.IsChecked==true; string audioMode=SelectedAudioMode(), audioPath=_audioPath ?? ""; string customTitle=GameTitleBox.Text.Trim();
        string profileChoice = (Ps2ProfileChoice.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Automatic";
        bool cacheFix = Ps2CacheFix.IsChecked == true, performanceFix = Ps2PerformanceFix.IsChecked == true, pressureFix = Ps2PressureFix.IsChecked == true; int multitap = Ps2Multitap.SelectedIndex;
        bool advancedConfig = !ps1 && (cacheFix || performanceFix || pressureFix || multitap > 0);
        if (advancedConfig && customConfig.Length > 0) { StudioDialog.Show(this, "Clear the custom CONFIG path or turn off the advanced options. Both cannot be applied together.", "PS2 config", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (advancedConfig && !kit) { StudioDialog.Show(this, "Advanced PS2 options require the built-in PS2 engine or a compatible toolkit.", "PS2 config", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var discs = _discImages.Select(d => d.Path).ToArray();
        if (ps1 && (ps1Config.Length > 0 || ps1Patch.Length > 0) && (discs.Length != 1 || !Path.GetExtension(discs[0]).Equals(".cue", StringComparison.OrdinalIgnoreCase)))
        { StudioDialog.Show(this, "A selected PS1 config or patch currently requires exactly one CUE disc. Clear these fields for queues or multidisc packages.", "PS1 options", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (ps1 && MultiDiscMode && discs.Length > 1)
        {
            int? previous = null;
            foreach (string disc in discs)
            {
                var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(disc), @"(?i)\b(?:disc|disk|cd)\s*[-_. ]*\s*(\d+)\b");
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out int number)) { previous = null; continue; }
                if (previous.HasValue && number <= previous.Value) AppendLog("Disc order warning: filenames suggest the discs may be out of order. Check the list before building.");
                previous = number;
            }
        }
        var outputs = queue
            ? discs.Select(path => Path.Combine(outputFolder, SafeFileName(Path.GetFileNameWithoutExtension(path)) + ".pkg")).ToArray()
            : new[] { outputFile };
        if (outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != outputs.Length)
        {
            StudioDialog.Show(this, "Two images would have the same package filename. Rename one image before building.", "Duplicate package names", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var existing = outputs.Where(File.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (existing.Count > 0)
        {
            string names = string.Join("\n", existing.Select(Path.GetFileName));
            if (StudioDialog.Show(this, $"These packages already exist. Replace them?\n\n{names}", "Replace packages?", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        }

        using var cancellation = new CancellationTokenSource();
        _buildCancel = cancellation;
        _isBuilding = true;
        InputPanels.IsEnabled = false;
        BuildButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true;
        BuildProgressBar.Foreground = ProgressPercentText.Foreground = (Brush)FindResource("AccentBrush");
        BuildProgressBar.Value = 0;
        int current = -1;
        int succeeded = 0;
        var failures = new List<string>();
        bool cancelled = false;

        try
        {
            Directory.CreateDirectory(outputFolder);
            for (int i = 0; i < outputs.Length; i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                current = i;
                int gameIndex = i;
                string[] gameDiscs = queue ? new[] { discs[i] } : discs;
                string output = outputs[i];
                string job = Path.Combine(outputFolder, ".classics-" + Guid.NewGuid().ToString("N"));
                string staged = Path.Combine(job, "result.pkg");
                string itemLabel = queue ? $"Game {i + 1} of {outputs.Length}" : "";
                void Show(BuildProgress progress)
                {
                    if (_buildCancel != cancellation || cancellation.IsCancellationRequested || current != gameIndex) return;
                    if (queue) progress = progress with { Stage = $"{itemLabel} · {progress.Stage}" };
                    DisplayProgress(progress);
                }
                IProgress<BuildProgress> live = new Progress<BuildProgress>(Show);
                Show(new BuildProgress("Starting build", kit ? 0 : null, Path.GetFileName(gameDiscs[0])));
                AppendLog(queue ? $"{itemLabel} · {Path.GetFileName(gameDiscs[0])}" : $"Building {output}");
                try
                {
                    Directory.CreateDirectory(job);
                    var token = cancellation.Token;
                    if (kit)
                    {
                        string title = queue ? "" : GameTitleBox.Text.Trim();
                        string? selectedCover = cover;
                        bool automaticCover = autoArtwork && string.IsNullOrWhiteSpace(cover) && _foundArtwork.TryGetValue(gameDiscs[0], out selectedCover);
                        selectedCover ??= "";
                        string effectiveConfig = customConfig;
                        if (advancedConfig) { effectiveConfig = Path.Combine(job, "advanced.CONFIG"); File.WriteAllBytes(effectiveConfig, Ps2ConfigBuilder.Build(cacheFix, performanceFix, pressureFix, multitap)); AppendLog("Using the selected advanced PS2 config instead of an automatic profile."); }
                        await Task.Run(() => ClassicsBackend.BuildAsync(engine, gameDiscs[0], job, staged, title, widescreen, line => AppendLog(line), live.Report, token, selectedCover, backdrop, false, automaticCover, effectiveConfig, profileChoice));
                    }
                    else
                    {
                        var cli = new CliProgress(live.Report);
                        string resolvedCover = cover;
                        if (ps1 && autoArtwork && string.IsNullOrWhiteSpace(cover))
                        {
                            resolvedCover = _foundArtwork.TryGetValue(gameDiscs[0], out var cachedCover) ? cachedCover : "";
                            if (!string.IsNullOrEmpty(resolvedCover)) AppendLog("Using the PS1 cover found before the build.");
                            else AppendLog("pop-fe will use its normal artwork selection.");
                        }
                        if (ps1 && (ps1Config.Length > 0 || ps1Patch.Length > 0)) AppendLog("Preparing a separate copy of the PS1 disc for its custom config or patch.");
                        var info = ps1 ? await Task.Run(() => Ps1Engine.CreateStartInfo(engine, job, staged, gameDiscs, queue ? "" : customTitle, resolvedCover, backdrop, newEmu, swap, audioMode, audioPath, token, ps1Config, ps1Patch)) : CreateStartInfo(engine, job, Path.GetFileName(staged), job, gameDiscs);
                        if (!ps1 && SelectedAudioMode() == "Custom WAV")
                        {
                            Show(new BuildProgress("Copying audio", null, Path.GetFileName(_audioPath)!));
                            using var audio = File.OpenRead(_audioPath!);
                            await using var copy = File.Create(Path.Combine(job, "sound.wav"));
                            await audio.CopyToAsync(copy, token);
                        }
                        Show(new BuildProgress(ps1 ? "Running pop-fe" : "Running pop-fe2", null, "Waiting for the engine to report its stage"));
                        CliProgress.EnableLiveOutput(info);
                        await Task.Run(() => ClassicsBackend.RunProcessAsync(info, line =>
                        {
                            AppendLog(line);
                            cli.Observe(line);
                        }, token));
                        Show(new BuildProgress("Checking package", null, Path.GetFileName(output)));
                        await Task.Run(() => ManualFreePackage.EnsureAsync(staged, job, message => AppendLog(message), token));
                    }
                    token.ThrowIfCancellationRequested();
                    File.Move(staged, output, existing.Contains(output));
                    succeeded++;
                    AppendLog($"Package created · {output}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(gameDiscs[0])} · {ex.Message}");
                    AppendLog($"Failed · {Path.GetFileName(gameDiscs[0])} · {ex.Message}", isError: true);
                }
                finally
                {
                    current = -1;
                    try { await Task.Run(() => { if (Directory.Exists(job)) Directory.Delete(job, true); }); }
                    catch (Exception ex) { AppendLog($"Temporary files remain in {job}. {ex.Message}", isError: true); }
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            AppendLog("Build cancelled.");
        }
        catch (Exception ex)
        {
            failures.Add(ex.Message);
            AppendLog(ex.Message, isError: true);
        }
        finally
        {
            _buildCancel = null;
            _isBuilding = false;
            InputPanels.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
        }

        UpdateSummary(resetActivity: false);
        string summary = $"{succeeded} of {outputs.Length} packages created";
        if (cancelled)
        {
            BuildProgressBar.IsIndeterminate = false;
            ProgressStageText.Text = "Cancelled";
            ProgressDetailText.Text = summary;
            BuildProgressBar.Foreground = ProgressPercentText.Foreground = (Brush)FindResource("MutedBrush");
        }
        else if (failures.Count > 0)
        {
            BuildProgressBar.IsIndeterminate = false;
            ProgressStageText.Text = "Finished with errors";
            ProgressDetailText.Text = summary;
            BuildProgressBar.Foreground = ProgressPercentText.Foreground = Brushes.Salmon;
        }
        else
        {
            BuildProgressBar.Foreground = ProgressPercentText.Foreground = (Brush)FindResource("SuccessBrush");
            DisplayProgress(new BuildProgress("Complete", 100, queue ? summary : Path.GetFileName(outputFile)));
        }

        FlushLog();
        if (_closeWhenDone) Close();
        else
        {
            string details = failures.Count == 0 ? "" : "\n\n" + string.Join("\n", failures);
            StudioDialog.Show(this, summary + details, cancelled ? "Build cancelled" : failures.Count > 0 ? "Finished with errors" : "PS3 package created",
                MessageBoxButton.OK, failures.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
    }

    private ProcessStartInfo CreateStartInfo(string engine, string outputFolder, string pkgName, string workingDirectory, IEnumerable<string> discs)
    {
        var isPythonScript = Path.GetExtension(engine).Equals(".py", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isPythonScript ? FindPythonLauncher() : engine,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (isPythonScript)
        {
            if (Path.GetFileName(startInfo.FileName).Equals("py.exe", StringComparison.OrdinalIgnoreCase)) startInfo.ArgumentList.Add("-3");
            startInfo.ArgumentList.Add(engine);
        }

        startInfo.ArgumentList.Add("--ps3-pkg=" + pkgName);
        startInfo.ArgumentList.Add("--output-directory=" + outputFolder);
        switch (SelectedAudioMode())
        {
            case "No sound": startInfo.ArgumentList.Add("--snd0=no"); break;
            case "Custom WAV":
                var copy = Path.Combine(workingDirectory, "sound.wav");
                startInfo.ArgumentList.Add("--snd0=" + copy);
                break;
        }
        if (!QueueMode && SwapDiscCheck.IsChecked == true) startInfo.ArgumentList.Add("--swap=0");
        foreach (var disc in discs) startInfo.ArgumentList.Add(disc);
        return startInfo;
    }

    private static string FindPythonLauncher()
    {
        foreach (var candidate in new[] { "python.exe", "py.exe" })
        {
            var path = FindOnPath(candidate);
            if (path is not null) return path;
        }
        throw new FileNotFoundException("The selected engine is a Python script, but Python was not found. Use a Windows command-line engine build or install Python.");
    }

    private static string? FindOnPath(string executable)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), executable);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private bool TryGetBuildInputs(out string engine, out string outputFile, out string outputFolder, out string error)
    {
        engine = EnginePathBox.Text.Trim().Trim('"');
        outputFile = OutputPathBox.Text.Trim().Trim('"');
        outputFolder = "";
        error = "";

        var kit = !IsPs1 && ClassicsBackend.IsToolFolder(engine);
        if (_discImages.Count == 0) error = "Add at least one disc image first.";
        else if (IsPs1 && _discImages.FirstOrDefault(d => d.FormatLabel == "CUE" && CueValidation.Check(d.Path) is not null) is { } invalidCue) error = $"{invalidCue.Name}: {CueValidation.Check(invalidCue.Path)}";
        else if (IsPs1 && _discImages.Any(d => d.FormatLabel is not ("CUE" or "BIN" or "IMG" or "CCD"))) error = "PS1 mode accepts CUE, BIN, IMG, or CCD images. Remove incompatible images first.";
        else if (!IsPs1 && _discImages.Any(d => d.FormatLabel is not ("ISO" or "CUE" or "CHD"))) error = "Remove PS1 images before building a PS2 package.";
        else if (IsPs1 && !Ps1Engine.IsSupported(engine)) error = "Select the PS1 pop-fe.exe or pop-fe.py command-line engine. pop-fe2 is for PS2 games.";
        else if (!IsPs1 && !kit && (!string.IsNullOrWhiteSpace(CoverPathBox.Text) || !string.IsNullOrWhiteSpace(BackdropPathBox.Text))) error = "Custom artwork for PS2 is supported with the PS2 Classics GUI toolkit. Choose that toolkit or clear the artwork fields.";
        else if ((!string.IsNullOrWhiteSpace(CoverPathBox.Text) && !File.Exists(CoverPathBox.Text.Trim())) || (!string.IsNullOrWhiteSpace(BackdropPathBox.Text) && !File.Exists(BackdropPathBox.Text.Trim()))) error = "Choose valid artwork files, or clear the artwork fields.";
        else if (!IsPs1 && !string.IsNullOrWhiteSpace(Ps2ConfigPathBox.Text) && (!kit || !File.Exists(Ps2ConfigPathBox.Text.Trim()))) error = "Choose an existing PS2 CONFIG with the built-in PS2 engine or clear the CONFIG field.";
        else if (MultiDiscMode && _discImages.Count > MaxDiscs) error = "A PS3 Classics package can contain up to five discs.";
        else if (kit && MultiDiscMode) error = "Select pop-fe2 to create a multi-disc package, or choose separate packages.";
        else if (kit && _discImages.Any(d => !d.FormatLabel.Equals("ISO"))) error = "The Classics toolkit takes DVD ISO images.";
        else if (!kit && !IsPs1 && !File.Exists(engine)) error = "Choose a compatible PS3 Classics command-line engine.";
        else if (Path.GetFileName(engine).Equals("pop-fe2-ps3.exe", StringComparison.OrdinalIgnoreCase)) error = "pop-fe2-ps3.exe is the graphical interface. Select its command-line engine (pop-fe2.py or a CLI executable) here.";
        else if (string.IsNullOrWhiteSpace(outputFile)) error = "Choose a destination .pkg file.";
        else if (!QueueMode && !Path.GetExtension(outputFile).Equals(".pkg", StringComparison.OrdinalIgnoreCase)) error = "The destination file must use the .pkg extension.";
        else if (!kit && SelectedAudioMode() == "Custom WAV" && (string.IsNullOrWhiteSpace(_audioPath) || !File.Exists(_audioPath))) error = "Choose a valid WAV file or change the focus sound option.";

        if (!string.IsNullOrEmpty(error)) return false;
        try
        {
            outputFile = Path.GetFullPath(outputFile);
            outputFolder = QueueMode ? outputFile : Path.GetDirectoryName(outputFile) ?? "";
            return !string.IsNullOrWhiteSpace(outputFolder);
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        ProgressStageText.Text = "Cancelling";
        ProgressDetailText.Text = "";
        _buildCancel?.Cancel();
    }

    private void DisplayProgress(BuildProgress update)
    {
        ProgressStageText.Text = update.Stage;
        ProgressDetailText.Text = update.Detail;
        BuildProgressBar.Visibility = Visibility.Visible;
        BuildProgressBar.IsIndeterminate = update.Percent is null;
        if (update.Percent is double percent)
        {
            BuildProgressBar.Value = Math.Clamp(percent, 0, 100);
            ProgressPercentText.Text = $"{Math.Floor(BuildProgressBar.Value):0}%";
        }
        else ProgressPercentText.Text = "";
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
        e.Handled = true;
    }

    private void DiscList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DiscList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
        e.Handled = true;
    }

    private void UpdateSummary(bool resetActivity = true)
    {
        if (DiscCountText is null || BuildProgressBar is null || BuildButton is null || EngineStatusText is null) return;
        UpdateDiscIndicator();
        DiscCountText.Text = MultiDiscMode ? $"{_discImages.Count} / {MaxDiscs} discs" : $"{_discImages.Count} games";
        var engine = EnginePathBox.Text.Trim().Trim('"');
        var kit = !IsPs1 && ClassicsBackend.IsToolFolder(engine);
        var exists = IsPs1 ? Ps1Engine.IsSupported(engine) : kit || File.Exists(engine);
        UpdateDestinationMode();
        KitOptions.Visibility = (kit || IsPs1) && !QueueMode ? Visibility.Visible : Visibility.Collapsed;
        WidescreenOptions.Visibility = kit ? Visibility.Visible : Visibility.Collapsed;
        Ps1Compat.Visibility = IsPs1 ? Visibility.Visible : Visibility.Collapsed;
        Ps1PatchOptions.Visibility = IsPs1 ? Visibility.Visible : Visibility.Collapsed;
        Ps1ProtectionOptions.Visibility = IsPs1 ? Visibility.Visible : Visibility.Collapsed;
        Ps2LibraryOptions.Visibility = IsPs1 ? Visibility.Collapsed : Visibility.Visible;
        Ps2Compat.Visibility = IsPs1 ? Visibility.Collapsed : Visibility.Visible;
        Ps2AdvancedConfig.Visibility = IsPs1 ? Visibility.Collapsed : Visibility.Visible;
        Ps2CompatibilityLink.Visibility = IsPs1 ? Visibility.Collapsed : Visibility.Visible;
        TargetText.Text = IsPs1 ? "PlayStation 3 · PS1 Classics · .PKG" : "PlayStation 3 · PS2 Classics · .PKG";
        CliOptions.Visibility = kit ? Visibility.Collapsed : Visibility.Visible;
        MultiDiscOptions.Visibility = MultiDiscMode ? Visibility.Visible : Visibility.Collapsed;
        if (exists)
        {
            EngineStatusDot.Fill = (Brush)FindResource("SuccessBrush");
            EngineStatusText.Text = IsPs1 ? (engine == Ps1Engine.Bundled ? "Built-in PS1 engine ready" : "PS1 pop-fe engine selected · package build") : kit ? (engine == ClassicsBackend.Bundled ? "Built-in PS2 engine ready · one DVD ISO per package" : "PS2 Classics GUI toolkit · one DVD ISO per package") : Path.GetExtension(engine).Equals(".py", StringComparison.OrdinalIgnoreCase)
                ? "CLI script found · needs Python on this PC"
                : "Engine file found · command-line support required";
        }
        else
        {
            EngineStatusDot.Fill = new SolidColorBrush(Color.FromRgb(215, 167, 95));
            EngineStatusText.Text = IsPs1 ? "Select the PS1 pop-fe command-line engine." : "Choose the toolkit folder or a pop-fe2 CLI.";
        }

        if (resetActivity && !_isBuilding)
        {
            ProgressStageText.Text = !exists ? "Waiting for a PS3 Classics engine" : _discImages.Count == 0 ? "Waiting for a disc image" : "Ready";
            BuildProgressBar.Visibility = Visibility.Collapsed;
            ProgressDetailText.Text = ProgressPercentText.Text = "";
        }

        var outputOk = QueueMode ? !string.IsNullOrWhiteSpace(OutputPathBox.Text) : Path.GetExtension(OutputPathBox.Text.Trim()).Equals(".pkg", StringComparison.OrdinalIgnoreCase);
        bool discsPresent = _discImages.All(d => File.Exists(d.Path) && (!IsPs1 || d.FormatLabel != "CUE" || CueValidation.Check(d.Path) is null));
        if (VerifyDiscButton is not null) { VerifyDiscButton.IsEnabled = _verificationCancel is not null || (!_isBuilding && _discImages.Count > 0); if (_verificationCancel is null) VerifyDiscButton.Content = "Verify disc"; }
        BuildButton.IsEnabled = !_isBuilding && _verificationCancel is null && !_checkingArtwork && _discImages.Count > 0 && discsPresent && (!MultiDiscMode || _discImages.Count <= MaxDiscs) && exists && outputOk && !(kit && MultiDiscMode);
    }

    private string SelectedAudioMode() => AudioModeCombo.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "Automatic" : "Automatic";

    private void AppendLog(string line, bool isError = false)
    {
        lock (_pendingLog)
        {
            bool schedule = _pendingLog.Length == 0;
            if (isError) _pendingLog.Append("! ");
            _pendingLog.AppendLine(line);
            if (schedule) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, FlushLog);
        }
    }

    private void FlushLog()
    {
        string text;
        lock (_pendingLog)
        {
            if (_pendingLog.Length == 0) return;
            text = _pendingLog.ToString();
            _pendingLog.Clear();
        }
        LogText.AppendText(text);
        LogText.ScrollToEnd();
    }

    private static string SafeFileName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
        return string.IsNullOrWhiteSpace(value) ? "PS2 Game" : value.Trim();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _manualCheckGeneration++;
        DeletePreview(_temporaryPreview);
        _protectionCheckGeneration++;
        _verificationCancel?.Cancel();
        _artworkCancel?.Cancel();
        try { if (Directory.Exists(_artworkTemp)) Directory.Delete(_artworkTemp, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        base.OnClosed(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isBuilding)
        {
            e.Cancel = true;
            _closeWhenDone = true;
            _buildCancel?.Cancel();
        }
        else SaveSettings();
        base.OnClosing(e);
    }
}

public sealed class DiscImage(string path)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public long Length => new FileInfo(Path).Length;
    public string Number { get; set; } = "D1";
    public string SizeLabel => Format(Length);
    public string FormatLabel => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public override string ToString() => Name;

    private static string Format(long bytes)
    {
        var value = (double)bytes;
        var unit = "B";
        foreach (var next in new[] { "KB", "MB", "GB", "TB" })
        {
            if (value < 1024) break;
            value /= 1024;
            unit = next;
        }
        return $"{value:0.#} {unit}";
    }
}

public sealed class StudioSettings
{
    public string EnginePath { get; set; } = "";
    public string Ps1EnginePath { get; set; } = "";
    public string ManualLibraryPath { get; set; } = "";
}
