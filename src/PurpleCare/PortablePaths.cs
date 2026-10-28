using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PurpleCare {
 public static class PortablePaths {
  public static string Scope(string machine,string user,string sid){
   Func<string,string> safe=s=>new string((s??"unknown").Select(c=>Array.IndexOf(Path.GetInvalidFileNameChars(),c)>=0?'_':c).Take(40).ToArray());
   string fingerprint;using(var sha=SHA256.Create()){fingerprint=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(machine+"\n"+sid))).Replace("-","").Substring(0,12);}
   return safe(machine)+"-"+safe(user)+"-"+fingerprint;
  }
  public static readonly string Root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data",Scope(Environment.MachineName,Environment.UserName,WindowsIdentity.GetCurrent().User.Value));
 }
}
