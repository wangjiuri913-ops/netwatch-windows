using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;

namespace NetWatch {
 public sealed class LocalServer : IDisposable {
  readonly TcpListener listener; readonly Engine engine; readonly Store store;
  readonly string token=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
  readonly Semaphore slots=new Semaphore(16,16); volatile bool stopped;
  readonly HashSet<string> allowedHosts;
  public readonly string Url; readonly string host;
  public LocalServer(Engine e,Store s,int port) {
   engine=e;store=s;
   var addresses=LocalAddresses();
   var preferred=addresses.FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a))??IPAddress.Loopback;
   host=HostValue(preferred,port);Url="http://"+host;
   allowedHosts=new HashSet<string>(addresses.Select(a=>HostValue(a,port)),StringComparer.OrdinalIgnoreCase);
   allowedHosts.Add("localhost:"+port);allowedHosts.Add("127.0.0.1:"+port);allowedHosts.Add("[::1]:"+port);
   listener=new TcpListener(IPAddress.Any,port);listener.Start(32);new Thread(Accept){IsBackground=true,Name="NetWatch HTTP"}.Start();
  }
  static string HostValue(IPAddress address,int port) {return address.AddressFamily==AddressFamily.InterNetworkV6?"["+address+"]:"+port:address+":"+port;}
  static List<IPAddress> LocalAddresses() {
   var preferred=new List<IPAddress>();var result=new List<IPAddress>();
   try {
    foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()) {
     if(nic.OperationalStatus!=OperationalStatus.Up||nic.NetworkInterfaceType==NetworkInterfaceType.Loopback||nic.NetworkInterfaceType==NetworkInterfaceType.Tunnel)continue;
     var properties=nic.GetIPProperties();
     bool primaryNic=(nic.NetworkInterfaceType==NetworkInterfaceType.Ethernet||nic.NetworkInterfaceType==NetworkInterfaceType.Wireless80211)&&properties.GatewayAddresses.Any(g=>g.Address!=null&&!g.Address.Equals(IPAddress.Any)&&!g.Address.Equals(IPAddress.IPv6Any));
     foreach(var item in properties.UnicastAddresses) {
      var address=item.Address;
      if(IPAddress.IsLoopback(address)||address.IsIPv6LinkLocal||(address.AddressFamily==AddressFamily.InterNetwork&&address.ToString().StartsWith("169.254.",StringComparison.Ordinal)))continue;
      var target=primaryNic&&address.AddressFamily==AddressFamily.InterNetwork?preferred:result;
      if(!preferred.Any(x=>x.Equals(address))&&!result.Any(x=>x.Equals(address)))target.Add(address);
     }
    }
   } catch {}
   try {
    foreach(var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)if(!IPAddress.IsLoopback(address)&&!result.Any(x=>x.Equals(address)))result.Add(address);
   } catch {}
   var ordered=preferred.Concat(result).ToList();ordered.Add(IPAddress.Loopback);return ordered;
  }
  bool AllowedOrigin(string origin) {Uri parsed;return Uri.TryCreate(origin,UriKind.Absolute,out parsed)&&parsed.Scheme=="http"&&allowedHosts.Contains(parsed.Authority);}
  void Accept() {while(!stopped){try{var client=listener.AcceptTcpClient();if(!slots.WaitOne(0)){client.Close();continue;}ThreadPool.QueueUserWorkItem(delegate{try{Handle(client);}finally{client.Close();slots.Release();}});}catch{if(!stopped)Thread.Sleep(100);}}}
  static string Line(Stream stream,ref int count) {
   var bytes=new List<byte>();int b;
   while((b=stream.ReadByte())!=-1) {if(++count>16384)throw new ArgumentException("请求头过大");if(b==10)break;if(b!=13)bytes.Add((byte)b);}
   if(b==-1&&bytes.Count==0)throw new EndOfStreamException();return Encoding.ASCII.GetString(bytes.ToArray());
  }
  void Handle(TcpClient client) {
   client.ReceiveTimeout=5000;client.SendTimeout=5000;
   using(var stream=client.GetStream()) {
    try {
     int count=0;string[] first=Line(stream,ref count).Split(' ');if(first.Length!=3)throw new ArgumentException("无效 HTTP 请求");
     string method=first[0],route=first[1];if(!route.StartsWith("/",StringComparison.Ordinal))throw new ArgumentException("无效路径");
     var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);string line;
     while((line=Line(stream,ref count)).Length>0){int split=line.IndexOf(':');if(split<=0)throw new ArgumentException("无效请求头");string k=line.Substring(0,split);if(headers.ContainsKey(k))throw new ArgumentException("重复请求头");headers[k]=line.Substring(split+1).Trim();}
     string header;
     if(headers.ContainsKey("Transfer-Encoding"))throw new ArgumentException("不支持分块请求");
     int length=0;if(headers.TryGetValue("Content-Length",out header)&&(!int.TryParse(header,out length)||length<0||length>65536))throw new ArgumentException("请求体过大");
     byte[] body=new byte[length];int got=0;while(got<length){int n=stream.Read(body,got,length-got);if(n==0)throw new EndOfStreamException();got+=n;}
     if(!headers.TryGetValue("Host",out header)||!allowedHosts.Contains(header)){Respond(stream,403,"text/plain","不允许的访问地址");return;}
     if(headers.TryGetValue("Origin",out header)&&!AllowedOrigin(header)){Respond(stream,403,"text/plain","不允许跨站访问");return;}
     if(headers.TryGetValue("Sec-Fetch-Site",out header)&&header=="cross-site"){Respond(stream,403,"text/plain","不允许跨站访问");return;}
     Uri uri=new Uri("http://127.0.0.1"+route);string path=uri.AbsolutePath;
      if(method=="GET"&&path=="/api/session"){Respond(stream,200,"application/json",Json.Encode(new {Token=token,Version="1.4.0",AccessUrl=Url}));return;}
     if(path.StartsWith("/api/",StringComparison.Ordinal)) {
      if(!headers.TryGetValue("X-NetWatch-Token",out header)||header!=token){Respond(stream,403,"application/json",Json.Encode(new {Error="会话已更新，请刷新页面"}));return;}
      string id=System.Web.HttpUtility.ParseQueryString(uri.Query)["id"];
      if(method=="GET"&&path=="/api/dashboard") {Respond(stream,200,"application/json",Json.Encode(engine.Dashboard()));return;}
      if(method=="GET"&&path=="/api/device") {Respond(stream,200,"application/json",Json.Encode(engine.Detail(id)));return;}
      if(method=="GET"&&path=="/api/export") {
       var csv=new StringBuilder("\uFEFF时间(UTC),采集状态,平均延迟(ms),探测无响应(%),所有接口入方向合计(bps),所有接口出方向合计(bps),CPU当前(%),CPU 1分钟(%),CPU 5分钟(%),CPU 5分钟来源,平台窗口样本数,内存(%),内存来源,最大端口利用率(%)\r\n");
       foreach(var p in engine.History(id))csv.Append(new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(p.Time).ToString("o")).Append(',').Append(p.Status).Append(',').Append(Num(p.Rtt)).Append(',').Append(Num(p.Loss)).Append(',').Append(Num(p.InBps)).Append(',').Append(Num(p.OutBps)).Append(',').Append(Num(p.Cpu)).Append(',').Append(Num(p.Cpu1Min)).Append(',').Append(Num(p.Cpu5Min)).Append(',').Append(p.Cpu5MinSource).Append(',').Append(p.Cpu5MinSamples).Append(',').Append(Num(p.Memory)).Append(',').Append(p.MemorySource).Append(',').Append(Num(p.MaxUtilization)).Append("\r\n");
       Respond(stream,200,"text/csv",csv.ToString());return;
      }
      if(method=="POST") {
       if(!headers.TryGetValue("Content-Type",out header)||!header.StartsWith("application/json",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("需要 JSON 请求");
       var data=Json.Decode<Dictionary<string,object>>(Encoding.UTF8.GetString(body));if(data==null)throw new ArgumentException("请求为空");object value;id=data.TryGetValue("Id",out value)?Convert.ToString(value):null;
       object answer=new {Ok=true};
        switch(path){case "/api/save":answer=engine.Upsert(data);break;case "/api/delete":engine.Delete(id);break;case "/api/poll":engine.RequestPoll(id);break;case "/api/ack":engine.Acknowledge(id);break;case "/api/demo":engine.AddDemo();break;case "/api/topology":answer=engine.SaveTopology(data);break;case "/api/topology/reset":engine.ResetTopology();break;default:Respond(stream,404,"application/json","{\"Error\":\"接口不存在\"}");return;}
       Respond(stream,200,"application/json",Json.Encode(answer));return;
      }
      Respond(stream,404,"application/json","{\"Error\":\"接口不存在\"}");return;
     }
     if(method!="GET"){Respond(stream,405,"text/plain","仅支持 GET");return;}
     string resource=null,mime="text/plain";
     if(path=="/"){resource="index.html";mime="text/html";}
     if(path=="/app.js"){resource="app.js";mime="text/javascript";}
     if(path=="/style.css"){resource="style.css";mime="text/css";}
     if(resource==null){Respond(stream,404,"text/plain","Not found");return;}
     using(var asset=Assembly.GetExecutingAssembly().GetManifestResourceStream("web."+resource))using(var reader=new StreamReader(asset,Encoding.UTF8))Respond(stream,200,mime,reader.ReadToEnd());
    } catch(ArgumentException ex) {SafeError(stream,400,ex.Message);} catch(Exception ex) {store.Log("HTTP "+ex.GetType().Name);SafeError(stream,500,"操作未完成，请检查运行日志和磁盘空间");}
   }
  }
  static string Num(double? value) {return value.HasValue?value.Value.ToString("0.###",CultureInfo.InvariantCulture):"";}
  static void SafeError(Stream stream,int code,string message) {try{Respond(stream,code,"application/json",Json.Encode(new {Error=message}));}catch{}}
  static void Respond(Stream stream,int code,string mime,string text) {
   byte[] content=Encoding.UTF8.GetBytes(text);string label=code==200?"OK":code==400?"Bad Request":code==403?"Forbidden":code==404?"Not Found":"Error";
    string header="HTTP/1.1 "+code+" "+label+"\r\nContent-Type: "+mime+"; charset=utf-8\r\nContent-Length: "+content.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'\r\n\r\n";
   byte[] h=Encoding.ASCII.GetBytes(header);stream.Write(h,0,h.Length);stream.Write(content,0,content.Length);
  }
  public void Dispose(){stopped=true;listener.Stop();}
 }
}
