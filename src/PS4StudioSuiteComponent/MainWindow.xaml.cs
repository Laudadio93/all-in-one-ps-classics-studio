using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace PS4Studio;
public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int enabled = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
    }
    record DiscItem(string Path,string Id,string Name);
    readonly ObservableCollection<DiscItem> items = new();
    CancellationTokenSource? cts;
    bool closeAfterBuild;
    CancellationTokenSource? verifyCancel, artworkCancel;
    readonly string artworkTemp=Path.Combine(Path.GetTempPath(),"PS4StudioArtwork-"+Guid.NewGuid().ToString("N"));
    readonly Dictionary<string,string> foundCovers=new(StringComparer.OrdinalIgnoreCase);
    int statusGeneration;
    int manualGeneration;
    string? checkedManual;
    public MainWindow(){InitializeComponent(); ConfigureSuiteView(); SetupHeaderGlow(); DiscList.ItemsSource=items;Toolkit.Text=BuiltInKit.Name; Output.Text=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);ManualFolder.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"PS2 Manual Library");}
    void ConfigureSuiteView()
    {
        string? view=Environment.GetEnvironmentVariable("CLASSICSSUITE_VIEW");
        if(view is not ("crypto" or "ps4"))return;
        StudioSwitcher.Visibility=Visibility.Collapsed;
        if(view=="crypto")
        {
            HeaderKicker.Text="PS2 & PS3 CLASSICS";
            HeaderBadge.Text="PS2  /  PS3";
            HeaderTitle.Text="Encrypt or decrypt your images.";
            HeaderDescription.Text="Convert PS2 Classics images and PS3 disc ISOs.";
            ShowCrypto(this,new RoutedEventArgs());
        }
    }
    void SetupHeaderGlow()
    {
        var rotation = new RotateTransform(0, 0.5, 0.5);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), RelativeTransform = rotation };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#E6002D"), 0.0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFCB05"), 0.25));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00A99D"), 0.5));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#168BFA"), 0.75));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#E6002D"), 1.0));
        HeaderBorder.BorderBrush = brush;
        HeaderGlow.BorderBrush = brush;
        if (SystemParameters.ClientAreaAnimation)
            rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(8)) { RepeatBehavior = RepeatBehavior.Forever });
    }
    CancellationTokenSource? cryptoCancel;
    string? autoCryptoKey;
    void ShowBuilder(object s,RoutedEventArgs e){BuilderView.Visibility=Visibility.Visible;BuilderFooter.Visibility=Visibility.Visible;CryptoView.Visibility=Visibility.Collapsed;CryptoFooter.Visibility=Visibility.Collapsed;}
    void ShowCrypto(object s,RoutedEventArgs e){BuilderView.Visibility=Visibility.Collapsed;BuilderFooter.Visibility=Visibility.Collapsed;CryptoView.Visibility=Visibility.Visible;CryptoFooter.Visibility=Visibility.Visible;}
    void CryptoSystemChanged(object s,SelectionChangedEventArgs e){if(!IsLoaded)return;CryptoKeyHelp.Text=CryptoSystem.SelectedIndex==1?"PS3 disc ISO needs a matching .dkey (32 hex characters or 16 raw bytes). An identically named .dkey beside the ISO is selected automatically. Check the key and output against a trusted hash.":"For PS2 Classics, leave blank to use the placeholder key. Other files need their matching 16-byte klicensee. Only CEX is supported.";CryptoOutput.Text="";CryptoKey.Text="";autoCryptoKey=null;}
    void CryptoBrowseSource(object s,RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="Disc images (*.iso;*.enc)|*.iso;*.enc|All files|*.*"};
        if(d.ShowDialog()!=true)return;
        CryptoSource.Text=d.FileName;
        if(CryptoSystem.SelectedIndex==1 && (string.IsNullOrWhiteSpace(CryptoKey.Text) || string.Equals(CryptoKey.Text,autoCryptoKey,StringComparison.OrdinalIgnoreCase)))
        {
            string candidate=Path.ChangeExtension(d.FileName,".dkey");
            autoCryptoKey=File.Exists(candidate)?candidate:null;
            CryptoKey.Text=autoCryptoKey??"";
            CryptoStatus.Text=autoCryptoKey is null?"No matching .dkey file found beside the ISO.":"Found a matching .dkey filename beside the ISO. Verify the key and output against a trusted source.";
        }
    }
    void CryptoBrowseOutput(object s,RoutedEventArgs e){bool decrypt=CryptoMode.SelectedIndex==1,ps3=CryptoSystem.SelectedIndex==1;var d=new SaveFileDialog{Filter=ps3?"PS3 ISO (*.iso)|*.iso":decrypt?"PS2 ISO (*.iso)|*.iso":"PS2 Classics (*.enc)|*.enc",FileName=ps3?(decrypt?"ps3-decrypted.iso":"ps3-encrypted.iso"):decrypt?"decrypted.iso":"ISO.BIN.ENC",OverwritePrompt=false};if(d.ShowDialog()==true)CryptoOutput.Text=d.FileName;}
    void CryptoBrowseKey(object s,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Key files|*.bin;*.key;*.klc;*.dkey|All files|*.*"};if(d.ShowDialog()==true)CryptoKey.Text=d.FileName;}
    void CancelCrypto(object s,RoutedEventArgs e)=>cryptoCancel?.Cancel();
    async void RunCrypto(object s,RoutedEventArgs e)
    {
        string source=CryptoSource.Text.Trim(),target=CryptoOutput.Text.Trim(),key=CryptoKey.Text.Trim();bool decrypt=CryptoMode.SelectedIndex==1,ps3=CryptoSystem.SelectedIndex==1;
        if(source.Length==0||target.Length==0){ShowMessage("Choose both an input and an output file.","PS2 & PS3 Classics conversion");return;}
        cryptoCancel=new CancellationTokenSource();var cancel=cryptoCancel;CryptoRun.IsEnabled=false;CryptoCancel.IsEnabled=true;CryptoProgress.IsIndeterminate=true;CryptoStatus.Text="Preparing conversion…";
        var progress=new Progress<double>(p=>{CryptoProgress.IsIndeterminate=false;CryptoProgress.Value=p*100;CryptoStatus.Text=$"Converting… {p:P0}";});
        try{await Task.Run(()=>ClassicsCrypto.Convert(source,target,key,ps3,decrypt,p=>((IProgress<double>)progress).Report(p),cancel.Token));CryptoStatus.Text="Conversion complete";ShowMessage("Conversion completed successfully.\n\nSaved to:\n"+Path.GetFullPath(target),"Conversion complete");}
        catch(OperationCanceledException){CryptoStatus.Text="Conversion canceled";}
        catch(Exception ex) when(ex is not OutOfMemoryException){CryptoStatus.Text="Conversion failed";ShowMessage(ex.Message,"Could not convert image");}
        finally{CryptoProgress.IsIndeterminate=false;CryptoRun.IsEnabled=true;CryptoCancel.IsEnabled=false;cryptoCancel=null;cancel.Dispose();}
    }
    void BrowseDisc(object s,RoutedEventArgs e){bool ps1=SystemChoice.SelectedIndex==0;var d=new OpenFileDialog{Multiselect=true,Filter=ps1?"PS1 disc (*.bin;*.iso)|*.bin;*.iso":"PS2 ISO (*.iso)|*.iso"};if(d.ShowDialog()!=true)return;foreach(var path in d.FileNames){if(items.Any(x=>string.Equals(x.Path,path,StringComparison.OrdinalIgnoreCase)))continue;items.Add(new(path,GameCatalog.Identify(path,ps1,Toolkit.Text.Trim()),Path.GetFileName(path)));}if(DiscList.SelectedIndex<0&&items.Count>0)DiscList.SelectedIndex=0;RefreshStatus();StartArtworkChecks();}
    void DiscSelected(object s,SelectionChangedEventArgs e){if(DiscList.SelectedItem is DiscItem x){GameTitle.Text=Path.GetFileNameWithoutExtension(x.Name);GameId.Text=x.Id;}if(IsLoaded){RefreshStatus();StartManualCheck();}}
    void RemoveDisc(object s,RoutedEventArgs e){int i=DiscList.SelectedIndex;if(i<0)return;items.RemoveAt(i);if(items.Count>0)DiscList.SelectedIndex=Math.Min(i,items.Count-1);RefreshStatus();StartArtworkChecks();StartManualCheck();}
    void BrowseIcon(object s,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Image files|*.png;*.jpg;*.jpeg;*.bmp"};if(d.ShowDialog()==true)IconPath.Text=d.FileName;}
    void BrowseBackground(object s,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Image files|*.png;*.jpg;*.jpeg;*.bmp"};if(d.ShowDialog()==true)BackgroundPath.Text=d.FileName;}
    void BrowseToolkit(object s,RoutedEventArgs e){var d=new OpenFolderDialog();if(d.ShowDialog()==true)Toolkit.Text=d.FolderName;}
    void UseBuiltIn(object s,RoutedEventArgs e)=>Toolkit.Text=BuiltInKit.Name;
    void BrowsePatch(object s,RoutedEventArgs e){var d=new OpenFileDialog{Filter="PS1 patches (*.ppf;*.xdelta)|*.ppf;*.xdelta"};if(d.ShowDialog()==true)PatchPath.Text=d.FileName;}
    void BrowseLua(object s,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Lua configs (*.lua)|*.lua"};if(d.ShowDialog()==true)LuaPath.Text=d.FileName;}
    void OpenCompatibility(object s,RoutedEventArgs e){string system=SystemChoice.SelectedIndex==0?"PS1":"PS2";try{Process.Start(new ProcessStartInfo($"https://www.psdevwiki.com/ps4/{system}_Classics_Emulator_Compatibility_List"){UseShellExecute=true});}catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or InvalidOperationException){ShowMessage("Could not open the compatibility list: "+ex.Message);}}
    void BrowseOutput(object s,RoutedEventArgs e){var d=new OpenFolderDialog();if(d.ShowDialog()==true)Output.Text=d.FolderName;}
    void CancelBuild(object s,RoutedEventArgs e)
        =>RequestBuildCancellation();

    void RequestBuildCancellation()
    {
        if(cts is null || cts.IsCancellationRequested)return;
        Cancel.IsEnabled=false;
        ProgressStatus.Text="Cancelling build…";
        cts.Cancel();
    }
    void ShowMessage(string message, string title="Check the details") => new ThemeDialog(this, message, title).ShowDialog();
    void About_Click(object s,RoutedEventArgs e)=>new AboutDialog(this).ShowDialog();
    void SystemChanged(object s,SelectionChangedEventArgs e){if(!IsLoaded)return;RefreshStatus();StartArtworkChecks();StartManualCheck();}
    void ArtworkChanged(object s,RoutedEventArgs e){if(IsLoaded)StartArtworkChecks();}
    void ArtworkPathChanged(object s,TextChangedEventArgs e){if(IsLoaded)UpdateArtworkPreview();}
    void CoverStyleChanged(object s,SelectionChangedEventArgs e){if(IsLoaded)StartArtworkChecks();}
    void PreviewHome(object s,RoutedEventArgs e){string icon=IconPath.Text.Trim();if(icon.Length==0&&DiscList.SelectedItem is DiscItem item&&foundCovers.TryGetValue(item.Path,out var cover))icon=cover;Ps4Preview.Show(this,GameTitle.Text.Trim(),icon,BackgroundPath.Text.Trim(),SystemChoice.SelectedIndex==0,"");}
    void BrowseManualFolder(object s,RoutedEventArgs e){var d=new OpenFolderDialog{Title="Choose a PS2 manual library"};if(d.ShowDialog()==true)ManualFolder.Text=d.FolderName;}
    void ManualOptionChanged(object s,RoutedEventArgs e){if(IsLoaded)StartManualCheck();}
    void ManualFolderChanged(object s,TextChangedEventArgs e){if(IsLoaded)StartManualCheck();}
    void OpenManual(object s,RoutedEventArgs e){if(checkedManual is null)return;try{ManualViewer.Show(this,checkedManual);}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException){ShowMessage("Could not open the manual: "+ex.Message,"Manual preview");}}
    async void StartManualCheck()
    {
        if(!IsLoaded || ManualStatus is null)return;int generation=++manualGeneration;checkedManual=null;ShowManual.IsEnabled=false;
        if(DiscList.SelectedItem is not DiscItem item){ManualStatus.Text="● Manual: waiting";return;}
        bool ps1=SystemChoice.SelectedIndex==0,online=OnlineManual.IsChecked==true;string folder=ManualFolder.Text.Trim();ManualStatus.Text="● Manual: checking (PC preview only)";
        try {
            string? path=await Task.Run(async()=>{
                string? found=ps1?ManualValidation.FindPs1(item.Path):Ps2ManualLibrary.Find(item.Path,folder);
                if(found is null&&!ps1)found=await OnlinePs2Manual.FindAsync(item.Path,online);
                if(found is null)return null;
                if(Path.GetExtension(found).Equals(".pdf",StringComparison.OrdinalIgnoreCase)&&ps1){Directory.CreateDirectory(artworkTemp);string converted=Path.Combine(artworkTemp,Guid.NewGuid().ToString("N")+".zip");PdfManual.ConvertToArchive(found,converted);found=converted;}
                if(Directory.Exists(found)||!new[]{".zip",".cbz"}.Contains(Path.GetExtension(found),StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Only PDF and image ZIP/CBZ manuals can be previewed on this PC.");
                if(ManualValidation.Check(found,false) is { } problem)throw new InvalidDataException(problem);return found;
            });
            if(generation!=manualGeneration)return;checkedManual=path;ShowManual.IsEnabled=path is not null;ManualStatus.Text=path is null?"● Manual: not found (PC preview only)":"● Manual: verified for PC preview; not packaged";ManualStatus.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(path is null?"#FF676B":"#43D6A0"));
        } catch(Exception ex) when(ex is not OutOfMemoryException){if(generation==manualGeneration){ManualStatus.Text="● Manual: unavailable (PC preview only)";Log.AppendText("Manual check: "+ex.Message+"\n");}}
    }
    async void RefreshStatus()
    {
        if(!IsLoaded || DiscStatus is null)return;
        int generation=++statusGeneration; bool ps1=SystemChoice.SelectedIndex==0; var item=DiscList.SelectedItem as DiscItem;
        UpdateArtworkPreview();
        if(item is null){DiscStatus.Text="● Disc: waiting";AudioStatus.Text="● Disc audio: waiting";ProtectionStatus.Text="● PS1 protection: waiting";ProfileStatus.Text="● PS2 profile: waiting";return;}
        string cue=Path.ChangeExtension(item.Path,".cue");
        string? error=!File.Exists(item.Path)?"image missing":ps1?(!File.Exists(cue)?"CUE missing":CueValidation.Check(cue)):null;
        DiscStatus.Text=error is null?"● Disc: files found":"● Disc: "+error;
        DiscStatus.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(error is null?"#43D6A0":"#FF676B"));
        var audio=ps1&&File.Exists(cue)?CueValidation.InspectAudio(cue):CueAudioStatus.Unknown;
        AudioStatus.Text=ps1?"● Disc audio: "+(audio==CueAudioStatus.Present?"files found":audio==CueAudioStatus.Missing?"missing files":audio==CueAudioStatus.None?"no audio tracks":"unknown"):"● Disc audio: PS1 only";
        AudioStatus.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(audio==CueAudioStatus.Present?"#43D6A0":audio==CueAudioStatus.Missing?"#FF676B":"#9EADC0"));
        ProtectionStatus.Text=ps1?"● PS1 protection: checking":"● PS1 protection: PS1 only";
        ProfileStatus.Text=ps1?"● PS2 profile: PS2 only":"● PS2 profile: checking";
        if(ps1)try { string kit=Toolkit.Text.Trim(); var result=await Task.Run(()=>{string? serial=DiscSystemDetector.ReadPs1Serial(item.Path);return (serial,value:serial is null?null:Ps1Protection.Find(kit,serial),sbi:File.Exists(Path.ChangeExtension(item.Path,".sbi")));});if(generation!=statusGeneration)return;ProtectionStatus.Text=result.serial is null?"● PS1 protection: disc ID unknown":result.value is not null?"● PS1 protection: matching LibCrypt setting found":"● PS1 protection: not listed (unknown)";if(result.sbi)ProtectionStatus.Text+=" · SBI not imported";ProtectionStatus.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(result.value is null?"#9EADC0":"#43D6A0")); }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException){if(generation==statusGeneration)ProtectionStatus.Text="● PS1 protection: check unavailable";}
        if(!ps1)try{var profile=await Task.Run(()=>{using var stream=File.OpenRead(item.Path);string serial=DiscMetadata.ReadSerial(stream);return(serial,found:Ps2ProfileIndex.Contains(serial));});if(generation!=statusGeneration)return;ProfileStatus.Text=profile.found?$"● PS2 profile: {profile.serial} found in built-in database":$"● PS2 profile: {profile.serial} not found";ProfileStatus.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(profile.found?"#43D6A0":"#9EADC0"));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException){if(generation==statusGeneration)ProfileStatus.Text="● PS2 profile: check unavailable";}

    }
    void UpdateArtworkPreview()
    {
        string manual=IconPath.Text.Trim();
        var item=DiscList.SelectedItem as DiscItem;
        string? path=manual.Length>0?manual:item is not null&&foundCovers.TryGetValue(item.Path,out var found)?found:null;
        bool valid=false;
        CoverPreview.Source=null;
        if(path is not null && File.Exists(path))
        {
            try
            {
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth=300;bitmap.UriSource=new Uri(Path.GetFullPath(path));bitmap.EndInit();bitmap.Freeze();
                CoverPreview.Source=bitmap;valid=true;
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or System.Runtime.InteropServices.COMException) { }
        }
        CoverPreviewFrame.Visibility=valid?Visibility.Visible:Visibility.Collapsed;
        CoverPreviewLabel.Text=manual.Length>0?"Custom image":CoverStyle.SelectedIndex==1?"3D cover":"Standard cover";
        bool checking=manual.Length==0 && item is not null && artworkCancel is not null && !valid;
        ArtworkStatus.Text=valid?"● Artwork: cover found":manual.Length>0?"● Artwork: selected image unavailable":item is null?"● Artwork: waiting":checking?"● Artwork: checking":"● Artwork: not found";
        ArtworkStatus.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(valid?"#43D6A0":checking||item is null&&manual.Length==0?"#9EADC0":"#FF676B"));
    }
    async void StartArtworkChecks()
    {
        if(!IsLoaded)return;
        artworkCancel?.Cancel();var cancel=new CancellationTokenSource();artworkCancel=cancel;foundCovers.Clear();
        bool ps1=SystemChoice.SelectedIndex==0,online=AutoArtwork.IsChecked==true,threeDimensional=CoverStyle.SelectedIndex==1;
        var queue=items.ToArray();UpdateArtworkPreview();
        try
        {
            Directory.CreateDirectory(artworkTemp);
            foreach(var item in queue)
            {
                cancel.Token.ThrowIfCancellationRequested();string dir=Path.Combine(artworkTemp,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
                string? cover=await Ps2CoverLookup.DownloadAsync(item.Id,dir,_=>{},cancel.Token,ps1,threeDimensional,online);
                if(artworkCancel==cancel && cover is not null){foundCovers[item.Path]=cover;UpdateArtworkPreview();}
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){Log.AppendText("Artwork check unavailable: "+ex.Message+"\n");}
        finally {if(artworkCancel==cancel){artworkCancel=null;UpdateArtworkPreview();}cancel.Dispose();}
    }
    async void VerifyDiscs(object s,RoutedEventArgs e)
    {
        if(verifyCancel is not null){verifyCancel.Cancel();return;}if(items.Count==0||cts is not null)return;
        bool ps1=SystemChoice.SelectedIndex==0;var queue=items.ToArray();using var cancel=new CancellationTokenSource();verifyCancel=cancel;Verify.Content="Cancel verification";Build.IsEnabled=false;
        Log.AppendText("Verifying supported discs using the offline Redump snapshot.\n");
        try {foreach(var item in queue){cancel.Token.ThrowIfCancellationRequested();var progress=new Progress<int>(p=>{if(verifyCancel==cancel)Verify.Content=$"Cancel check {p}%";});try {string source=ps1?Path.ChangeExtension(item.Path,".cue"):item.Path;var result=await Task.Run(()=>ps1?Ps1DiscVerifier.VerifyAsync(source,progress,cancel.Token):Ps2DiscVerifier.VerifyAsync(source,progress,cancel.Token),cancel.Token);Log.AppendText($"{item.Name}: {result.Status} — {result.Detail}\n");}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){Log.AppendText($"{item.Name}: verification unavailable — {ex.Message}\n");}}}
        catch(OperationCanceledException){Log.AppendText("Verification cancelled.\n");}finally{verifyCancel=null;Verify.Content="Verify discs offline";Build.IsEnabled=true;Log.ScrollToEnd();}
    }
    async void BuildPackage(object s,RoutedEventArgs e){
        if(cts is not null)return;
        if(verifyCancel is not null){ShowMessage("Wait for disc verification to finish or cancel it first.");return;}
        var list=items.ToArray();var title=GameTitle.Text.Trim();var id=GameId.Text.Trim().ToUpperInvariant();var kit=Toolkit.Text.Trim();var output=Output.Text.Trim();bool ps1=SystemChoice.SelectedIndex==0,multi=PackageMode.SelectedIndex==1;string icon=IconPath.Text.Trim(),background=BackgroundPath.Text.Trim();bool analog=Analog.IsChecked==true, skip=SkipBoot.IsChecked==true, hz=Force60.IsChecked==true, mtvu=DisableMTVU.IsChecked==true, graphics=GraphicsFix.IsChecked==true, wide=!ps1 && Widescreen.IsChecked==true;
        if(list.Length==0||string.IsNullOrWhiteSpace(title)||!System.Text.RegularExpressions.Regex.IsMatch(id,@"^[A-Z]{4}\d{5}$")||!Directory.Exists(output)){ShowMessage("Add disc images, select an output folder, and enter a title and a nine-character game ID.","Check the details");return;}
        string patch=PatchPath.Text.Trim();if(patch.Length>0&&(!ps1||list.Length!=1||multi)){ShowMessage("A PS1 patch currently requires one PS1 disc and a separate package.");return;}
        string customLua=LuaPath.Text.Trim();if(customLua.Length>0&&(list.Length!=1||multi||(!ps1&&wide))){ShowMessage("A custom PS4 Lua config requires one disc and cannot be combined with automatic PS2 widescreen.");return;}
        if(patch.Length>0&&!File.Exists(patch)){ShowMessage("The selected PS1 patch was not found.");return;}
        if(artworkCancel is not null&&icon.Length==0){ShowMessage("Wait for the artwork check to finish before creating the package.");return;}
        if(multi&&DiscList.SelectedIndex!=0){ShowMessage("Select disc 1 before creating a multi-disc package so its title and game ID are used.");return;}
        if(wide&&multi&&list.Length>1){ShowMessage("Automatic widescreen matching currently supports one PS2 disc per package. Build this game without widescreen, or use a single disc.");return;}
        if(multi&&list.Length>(ps1?4:5)){ShowMessage("Multi-disc packages support up to four PS1 or five PS2 discs.");return;}
        if(list.Any(x=>!File.Exists(x.Path)||!(ps1?(x.Path.EndsWith(".bin",StringComparison.OrdinalIgnoreCase)||x.Path.EndsWith(".iso",StringComparison.OrdinalIgnoreCase)):x.Path.EndsWith(".iso",StringComparison.OrdinalIgnoreCase))||(ps1&&!File.Exists(Path.ChangeExtension(x.Path,".cue"))))){ShowMessage("Each PS1 disc needs a BIN or raw ISO with a matching CUE; PS2 discs need an ISO. Check the selected system.");return;}
        int selected=DiscList.SelectedIndex; if(!multi&&list.Where((_,i)=>i!=selected).Any(x=>string.IsNullOrWhiteSpace(x.Id))){ShowMessage("A queued disc has no detected game ID. Process it individually and enter its ID.");return;}
        var ids=multi?new[]{id}:list.Select((x,i)=>i==selected?id:x.Id).ToArray();if(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=ids.Length){ShowMessage("Two queued games have the same ID. Process them separately.");return;}
        bool autoProfile=AutoProfile.IsChecked==true;
        var covers=new Dictionary<string,string>(foundCovers,StringComparer.OrdinalIgnoreCase);
        using var build=new CancellationTokenSource();
        cts=build;Build.IsEnabled=false;Cancel.IsEnabled=true;Progress.IsIndeterminate=false;Progress.Value=0;ProgressStatus.Text="Estimated 0% - calculating time";Log.Clear();
        var successes=new List<string>(); var failures=new List<string>(); bool cancelled=false;
        var clock=Stopwatch.StartNew(); double shown=0;
        void Report(string text)
        {
            void Append(){Log.AppendText(text+Environment.NewLine);Log.ScrollToEnd();}
            if(Dispatcher.CheckAccess())Append();
            else Dispatcher.BeginInvoke(()=>{if(cts==build)Append();});
        }
        try {
            var token=build.Token;int count=multi?1:list.Length;
            for(int i=0;i<count;i++){
                token.ThrowIfCancellationRequested();int index=i;var item=multi?list[Math.Max(selected,0)]:list[i];
                string buildId=multi?id:ids[i];string buildTitle=multi||i==selected?title:Path.GetFileNameWithoutExtension(item.Name);
                string[] extra=multi?list.Skip(1).Select(x=>x.Path).ToArray():Array.Empty<string>();string first=multi?list[0].Path:item.Path;
                Report($"Building {index+1} of {count}: {buildTitle}");
                void Update(double fraction)=>Dispatcher.BeginInvoke(()=>{
                    if(cts!=build || token.IsCancellationRequested)return;
                    double pct=Math.Clamp((index+fraction)/count*100,0,99);
                    shown=Math.Max(shown,pct);Progress.Value=shown;
                    string eta="calculating time";
                    if(shown>=3 && clock.Elapsed.TotalSeconds>=5){double seconds=clock.Elapsed.TotalSeconds*(100-shown)/shown;eta=seconds<60?"about "+Math.Max(1,(int)Math.Ceiling(seconds))+" sec remaining":"about "+Math.Ceiling(seconds/60)+" min remaining";}
                    ProgressStatus.Text=$"Estimated {shown:0}% - {eta}";
                });
                string chosenIcon=icon;if(chosenIcon.Length==0&&covers.TryGetValue(first,out var autoCover))chosenIcon=autoCover;
                try{string result=await Task.Run(()=>Backend.Build(kit,first,buildTitle,buildId,output,ps1,Report,token,chosenIcon,background,extra,analog,skip,hz,mtvu,graphics,wide,Update,patch,customLua,autoProfile));successes.Add(result);Report("Package created: "+result);}
                catch(OperationCanceledException){throw;}
                catch(Exception ex){failures.Add(buildTitle+": "+ex.Message);Report("Failed: "+ex.Message);}
            }
        }
        catch(OperationCanceledException){cancelled=true;Report("Build cancelled.");}
        finally{Progress.IsIndeterminate=false;Build.IsEnabled=true;Cancel.IsEnabled=false;cts=null;}
        if(closeAfterBuild){closeAfterBuild=false;Close();return;}
        if(cancelled){ProgressStatus.Text="Cancelled";ShowMessage("The build was cancelled."+(successes.Count>0?"\n\nCompleted packages:\n"+string.Join("\n",successes):""),"Build cancelled");}
        else if(failures.Count>0){ProgressStatus.Text="Finished with errors";ShowMessage((successes.Count>0?"Completed packages:\n"+string.Join("\n",successes)+"\n\n":"")+"Some packages could not be created. Review Build Activity for details.","Build finished with errors");}
        else{Progress.Value=100;ProgressStatus.Text="100% - complete";ShowMessage((successes.Count==1?"Package created successfully:\n\n":"Packages created successfully:\n\n")+string.Join("\n",successes),"Build complete");}
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if(cryptoCancel is not null){e.Cancel=true;cryptoCancel.Cancel();ShowMessage("The conversion is being canceled. Close the window after cancellation finishes.","Canceling conversion");return;}
        if(cts is not null){e.Cancel=true;closeAfterBuild=true;RequestBuildCancellation();return;}
        verifyCancel?.Cancel();artworkCancel?.Cancel();base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        statusGeneration++;manualGeneration++;BuiltInKit.Cleanup();try{if(Directory.Exists(artworkTemp))Directory.Delete(artworkTemp,true);}catch(IOException){}catch(UnauthorizedAccessException){}base.OnClosed(e);
    }
}



