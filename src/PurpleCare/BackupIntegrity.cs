using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
namespace PurpleCare {
 public sealed class BackupPlan {public long Bytes{get;set;} public long Files{get;set;} public long Largest{get;set;} public long Available{get;set;} public string Format{get;set;}}
 public static class BackupIntegrity {
  public static bool Fits(long bytes,long files,long available){return bytes>=0&&files>=0&&available>=bytes&&available-bytes>=Math.Max(32L*1024*1024,files*65536L);}
  public static BackupPlan Preflight(string source,string root,CancellationToken cancel){
   EdgeBackup.Validate(source,root);cancel.ThrowIfCancellationRequested();var p=new BackupPlan();Count(source,p,cancel);
   var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)));p.Available=drive.AvailableFreeSpace;p.Format=drive.DriveFormat;
   if(!Fits(p.Bytes,p.Files,p.Available))throw new IOException("目标盘空间不足。数据约 "+(p.Bytes/1048576.0).ToString("0.0")+" MB，可用 "+(p.Available/1048576.0).ToString("0.0")+" MB；另需为目录、分配单元和结果文件预留空间。");
   if(p.Format.Equals("FAT32",StringComparison.OrdinalIgnoreCase)&&p.Largest>=4294967296L)throw new IOException("存在超过 FAT32 单文件上限的文件，请换用 exFAT 或 NTFS 目标盘。");
   cancel.ThrowIfCancellationRequested();Directory.CreateDirectory(root);string probe=Path.Combine(root,".write-check-"+Guid.NewGuid().ToString("N"));
   try{using(var f=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.WriteByte(0);f.Flush(true);}}finally{if(File.Exists(probe))File.Delete(probe);}
   return p;
  }
  static void Count(string dir,BackupPlan p,CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();if(EdgeBackup.Link(dir))return;
   foreach(var entry in Directory.EnumerateFileSystemEntries(dir)){cancel.ThrowIfCancellationRequested();var attr=File.GetAttributes(entry);if((attr&FileAttributes.ReparsePoint)!=0)continue;if((attr&FileAttributes.Directory)!=0)Count(entry,p,cancel);else{long n=new FileInfo(entry).Length;p.Bytes=checked(p.Bytes+n);p.Files++;p.Largest=Math.Max(p.Largest,n);}}
  }
  public static bool VerifyFile(string path,byte[] expected,CancellationToken cancel,Action<int> readProgress){
   using(var hash=SHA256.Create())using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
    byte[] buffer=new byte[1024*1024];int count;
    while((count=file.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();hash.TransformBlock(buffer,0,count,null,0);readProgress(count);}
    cancel.ThrowIfCancellationRequested();hash.TransformFinalBlock(new byte[0],0,0);return hash.Hash.SequenceEqual(expected);
   }
  }
 }
}
