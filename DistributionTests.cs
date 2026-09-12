using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using ArenaCompanion;
static class DistributionTests {
    static int passed;
    static void Check(bool value,string text){if(!value)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
    [STAThread] static int Main(string[] args) {
        try {
            string root=Path.GetFullPath(args[0]);bool existing=args.Length>1;Directory.CreateDirectory(root);
            Environment.SetEnvironmentVariable("ARENA_BACKGROUND","1");Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",null);
            Environment.SetEnvironmentVariable("ARENA_QA_CAPTURE",Path.Combine(root,"capture"));
            int dialogs=0;bool ready=false;Exception failure=null;DateTime deadline=DateTime.UtcNow.AddSeconds(45);
            using(var timer=new Timer {Interval=200}) {
                timer.Tick+=(s,e)=>{
                    try {
                        if(DateTime.UtcNow>deadline)throw new TimeoutException("First-run initialization timed out");
                        foreach(Form form in new System.Collections.Generic.List<Form>(GetForms())) {
                            if(form is AccountSetupDialog) {
                                if(existing)throw new Exception("Restart unexpectedly requested credentials");dialogs++;
                                var email=(TextBox)form.Controls.Find("email",true)[0];var password=(TextBox)form.Controls.Find("password",true)[0];var confirm=(TextBox)form.Controls.Find("confirmation",true)[0];var save=(Button)form.Controls.Find("save",true)[0];
                                Check(password.Text==""&&confirm.Text==""&&email.Text=="","first-run fields contain no bundled identity or password");
                                Check(password.UseSystemPasswordChar&&confirm.UseSystemPasswordChar,"both password fields are masked");
                                password.Text=confirm.Text="lowercase!";save.PerformClick();
                                Check(form.DialogResult!=DialogResult.OK&&form.Controls.Find("error",true)[0].Text.Contains("大写"),"UI blocks a password without uppercase");
                                password.Text=confirm.Text="UppercaseOnly";save.PerformClick();Check(form.DialogResult!=DialogResult.OK,"UI blocks a password without a symbol");
                                password.Text="FixturePass!";confirm.Text="Different!";save.PerformClick();Check(form.DialogResult!=DialogResult.OK,"UI blocks mismatched confirmation");
                                password.Text=confirm.Text="";form.Controls.Find("error",true)[0].Text="";
                                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(root,"first-run.png"));}
                                password.Text=confirm.Text="FixturePass!";save.PerformClick();Check(form.DialogResult==DialogResult.OK,"valid user password enters the application with optional new email");
                            }
                            if(form is MainForm&& (bool)typeof(MainForm).GetField("ready",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form)) {ready=true;timer.Stop();form.Close();}
                        }
                    } catch(Exception ex){failure=ex;timer.Stop();foreach(Form form in new System.Collections.Generic.List<Form>(GetForms()))form.Close();}
                };
                EventHandler idle=null;idle=(s,e)=>{Application.Idle-=idle;timer.Start();};Application.Idle+=idle;typeof(MainForm).Assembly.GetType("ArenaCompanion.Program").GetMethod("Main",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{new[]{"--root",root,"--instance","first-run"}});
            }
            if(failure!=null)throw failure;
            Check(ready&&dialogs==(existing?0:1),existing?"reopening the same instance skips setup and initializes WebView":"actual program startup shows setup before initializing WebView");
            Check(new AccountStore(Path.Combine(root,"first-run")).Load().Password=="FixturePass!","user-entered credential can be read after startup");
            foreach(string invalid in new[]{"", "Short!", "lowercase!", "UppercaseOnly"})Check(AccountSetup.Validate("",invalid,invalid)!=null,"password requirements reject invalid input");
            Check(AccountSetup.Validate("invalid-address","FixturePass!","FixturePass!")!=null,"invalid supplied email is rejected");
            Check(AccountSetup.Validate("local@example.invalid","FixturePass!","FixturePass!")==null,"existing email with a valid password is accepted");
            Console.WriteLine("ALL "+passed+" PASSED");return 0;
        }catch(Exception ex){Console.WriteLine(ex);return 1;}
    }
    static System.Collections.Generic.IEnumerable<Form> GetForms(){foreach(Form form in Application.OpenForms)yield return form;}
}
