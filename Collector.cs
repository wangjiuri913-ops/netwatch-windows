using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace NetWatch {
 public class SnmpSession {
  readonly IPEndPoint endpoint; readonly OctetString community; readonly int timeout;
  readonly Stopwatch budget=Stopwatch.StartNew(); static int requestId=1000;
  public SnmpSession(Device d) { endpoint=new IPEndPoint(IPAddress.Parse(d.Address),d.Port); community=new OctetString(Vault.Reveal(d.Secret)); timeout=d.Timeout; }
  void CheckBudget() { if(budget.ElapsedMilliseconds>30000) throw new System.TimeoutException("采集超过 30 秒，请增大轮询间隔或减少设备端 SNMP 延迟"); }
  public Dictionary<string,ISnmpData> Get(params string[] oids) {
   CheckBudget(); var vars=oids.Select(o=>new Variable(new ObjectIdentifier(o))).ToList();
   return Messenger.Get(VersionCode.V2,endpoint,community,vars,timeout).ToDictionary(v=>v.Id.ToString(),v=>v.Data);
  }
  public Dictionary<string,ISnmpData> Walk(string root,int max) {
   var result=new Dictionary<string,ISnmpData>(); var next=new ObjectIdentifier(root);
   for(int page=0;page<600;page++) {
    CheckBudget();
    var req=new GetBulkRequestMessage(Interlocked.Increment(ref requestId),VersionCode.V2,community,0,20,new List<Variable>{new Variable(next)});
    var response=req.GetResponse(timeout,endpoint);
    if(response.Pdu().ErrorStatus.ToInt32()!=0) throw new InvalidOperationException("SNMP 返回错误 "+response.Pdu().ErrorStatus);
    var vars=response.Variables(); if(vars.Count==0) break;
    foreach(var v in vars) {
     string id=v.Id.ToString();
     if(!id.StartsWith(root+".",StringComparison.Ordinal)||!Valid(v.Data)) return result;
     if(v.Id.CompareTo(next)<=0) throw new InvalidOperationException("设备 SNMP 返回了不递增的 OID");
     result[id]=v.Data; next=v.Id;
     if(result.Count>=max) throw new InvalidOperationException("端口表超过单台设备支持上限（512 个接口）");
    }
   }
   return result;
  }
  public static bool Valid(ISnmpData d) { return d!=null&&!(d is NoSuchObject)&&!(d is NoSuchInstance)&&!(d is EndOfMibView)&&!(d is Null); }
  public static ulong? Number(ISnmpData d) {
   if(d is Counter64) return ((Counter64)d).ToUInt64();
   if(d is Counter32) return ((Counter32)d).ToUInt32();
   if(d is Gauge32) return ((Gauge32)d).ToUInt32();
   if(d is TimeTicks) return ((TimeTicks)d).ToUInt32();
   if(d is Integer32) {int n=((Integer32)d).ToInt32(); return n<0?(ulong?)null:(ulong)n;}
   ulong parsed; return d!=null&&ulong.TryParse(d.ToString(),out parsed)?parsed:(ulong?)null;
  }
 }
 public class Collector {
  const string Sys="1.3.6.1.2.1.1.";
  const string If="1.3.6.1.2.1.2.2.1.";
  const string IfX="1.3.6.1.2.1.31.1.1.1.";
  static ISnmpData Read(Dictionary<string,ISnmpData> data,string key) {ISnmpData v; return data.TryGetValue(key,out v)&&SnmpSession.Valid(v)?v:null;}
  static string Str(Dictionary<string,ISnmpData> data,string key) {var v=Read(data,key);return v==null?"":v.ToString();}
  static ulong? Num(Dictionary<string,ISnmpData> data,string key) {return SnmpSession.Number(Read(data,key));}
  public Snapshot Poll(Device d,Snapshot previous) {
   if(d.Demo) return Demo(d,previous);
   var s=new Snapshot {Id=d.Id,Time=Clock.Now()}; int received=0; long rtt=0;
   using(var ping=new Ping()) {
    for(int i=0;i<3;i++) {try {var reply=ping.Send(IPAddress.Parse(d.Address),Math.Min(d.Timeout,1500));if(reply.Status==IPStatus.Success){received++;rtt+=reply.RoundtripTime;}} catch {} }
   }
   s.Rtt=received>0?(double?)rtt/received:null; s.Loss=(3-received)*100.0/3;
   try {
    var session=new SnmpSession(d);
    var sys=session.Get(Sys+"1.0",Sys+"3.0",Sys+"5.0");
    if(sys.Values.All(v=>!SnmpSession.Valid(v))) throw new InvalidOperationException("SNMP 系统信息不可读，请检查只读视图权限");
    s.Snmp=true; s.Status="online"; s.SysName=Str(sys,Sys+"5.0");s.Description=Str(sys,Sys+"1.0");s.UptimeTicks=Num(sys,Sys+"3.0");
    try {
     var table=session.Walk("1.3.6.1.2.1.2.2",512*23+1);
     Dictionary<string,ISnmpData> ext;
     try { ext=session.Walk("1.3.6.1.2.1.31.1.1",512*20+1); } catch {ext=new Dictionary<string,ISnmpData>();s.Error="扩展端口表不可用；已尝试使用 32 位计数器。高速端口可能无法准确计算流量。";}
     var indexes=table.Keys.Where(k=>k.StartsWith(If+"1.",StringComparison.Ordinal)).Select(k=>int.Parse(k.Substring((If+"1.").Length))).Take(513).ToList();
     if(indexes.Count>512) throw new InvalidOperationException("单台设备最多支持 512 个接口");
     if(indexes.Count==0) s.Error="SNMP 可达，但端口表为空；请检查 IF-MIB 读取权限。";
     foreach(int idx in indexes) {
      string n=idx.ToString(); ulong? high=Num(ext,IfX+"15."+n);
      var p=new InterfaceData {Index=idx,Name=Str(ext,IfX+"1."+n),Alias=Str(ext,IfX+"18."+n),Admin=(int)(Num(table,If+"7."+n)??0),Oper=(int)(Num(table,If+"8."+n)??0),Speed=high.HasValue&&high.Value>0?high.Value*1000000.0:(Num(table,If+"5."+n)??0),InErrors=Num(table,If+"14."+n),OutErrors=Num(table,If+"20."+n),InDiscards=Num(table,If+"13."+n),OutDiscards=Num(table,If+"19."+n),Discontinuity=Num(ext,IfX+"19."+n),CounterTime=Clock.Now()};
      if(string.IsNullOrEmpty(p.Name)) p.Name=Str(table,If+"2."+n);
      if(string.IsNullOrEmpty(p.Name)) p.Name="ifIndex "+idx;
      p.InOctets=Num(ext,IfX+"6."+n);p.OutOctets=Num(ext,IfX+"10."+n);p.Counter64=p.InOctets.HasValue&&p.OutOctets.HasValue;
      if(!p.Counter64) {p.InOctets=Num(table,If+"10."+n);p.OutOctets=Num(table,If+"16."+n);}
      var old=previous==null?null:previous.Interfaces.FirstOrDefault(x=>x.Index==idx);
      if(old!=null&&previous.Snmp) {
       bool reset=!s.UptimeTicks.HasValue||!previous.UptimeTicks.HasValue||s.UptimeTicks<previous.UptimeTicks||old.Counter64!=p.Counter64||old.Discontinuity!=p.Discontinuity||old.Name!=p.Name||old.Speed!=p.Speed||old.Oper!=1||p.Oper!=1;
       double seconds=(p.CounterTime-old.CounterTime)/1000.0;
       p.InBps=Rates.Bps(old.InOctets,p.InOctets,p.Counter64,seconds,p.Speed,reset);
       p.OutBps=Rates.Bps(old.OutOctets,p.OutOctets,p.Counter64,seconds,p.Speed,reset);
       if(p.Speed>0&&p.InBps.HasValue&&p.OutBps.HasValue) p.Utilization=Math.Max(p.InBps.Value,p.OutBps.Value)/p.Speed*100;
      }
      s.Interfaces.Add(p);
     }
    } catch(Exception ex) {s.Error="端口采集未完成："+Friendly(ex);}
    // A blank OID uses the H3C ENTITY-EXT-MIB table automatically. This
    // avoids asking users to discover the physical entity index by hand.
    s.Cpu=string.IsNullOrEmpty(d.CpuOid)?AutoMetric(session,"1.3.6.1.4.1.25506.2.6.1.1.1.1.6"):Metric(session,d.CpuOid);
    s.Memory=string.IsNullOrEmpty(d.MemoryOid)?AutoMetric(session,"1.3.6.1.4.1.25506.2.6.1.1.1.1.8"):Metric(session,d.MemoryOid);
    if((!string.IsNullOrEmpty(d.CpuOid)&&!s.Cpu.HasValue)||(!string.IsNullOrEmpty(d.MemoryOid)&&!s.Memory.HasValue)) s.Error=(s.Error??"")+" CPU/内存 OID 不可读或返回值不在 0–100 之间。";
   } catch(Exception ex) {s.Status=received>0?"degraded":"offline";s.Error=Friendly(ex);}
   Summarize(s);return s;
  }
  static double? Metric(SnmpSession session,string oid) {
   if(string.IsNullOrWhiteSpace(oid)) return null;
   try {var v=session.Get(oid).Values.FirstOrDefault();double n;return SnmpSession.Valid(v)&&double.TryParse(v.ToString(),out n)&&n>=0&&n<=100?(double?)n:null;} catch{return null;}
  }
  static double? AutoMetric(SnmpSession session,string column) {
   try {
    var values=session.Walk(column,64).Values;
    foreach(var v in values) { double n; if(SnmpSession.Valid(v)&&double.TryParse(v.ToString(),out n)&&n>=0&&n<=100)return n; }
   } catch {}
   return null;
  }
  static string Friendly(Exception ex) {
   if(ex is System.Security.Cryptography.CryptographicException) return "社区字符串无法解密，请在当前 Windows 用户下重新填写";
   if(ex.GetType().Name.Contains("Timeout")) return "SNMP 超时：检查设备已启用 SNMP v2c、社区字符串及 UDP 端口/ACL";
   return "SNMP 采集失败（"+ex.GetType().Name+"）；检查设备端配置和只读权限";
  }
  public static void Summarize(Snapshot s) {
   if(s.Interfaces.Any(p=>p.InBps.HasValue)) s.InBps=s.Interfaces.Sum(p=>p.InBps??0);
   if(s.Interfaces.Any(p=>p.OutBps.HasValue)) s.OutBps=s.Interfaces.Sum(p=>p.OutBps??0);
   if(s.Interfaces.Any(p=>p.Utilization.HasValue)) s.MaxUtilization=s.Interfaces.Max(p=>p.Utilization??0);
  }
  public static Snapshot Demo(Device d,Snapshot previous) {
   double phase=Clock.Now()/20000.0+(d.Type=="交换机"?1:d.Type=="路由器"?2:3);
   var s=new Snapshot {Id=d.Id,Time=Clock.Now(),Status="online",Snmp=true,SysName=d.Name,Description="演示数据 · 不连接真实网络设备",UptimeTicks=(ulong)(8640000+Clock.Now()%8640000),Rtt=1.2+Math.Abs(Math.Sin(phase))*3,Loss=0,Cpu=24+Math.Sin(phase)*12,Memory=42+Math.Cos(phase)*4};
   for(int i=1;i<=8;i++) {double input=(28+Math.Sin(phase+i)*18)*1000000,output=(18+Math.Cos(phase+i)*10)*1000000;var p=new InterfaceData {Index=i,Name="GE0/0/"+i,Alias=i==1?"上联口":(i>6?"备用":"接入端口"),Admin=i>6?2:1,Oper=i>6?2:1,Speed=1000000000,InBps=i>6?0:input,OutBps=i>6?0:output,InErrors=0,OutErrors=0,InDiscards=0,OutDiscards=0,Counter64=true};p.Utilization=Math.Max(p.InBps.Value,p.OutBps.Value)/p.Speed*100;s.Interfaces.Add(p);}
   Summarize(s);return s;
  }
 }
}
