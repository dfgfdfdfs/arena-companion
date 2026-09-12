using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using ArenaCompanion;
static class HandoffTests {
    static int passed;static BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Check(bool value,string text){if(!value)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
    sealed class LoggedIn : IAuthPages {
        public Task<Dictionary<string,object>> Read(string target){return Task.FromResult(new Dictionary<string,object>{{"stage","authenticated"},{"account","fixture@example.invalid"}});}
        public Task Act(string target,string action,AccountData account){return Task.FromResult(0);}
        public void Navigate(string target,string url){}
    }
    [STAThread] static int Main(string[] args) {
        if(args.Length>0)return 0;
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);int result=0;
        Environment.SetEnvironmentVariable("ARENA_BACKGROUND","1");Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",null);
        using(var host=new Form {Opacity=0,ShowInTaskbar=false}) {
            host.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){Console.WriteLine(ex);result=1;}finally{host.Close();}};Application.Run(host);
        }
        return result;
    }
    static async Task Run() {
        var gate=new LoginTaskStart(true);
        Check(!gate.Take(true,"inspect")&&!gate.Take(false,"paused")&&gate.Pending,"incomplete or interrupted login never starts a task");
        Check(gate.Take(false,"complete")&&!gate.Take(false,"complete"),"confirmed login starts exactly once");
        gate=new LoginTaskStart(true);gate.Cancel();Check(!gate.Take(false,"complete"),"manual cancellation prevents delayed task start");
        Check(!new LoginTaskStart(false).Take(false,"complete"),"ordinary windows do not auto-start after login");
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","qa","handoff-"+DateTime.Now.ToString("HHmmss"));Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("ARENA_INSTANCE_ROOT",root);InstanceContext.Configure("Parent");
        string parent=InstanceContext.DirectoryFor(root,"Parent");new AccountStore(parent).Save(new AccountData {Email="fixture@example.invalid",Password="FixtureOnly!",Name="Kai"});
        var taskStore=new TaskSettingsStore(parent);taskStore.Save(new TaskSettings {Prompt="handoff fixture prompt",ContinueCollecting=true});
        using(var form=new MainForm(true)) {
            form.Show();for(int n=0;n<100&&!(bool)typeof(MainForm).GetField("ready",hidden).GetValue(form);n++)await Task.Delay(100);
            Check((bool)typeof(MainForm).GetField("ready",hidden).GetValue(form),"parent window initializes with isolated fixture data");
            ((Timer)typeof(MainForm).GetField("timer",hidden).GetValue(form)).Stop();
            var auth=new AuthFlow(new LoggedIn(),new AccountStore(parent));auth.Start();await auth.Tick();
            typeof(MainForm).GetField("authFlow",hidden).SetValue(form,auth);
            typeof(MainForm).GetField("loginTaskStart",hidden).SetValue(form,new LoginTaskStart(true));
            Check((bool)typeof(MainForm).GetMethod("StartTaskAfterLogin",hidden).Invoke(form,null),"real form handles the confirmed login transition");
            var controller=(RetryController)typeof(MainForm).GetField("controller",hidden).GetValue(form);
            for(int n=0;n<20&&controller.Attempt==0;n++){await controller.Tick();await Task.Delay(100);}
            Check(controller.Running&&controller.Attempt==1&&controller.Prompt=="handoff fixture prompt"&&controller.Limit==0,"real form fills and submits the inherited prompt automatically in the local browser fixture");
            Check(!(bool)typeof(MainForm).GetMethod("StartTaskAfterLogin",hidden).Invoke(form,null),"subsequent ticks cannot restart the task");
            controller.Pause("fixture complete");
            string name=AccountReplacement.Create(root,parent,"Parent");var start=ReplacementHandoff.StartInfo(root,name);
            Check(start.Arguments.Contains("--register --run-after-login"),"replacement launch requests registration and automatic task continuation");
            start.Arguments+=" --demo";
            using(var child=Process.Start(start)) {
                try {
                    await ReplacementHandoff.WaitReady(child,InstanceContext.DirectoryFor(root,name));
                    Check(!form.IsDisposed&&!child.HasExited,"old window remains open until the actual child reports readiness");
                    typeof(MainForm).GetMethod("CloseAfterHandoff",hidden).Invoke(form,null);await Task.Delay(600);
                    Check(form.IsDisposed&&!child.HasExited,"handoff closes only the old window after child readiness");
                } finally {if(!child.HasExited){child.CloseMainWindow();if(!child.WaitForExit(3000))child.Kill();}}
            }
        }
        using(var failed=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--exit") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
            failed.WaitForExit();bool rejected=false;try{await ReplacementHandoff.WaitReady(failed,root);}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"failed child cannot be accepted as a completed handoff");
        }
        Console.WriteLine("ALL "+passed+" PASSED");
    }
}
