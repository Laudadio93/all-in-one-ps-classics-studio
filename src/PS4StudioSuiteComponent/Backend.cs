using ClassicsStudio.Packaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace PS4Studio;
internal static class Backend
{
    public static string Build(string kit,string disc,string title,string id,string output,bool ps1,Action<string> log,CancellationToken token,string icon="",string background="",string[]? additionalDiscs=null, bool analog=false, bool skipBoot=false, bool force60=false, bool disableMtvu=false, bool graphicsFix=false, bool widescreen=false, Action<double>? progress=null,string patch="",string customLua="",bool autoProfile=true)
    {
        token.ThrowIfCancellationRequested();
        output = Path.GetFullPath(output);
        string final = Path.Combine(output, id + ".pkg");
        if (File.Exists(final)) throw new IOException("The output file already exists: " + final);
        string[] discs = new[]{disc}.Concat(additionalDiscs ?? Array.Empty<string>()).ToArray();
        if(discs.Length > (ps1?4:5)) throw new InvalidDataException("Too many discs for this system.");
        if(!string.IsNullOrWhiteSpace(customLua)){var f=new FileInfo(customLua);if(!f.Exists||!f.Extension.Equals(".lua",StringComparison.OrdinalIgnoreCase)||f.Length is <=0 or >2097152)throw new InvalidDataException("Choose a non-empty Lua config no larger than 2 MiB.");if(discs.Length!=1)throw new InvalidDataException("A custom Lua config requires one disc per package.");if(!ps1&&widescreen)throw new InvalidDataException("Choose either a custom PS2 Lua config or automatic widescreen, not both.");}
        if(kit==BuiltInKit.Name){log("Preparing the built-in PS4 engine.");kit=BuiltInKit.Resolve(kit);}
        string tools=Path.Combine(kit,"Tools"),ps4=Path.Combine(tools,"PS4"),emu=Path.Combine(ps4,"emus",ps1?"ps1hd":"Rogue v1"),generator=Path.Combine(ps4,"gengp4_patch.exe"),packer=Path.Combine(ps4,"mod-pub","orbis-pub-cmd-3.38.exe");
        if(!Directory.Exists(emu)||!File.Exists(generator)||!File.Exists(packer))throw new InvalidDataException("The selected toolkit is missing an emulator or package tool.");
        string job=Path.Combine(output,".ps4studio-"+Guid.NewGuid().ToString("N")); string content=Path.Combine(job,"content");
        Directory.CreateDirectory(content);
        try {
            progress?.Invoke(0);
            log("Preparing emulator files."); CopyDir(emu,content,token);
            Directory.CreateDirectory(Path.Combine(content,"sce_sys"));
            if(!string.IsNullOrWhiteSpace(icon)){log("Preparing package icon."); Artwork.Write(icon,Path.Combine(content,"sce_sys","icon0.png"),512,512);}
            if(!string.IsNullOrWhiteSpace(background)){log("Preparing package background."); Artwork.Write(background,Path.Combine(content,"sce_sys","pic0.png"),1920,1080);}
            string contentId=ps1?$"UP9000-{id}_00-{id}PS1FPKG":$"UP9000-{id}_00-{id}0000001";
            WriteSfo(Path.Combine(content,"sce_sys","param.sfo"),title,id,contentId,!ps1);
            if(ps1){
                var data=Path.Combine(content,"data");Directory.CreateDirectory(data);
                var config=new StringBuilder("--ps4-trophies=0\n--ps5-uds=0\n--trophies=0\n");
                string?[] actualIds=discs.Select(DiscSystemDetector.ReadPs1Serial).ToArray();string? actualId=actualIds[0];
                if(actualId is not null && !actualId.Equals(id,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException($"The entered PS1 game ID {id} does not match disc ID {actualId}.");
                string? protection=actualId is null ? null : Ps1Protection.Find(kit,actualId);
                if(actualIds.Any(x=>x is null))log("A PS1 disc ID could not be read; its automatic LibCrypt matching was skipped.");
                if(discs.Length>1 && actualIds.Any(x=>x is not null&&Ps1Protection.Find(kit,x) is not null))throw new InvalidDataException("A PS1 disc has a LibCrypt profile; automatic protection settings currently require a single-disc package.");
                if(protection is not null) { config.AppendLine("--libcrypt="+protection); log("Applied the matching PS1 LibCrypt configuration."); }
                for(int i=0;i<discs.Length;i++){
                    string source=discs[i], name=$"disc{i+1}", bin=Path.Combine(data,name+".bin"), cue=Path.Combine(data,name+".cue");
                    if(!source.EndsWith(".bin",StringComparison.OrdinalIgnoreCase) && !source.EndsWith(".iso",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("PS1 discs require raw BIN or ISO images with matching CUE files.");
                    if(source.EndsWith(".iso",StringComparison.OrdinalIgnoreCase)){using var rawDisc=File.OpenRead(source);byte[] header=new byte[16];rawDisc.ReadExactly(header);if(header[0]!=0||header[11]!=0||header.Skip(1).Take(10).Any(b=>b!=255)||(header[15]!=1&&header[15]!=2))throw new InvalidDataException("PS1 ISO is not a supported raw 2352-byte sector image.");}
                    log($"Copying PS1 disc {i+1} of {discs.Length}.");CopyFile(source,bin,token, fraction => progress?.Invoke(0.04 + 0.35 * (i + fraction) / discs.Length));
                    if(!string.IsNullOrWhiteSpace(patch)) { if(discs.Length!=1)throw new InvalidDataException("A PS1 patch requires a single disc."); log("Applying patch to a working copy of the PS1 image."); if(Path.GetExtension(patch).Equals(".ppf",StringComparison.OrdinalIgnoreCase))Ps1PpfPatch.Apply(bin,patch,token); else if(Path.GetExtension(patch).Equals(".xdelta",StringComparison.OrdinalIgnoreCase))Ps1XdeltaPatch.Apply(bin,patch,job,token); else throw new InvalidDataException("Choose a PPF or xdelta patch."); }
                    string raw=File.ReadAllText(Path.ChangeExtension(source,".cue"));
                    if(source.EndsWith(".iso",StringComparison.OrdinalIgnoreCase) && (!Regex.IsMatch(raw,@"(?im)^\s*TRACK\s+01\s+MODE[12]/2352\s*$") || new FileInfo(source).Length % 2352 != 0))throw new InvalidDataException("PS1 ISO support currently requires a matching CUE and a raw 2352-byte-sector disc image. Cooked 2048-byte ISOs are not yet supported.");
                    var matches=Regex.Matches(raw,@"(?im)^\s*FILE\s+\""([^\""\r\n]+)\""\s+BINARY\s*$");
                    if(matches.Count!=1||!string.Equals(Path.GetFileName(matches[0].Groups[1].Value),Path.GetFileName(source),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Each CUE must reference only its selected BIN.");
                    File.WriteAllText(cue,Regex.Replace(raw,@"(?im)^\s*FILE\s+\""([^\""\r\n]+)\""\s+BINARY\s*$",$"FILE \"{name}.bin\" BINARY"));
                    config.AppendLine($"--image=\"data/{name}.bin\"");
                    log($"Generating track information for disc {i+1}."); Run(Path.Combine(tools,"cue2toc.exe"),[cue],data,token);
                    if(!File.Exists(Path.Combine(data,name+".toc")))throw new InvalidDataException("The toolkit did not generate the PS1 track table.");
                }
                if(!string.IsNullOrWhiteSpace(customLua)){string scripts=Path.Combine(content,"scripts");Directory.CreateDirectory(scripts);File.Copy(customLua,Path.Combine(scripts,id+".lua"),true);config.AppendLine("--ps1-title-id="+id);log("Added the selected PS1 Lua configuration.");}
                config.AppendLine("--scale=1");if(analog)config.AppendLine("--sim-analog-pad=0x2020");if(skipBoot)config.AppendLine("--bios-hide-sce-osd=1");if(force60)config.AppendLine("--gpu-scanout-fps-override=60");File.WriteAllText(Path.Combine(content,"config-title.txt"),config.ToString());
            } else {
                string serial=id[..4]+"-"+id[4..];if(widescreen) Widescreen.Apply(kit,discs,serial,content,job,log,token);File.WriteAllText(Path.Combine(content,"config-emu-ps4.txt"),$"--path-vmc=\"/tmp/vmc\"\n--config-local-lua=\"\"\n--ps2-title-id={serial}\n--max-disc-num={discs.Length}\n--gs-uprender=2x\n--gs-upscale=2x\n--host-audio=1\n--rom=\"PS20220WD20050620.crack\"\n--verbose-cdvd-reads=0\n--host-display-mode=full\n");
                if(!string.IsNullOrWhiteSpace(customLua)){string patches=Path.Combine(content,"patches");Directory.CreateDirectory(patches);File.Copy(customLua,Path.Combine(patches,serial+"_config.lua"),true);log("Added the selected PS2 Lua configuration.");}
                else if(autoProfile&&!widescreen&&discs.Length==1){using var input=File.OpenRead(disc);string actual=DiscMetadata.ReadSerial(input);if(!actual.Equals(id,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException($"The entered PS2 game ID {id} does not match disc ID {actual}.");ApplyPs2Profile(kit,id,content,job,log,token);}
                else if(autoProfile&&widescreen)log("Widescreen selected; automatic PS2 Lua profile was skipped to avoid combining incompatible scripts.");
                if(File.Exists(Path.Combine(content,"patches",serial+"_config.lua")))File.AppendAllText(Path.Combine(content,"config-emu-ps4.txt"),"--path-patches=\"/app0/patches\"\n");
                if(disableMtvu)File.AppendAllText(Path.Combine(content,"config-emu-ps4.txt"),"--vu1=jit-sync\n");
                if(graphicsFix)File.AppendAllText(Path.Combine(content,"config-emu-ps4.txt"),"--fpu-no-clamping=0\n--fpu-clamp-results=1\n--vu0-no-clamping=0\n--vu0-clamp-results=1\n--vu1-no-clamping=0\n--vu1-clamp-results=1\n--cop2-no-clamping=0\n--cop2-clamp-results=1\n");
                CopyDir(Path.Combine(ps4,"lua_include"),Path.Combine(content,"lua_include"),token);Directory.CreateDirectory(Path.Combine(content,"image"));
            }
            progress?.Invoke(ps1 ? 0.40 : 0.05);
            log("Generating the PS4 package project.");Run(generator,[content],job,token);string gp4=content+".gp4";if(!File.Exists(gp4))throw new InvalidDataException("The toolkit did not create the package project.");
            var text=File.ReadAllText(gp4).Replace("<?xml version=\"1.1\"","<?xml version=\"1.0\"").Replace("<scenarios default_id=\"1\">","<scenarios default_id=\"0\">");text=Regex.Replace(text,@"&(?!amp;|lt;|gt;|quot;|apos;|#\d+;|#x[0-9a-fA-F]+;)","&amp;");var xml=XDocument.Parse(text);var files=xml.Descendants("files").Single();
            if(!ps1)for(int i=0;i<discs.Length;i++)files.Add(new XElement("file",new XAttribute("targ_path",$"image/disc{i+1:00}.iso"),new XAttribute("orig_path",Path.GetFullPath(discs[i])),new XAttribute("pfs_compression","disable")));
            xml.Save(gp4);
            log("Building the PS4 package. This can take several minutes.");string built=Path.Combine(job,"built");Directory.CreateDirectory(built);long estimatedBytes=Directory.EnumerateFiles(content,"*",SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) + (ps1 ? 0 : discs.Sum(f => new FileInfo(f).Length));
            double packStart=ps1 ? 0.40 : 0.05;
            Run(packer,["img_create","--oformat","pkg","--skip_digest","--no_progress_bar",gp4,built],job,token,built,estimatedBytes, fraction => progress?.Invoke(packStart+(0.94-packStart)*fraction));
            progress?.Invoke(0.95);
            var pkgs=Directory.GetFiles(built,"*.pkg");if(pkgs.Length!=1||new FileInfo(pkgs[0]).Length<4096)throw new InvalidDataException("The toolkit did not produce one complete package.");
            using(var f=File.OpenRead(pkgs[0])){byte[] h=new byte[4];f.ReadExactly(h);if(!h.AsSpan().SequenceEqual(new byte[]{0x7f,0x43,0x4e,0x54}))throw new InvalidDataException("The output is not a PS4 package.");}
            log("Checking the finished package with the PS4 toolkit.");Run(packer,["img_info",pkgs[0]],job,token);
            token.ThrowIfCancellationRequested();
            File.Move(pkgs[0],final);
            progress?.Invoke(1);
            return final;
        } finally {try{Directory.Delete(job,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    internal static bool ApplyPs2Profile(string kit,string id,string content,string job,Action<string> log,CancellationToken token)
    {
        string tools=Path.Combine(kit,"Tools"),database=Path.Combine(tools,"PS4","ps2-configs","configs_lua.dat"),seven=Path.Combine(tools,"7z.exe");
        if(!File.Exists(database)||!File.Exists(seven)){log("No built-in PS2 Lua profile database is available.");return false;}
        string discId=id[..4]+"_"+id.Substring(4,3)+"."+id[7..],serial=id[..4]+"-"+id[4..],temp=Path.Combine(job,"ps2-profile");Directory.CreateDirectory(temp);
        var info = PackageTool.Command(seven, job, ["e", database, discId, "-o" + temp, "-y"]);
        PackageTool.RunAsync(info, token, allowWarningExit: true).GetAwaiter().GetResult();
        string file=Path.Combine(temp,discId);if(!File.Exists(file)||new FileInfo(file).Length<=1){log("No matching PS2 Lua profile was found; continuing without one.");return false;}
        if(new FileInfo(file).Length>2097152)throw new InvalidDataException("PS2 profile is unexpectedly large.");string patches=Path.Combine(content,"patches");Directory.CreateDirectory(patches);File.Copy(file,Path.Combine(patches,serial+"_config.lua"),true);log("Applied the matching PS2 Lua profile for "+serial+".");return true;
    }
    static void CopyDir(string src,string dst,CancellationToken t){Directory.CreateDirectory(dst);foreach(string f in Directory.GetFiles(src)){t.ThrowIfCancellationRequested();File.Copy(f,Path.Combine(dst,Path.GetFileName(f)),true);}foreach(string d in Directory.GetDirectories(src))CopyDir(d,Path.Combine(dst,Path.GetFileName(d)),t);}
    static void CopyFile(string src,string dst,CancellationToken t,Action<double>? report=null){using var a=File.OpenRead(src);using var b=File.Create(dst);byte[] buf=new byte[1024*1024];int n;long next=0;while((n=a.Read(buf))>0){t.ThrowIfCancellationRequested();b.Write(buf,0,n);if(a.Position>=next){report?.Invoke((double)a.Position/Math.Max(1,a.Length));next=a.Position+8*1024*1024;}}report?.Invoke(1);}
    static void Run(string exe, string[] args, string cwd, CancellationToken token,
        string? monitorDir = null, long expectedBytes = 0, Action<double>? report = null)
    {
        Action? sample = null;
        if (monitorDir != null && expectedBytes > 0 && report != null)
        {
            long previous = -1;
            sample = () =>
            {
                try
                {
                    long bytes = Directory.EnumerateFiles(monitorDir, "*.pkg").Sum(f => new FileInfo(f).Length);
                    if (bytes == previous) return;
                    previous = bytes;
                    report(Math.Clamp((double)bytes / expectedBytes, 0, 0.98));
                }
                catch (IOException) { /* The packer can rename its file between samples. */ }
                catch (UnauthorizedAccessException) { }
            };
        }
        PackageTool.RunAsync(PackageTool.Command(exe, cwd, args), token, sample: sample).GetAwaiter().GetResult();
    }
    static void WriteSfo(string path,string title,string id,string contentId,bool ps2){var values=new List<(string key,int format,byte[] data,int cap)>();void S(string k,string v,int cap){var b=Encoding.UTF8.GetBytes(v+"\0");if(b.Length>cap)throw new InvalidDataException(k+" is too long.");values.Add((k,0x204,b,cap));}void I(string k,int v)=>values.Add((k,0x404,BitConverter.GetBytes(v),4));I("APP_TYPE",1);S("APP_VER","01.00",8);I("ATTRIBUTE",0);S("CATEGORY","gd",4);S("CONTENT_ID",contentId,48);I("DOWNLOAD_DATA_SIZE",0);S("FORMAT","obs",4);I("PARENTAL_LEVEL",5);if(ps2)I("REMOTE_PLAY_KEY_ASSIGN",0);I("SYSTEM_VER",0);S("TITLE",title,128);S("TITLE_ID",id,12);S("VERSION","01.00",8);
        using var key=new MemoryStream();using var data=new MemoryStream();using var entries=new MemoryStream();using(var e=new BinaryWriter(entries,Encoding.UTF8,true)){foreach(var v in values){e.Write((ushort)key.Position);e.Write((ushort)v.format);e.Write(v.data.Length);e.Write(v.cap);e.Write((int)data.Position);var kb=Encoding.ASCII.GetBytes(v.key+"\0");key.Write(kb);data.Write(v.data);data.Write(new byte[v.cap-v.data.Length]);}}while(key.Length%4!=0)key.WriteByte(0);using var file=File.Create(path);using var w=new BinaryWriter(file);w.Write(0x46535000);w.Write(0x101);w.Write((int)(20+entries.Length));w.Write((int)(20+entries.Length+key.Length));w.Write(values.Count);w.Write(entries.ToArray());w.Write(key.ToArray());w.Write(data.ToArray());}
}




