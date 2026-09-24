using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace NetWatch {
 class Fixture : IDisposable {
  readonly UdpClient udp;readonly Thread thread;volatile bool stopped;
  public int Port;public volatile bool Respond=true;public volatile bool Reset=false;public volatile bool PortDown=false;public volatile bool Discontinuity=false;public volatile bool Errors=false;public volatile bool HighCpu=false;public volatile bool Wide=true;public volatile bool H3cMetrics=false;public volatile bool HuaweiMetrics=false;public volatile bool StandardMemory=false;public volatile int H3cFirstCpu=0;public volatile int H3cSecondCpu=37;public volatile int H3cFirstMemory=0;public volatile int H3cSecondMemory=55;
  readonly Stopwatch watch=Stopwatch.StartNew();
  public Fixture(){udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));Port=((IPEndPoint)udp.Client.LocalEndPoint).Port;thread=new Thread(Loop){IsBackground=true};thread.Start();}
  List<Variable> Values(){
   string i="1.3.6.1.2.1.2.2.1.",x="1.3.6.1.2.1.31.1.1.1.";
   ulong bytes=1000000UL+(ulong)watch.ElapsedMilliseconds*1250UL;
   var list=new List<Variable>{
    new Variable(new ObjectIdentifier("1.3.6.1.2.1.1.1.0"),new OctetString("SNMP loopback test device")),
    new Variable(new ObjectIdentifier("1.3.6.1.2.1.1.3.0"),new TimeTicks((uint)((Reset?10:1000000)+watch.ElapsedMilliseconds/10))),
    new Variable(new ObjectIdentifier("1.3.6.1.2.1.1.5.0"),new OctetString("Test-Switch")),
    new Variable(new ObjectIdentifier(i+"1.1"),new Integer32(1)),new Variable(new ObjectIdentifier(i+"2.1"),new OctetString("Ethernet1")),new Variable(new ObjectIdentifier(i+"5.1"),new Gauge32(1000000000)),
    new Variable(new ObjectIdentifier(i+"7.1"),new Integer32(1)),new Variable(new ObjectIdentifier(i+"8.1"),new Integer32(PortDown?2:1)),new Variable(new ObjectIdentifier(i+"10.1"),new Counter32((uint)bytes)),new Variable(new ObjectIdentifier(i+"16.1"),new Counter32((uint)(bytes/2))),
    new Variable(new ObjectIdentifier(i+"13.1"),new Counter32(0)),new Variable(new ObjectIdentifier(i+"14.1"),new Counter32(Errors?3U:0U)),new Variable(new ObjectIdentifier(i+"19.1"),new Counter32(0)),new Variable(new ObjectIdentifier(i+"20.1"),new Counter32(0)),
    new Variable(new ObjectIdentifier(x+"1.1"),new OctetString("GE1")),new Variable(new ObjectIdentifier(x+"15.1"),new Gauge32(1000)),new Variable(new ObjectIdentifier(x+"18.1"),new OctetString("Uplink")),new Variable(new ObjectIdentifier(x+"19.1"),new TimeTicks(Discontinuity?100U:0U)),
   new Variable(new ObjectIdentifier("1.3.6.1.4.1.55555.1.0"),new Integer32(HighCpu?95:23)),new Variable(new ObjectIdentifier("1.3.6.1.4.1.55555.2.0"),new Integer32(41))
   };
   if(H3cMetrics){list.Add(new Variable(new ObjectIdentifier("1.3.6.1.4.1.25506.2.6.1.1.1.1.6.1"),new Integer32(H3cFirstCpu)));list.Add(new Variable(new ObjectIdentifier("1.3.6.1.4.1.25506.2.6.1.1.1.1.6.2"),new Integer32(H3cSecondCpu)));list.Add(new Variable(new ObjectIdentifier("1.3.6.1.4.1.25506.2.6.1.1.1.1.8.1"),new Integer32(H3cFirstMemory)));list.Add(new Variable(new ObjectIdentifier("1.3.6.1.4.1.25506.2.6.1.1.1.1.8.2"),new Integer32(H3cSecondMemory)));}
   if(HuaweiMetrics){list.Add(new Variable(new ObjectIdentifier("1.3.6.1.2.1.1.2.0"),new ObjectIdentifier("1.3.6.1.4.1.2011.2.239.1")));list.Add(new Variable(new ObjectIdentifier("1.3.6.1.4.1.2011.5.25.31.1.1.1.1.5.1"),new Integer32(0)));list.Add(new Variable(new ObjectIdentifier("1.3.6.1.4.1.2011.5.25.31.1.1.1.1.5.2"),new Integer32(44)));list.Add(new Variable(new ObjectIdentifier("1.3.6.1.4.1.2011.5.25.31.1.1.1.1.7.1"),new Integer32(0)));list.Add(new Variable(new ObjectIdentifier("1.3.6.1.4.1.2011.5.25.31.1.1.1.1.7.2"),new Integer32(66)));}
   if(StandardMemory){list.Add(new Variable(new ObjectIdentifier("1.3.6.1.2.1.25.2.3.1.2.1"),new ObjectIdentifier("1.3.6.1.2.1.25.2.1.2")));list.Add(new Variable(new ObjectIdentifier("1.3.6.1.2.1.25.2.3.1.5.1"),new Integer32(1000)));list.Add(new Variable(new ObjectIdentifier("1.3.6.1.2.1.25.2.3.1.6.1"),new Integer32(300)));}
   if(Wide){list.Add(new Variable(new ObjectIdentifier(x+"6.1"),new Counter64(bytes)));list.Add(new Variable(new ObjectIdentifier(x+"10.1"),new Counter64(bytes/2)));}
   return list.OrderBy(v=>v.Id).ToList();
  }
  void Loop(){while(!stopped){try{IPEndPoint remote=new IPEndPoint(IPAddress.Any,0);byte[] bytes=udp.Receive(ref remote);if(!Respond)continue;var msg=MessageFactory.ParseMessages(bytes,new UserRegistry())[0];var all=Values();var reply=new List<Variable>();if(msg is GetBulkRequestMessage){var oid=msg.Variables()[0].Id;reply=all.Where(v=>v.Id.CompareTo(oid)>0).Take(20).ToList();if(reply.Count==0)reply.Add(new Variable(oid,new EndOfMibView()));}else{foreach(var v in msg.Variables())reply.Add(all.FirstOrDefault(x=>x.Id.ToString()==v.Id.ToString())??new Variable(v.Id,new NoSuchInstance()));}byte[] result=new ResponseMessage(msg.RequestId(),VersionCode.V2,new OctetString("fixture-read-only"),ErrorCode.NoError,0,reply).ToBytes();udp.Send(result,result.Length,remote);}catch{if(stopped)return;}}}
  public void Dispose(){stopped=true;udp.Close();thread.Join(1000);}
 }
 class Tests {
  static int checks;
  static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);checks++;Console.WriteLine("PASS "+message);}
  static Dictionary<string,object> Config(Fixture f){return new Dictionary<string,object>{{"Name","Lab switch"},{"Address","127.0.0.1"},{"Community","fixture-read-only"},{"Port",f.Port},{"Timeout",300},{"Interval",60},{"CpuOid","1.3.6.1.4.1.55555.1.0"},{"MemoryOid","1.3.6.1.4.1.55555.2.0"}};}
  static Snapshot Current(Engine e,string id){return Json.Decode<DetailResult>(Json.Encode(e.Detail(id))).Current;}
  class DetailResult {public Snapshot Current{get;set;}public List<Sample> History{get;set;}}
  class DashboardResult {public List<Alert> Alerts{get;set;}public Dictionary<string,TopologyPosition> Topology{get;set;}}
  static Snapshot Poll(Engine e,string id){var before=Current(e,id);long time=before==null?0:before.Time;e.RequestPoll(id);for(int i=0;i<150;i++){Thread.Sleep(50);var after=Current(e,id);if(after!=null&&after.Time>time)return after;}throw new Exception("Poll timed out");}
  static bool Active(Engine e,string key){return Json.Decode<DashboardResult>(Json.Encode(e.Dashboard())).Alerts.Any(a=>a.Key==key&&!a.Resolved.HasValue);}
  public static int Main(string[] args){try{
   string root=Path.GetFullPath(args.Length>0?args[0]:"test-data-"+Guid.NewGuid().ToString("N"));
   Check(Rates.Bps(1000,2000,true,2,1000000,false)==4000,"64-bit rate uses actual elapsed time");
   Check(Rates.Bps(4294967200,100,false,1,10000000,false)==1568,"safe 32-bit wrap");
   Check(!Rates.Bps(100,200,false,60,1000000000,false).HasValue,"ambiguous high-speed 32-bit interval is rejected");
   Check(!Rates.Bps(900,100,true,1,1000000,false).HasValue,"64-bit counter reset is rejected");
   Check(!Rates.Bps(100,200,true,1,1000000,true).HasValue,"reboot invalidates rate baseline");
   Check(!Rates.Bps(100,200,true,601,1000000,false).HasValue,"stale baseline rejected");
   string secret=Vault.Protect("fixture-read-only");Check(secret!="fixture-read-only"&&Vault.Reveal(secret)=="fixture-read-only","Windows DPAPI round-trip");
   using(var fixture=new Fixture()){
    var d=new Device{Id="fixture",Address="127.0.0.1",Port=fixture.Port,Timeout=300,Secret=secret,CpuOid="1.3.6.1.4.1.55555.1.0",MemoryOid="1.3.6.1.4.1.55555.2.0"};var c=new Collector();
    var first=c.Poll(d,null);Check(first.Snmp&&first.Status=="online"&&first.SysName=="Test-Switch","UDP SNMP system GET");Check(first.Interfaces.Count==1&&first.Interfaces[0].Name=="GE1"&&first.Interfaces[0].Counter64,"GETBULK interface discovery with IF-MIB extension");Check(!first.InBps.HasValue,"first sample has no invented traffic");Check(first.Cpu==23&&first.Memory==41,"custom percentage OIDs");
    fixture.H3cMetrics=true;d.CpuOid="1.3.6.1.4.1.25506.2.6.1.1.1.1.6.1";d.MemoryOid="1.3.6.1.4.1.25506.2.6.1.1.1.1.8.1";var h3c=c.Poll(d,null);Check(h3c.Cpu==37&&h3c.Memory==55,"H3C metric auto fallback scans past zero-valued entity rows");fixture.H3cFirstCpu=5;fixture.H3cSecondCpu=70;fixture.H3cFirstMemory=18;fixture.H3cSecondMemory=92;var main=c.Poll(d,null);Check(main.Cpu==5&&main.Memory==18,"H3C metric prefers the main entity instead of the highest board value");fixture.StandardMemory=true;var systemMemory=c.Poll(d,null);Check(systemMemory.Memory==30&&systemMemory.MemorySource=="HOST-RESOURCES-MIB 系统 RAM","system memory prefers HOST-RESOURCES-MIB over entity memory");fixture.H3cMetrics=false;fixture.StandardMemory=false;fixture.HuaweiMetrics=true;var huawei=c.Poll(d,null);Check(huawei.Cpu==44&&huawei.Memory==66&&huawei.MemorySource=="华为 HUAWEI-ENTITY-EXTENT-MIB 实体","Huawei entity CPU and memory auto detection");fixture.HuaweiMetrics=false;d.CpuOid="1.3.6.1.4.1.55555.1.0";d.MemoryOid="1.3.6.1.4.1.55555.2.0";
    Thread.Sleep(150);var second=c.Poll(d,first);Check(second.InBps>7000000&&second.InBps<13000000,"live UDP counter delta is approximately 10 Mbps");
    fixture.Reset=true;var reboot=c.Poll(d,second);Check(!reboot.InBps.HasValue,"reboot detected from sysUpTime");
    fixture.Discontinuity=true;var discontinuity=c.Poll(d,reboot);Check(!discontinuity.InBps.HasValue,"ifCounterDiscontinuityTime resets baseline");
    fixture.Respond=false;var down=c.Poll(d,second);Check(!down.Snmp&&down.Status=="degraded"&&down.Rtt.HasValue,"ICMP reachable does not imply SNMP healthy");fixture.Respond=true;
    var store=new Store(root);string id;
    using(var engine=new Engine(store,false)){
     var config=Config(fixture);var saved=Json.Decode<Dictionary<string,object>>(Json.Encode(engine.Upsert(config)));id=(string)saved["Id"];
     Check(!Json.Encode(engine.Dashboard()).Contains("fixture-read-only")&&!Json.Encode(engine.Dashboard()).Contains("Secret"),"public API redacts community and encrypted secret");
     Check(!File.ReadAllText(Path.Combine(root,"devices.json")).Contains("fixture-read-only"),"configuration does not persist plaintext community");
     engine.SaveTopology(new Dictionary<string,object>{{"Id",id},{"X",23.5},{"Y",67.25}});var savedTopology=Json.Decode<DashboardResult>(Json.Encode(engine.Dashboard())).Topology;Check(savedTopology.ContainsKey(id)&&savedTopology[id].X==23.5&&savedTopology[id].Y==67.25&&File.Exists(Path.Combine(root,"topology.json")),"topology coordinates save to data directory");
     bool rejectedUnknown=false;try{engine.SaveTopology(new Dictionary<string,object>{{"Id","missing"},{"X",30},{"Y",30}});}catch(ArgumentException){rejectedUnknown=true;}Check(rejectedUnknown,"topology rejects unknown device id");
     bool rejectedBounds=false;try{engine.SaveTopology(new Dictionary<string,object>{{"Id",id},{"X",120},{"Y",30}});}catch(ArgumentException){rejectedBounds=true;}Check(rejectedBounds,"topology rejects out-of-bounds coordinates");
     var baseline=Poll(engine,id);Check(baseline.Snmp,"engine collects fixture device");
     fixture.PortDown=true;fixture.HighCpu=true;fixture.Errors=true;Poll(engine,id);Check(Active(engine,"port:1")&&Active(engine,"cpu")&&Active(engine,"errors:1"),"link change, CPU and error-counter alarms");
     fixture.PortDown=false;fixture.HighCpu=false;Poll(engine,id);Check(!Active(engine,"port:1")&&!Active(engine,"cpu")&&!Active(engine,"errors:1"),"alarm recovery and threshold hysteresis");
     fixture.Respond=false;Poll(engine,id);Poll(engine,id);Check(!Active(engine,"device"),"two SNMP failures do not alert");Poll(engine,id);Check(Active(engine,"device"),"third SNMP failure raises alarm");
     var alert=Json.Decode<DashboardResult>(Json.Encode(engine.Dashboard())).Alerts.First(a=>a.Key=="device"&&!a.Resolved.HasValue);engine.Acknowledge(alert.Id);Check(Active(engine,"device"),"acknowledging does not falsely resolve alarm");
     fixture.Respond=true;Poll(engine,id);Check(!Active(engine,"device"),"SNMP recovery resolves device alarm");
     config["Id"]=id;config["Community"]="";config["Name"]="Edited name";engine.Upsert(config);Check(Poll(engine,id).Snmp,"editing with empty community preserves stored secret");
     Check(engine.History(id).Count>=8,"history records successful and failed polls");
    }
    using(var restored=new Engine(store,false)){Check(restored.History(id).Count>=8&&Current(restored,id).Snmp,"restart restores configuration, state and history");Check(Json.Decode<DashboardResult>(Json.Encode(restored.Dashboard())).Topology.ContainsKey(id),"restart restores topology coordinates");restored.ResetTopology();Check(Json.Decode<DashboardResult>(Json.Encode(restored.Dashboard())).Topology.Count==0,"topology reset clears saved layout");restored.SaveTopology(new Dictionary<string,object>{{"Id",id},{"X",40},{"Y",40}});restored.Delete(id);Check(File.Exists(Path.Combine(root,id+".json")),"removal retains historical file for recovery");Check(Json.Decode<DashboardResult>(Json.Encode(restored.Dashboard())).Topology.Count==0,"device removal deletes topology coordinate");}
   }
   Console.WriteLine("ALL "+checks+" CHECKS PASSED");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
