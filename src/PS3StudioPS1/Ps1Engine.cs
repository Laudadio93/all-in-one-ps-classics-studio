using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PS2ClassicsPackageStudio;
internal static class Ps1Engine
{
    public const string Bundled = "Built-in PS1 engine (pop-fe)";
    public static bool IsSupported(string path)
    {
        if (path == Bundled) return true;
        if (!File.Exists(path)) return false;
        string name=Path.GetFileName(path);
        return name.Equals("pop-fe.exe",StringComparison.OrdinalIgnoreCase) || name.Equals("pop-fe.py",StringComparison.OrdinalIgnoreCase);
    }

    public static ProcessStartInfo CreateStartInfo(string engine,string job,string output,string[] discs,string title,string cover,string backdrop,bool alternate,bool swap,string audioMode,string audio,CancellationToken token,string customConfig="",string patch="")
    {
        if(!IsSupported(engine)) throw new FileNotFoundException("Select pop-fe.exe or pop-fe.py from a complete PS1 pop-fe distribution.");
        string isolated=Path.Combine(job,"ps1-engine");
        string stagedEngine;
        long total=0; int count=0;
        if (engine == Bundled)
        {
            using var resource=typeof(Ps1Engine).Assembly.GetManifestResourceStream("PS2ClassicsPackageStudio.Resources.pop-fe-v1.31.zip") ?? throw new FileNotFoundException("The built-in PS1 engine is missing from the app.");
            PackageWriterRuntime.Extract(resource, isolated, token);
            stagedEngine=Path.Combine(isolated,"pop-fe.exe");
        }
        else
        {
            engine=Path.GetFullPath(engine);
            string original=Path.GetDirectoryName(engine)!;
            if (Path.GetFullPath(job).StartsWith(original.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose an output folder outside the pop-fe distribution folder.");
            CopyFolder(original,isolated);
            stagedEngine=Path.Combine(isolated,Path.GetFileName(engine));
        }
        bool python=Path.GetExtension(engine).Equals(".py",StringComparison.OrdinalIgnoreCase);
        var info=new ProcessStartInfo { FileName=python ? FindPython() : stagedEngine, WorkingDirectory=isolated,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
        if(python){if(Path.GetFileName(info.FileName).Equals("py.exe",StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add("-3");info.ArgumentList.Add(stagedEngine);}
        info.ArgumentList.Add("--ps3-pkg="+output);
        if(!string.IsNullOrWhiteSpace(title)) info.ArgumentList.Add("--title="+title);
        if(!string.IsNullOrWhiteSpace(cover)) info.ArgumentList.Add("--cover="+Path.GetFullPath(cover));
        if(!string.IsNullOrWhiteSpace(backdrop)) info.ArgumentList.Add("--pic1="+Path.GetFullPath(backdrop));
        // pop-fe v1.31 probes sidecars and its online catalog when --manual is omitted
        // or set to "none". An explicit empty page collection takes precedence and
        // produces no DOCUMENT.DAT while automatic menu audio remains available.
        string emptyManual = Directory.CreateDirectory(Path.Combine(job, "empty-manual")).FullName;
        info.ArgumentList.Add("--manual=" + emptyManual);
        if(alternate) info.ArgumentList.Add("--ps1-newemu");
        if(swap) info.ArgumentList.Add("--swap-discs");
        if(audioMode=="No sound") info.ArgumentList.Add("--snd0=no");
        else if(audioMode=="Custom WAV") info.ArgumentList.Add("--snd0="+Path.GetFullPath(audio));
        if (!string.IsNullOrWhiteSpace(customConfig) || !string.IsNullOrWhiteSpace(patch)) discs = [StageCueWithConfig(discs, customConfig, job, token, patch)];
        foreach(string disc in discs) info.ArgumentList.Add(Path.GetFullPath(disc));
        return info;

        void CopyFolder(string source,string destination)
        {
            token.ThrowIfCancellationRequested();
            if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0) throw new IOException("The pop-fe distribution contains a linked folder. Select a regular extracted distribution.");
            Directory.CreateDirectory(destination);
            foreach(var file in Directory.EnumerateFiles(source)){
                token.ThrowIfCancellationRequested(); var item=new FileInfo(file);
                if((item.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("The pop-fe distribution contains a linked file.");
                if(++count>20000 || (total+=item.Length)>2L*1024*1024*1024) throw new IOException("The selected pop-fe folder is too large. Select the engine in its own extracted folder.");
                File.Copy(file,Path.Combine(destination,item.Name));
            }
            foreach(var dir in Directory.EnumerateDirectories(source)) CopyFolder(dir,Path.Combine(destination,Path.GetFileName(dir)));
        }
    }
    internal static string StageCueWithConfig(string[] discs, string config, string job, CancellationToken token, string patch = "")
    {
        if (discs.Length != 1 || !Path.GetExtension(discs[0]).Equals(".cue", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A custom PS1 config requires one CUE disc per package.");
        if (CueValidation.Check(discs[0]) is { } problem) throw new InvalidDataException(problem);
        if (!string.IsNullOrWhiteSpace(config))
        {
            var file = new FileInfo(config);
            if (!file.Exists || file.Length < 8 || file.Length > 1024 * 1024 || (file.Length - 8) % 8 != 0) throw new InvalidDataException("The PS1 config must have an 8-byte header followed by complete 8-byte commands.");
            using var input = File.OpenRead(config); byte[] header = new byte[8]; input.ReadExactly(header);
            if (!header.AsSpan().SequenceEqual(new byte[] { 0, 0, 0x20, 0, 0, 0, 0, 0 })) throw new InvalidDataException("The selected file does not have a recognized PS1 config header.");
        }
        string source = Path.GetFullPath(discs[0]); string parent = Path.GetDirectoryName(source)!;
        string directory = Path.Combine(job, "ps1-config-input"); Directory.CreateDirectory(directory);
        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string cue = File.ReadAllText(source);
        cue = Regex.Replace(cue, "(?im)^(\\s*FILE\\s+)(?:\"([^\"\\r\\n]+)\"|(\\S+))(\\s+BINARY\\s*)$", m => {
            string name = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            string track = Path.GetFullPath(Path.Combine(parent, name)); string baseName = Path.GetFileName(track);
            if (copied.TryGetValue(baseName, out string? previous) && !previous.Equals(track, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Two tracks have the same filename; rename one before using a custom PS1 config.");
            if (!copied.ContainsKey(baseName))
            {
                using var input = File.OpenRead(track); using var output = File.Create(Path.Combine(directory, baseName));
                byte[] buffer = new byte[1024 * 1024]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
                copied.Add(baseName, track);
            }
            return m.Groups[1].Value + "\"" + baseName + "\"" + m.Groups[4].Value;
        });
        string result = Path.Combine(directory, "disc.cue"); File.WriteAllText(result, cue);
        string sidecar = !string.IsNullOrWhiteSpace(config) ? config : Path.ChangeExtension(source, ".ps3config");
        if (File.Exists(sidecar)) File.Copy(sidecar, Path.Combine(directory, "disc.ps3config"), true);
        if (!string.IsNullOrWhiteSpace(patch))
        {
            if (copied.Count != 1) throw new InvalidDataException("PPF patching currently requires a single BIN file. No source files were changed.");
            string image = Path.Combine(directory, copied.Keys.Single());
            if (Path.GetExtension(patch).Equals(".xdelta", StringComparison.OrdinalIgnoreCase)) Ps1XdeltaPatch.Apply(image, patch, directory, token);
            else if (Path.GetExtension(patch).Equals(".ppf", StringComparison.OrdinalIgnoreCase)) Ps1PpfPatch.Apply(image, patch, token);
            else throw new InvalidDataException("Choose a PPF or xdelta patch.");
        }
        return result;
    }
    private static string FindPython()
    {
        foreach(var name in new[]{"python.exe","py.exe"})
            foreach(var folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries)){
                var p=Path.Combine(folder.Trim('"'),name); if(File.Exists(p)) return p;
            }
        throw new FileNotFoundException("Python was not found. Select a pop-fe.exe CLI build, or configure Python and the pop-fe dependencies.");
    }
}
