using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Net;
using System.Threading;

namespace NetWatch {
 public sealed class Engine : IDisposable {
  readonly object gate=new object(); readonly Store store; readonly Collector collector=new Collector();
  List<Device> devices; List<Alert> alerts; Dictionary<string,TopologyPosition> topology;
  readonly Dictionary<string,Snapshot> current=new Dictionary<string,Snapshot>();
  readonly Dictionary<string,List<Sample>> histories=new Dictionary<string,List<Sample>>();
  readonly Dictionary<string,long> next=new Dictionary<string,long>();
  readonly Dictionary<string,int> failures=new Dictionary<string,int>();
  readonly HashSet<string> busy=new HashSet<string>();
  readonly System.Threading.Timer timer; bool stopped; public string StorageWarning="";
  public Engine(Store storage,bool start) {
   store=storage;devices=store.Read("devices.json",new List<Device>());alerts=store.Read("alerts.json",new List<Alert>());topology=store.Read("topology.json",new Dictionary<string,TopologyPosition>());
   topology=topology.Where(x=>devices.Any(d=>d.Id==x.Key)&&ValidPosition(x.Value)).ToDictionary(x=>x.Key,x=>x.Value);
   foreach(var d in devices) {
    StateFile state=store.Read<StateFile>(d.Id+".json",null);
    if(state!=null) {if(state.Current!=null)current[d.Id]=state.Current;histories[d.Id]=(state.History??new List<Sample>()).Where(p=>p.Time>Clock.Now()-86400000).ToList();}
   }
   if(start) timer=new System.Threading.Timer(Tick,null,700,1000);
  }
  static object Summary(Snapshot s) {return new {s.Id,s.Time,s.Status,s.Snmp,s.Rtt,s.Loss,s.InBps,s.OutBps,s.Cpu,s.Cpu1Min,s.Cpu5Min,s.Cpu5MinSource,s.Cpu5MinSamples,s.CpuTime,s.Memory,s.MemorySource,s.MemoryTime,s.MetricEntity,s.SysObjectId,s.Error,s.MaxUtilization,PortCount=s.Interfaces.Count,UpPorts=s.Interfaces.Count(p=>p.Oper==1)};}
  List<DashboardTrendPoint> OverviewTrend() {
   long cutoff=Clock.Now()-21600000;
   var latest=histories.SelectMany(pair=>pair.Value.Where(p=>p.Time>=cutoff).Select(p=>new {DeviceId=pair.Key,Bucket=p.Time/300000,Point=p})).GroupBy(x=>new {x.DeviceId,x.Bucket}).Select(g=>g.OrderByDescending(x=>x.Point.Time).First()).ToList();
   var result=latest.GroupBy(x=>x.Bucket).OrderBy(g=>g.Key).Select(g=>new DashboardTrendPoint {Time=g.Key*300000,InBps=g.Any(x=>x.Point.InBps.HasValue)?(double?)g.Where(x=>x.Point.InBps.HasValue).Sum(x=>x.Point.InBps.Value):null,OutBps=g.Any(x=>x.Point.OutBps.HasValue)?(double?)g.Where(x=>x.Point.OutBps.HasValue).Sum(x=>x.Point.OutBps.Value):null,Cpu=g.Any(x=>x.Point.Cpu.HasValue)?(double?)g.Where(x=>x.Point.Cpu.HasValue).Average(x=>x.Point.Cpu.Value):null,Memory=g.Any(x=>x.Point.Memory.HasValue)?(double?)g.Where(x=>x.Point.Memory.HasValue).Average(x=>x.Point.Memory.Value):null}).ToList();
   return result.Count>48?result.Skip(result.Count-48).ToList():result;
  }
  List<DashboardLinkRow> LinkRows() {
   var rows=new List<DashboardLinkRow>();
   foreach(var item in current) {var d=devices.FirstOrDefault(x=>x.Id==item.Key);if(d==null)continue;foreach(var p in item.Value.Interfaces)rows.Add(new DashboardLinkRow {DeviceId=d.Id,DeviceName=d.Name,Address=d.Address,Interface=p.Name,Admin=p.Admin,Oper=p.Oper,Speed=p.Speed,InBps=p.InBps,OutBps=p.OutBps,Utilization=p.Utilization});}
   return rows.OrderByDescending(x=>x.Oper==1).ThenByDescending(x=>x.Utilization??-1).ThenByDescending(x=>(x.InBps??0)+(x.OutBps??0)).Take(12).ToList();
  }
  public object Dashboard() {lock(gate) {return new {Now=Clock.Now(),Warning=StorageWarning,Devices=devices.Select(d=>new {Config=d.Public(),Current=current.ContainsKey(d.Id)?Summary(current[d.Id]):null,Polling=busy.Contains(d.Id),Next=next.ContainsKey(d.Id)?next[d.Id]:0}).ToList(),Alerts=alerts.OrderByDescending(a=>a.Time).Take(250).ToList(),ActiveAlerts=alerts.Count(a=>!a.Resolved.HasValue),Topology=topology.ToDictionary(x=>x.Key,x=>new {x=x.Value.X,y=x.Value.Y}),Trend=OverviewTrend(),Links=LinkRows(),DataDirectory=store.Root};}}
  public object Detail(string id) {lock(gate) {var d=Find(id);return new {Config=d.Public(),Current=current.ContainsKey(id)?current[id]:null,History=histories.ContainsKey(id)?histories[id].ToArray():new Sample[0]};}}
  public List<Sample> History(string id) {lock(gate) {Find(id);return histories.ContainsKey(id)?histories[id].ToList():new List<Sample>();}}
  Device Find(string id) {var d=devices.FirstOrDefault(x=>x.Id==id);if(d==null) throw new ArgumentException("设备不存在");return d;}
  static string Text(Dictionary<string,object> data,string key,string fallback) {object o;return data.TryGetValue(key,out o)&&o!=null?Convert.ToString(o):fallback;}
  static int Int(Dictionary<string,object> data,string key,int fallback,int low,int high) {int n;if(!int.TryParse(Text(data,key,fallback.ToString()),out n)||n<low||n>high)throw new ArgumentException(key+" 必须在 "+low+"–"+high+" 之间");return n;}
  static bool Bool(Dictionary<string,object> data,string key,bool fallback) {bool b;return bool.TryParse(Text(data,key,fallback.ToString()),out b)?b:fallback;}
  static double Number(Dictionary<string,object> data,string key) {object value;double result;if(!data.TryGetValue(key,out value)||value==null||!double.TryParse(Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out result)||double.IsNaN(result)||double.IsInfinity(result))throw new ArgumentException(key+" 必须是有效数字");return result;}
  static bool ValidPosition(TopologyPosition position) {return position!=null&&!double.IsNaN(position.X)&&!double.IsInfinity(position.X)&&!double.IsNaN(position.Y)&&!double.IsInfinity(position.Y)&&position.X>=8&&position.X<=92&&position.Y>=10&&position.Y<=90;}
  public object SaveTopology(Dictionary<string,object> data) {lock(gate) {string id=Text(data,"Id","");Find(id);var position=new TopologyPosition {X=Number(data,"X"),Y=Number(data,"Y")};if(!ValidPosition(position))throw new ArgumentException("拓扑坐标超出可用区域");topology[id]=position;store.Save("topology.json",topology);return new {Id=id,x=position.X,y=position.Y};}}
  public void ResetTopology() {lock(gate) {topology=new Dictionary<string,TopologyPosition>();store.Save("topology.json",topology);}}
  public object Upsert(Dictionary<string,object> data) {
   lock(gate) {
    string id=Text(data,"Id","");var old=string.IsNullOrEmpty(id)?null:Find(id);
    if(old!=null&&old.Demo) throw new ArgumentException("演示设备不可编辑，请添加真实设备");
    if(old==null&&devices.Count>=100) throw new ArgumentException("本版本最多管理 100 台设备");
    var d=new Device {Id=old==null?Guid.NewGuid().ToString("N"):old.Id,Name=Text(data,"Name","").Trim(),Address=Text(data,"Address","").Trim(),Type=Text(data,"Type","交换机"),Location=Text(data,"Location","").Trim(),Port=Int(data,"Port",161,1,65535),Interval=Int(data,"Interval",60,30,3600),Timeout=Int(data,"Timeout",1500,300,5000),Threshold=Int(data,"Threshold",85,1,100),Enabled=Bool(data,"Enabled",true),CpuOid=Text(data,"CpuOid",old==null?"":old.CpuOid).Trim().TrimStart('.'),MemoryOid=Text(data,"MemoryOid",old==null?"":old.MemoryOid).Trim().TrimStart('.')};
    if(d.Name.Length<1||d.Name.Length>80||d.Location.Length>100) throw new ArgumentException("设备名称需为 1–80 字，位置最多 100 字");
    if(!new[]{"交换机","路由器","防火墙","其他"}.Contains(d.Type))throw new ArgumentException("设备类型无效");
    IPAddress address;if(!IPAddress.TryParse(d.Address,out address)||address.Equals(IPAddress.Any)||address.Equals(IPAddress.IPv6Any)||address.Equals(IPAddress.Broadcast)||address.IsIPv6Multicast||(address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&address.GetAddressBytes()[0]>=224)) throw new ArgumentException("请填写有效的单台设备 IPv4/IPv6 地址，不接受网段或广播地址");
    d.Address=address.ToString();
    if(devices.Any(x=>x.Id!=d.Id&&!x.Demo&&x.Address==d.Address&&x.Port==d.Port))throw new ArgumentException("该 IP 和 SNMP 端口已存在");
    string community=Text(data,"Community","");
    if(community.Length>128)throw new ArgumentException("社区字符串最多 128 字符");
    if(string.IsNullOrEmpty(community)) {if(old==null||string.IsNullOrEmpty(old.Secret))throw new ArgumentException("请填写 SNMP 只读社区字符串");d.Secret=old.Secret;}else d.Secret=Vault.Protect(community);
    var updated=devices.Where(x=>x.Id!=d.Id).Concat(new[]{d}).ToList();store.Save("devices.json",updated);devices=updated;
    current.Remove(d.Id);failures.Remove(d.Id);next[d.Id]=0;
    foreach(var a in alerts.Where(a=>a.DeviceId==d.Id&&!a.Resolved.HasValue))a.Resolved=Clock.Now();
    PersistAlerts();return d.Public();
   }
  }
  public void Delete(string id) {lock(gate) {Find(id);var updated=devices.Where(d=>d.Id!=id).ToList();store.Save("devices.json",updated);devices=updated;current.Remove(id);histories.Remove(id);next.Remove(id);failures.Remove(id);if(topology.Remove(id))store.Save("topology.json",topology);foreach(var a in alerts.Where(a=>a.DeviceId==id&&!a.Resolved.HasValue))a.Resolved=Clock.Now();PersistAlerts();}}
  public void Acknowledge(string id) {lock(gate) {var a=alerts.FirstOrDefault(x=>x.Id==id);if(a==null)throw new ArgumentException("告警不存在");a.Acknowledged=true;PersistAlerts();}}
  public void RequestPoll(string id) {lock(gate) {if(string.IsNullOrEmpty(id)){foreach(var d in devices)next[d.Id]=0;}else{Find(id);next[id]=0;}}Tick(null);}
  public void AddDemo() {lock(gate) {
   if(devices.Any(d=>d.Demo))return;
   if(devices.Count>97)throw new ArgumentException("没有足够设备名额");
   var updated=new List<Device>(devices);
   foreach(string type in new[]{"交换机","路由器","防火墙"}) {
    var d=new Device {Id=Guid.NewGuid().ToString("N"),Name=type=="交换机"?"核心交换机 · 演示":type=="路由器"?"出口路由器 · 演示":"边界防火墙 · 演示",Address="模拟设备",Type=type,Location="演示机房",Demo=true,Interval=30};updated.Add(d);
    var s=Collector.Demo(d,null);current[d.Id]=s;var h=new List<Sample>();for(int i=120;i>=1;i--){var p=s.Point();p.Time=Clock.Now()-i*60000;p.InBps*=0.65+0.25*Math.Sin(i/7.0);p.OutBps*=0.7+0.2*Math.Cos(i/9.0);h.Add(p);}histories[d.Id]=h;
   }store.Save("devices.json",updated);devices=updated;
  }RequestPoll(null);}
  void Tick(object state) {
   lock(gate) {
    if(stopped)return;
    foreach(var d in devices.Where(x=>x.Enabled).OrderBy(x=>next.ContainsKey(x.Id)?next[x.Id]:0).ToList()) {
     if(busy.Count>=4)break;
     if(busy.Contains(d.Id)||(next.ContainsKey(d.Id)&&next[d.Id]>Clock.Now()))continue;
     busy.Add(d.Id);next[d.Id]=Clock.Now()+d.Interval*1000;Device captured=d;
     ThreadPool.QueueUserWorkItem(delegate {Run(captured);});
    }
   }
  }
  void Run(Device d) {
   Snapshot old;lock(gate)current.TryGetValue(d.Id,out old);
   Snapshot s;
   try {s=collector.Poll(d,old);}catch(Exception ex){store.Log("采集失败 "+d.Id+" "+ex.GetType().Name);s=new Snapshot {Id=d.Id,Time=Clock.Now(),Status="offline",Error="采集异常，请查看运行日志",Loss=100};}
   lock(gate) {
    busy.Remove(d.Id);if(stopped||!devices.Any(x=>object.ReferenceEquals(x,d)))return;
    List<Sample> h;if(!histories.TryGetValue(d.Id,out h)){h=new List<Sample>();histories[d.Id]=h;}
    ApplyMetricWindow(s,h);
    Evaluate(d,old,s);current[d.Id]=s;
    h.Add(s.Point());h.RemoveAll(p=>p.Time<Clock.Now()-86400000);if(h.Count>2880)h.RemoveRange(0,h.Count-2880);
    try {store.Save(d.Id+".json",new StateFile {Current=s,History=h});PersistAlerts();StorageWarning="";}catch(Exception ex){StorageWarning="数据保存失败，请检查磁盘可用空间和目录权限";store.Log(StorageWarning+" "+ex.GetType().Name);}
   }
  }
  static void ApplyMetricWindow(Snapshot s,List<Sample> history) {
   if(!s.Cpu.HasValue){if(!s.Cpu5Min.HasValue)s.Cpu5Min=null;s.Cpu5MinSamples=0;return;}
   var values=history.Where(p=>p.Time>=s.Time-300000&&p.Time<=s.Time&&p.Cpu.HasValue).Select(p=>p.Cpu.Value).ToList();
   values.Add(s.Cpu.Value);s.Cpu5MinSamples=values.Count;
   if(!s.Cpu5Min.HasValue){s.Cpu5Min=values.Average();s.Cpu5MinSource="平台采样均值";}
  }
  void Evaluate(Device d,Snapshot old,Snapshot s) {
   int fail;failures.TryGetValue(d.Id,out fail);fail=s.Snmp?0:fail+1;failures[d.Id]=fail;
   if(s.Snmp)Resolve(d.Id,"device");else if(fail>=3)Raise(d,"device","critical",s.Status=="degraded"?"连续 3 次 SNMP 失败，Ping 仍可达":"连续 3 次 SNMP 和 Ping 未响应（也可能被 ACL 阻断）");
   if(!s.Snmp)return;
   if(!string.IsNullOrEmpty(s.Error))Raise(d,"collection","warning",s.Error);else Resolve(d.Id,"collection");
   if(old!=null&&old.UptimeTicks.HasValue&&s.UptimeTicks<old.UptimeTicks) {
    alerts.Add(new Alert {Id=Guid.NewGuid().ToString("N"),DeviceId=d.Id,DeviceName=d.Name,Key="restart",Level="info",Message="设备运行计数减少：可能重启或 sysUpTime 回绕，已重建流量基线",Time=Clock.Now(),Resolved=Clock.Now()});
   }
   Threshold(d,"cpu",s.Cpu,"CPU 使用率");Threshold(d,"memory",s.Memory,"内存使用率");
   foreach(var p in s.Interfaces) {
    var before=old==null?null:old.Interfaces.FirstOrDefault(x=>x.Index==p.Index);
    string key="port:"+p.Index;
    if(p.Oper==1||p.Admin!=1)Resolve(d.Id,key);else if(before!=null&&before.Oper==1&&p.Admin==1)Raise(d,key,"warning",p.Name+" 从 UP 变为非 UP");
    Threshold(d,"util:"+p.Index,p.Utilization,p.Name+" 带宽利用率");
    if(before!=null&&s.UptimeTicks>=old.UptimeTicks&&p.Discontinuity==before.Discontinuity) {
     bool error=(p.InErrors>before.InErrors)||(p.OutErrors>before.OutErrors)||(p.InDiscards>before.InDiscards)||(p.OutDiscards>before.OutDiscards);
     if(error)Raise(d,"errors:"+p.Index,"warning",p.Name+" 错误包或丢弃计数增加");else Resolve(d.Id,"errors:"+p.Index);
    }
   }
  }
  void Threshold(Device d,string key,double? value,string label) {if(!value.HasValue)return;if(value>=d.Threshold)Raise(d,key,"warning",label+" 达到 "+value.Value.ToString("F1")+"%（阈值 "+d.Threshold+"%）");else if(value<d.Threshold&&value<=Math.Max(0,d.Threshold-5))Resolve(d.Id,key);}
  void Raise(Device d,string key,string level,string message) {if(alerts.Any(a=>a.DeviceId==d.Id&&a.Key==key&&!a.Resolved.HasValue))return;alerts.Add(new Alert {Id=Guid.NewGuid().ToString("N"),DeviceId=d.Id,DeviceName=d.Name,Key=key,Level=level,Message=message,Time=Clock.Now()});}
  void Resolve(string id,string key) {foreach(var a in alerts.Where(x=>x.DeviceId==id&&x.Key==key&&!x.Resolved.HasValue))a.Resolved=Clock.Now();}
  void PersistAlerts() {alerts=alerts.OrderByDescending(a=>a.Time).Take(2000).ToList();store.Save("alerts.json",alerts);}
  public void Dispose() {lock(gate)stopped=true;if(timer!=null)timer.Dispose();}
 }
}
