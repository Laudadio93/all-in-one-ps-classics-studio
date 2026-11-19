using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Forms.Integration;
using Forms=System.Windows.Forms;

namespace ClassicsSuite;

public partial class MainWindow : Window
{
    private sealed class Studio(string label,string resource,string exeName,WindowsFormsHost host,System.Windows.Controls.Button button)
    {
        public string Label=label,Resource=resource,ExeName=exeName;
        public string Folder=resource.Replace(".exe","");
        public string? View;
        public int MinHeight=650,MinWidth=780;
        public WindowsFormsHost Host=host;
        public System.Windows.Controls.Button Button=button;
        public Forms.Panel Panel=new(){BackColor=System.Drawing.Color.FromArgb(12,20,33),Dock=Forms.DockStyle.Fill};
        public Forms.Label LoadingLabel=new(){BackColor=System.Drawing.Color.FromArgb(12,20,33),ForeColor=System.Drawing.Color.FromArgb(158,173,192),Dock=Forms.DockStyle.Fill,TextAlign=System.Drawing.ContentAlignment.MiddleCenter,Font=new System.Drawing.Font("Segoe UI",11)};
        public Process? Process;
        public IntPtr Window;
        public string? Path;
        public bool Starting;
        public bool NeedsFrameRefresh=true;
        public bool HiddenAfterStart;
        public System.Drawing.Rectangle? Bounds;
        public double ScrollY,ScrollX;
    }
    private Studio[] studios=[];
    private Studio? selected;
    private readonly string session=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"PlayStationClassicsStudio-"+Guid.NewGuid().ToString("N"));
    private static readonly string componentCache=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"PlayStationClassicsStudio-component-cache");
    private static readonly Lazy<Dictionary<string,byte[]>> componentHashes=new(ReadComponentHashes);
    private readonly Dictionary<string,object> extractionLocks=new(){{"ps4.exe",new()},{"ps3.exe",new()},{"memory.exe",new()}};
    private bool allowClose, closing;
    private MouseHook? mouseHook;
    private IntPtr mouseHookHandle;
    private bool updatingWorkspace;
    private static readonly SolidColorBrush ActiveTabBrush=TabBrush(0x14,0x61,0x7b);
    private static readonly SolidColorBrush IdleTabBrush=TabBrush(0x1e,0x30,0x44);
    private static SolidColorBrush TabBrush(byte r,byte g,byte b)
    {
        var brush=new SolidColorBrush(System.Windows.Media.Color.FromRgb(r,g,b));
        brush.Freeze();
        return brush;
    }
    public MainWindow()
    {
        InitializeComponent();
        MaxHeight=SystemParameters.WorkArea.Height;Height=Math.Min(Height,MaxHeight);MaxWidth=SystemParameters.WorkArea.Width;Width=Math.Min(Width,MaxWidth);
        studios=[new("PS3 Package Studio","ps3.exe","PS1 and PS2 Classics Package Studio.exe",MainHost,Ps3Button),new("PS4 Package Studio","ps4.exe","PS4 Classics Package Studio.exe",MainHost,Ps4Button),new("PS2 Memory Card Studio","memory.exe","PS2 Memory Card Studio.exe",MainHost,MemoryButton),new("PS2 & PS3 Encrypt / Decrypt","ps4.exe","PS4 Classics Package Studio.exe",MainHost,CryptoButton){Folder="crypto",View="crypto"}];
        studios=[..studios,new("PS1 Memory Card Studio","memory.exe","PS2 Memory Card Studio.exe",MainHost,Ps1MemoryButton){Folder="memory-ps1",View="ps1-memory",MinWidth=1050,MinHeight=880}];
        studios[1].View="ps4";
        studios[0].MinWidth=980;studios[0].MinHeight=830;studios[2].MinWidth=1050;studios[2].MinHeight=880;
        var container=new Forms.Panel { BackColor=System.Drawing.Color.FromArgb(12,20,33), Dock=Forms.DockStyle.Fill };
        MainHost.Child=container;
        foreach(var item in studios)
        {
            item.LoadingLabel.Text="Preparing "+item.Label+"…";
            item.Panel.Controls.Add(item.LoadingLabel);
            item.Panel.Visible=false;container.Controls.Add(item.Panel);
            item.Panel.Resize+=(_,_)=>ResizeStudio(item);
        }
        Loaded+=async (_,e)=>{if(ReferenceEquals(e.OriginalSource,this) && selected is null)await Select(studios[1]);};
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int enabled=1;
        DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,20,ref enabled,sizeof(int));
        mouseHook=OnMouseWheel;
        mouseHookHandle=SetWindowsHookEx(14,mouseHook,GetModuleHandle(null),0);
    }
    private async void SelectPs3(object sender,RoutedEventArgs e)=>await Select(studios[0]);
    private async void SelectPs4(object sender,RoutedEventArgs e)=>await Select(studios[1]);
    private async void SelectMemory(object sender,RoutedEventArgs e)=>await Select(studios[2]);
    private async void SelectPs1Memory(object sender,RoutedEventArgs e)=>await Select(studios[4]);
    private async void SelectCrypto(object sender,RoutedEventArgs e)=>await Select(studios[3]);
    private async Task Select(Studio item)
    {
        if(closing || (selected==item && (item.Starting || (item.Window!=IntPtr.Zero && IsWindow(item.Window)))))return;
        if(selected is not null && selected.Window!=IntPtr.Zero && IsWindow(selected.Window))
        {
            selected.ScrollY=WorkspaceScroll.Value;
            selected.ScrollX=WorkspaceHorizontal.Value;
        }
        var previous=selected;
        selected=item;
        updatingWorkspace=true;
        if(previous!=item)
        {
            if(previous is not null){previous.Panel.Visible=false;previous.Button.Background=IdleTabBrush;}
            item.Panel.Visible=true;item.Button.Background=ActiveTabBrush;
        }
        updatingWorkspace=false;
        if(item.Window!=IntPtr.Zero && IsWindow(item.Window))
        {
            item.LoadingLabel.Visible=false;Loading.Visibility=Visibility.Collapsed;Status.Text=item.Label+" · ready";
            ResizeStudio(item,restoreScroll:true);
            if(item.HiddenAfterStart){ShowWindowAsync(item.Window,4);item.HiddenAfterStart=false;}
            return;
        }
        updatingWorkspace=true;
        WorkspaceScroll.Value=0;WorkspaceHorizontal.Value=0;
        WorkspaceScroll.Visibility=Visibility.Collapsed;WorkspaceHorizontal.Visibility=Visibility.Collapsed;
        updatingWorkspace=false;
        item.LoadingLabel.Text="Preparing "+item.Label+"…";item.LoadingLabel.Visible=true;item.LoadingLabel.BringToFront();
        if(item.Starting){Loading.Visibility=Visibility.Visible;Status.Text="Starting "+item.Label+"…";return;}
        item.Starting=true;Loading.Visibility=Visibility.Visible;Loading.Text="Preparing "+item.Label+"…";Status.Text="Loading built-in studio. The first launch can take a moment.";
        try
        {
            string dir=System.IO.Path.Combine(session,item.Folder);
            string exe=System.IO.Path.Combine(dir,item.ExeName);item.Path=exe;
            await Task.Run(()=>{
                Directory.CreateDirectory(dir);ExtractStudio(item.Resource,exe);
                if(item.Resource=="ps3.exe")Extract("ThirdParty.zip",System.IO.Path.Combine(dir,"ThirdParty.zip"));
            });
            if(closing)return;
            var info=new ProcessStartInfo(exe){UseShellExecute=false,WorkingDirectory=dir,WindowStyle=ProcessWindowStyle.Hidden};
            if(item.View=="ps1-memory")info.Environment["CLASSICSSUITE_MEMORY_VIEW"]="ps1";
            else if(item.View is not null)info.Environment["CLASSICSSUITE_VIEW"]=item.View;
            item.Process=await Task.Run(()=>Process.Start(info)??throw new IOException("Windows could not start the studio."));
            int launcherPid=item.Process.Id;
            bool usesChildProcess=item.Resource=="memory.exe";
            var timeout=Stopwatch.StartNew();IntPtr hwnd=IntPtr.Zero;
            while(timeout.Elapsed<TimeSpan.FromSeconds(60)){
                hwnd=await Task.Run(()=>FindStudioWindow(exe,launcherPid,usesChildProcess));if(hwnd!=IntPtr.Zero)break;
                if(item.Process.HasExited)throw new IOException("The studio exited before opening.");
                await Task.Delay(timeout.ElapsedMilliseconds<2000?40:200);
            }
            if(hwnd==IntPtr.Zero)throw new IOException("The studio did not open within 60 seconds.");
            IntPtr parentHandle=item.Panel.Handle;
            await Task.Run(()=>{
                var style=GetWindowLongPtr(hwnd,-16).ToInt64();
                SetWindowLongPtr(hwnd,-16,new IntPtr((style | 0x40000000L) & ~0x80000000L & ~0x00C00000L & ~0x00040000L & ~0x00080000L));
                Marshal.SetLastPInvokeError(0);SetParent(hwnd,parentHandle);int error=Marshal.GetLastPInvokeError();
                if(error!=0){PostMessage(hwnd,0x0010,IntPtr.Zero,IntPtr.Zero);throw new Win32Exception(error,"The studio window could not be attached to the tab.");}
            });
            item.Window=hwnd;item.Bounds=null;
            item.NeedsFrameRefresh=true;
            ResizeStudio(item);item.LoadingLabel.Visible=false;item.HiddenAfterStart=closing||selected!=item;ShowWindowAsync(hwnd,item.HiddenAfterStart?0:4);
            if(!closing && selected==item){Loading.Visibility=Visibility.Collapsed;Status.Text=item.Label+" · ready";}
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            if(item.Window!=IntPtr.Zero){PostMessage(item.Window,0x0010,IntPtr.Zero,IntPtr.Zero);item.Window=IntPtr.Zero;}
            item.LoadingLabel.Text="Could not open "+item.Label+". Select its tab to retry.";item.LoadingLabel.Visible=true;
            if(selected==item){Loading.Visibility=Visibility.Visible;Loading.Text=item.LoadingLabel.Text;Status.Text=ex.Message;}
        }
        finally{item.Starting=false;}
    }
    private static void Extract(string resource,string target)
    {
        if(File.Exists(target))return;
        using var input=typeof(MainWindow).Assembly.GetManifestResourceStream("ClassicsSuite.Resources."+resource)??throw new IOException("A built-in studio resource is missing: "+resource);
        try { using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);input.CopyTo(output); }
        catch { try{File.Delete(target);}catch(IOException){}catch(UnauthorizedAccessException){} throw; }
    }
    private void ExtractStudio(string resource,string target)
    {
        // The PS4/encryption and PS1/PS2 memory tabs share their respective
        // executable. Extract each one once per session while retaining a
        // separate executable path so window ownership remains unambiguous.
        lock(extractionLocks[resource])
        {
            if(File.Exists(target))return;
            string shared=System.IO.Path.Combine(session,"shared");
            Directory.CreateDirectory(shared);
            string source=System.IO.Path.Combine(shared,resource);
            if(!File.Exists(source) && !TryReuseCachedComponent(resource,source))Extract(resource,source);
            if(!CreateHardLink(target,source,IntPtr.Zero))File.Copy(source,target,false);
        }
    }
    private static bool TryReuseCachedComponent(string resource,string target)
    {
        // Always compare the cached executable with the embedded component.
        // A missing, modified, locked, or unwritable cache falls back to the
        // session-only extraction path. Cache files are never trusted by name.
        string? temporary=null;
        try
        {
            Directory.CreateDirectory(componentCache);
            string cached=System.IO.Path.Combine(componentCache,resource);
            bool matches=false;
            if(File.Exists(cached))
            {
                if(!componentHashes.Value.TryGetValue(resource,out byte[]? expected))return false;
                using var candidate=new FileStream(cached,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                matches=CryptographicOperations.FixedTimeEquals(expected,SHA256.HashData(candidate));
            }
            if(!matches)
            {
                temporary=System.IO.Path.Combine(componentCache,resource+"."+Guid.NewGuid().ToString("N")+".tmp");
                Extract(resource,temporary);
                File.Move(temporary,cached,true);temporary=null;
            }
            if(!CreateHardLink(target,cached,IntPtr.Zero))File.Copy(cached,target,false);
            return true;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or Win32Exception or CryptographicException){return false;}
        finally{if(temporary is not null)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private static Dictionary<string,byte[]> ReadComponentHashes()
    {
        using var input=typeof(MainWindow).Assembly.GetManifestResourceStream("ClassicsSuite.Resources.component-hashes.txt")??throw new IOException("Missing component verification data.");
        using var reader=new StreamReader(input);
        var result=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        while(reader.ReadLine() is { } line)
        {
            int split=line.IndexOf('=');
            if(split<=0)throw new IOException("Invalid component verification data.");
            try { result.Add(line[..split],Convert.FromHexString(line[(split+1)..])); }
            catch(FormatException ex){throw new IOException("Invalid component verification data.",ex);}
        }
        return result;
    }
    private static IntPtr FindStudioWindow(string exe,int launcherPid,bool usesChildProcess)
    {
        var expectedTitle=System.IO.Path.GetFileNameWithoutExtension(exe);
        var title=new System.Text.StringBuilder(128);
        IntPtr found=IntPtr.Zero;
        EnumWindows((hwnd,_)=>{
            GetWindowThreadProcessId(hwnd,out uint pid);
            if(pid!=launcherPid && !usesChildProcess)return true;
            if(GetWindowText(hwnd,title,title.Capacity)!=expectedTitle.Length ||
               !string.Equals(title.ToString(),expectedTitle,StringComparison.OrdinalIgnoreCase))return true;
            // PyInstaller owns the window in a second process. The identical
            // title of the other card studio is insufficient to establish ownership.
            if(pid!=launcherPid)
            {
                try
                {
                    using var process=Process.GetProcessById((int)pid);
                    if(!string.Equals(process.MainModule?.FileName,exe,StringComparison.OrdinalIgnoreCase))return true;
                }
                catch(Exception ex) when(ex is Win32Exception or ArgumentException or InvalidOperationException){return true;}
            }
            found=hwnd;return false;
        },IntPtr.Zero);
        return found;
    }
    private void WorkspaceScrollChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(!updatingWorkspace && selected is not null)ResizeStudio(selected);}
    private IntPtr OnMouseWheel(int code,IntPtr message,IntPtr data)
    {
        if(code>=0 && message.ToInt64()==0x020A && selected is { } item && item.Window!=IntPtr.Zero)
        {
            var info=Marshal.PtrToStructure<MouseHookData>(data);
            IntPtr under=WindowFromPoint(info.Point);
            if(under==item.Window || IsChild(item.Window,under))
            {
                int delta=unchecked((short)(info.MouseData >> 16));
                double next=Math.Clamp(WorkspaceScroll.Value-delta*0.6,0,WorkspaceScroll.Maximum);
                if(next!=WorkspaceScroll.Value)
                {
                    WorkspaceScroll.Value=next;
                    return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(mouseHookHandle,code,message,data);
    }
    private void ResizeStudio(Studio item,bool restoreScroll=false)
    {
        if(updatingWorkspace||selected!=item||item.Window==IntPtr.Zero||!IsWindow(item.Window)||!item.Panel.IsHandleCreated)return;
        double scale=GetDpiForWindow(item.Panel.Handle)/96.0;
        if(scale<=0)scale=1;
        int w=Math.Max((int)Math.Ceiling(item.MinWidth*scale),item.Panel.ClientSize.Width),h=Math.Max((int)Math.Ceiling(item.MinHeight*scale),item.Panel.ClientSize.Height);
        updatingWorkspace=true;
        try
        {
            double maximum=Math.Max(0,h-item.Panel.ClientSize.Height);
            WorkspaceScroll.Maximum=maximum;WorkspaceScroll.ViewportSize=item.Panel.ClientSize.Height;
            WorkspaceScroll.LargeChange=Math.Max(1,item.Panel.ClientSize.Height-80);WorkspaceScroll.SmallChange=48;
            WorkspaceScroll.Visibility=maximum>0?Visibility.Visible:Visibility.Collapsed;
            double horizontal=Math.Max(0,w-item.Panel.ClientSize.Width);
            WorkspaceHorizontal.Maximum=horizontal;WorkspaceHorizontal.ViewportSize=item.Panel.ClientSize.Width;
            WorkspaceHorizontal.LargeChange=Math.Max(1,item.Panel.ClientSize.Width-80);WorkspaceHorizontal.SmallChange=48;
            WorkspaceHorizontal.Visibility=horizontal>0?Visibility.Visible:Visibility.Collapsed;
            if(restoreScroll)
            {
                WorkspaceScroll.Value=Math.Clamp(item.ScrollY,0,maximum);
                WorkspaceHorizontal.Value=Math.Clamp(item.ScrollX,0,horizontal);
            }
        }
        finally{updatingWorkspace=false;}
        int y=-(int)WorkspaceScroll.Value;
        int x=-(int)WorkspaceHorizontal.Value;
        var bounds=new System.Drawing.Rectangle(x,y,w,h);
        if(!item.NeedsFrameRefresh && bounds==item.Bounds)return;
        uint flags=0x0010|0x0004|0x4000;
        if(item.NeedsFrameRefresh)flags|=0x0020;
        if(SetWindowPos(item.Window,IntPtr.Zero,x,y,w,h,flags)){item.Bounds=bounds;item.NeedsFrameRefresh=false;}
    }
    private void About(object sender,RoutedEventArgs e)=>new AboutDialog(this).ShowDialog();
    protected override async void OnClosing(CancelEventArgs e)
    {
        if(allowClose){base.OnClosing(e);return;}
        e.Cancel=true;
        if(closing)return;
        closing=true;
        Status.Text="Closing studios…";
        // Leave WPF's initial Closing event before calling Hide or Close again.
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Normal);
        var requests=new Dictionary<Studio,long>();
        var timer=Stopwatch.StartNew();
        while(true)
        {
            bool pending=false;
            Studio? needsAttention=null;
            foreach(var studio in studios)
            {
                if(studio.Starting){pending=true;continue;}
                if(studio.Window==IntPtr.Zero || !IsWindow(studio.Window))continue;
                pending=true;
                if(!IsWindowEnabled(studio.Window)){needsAttention=studio;continue;}
                if(requests.TryAdd(studio,timer.ElapsedMilliseconds))
                    PostMessage(studio.Window,0x0010,IntPtr.Zero,IntPtr.Zero);
                else if(timer.ElapsedMilliseconds-requests[studio]>=1500)
                    needsAttention=studio;
            }
            if(!pending)break;
            if(needsAttention is not null && !studios.Any(s=>s.Starting))
            {
                closing=false;
                await Select(needsAttention);
                Status.Text="Finish the open operation or dialog, then close the suite.";
                return;
            }
            await Task.Delay(20);
        }
        Hide();
        // The frozen Python launcher can briefly hold the session executable
        // after its Tk window closes. Give it time to release that file.
        var exits=studios.Where(s=>s.Process is not null).Select(s=>s.Process!.WaitForExitAsync());
        try{await Task.WhenAll(exits).WaitAsync(TimeSpan.FromSeconds(3));}
        catch(TimeoutException){ /* Background cleanup may outlive its window. */ }
        allowClose=true;Close();
    }
    protected override void OnClosed(EventArgs e)
    {
        if(mouseHookHandle!=IntPtr.Zero)UnhookWindowsHookEx(mouseHookHandle);
        foreach(var studio in studios)studio.Process?.Dispose();
        try{if(Directory.Exists(session))Directory.Delete(session,true);}catch(IOException){}catch(UnauthorizedAccessException){}base.OnClosed(e);
    }
    private delegate bool EnumWindowsCallback(IntPtr hWnd,IntPtr lParam);
    private delegate IntPtr MouseHook(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseHookData { public NativePoint Point; public uint MouseData,Flags,Time; public IntPtr ExtraInfo; }
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int id,MouseHook callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent,IntPtr child);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="CreateHardLinkW")] private static extern bool CreateHardLink(string name,string existing,IntPtr securityAttributes);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback,IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd,System.Text.StringBuilder text,int maxCount);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetParent(IntPtr child,IntPtr parent);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd,int index,IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd,int command);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd,int command);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd,uint msg,IntPtr w,IntPtr l);
}
