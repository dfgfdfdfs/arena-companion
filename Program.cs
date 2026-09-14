using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ArenaCompanion {
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static void Main(string[] args) {
            int savedOption=Array.IndexOf(args,"--saved"),savedRootOption=Array.IndexOf(args,"--saved-root"),savedIdOption=Array.IndexOf(args,"--saved-id");
            if((savedOption>=0&&savedOption+1<args.Length)||(savedRootOption>=0&&savedRootOption+1<args.Length&&savedIdOption>=0&&savedIdOption+1<args.Length)) {
                try {
                    string folder=savedOption>=0?SavedConversationStore.ResolveLegacyFolder(args[savedOption+1]):SavedConversationStore.ResolveById(args[savedRootOption+1],args[savedIdOption+1]);
                    SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SavedConversationWindow(folder));
                }
                catch(Exception ex){MessageBox.Show("无法打开保存的对话："+ex.Message,"Arena 对话入口");}
                return;
            }
            int rootOption=Array.IndexOf(args,"--root");if(rootOption>=0&&rootOption+1<args.Length)Environment.SetEnvironmentVariable("ARENA_INSTANCE_ROOT",Path.GetFullPath(args[rootOption+1]));
            Environment.SetEnvironmentVariable("ARENA_AUTO_LOGIN",Array.IndexOf(args,"--register")>=0?"1":null);
            Environment.SetEnvironmentVariable("ARENA_RUN_AFTER_LOGIN",Array.IndexOf(args,"--run-after-login")>=0?"1":null);
            SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string instance=null;int option=Array.IndexOf(args,"--instance");
            if(option>=0&&option+1<args.Length)instance=args[option+1];
            if(instance==null){using(var picker=new InstancePicker()){if(picker.ShowDialog()!=DialogResult.OK)return;instance=picker.SelectedName;}}
            try {InstanceContext.Configure(instance);}catch(Exception ex){MessageBox.Show(ex.Message,"实例无法打开");return;}
            bool created;
            using (var mutex = new Mutex(true, InstanceContext.MutexName(Environment.GetEnvironmentVariable("ARENA_DATA_DIRECTORY")), out created)) {
                if (!created) { if(Environment.GetEnvironmentVariable("ARENA_BACKGROUND")!="1")MessageBox.Show("这个实例已经打开。请选用另一个实例名。", "Arena 多开"); return; }
                try {
                    Application.Run(new MainForm(Array.IndexOf(args, "--demo") >= 0));
                } catch (Exception ex) {
                    MessageBox.Show("软件未能启动：\n" + ex.Message + "\n请保留整个软件文件夹，不要只移动 exe。", "Arena 筛选助手");
                }
            }
        }
    }
}
