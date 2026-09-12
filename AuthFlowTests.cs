using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ArenaCompanion {
    sealed class FakeAuthPages : IAuthPages {
        public Dictionary<string,object> Arena=new Dictionary<string,object>{{"stage","loggedOut"}};
        public Dictionary<string,object> Auth=new Dictionary<string,object>{{"stage","mail"},{"mailbox","old@example.com"},{"canChangeMailbox",true}};
        public List<string> Actions=new List<string>();
        public bool Ambiguous;
        public bool KeepMailbox;
        public Task<Dictionary<string,object>> Read(string target) {return Task.FromResult(target=="arena"?Arena:Auth);}
        public void Navigate(string target,string url) {Actions.Add("navigate:"+target+":"+new Uri(url).AbsolutePath);}
        public Task Act(string target,string action,AccountData data) {
            Actions.Add(action);
            if(action=="refreshMail")Auth["canConfirmMailboxChange"]=true;
            if(action=="confirmRefreshMail"){Auth["canConfirmMailboxChange"]=false;if(!KeepMailbox)Auth["mailbox"]="test@example.com";}
            if(action=="openLogin")Arena["stage"]="email";
            if(action=="submitEmail")Arena["stage"]="create";
            if(action=="create") {Arena["stage"]="verification";if(Ambiguous)throw new IOException("ambiguous");}
            return Task.FromResult(0);
        }
    }
    static class AuthFlowTests {
        static int count;
        static void Check(bool ok,string message) {if(!ok)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
        static void Step(AuthFlow flow,int n) {for(int i=0;i<n;i++)flow.Tick().GetAwaiter().GetResult();}
        static AccountStore NewStore(string root) {
            string folder=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            var store=new AccountStore(folder);store.Save(new AccountData {Name="Kai",Password="Test-password!"});return store;
        }
        static void Main() {
            string root=Path.Combine(Path.GetTempPath(),"ArenaAuthTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var store=NewStore(root);var pages=new FakeAuthPages();var flow=new AuthFlow(pages,store);
            flow.Start();Step(flow,10);
            Check(flow.Phase=="mail"&&store.Load().Email=="test@example.com","new registration stores mailbox and waits for email");
            Check(pages.Actions.FindAll(x=>x=="create").Count==1,"registration submitted exactly once");
            Step(flow,5);Check(pages.Actions.FindAll(x=>x=="create").Count==1,"waiting does not resubmit registration");
            pages.Auth["verifyUrl"]="https://arena.ai/nextjs-api/callback/email?token=test";Step(flow,1);
            Check(flow.Phase=="verify","confirmed mail follows allowlisted callback");
            pages.Auth["stage"]="setPassword";Step(flow,2);
            Check(flow.Phase=="passwordSubmitted"&&pages.Actions.Contains("submitPassword"),"password fill and submission use separate steps");
            Step(flow,3);Check(pages.Actions.FindAll(x=>x=="submitPassword").Count==1,"password is not submitted again while result pending");
            pages.Auth["stage"]="authenticated";pages.Auth["account"]="test@example.com";Step(flow,1);
            Check(flow.Running&&flow.Phase=="return"&&!store.Load().Verified,"password page alone is not final login proof");
            pages.Arena["stage"]="authenticated";pages.Arena["account"]="test@example.com";Step(flow,1);
            Check(flow.Phase=="complete"&&!flow.Running&&store.Load().Verified,"main page matching account confirms success");
            Check(!File.ReadAllText(Directory.GetFiles(root,"account.dpapi",SearchOption.AllDirectories)[0]).Contains("Test-password"),"password storage is encrypted");
            pages=new FakeAuthPages();pages.Arena["blocker"]="需要亲自完成人机验证";flow=new AuthFlow(pages,NewStore(root));flow.Start();Step(flow,1);
            Check(!flow.Running&&pages.Actions.Count==1,"challenge stops before form actions");
            pages=new FakeAuthPages();pages.Ambiguous=true;flow=new AuthFlow(pages,NewStore(root));flow.Start();Step(flow,12);
            Check(!flow.Running&&pages.Actions.FindAll(x=>x=="create").Count==1,"ambiguous registration stops without duplicate submission");
            pages=new FakeAuthPages();flow=new AuthFlow(pages,NewStore(root));flow.Start();Step(flow,10);
            pages.Auth["mailbox"]="wrong@example.com";Step(flow,1);
            Check(!flow.Running&&!pages.Actions.Contains("openMail"),"changed mailbox stops before using wrong recipient");
            pages=new FakeAuthPages();flow=new AuthFlow(pages,NewStore(root));flow.Start();Step(flow,10);
            pages.Auth["verifyUrl"]="https://evil.example/callback";Step(flow,1);
            Check(!flow.Running&&flow.Phase=="paused","foreign confirmation URL rejected");
            var now=DateTime.UtcNow;pages=new FakeAuthPages();flow=new AuthFlow(pages,NewStore(root),()=>now);flow.Start();now=now.AddMinutes(9);Step(flow,1);
            Check(!flow.Running,"overall timeout stops registration");
            store=NewStore(root);var existing=store.Load();existing.Email="saved@example.com";store.Save(existing);
            pages=new FakeAuthPages();flow=new AuthFlow(pages,store,null,true);flow.Start();pages.Arena["stage"]="create";Step(flow,1);
            Check(!flow.Running&&!pages.Actions.Contains("create")&&!pages.Actions.Contains("name"),"saved entry never registers a replacement account");
            pages=new FakeAuthPages();flow=new AuthFlow(pages,store,null,true);flow.Start();pages.Arena["stage"]="authenticated";pages.Arena["account"]="wrong@example.com";Step(flow,1);
            Check(!flow.Running&&flow.Phase!="complete","saved entry rejects a different authenticated account");
            pages=new FakeAuthPages();pages.KeepMailbox=true;store=NewStore(root);flow=new AuthFlow(pages,store);flow.Start();Step(flow,12);
            Check(pages.Actions.FindAll(x=>x=="refreshMail").Count==1&&!pages.Actions.Contains("submitEmail")&&String.IsNullOrEmpty(store.Load().Email),"unchanged mailbox is never submitted and refresh is not repeated blindly");
            Check(pages.Actions.FindAll(x=>x=="confirmRefreshMail").Count==1,"mailbox change confirmation is submitted exactly once");
            pages.Auth["mailbox"]="test@example.com";Step(flow,1);
            Check(store.Load().Email=="test@example.com","registration waits for a genuinely changed address");
            pages=new FakeAuthPages();store=NewStore(root);existing=store.Load();existing.ExcludedEmails=new[]{"test@example.com"};store.Save(existing);flow=new AuthFlow(pages,store);flow.Start();Step(flow,8);
            Check(!flow.Running&&!pages.Actions.Contains("submitEmail"),"a mailbox already used by another instance is rejected");
            Console.WriteLine("ALL "+count+" PASSED");
            Directory.Delete(root,true);
        }
    }
}
