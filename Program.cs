using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace NetWatch {
 class TrayContext : ApplicationContext {
  readonly NotifyIcon icon;
  public TrayContext(string url,string folder) {
   var menu=new ContextMenuStrip();menu.Items.Add("打开网络监测平台",null,delegate{Open(url);});menu.Items.Add("打开数据目录",null,delegate{Open(folder);});menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出并停止监测",null,delegate{ExitThread();});
   icon=new NotifyIcon {Icon=SystemIcons.Application,Text="NetWatch 网络监测平台",ContextMenuStrip=menu,Visible=true};icon.DoubleClick+=delegate{Open(url);};
   icon.ShowBalloonTip(2500,"NetWatch 已启动","关闭浏览器后继续采集。右键托盘图标可退出。",ToolTipIcon.Info);
  }
  static void Open(string value){try{Process.Start(new ProcessStartInfo(value){UseShellExecute=true});}catch{}}
  protected override void ExitThreadCore(){icon.Visible=false;icon.Dispose();base.ExitThreadCore();}
 }
 static class Program {
  [STAThread] static int Main(string[] args) {
   string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NetWatch");int port=18765;bool headless=args.Contains("--headless");
   foreach(string arg in args){if(arg.StartsWith("--data="))folder=Path.GetFullPath(arg.Substring(7));if(arg.StartsWith("--port=")&&!int.TryParse(arg.Substring(7),out port))return 2;}
   if(port<1024||port>65535)return 2;
   try {
    string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(folder.ToLowerInvariant()))).Replace("-","").Substring(0,24);
    bool created;using(var mutex=new Mutex(true,"Local\\NetWatch-"+key,out created)) {
     if(!created){if(!headless)MessageBox.Show("此数据目录的监测程序已在运行，请通过托盘图标打开。","NetWatch");return 0;}
     var store=new Store(folder);using(var engine=new Engine(store,true))using(var server=new LocalServer(engine,store,port)) {
      if(args.Contains("--demo"))engine.AddDemo();
      store.Log("平台启动 "+server.Url);
      if(headless)new ManualResetEvent(false).WaitOne();
      else {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);try{Process.Start(new ProcessStartInfo(server.Url){UseShellExecute=true});}catch{}Application.Run(new TrayContext(server.Url,folder));}
     }
    }
    return 0;
   }catch(Exception ex){try{new Store(folder).Log("启动失败 "+ex);}catch{}if(!headless)MessageBox.Show("启动失败："+ex.Message+"\n\n请检查端口 "+port+" 是否占用、.NET Framework 4.7.2+ 是否已安装。","NetWatch",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
}
