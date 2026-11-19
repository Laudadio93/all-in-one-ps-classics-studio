using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace PS4Studio;
internal static class ClassicsCrypto
{
    const string ContentId="2P0001-PS2U10000_00-0000111122223333";
    public static void Convert(string source,string target,string keyPath,bool ps3,bool decrypt,Action<double> report,CancellationToken token)
    {
        if(ps3)ConvertPs3(source,target,keyPath,decrypt,report,token);else ConvertPs2(source,target,keyPath,decrypt,report,token);
    }
    // Region boundaries and per-sector IV follow the documented PS3 disc layout.
    static void ConvertPs3(string source,string target,string keyPath,bool decrypt,Action<double> report,CancellationToken token)
    {
        source=Path.GetFullPath(source);target=Path.GetFullPath(target);
        if(!File.Exists(source))throw new FileNotFoundException("Choose an existing PS3 ISO.");
        if(File.Exists(target))throw new IOException("The output file already exists. Choose a different name.");
        if(string.IsNullOrWhiteSpace(keyPath)||!File.Exists(keyPath))throw new InvalidDataException("Select the matching PS3 disc key file.");
        if(new FileInfo(keyPath).Length>256)throw new InvalidDataException("The PS3 disc key file is too large.");byte[] key=File.ReadAllBytes(keyPath);if(key.Length!=16){string hex=System.Text.Encoding.ASCII.GetString(key).Trim();if(hex.Length!=32)throw new InvalidDataException("A PS3 disc key must be 16 binary bytes or 32 hexadecimal characters.");try{key=System.Convert.FromHexString(hex);}catch(FormatException){throw new InvalidDataException("The PS3 disc key contains invalid hexadecimal characters.");}}
        string temp=Path.Combine(Path.GetDirectoryName(target)!,".ps3crypto-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using var input=File.OpenRead(source);if(input.Length<4096||input.Length%2048!=0)throw new InvalidDataException("The PS3 ISO must contain complete 2048-byte sectors.");
            byte[] header=new byte[4096];input.ReadExactly(header);int count=checked((int)BinaryPrimitives.ReadUInt32BigEndian(header));if(count<2||count>127)throw new InvalidDataException("The PS3 region map is missing or unsupported.");
            int regions=2*count-1;long sectors=input.Length/2048;long first=BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));if(first!=0)throw new InvalidDataException("The PS3 region map does not start at sector zero.");
            var ends=new long[regions];long previous=-1;for(int i=0;i<regions;i++){long bound=BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12+4*i));long end=bound-(i%2);if(end<=previous||end>=sectors)throw new InvalidDataException("The PS3 region map is inconsistent with this ISO.");ends[i]=end;previous=end;}
            if(ends[^1]!=sectors-1)throw new InvalidDataException("The PS3 region map does not cover the complete image.");
            input.Position=0;using var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024);using var aes=Aes.Create();aes.Key=key;aes.Padding=PaddingMode.None;
            byte[] sector=new byte[2048],converted=new byte[2048],iv=new byte[16];long lba=0;int region=0;
            while(lba<sectors){token.ThrowIfCancellationRequested();while(lba>ends[region])region++;input.ReadExactly(sector);if((region&1)==1){BinaryPrimitives.WriteInt64BigEndian(iv.AsSpan(8),lba);if(decrypt)aes.DecryptCbc(sector,iv,converted,PaddingMode.None);else aes.EncryptCbc(sector,iv,converted,PaddingMode.None);output.Write(converted);}else {if(lba==1){string marker=System.Text.Encoding.ASCII.GetString(sector,0x770,16);if(marker=="Encrypted 3K ISO"||marker=="Decrypted 3K ISO")System.Text.Encoding.ASCII.GetBytes(decrypt?"Decrypted 3K ISO":"Encrypted 3K ISO").CopyTo(sector,0x770);}output.Write(sector);}lba++;if((lba&8191)==0)report(lba/(double)sectors);}
            output.Flush();output.Close();token.ThrowIfCancellationRequested();File.Move(temp,target);report(1);
        }
        finally{try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){ } CryptographicOperations.ZeroMemory(key);}
    }
    public static void ConvertPs2(string source,string target,string keyPath,bool decrypt,Action<double> report,CancellationToken token)
    {
        source=Path.GetFullPath(source);target=Path.GetFullPath(target);
        if(!File.Exists(source))throw new FileNotFoundException("Choose an existing input file.");
        if(File.Exists(target))throw new IOException("The output file already exists. Choose a different name.");
        string dir=Path.Combine(Path.GetTempPath(),"PS4StudioCrypto-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string temp=Path.Combine(Path.GetDirectoryName(target)!,".ps3crypto-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var zip=typeof(ClassicsCrypto).Assembly.GetManifestResourceStream("PS4Studio.Resources.ps3-ps2-crypto.zip") ?? throw new IOException("Built-in conversion tools are missing."))ZipFile.ExtractToDirectory(zip,dir);
            string key=Path.Combine(dir,"key.bin");
            if(string.IsNullOrWhiteSpace(keyPath)) Run(Path.Combine(dir,"rap2rifkey.exe"),[Path.Combine(dir,ContentId+".rap"),key],dir,token);
            else {if(new FileInfo(keyPath).Length!=16)throw new InvalidDataException("A PS2 klicensee must contain exactly 16 bytes.");File.Copy(keyPath,key);}
            if(!File.Exists(key)||new FileInfo(key).Length!=16)throw new IOException("Could not prepare the license key.");
            long length;
            using(var input=File.OpenRead(source))
            {
                if(decrypt){byte[] header=new byte[144];input.ReadExactly(header);if(!header.AsSpan(0,4).SequenceEqual("PS2\0"u8))throw new InvalidDataException("The input is not a recognized PS2 Classics image.");length=BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(136));if(length<=0 || length>input.Length)throw new InvalidDataException("The encrypted image has an invalid data length.");}
                else {DiscMetadata.ReadSerial(input);length=input.Length;}
            }
            string? staged=null;
            if(!decrypt){staged=Path.Combine(dir,"disc.iso");using var f=File.OpenRead(source);DiscMetadata.PrepareAsync(f,staged,_=>{},token).GetAwaiter().GetResult();length=new FileInfo(staged).Length;}
            var args=decrypt?new[]{"d","cex",key,source,temp,Path.Combine(dir,"meta.dat")}:new[]{"e","cex",key,staged!,temp,"ISO.BIN.ENC",ContentId};
            Run(Path.Combine(dir,"ps2classic.exe"),args,dir,token,()=>{if(File.Exists(temp))report(Math.Min(.97,new FileInfo(temp).Length/(double)(length+32768)));});
            if(!File.Exists(temp))throw new IOException("The converter produced no output.");
            using(var output=File.OpenRead(temp))
            {
                if(decrypt){if(output.Length!=length)throw new InvalidDataException("The decrypted output is incomplete.");try{DiscMetadata.ReadSerial(output);}catch(InvalidDataException){throw new InvalidDataException("The decrypted output is not a recognizable PS2 DVD ISO. The key may be wrong or the source may be unsupported.");}}
                else {byte[] header=new byte[144];output.ReadExactly(header);if(!header.AsSpan(0,4).SequenceEqual("PS2\0"u8)||BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(136))!=length)throw new InvalidDataException("The encrypted output is incomplete.");}
            }
            token.ThrowIfCancellationRequested();File.Move(temp,target);report(1);
        }
        finally {try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}try{Directory.Delete(dir,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    static void Run(string exe,string[] args,string dir,CancellationToken token,Action? update=null)
    {
        var info=new ProcessStartInfo(exe){WorkingDirectory=dir,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string a in args)info.ArgumentList.Add(a);
        using var p=Process.Start(info)??throw new IOException("The conversion tool could not start.");using var reg=token.Register(()=>{try{p.Kill(true);}catch(InvalidOperationException){}});var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
        while(!p.WaitForExit(250)){token.ThrowIfCancellationRequested();update?.Invoke();}Task.WaitAll(stdout,stderr);token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new IOException("Conversion failed: "+stderr.Result+" "+stdout.Result);
    }
}
