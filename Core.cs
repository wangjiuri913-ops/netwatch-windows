using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace NetWatch {
 public class Device {
  public string Id {get;set;}
  public string Name {get;set;}
  public string Address {get;set;}
  public string Type {get;set;}
  public string Location {get;set;}
  public int Port {get;set;}
  public string Secret {get;set;}
  public bool Enabled {get;set;}
  public bool Demo {get;set;}
  public int Interval {get;set;}
  public int Timeout {get;set;}
  public double Threshold {get;set;}
  public string CpuOid {get;set;}
  public string MemoryOid {get;set;}
  public Device() { Port=161; Enabled=true; Interval=60; Timeout=1500; Threshold=85; Type="交换机"; }
  public object Public() { return new {Id,Name,Address,Type,Location,Port,Enabled,Demo,Interval,Timeout,Threshold,CpuOid,MemoryOid,HasCommunity=!string.IsNullOrEmpty(Secret)}; }
 }
 public class InterfaceData {
  public int Index {get;set;}
  public string Name {get;set;}
  public string Alias {get;set;}
  public int Admin {get;set;}
  public int Oper {get;set;}
  public double Speed {get;set;}
  public double? InBps {get;set;}
  public double? OutBps {get;set;}
  public double? Utilization {get;set;}
  public ulong? InErrors {get;set;}
  public ulong? OutErrors {get;set;}
  public ulong? InDiscards {get;set;}
  public ulong? OutDiscards {get;set;}
  public ulong? InOctets {get;set;}
  public ulong? OutOctets {get;set;}
  public ulong? Discontinuity {get;set;}
  public bool Counter64 {get;set;}
  public long CounterTime {get;set;}
 }
 public class Sample {
  public long Time {get;set;}
  public string Status {get;set;}
  public double? Rtt {get;set;}
  public double Loss {get;set;}
  public double? InBps {get;set;}
  public double? OutBps {get;set;}
  public double? Cpu {get;set;}
  public double? Memory {get;set;}
  public double? MaxUtilization {get;set;}
 }
 public class Snapshot : Sample {
  public string Id {get;set;}
  public string SysName {get;set;}
  public string Description {get;set;}
  public string Error {get;set;}
  public ulong? UptimeTicks {get;set;}
  public bool Snmp {get;set;}
  public List<InterfaceData> Interfaces {get;set;}
  public Snapshot() { Status="pending"; Interfaces=new List<InterfaceData>(); }
  public Sample Point() { return new Sample {Time=Time, Status=Status,Rtt=Rtt,Loss=Loss,InBps=InBps,OutBps=OutBps,Cpu=Cpu,Memory=Memory,MaxUtilization=MaxUtilization}; }
 }
 public class Alert {
  public string Id {get;set;}
  public string DeviceId {get;set;}
  public string DeviceName {get;set;}
  public string Key {get;set;}
  public string Level {get;set;}
  public string Message {get;set;}
  public long Time {get;set;}
  public long? Resolved {get;set;}
  public bool Acknowledged {get;set;}
 }
 public class StateFile {
  public List<Sample> History {get;set;}
  public Snapshot Current {get;set;}
 }
 public static class Json {
  public static string Encode(object value) { return new JavaScriptSerializer {MaxJsonLength=32*1024*1024,RecursionLimit=80}.Serialize(value); }
  public static T Decode<T>(string value) { return new JavaScriptSerializer {MaxJsonLength=32*1024*1024,RecursionLimit=80}.Deserialize<T>(value); }
 }
 public static class Clock {
  public static long Now() { return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalMilliseconds; }
 }
 public static class Vault {
  public static string Protect(string plain) { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain),null,DataProtectionScope.CurrentUser)); }
  public static string Reveal(string encrypted) { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted),null,DataProtectionScope.CurrentUser)); }
 }
 public class Store {
  public readonly string Root;
  public Store(string root) { Root=Path.GetFullPath(root); Directory.CreateDirectory(Root); }
  public void Save(string file,object value) {
   string path=Path.Combine(Root,file), temp=path+".tmp";
   File.WriteAllText(temp,Json.Encode(value),new UTF8Encoding(false));
   if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
  }
  public T Read<T>(string file,T fallback) {
   string path=Path.Combine(Root,file);
   if(!File.Exists(path)) return fallback;
   try { return Json.Decode<T>(File.ReadAllText(path,Encoding.UTF8)); }
   catch(Exception ex) {
    Log("读取 "+file+" 失败："+ex.GetType().Name);
    if(File.Exists(path+".bak")) { try { var restored=Json.Decode<T>(File.ReadAllText(path+".bak",Encoding.UTF8)); Log("已使用备份 "+file); return restored; } catch {} }
    throw new InvalidDataException("数据文件损坏："+path+"。请从备份恢复，原文件已保留。");
   }
  }
  public void Log(string text) {
   try { lock(this) { string path=Path.Combine(Root,"netwatch.log"); if(File.Exists(path)&&new FileInfo(path).Length>1024*1024) File.Move(path,Path.Combine(Root,"netwatch-"+DateTime.Now.ToString("yyyyMMddHHmmss")+".log")); File.AppendAllText(path,DateTime.Now.ToString("s")+" "+text+Environment.NewLine); foreach(var f in Directory.GetFiles(Root,"netwatch-*.log").OrderByDescending(x=>x).Skip(3)) File.Delete(f); } } catch {}
  }
 }
 public static class Rates {
  // Reject resets and ambiguous 32-bit wraps. Rates are based on the actual sample interval.
  public static double? Bps(ulong? oldValue,ulong? newValue,bool wide,double seconds,double speed,bool reset) {
   if(reset||!oldValue.HasValue||!newValue.HasValue||seconds<=0||seconds>600) return null;
   if(!wide && (speed<=0 || speed*seconds/8>=4294967296.0)) return null;
   ulong delta;
   if(newValue.Value<oldValue.Value) {
    if(wide) return null;
    delta=4294967296UL-oldValue.Value+newValue.Value;
   } else delta=newValue.Value-oldValue.Value;
   double rate=delta*8.0/seconds;
   if(speed>0&&rate>speed*1.15) return null;
   return rate;
  }
 }
}
