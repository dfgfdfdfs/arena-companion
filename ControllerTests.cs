using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace ArenaCompanion {
    sealed class FakePreparation : IRequestPreparation {
        public bool Required {get {return true;}}
        public bool Ready;
        public int Rounds,Checks;
        public void BeginRound(){Rounds++;Ready=false;}
        public Task<bool> Prepare(){Checks++;return Task.FromResult(Ready);}
        public Task<bool> Check(){return Task.FromResult(Ready);}
    }
    sealed class FakePage : IArenaPage {
        public PageState V = new PageState {url="https://arena.ai/agent",main=true,editor=true,draft="",sendReady=true,newLinks=1};
        public List<string> Actions = new List<string>();
        public bool AmbiguousSend;
        public bool ReadTimeout,SendTimeout;
        public bool AmbiguousTerms,KeepTerms,RestoreDraftAfterTerms;
        public TaskCompletionSource<PageState> Pending;
        public Task<PageState> Read(string prompt) {if(ReadTimeout)throw new TimeoutException("fixture read timeout");return Pending == null ? Task.FromResult(V) : Pending.Task;}
        public bool IsAllowed(string url) {return url.StartsWith("https://arena.ai/agent");}
        public Task Act(string action,string prompt) {
            if(action=="retrySend")action="send";else if(action=="retryFill")action="fill";
            Actions.Add(action);
            if(action=="terms"){if(!KeepTerms){V.termsPending=false;V.blocker="";}if(AmbiguousTerms)throw new Exception("ambiguous consent");}
            if(action=="terms"&&RestoreDraftAfterTerms){V.url="https://arena.ai/agent";V.conversation=V.generating=V.promptConfirmed=false;V.draft=prompt;}
            if(action=="fill")V.draft=prompt;
            if(action=="stop")V.generating=false;
            if(action=="new"){V.url="https://arena.ai/agent";V.conversation=V.thinking=V.failed=false;V.draft="";}
            if(action=="send"){V.conversation=V.promptConfirmed=V.generating=true;V.url="https://arena.ai/agent/new";if(SendTimeout)throw new TimeoutException("ambiguous send timeout");if(AmbiguousSend)throw new Exception("ambiguous");}
            return Task.FromResult(0);
        }
    }
    static class ControllerTests {
        static int passed;
        static void Check(bool result,string description){if(!result)throw new Exception(description);Console.WriteLine("PASS "+description);passed++;}
        static void Steps(RetryController c,int n){for(int i=0;i<n;i++)c.Tick().GetAwaiter().GetResult();}
        static void Main(){
            var now=DateTime.UtcNow;
            var p=new FakePage();var c=new RetryController(p,()=>now);c.Start("hello",2,true);Steps(c,4);
            Check(c.Attempt==1&&p.Actions.FindAll(a=>a=="send").Count==1,"blank page sends exactly once");
            p.V.thinking=true;Steps(c,8);
            Check(c.Attempt==2&&p.Actions.Contains("stop")&&p.Actions.Contains("new"),"thinking triggers stop, new, refill, send");
            p.V.thinking=true;Steps(c,5);Check(c.Finished&&c.Rejected==2&&c.Attempt==2,"attempt cap excludes both thinking replies");
            p=new FakePage();p.V.blocker="需要人机验证";c=new RetryController(p);c.Start("hello",2,true);Steps(c,1);
            Check(!c.Running&&c.WaitingForVerification&&p.Actions.Count==0,"captcha pauses before writes and remains read-only observable");
            Steps(c,3);Check(p.Actions.Count==0,"captcha polling never writes to the page");
            p.V.blocker="";Steps(c,4);Check(c.Running&&c.Attempt==1,"cleared captcha automatically resumes preserved progress");
            p=new FakePage();p.V.draft="someone else's draft";c=new RetryController(p);c.Start("hello",2,true);Steps(c,2);
            Check(!c.Running&&p.Actions.Count==0&&p.V.draft!="hello","preserves unrelated draft");
            p=new FakePage();p.AmbiguousSend=true;c=new RetryController(p);c.Start("hello",2,true);Steps(c,3);c.Resume();Steps(c,2);
            Check(p.Actions.FindAll(a=>a=="send").Count==1,"ambiguous send is never repeated");
            p=new FakePage();c=new RetryController(p,()=>now);c.Start("hello",2,true);Steps(c,4);p.V.generating=false;p.V.response=true;p.V.responseSignature="reply";Steps(c,1);now=now.AddSeconds(11);Steps(c,1);
            Check(c.Finished&&c.Message.StartsWith("找到候选"),"completed stable response becomes candidate");
            Check(c.CandidateReady&&!c.Running,"candidate waits for collection before new conversation");
            c.AfterCollection(true);Steps(c,5);
            Check(c.Attempt==2&&c.Rejected==0&&p.Actions.FindAll(a=>a=="send").Count==2,"collection continuation keeps total attempts and does not reject accepted candidate");
            p.V.generating=false;p.V.response=true;p.V.responseSignature="second";Steps(c,1);now=now.AddSeconds(11);Steps(c,1);c.AfterCollection(true);Steps(c,5);
            Check(c.Finished&&!c.Running&&c.Attempt==2&&!c.CandidateReady,"successful candidates also obey the original attempt cap");
            p=new FakePage();c=new RetryController(p,()=>now);c.Start("hello",2,true);Steps(c,4);now=now.AddMinutes(3);Steps(c,1);
            Check(c.Running&&!c.Finished&&!c.CandidateReady&&c.Attempt==1,"generation longer than two minutes keeps observing without a false candidate");
            now=now.AddMinutes(8);Steps(c,1);
            Check(c.Running&&p.Actions.FindAll(a=>a=="send").Count==1,"long generation never resubmits the prompt");
            p.V.generating=false;p.V.response=true;p.V.responseSignature="long reply";Steps(c,1);now=now.AddSeconds(11);Steps(c,1);
            Check(c.CandidateReady&&c.Finished,"completion after eleven minutes automatically becomes collectable");
            c.AfterCollection(true);Steps(c,5);
            Check(c.Running&&c.Attempt==2,"collection after a long answer continues with a fresh operation deadline");
            now=now.AddMinutes(3);p.V.thinking=true;Steps(c,5);
            Check(c.Finished&&c.Rejected==1&&p.Actions.Contains("stop"),"late Thinking is still rejected and obeys the attempt cap");
            p=new FakePage();c=new RetryController(p,()=>now);c.Start("hello",2,true);Steps(c,4);c.Pause("user stop");now=now.AddMinutes(3);p.V.generating=false;p.V.response=true;Steps(c,2);
            Check(!c.Running&&!c.CandidateReady,"manual pause still prevents automatic collection");
            p=new FakePage();c=new RetryController(p,()=>now);c.Start("hello",2,true);now=now.AddMinutes(3);Steps(c,1);
            Check(!c.Running&&p.Actions.Count==0,"stalled page operations retain their two minute guard");
            p=new FakePage();c=new RetryController(p);c.Start("hello",2,true);Steps(c,4);p.V.url="https://arena.ai/agent/other";Steps(c,1);
            Check(!c.Running,"external navigation pauses");
            p=new FakePage();p.Pending=new TaskCompletionSource<PageState>();c=new RetryController(p);c.Start("hello",2,true);var pending=c.Tick();c.Pause("user stop");p.Pending.SetResult(p.V);pending.GetAwaiter().GetResult();
            Check(!c.Running&&p.Actions.Count==0,"pause cancels pending read before any action");
            p=new FakePage();var attachments=new FakePreparation();c=new RetryController(p,null,attachments);c.Start("hello",3,false);Steps(c,5);
            Check(c.Attempt==0&&!p.Actions.Contains("send")&&attachments.Checks>0,"pending attachment blocks sending");
            attachments.Ready=true;Steps(c,3);Check(c.Attempt==1,"confirmed attachment allows send");
            p.V.thinking=true;Steps(c,9);Check(attachments.Rounds==2&&c.Attempt==1,"new conversation resets attachment preparation before second send");
            attachments.Ready=true;Steps(c,3);Check(c.Attempt==2,"second conversation waits for its own attachment");
            p=new FakePage();attachments=new FakePreparation();c=new RetryController(p,null,attachments);c.Start("hello",2,false);Steps(c,2);attachments.Ready=true;Steps(c,1);attachments.Ready=false;Steps(c,1);
            Check(!c.Running&&!p.Actions.Contains("send"),"attachment removed before send pauses safely");
            p=new FakePage();p.V.conversation=true;p.V.promptConfirmed=true;attachments=new FakePreparation();c=new RetryController(p,null,attachments);c.Start("hello",2,true);Steps(c,1);
            Check(!c.Running&&c.Attempt==0,"cannot adopt an old conversation without confirming bound attachments");
            UnlimitedRounds();
            AutomaticTerms();
            CooldownWorkflow();
            ReadRecovery();
            Console.WriteLine("ALL "+passed+" PASSED");
        }
        static void ReadRecovery() {
            var now=DateTime.UtcNow;var p=new FakePage();var c=new RetryController(p,()=>now);c.Start("hello",0,false);Steps(c,4);
            p.ReadTimeout=true;Steps(c,1);Check(c.Running&&c.Message.Contains("自动检查")&&p.Actions.FindAll(a=>a=="send").Count==1,"read timeout retains task without another submission");
            Steps(c,10);Check(c.Running&&p.Actions.FindAll(a=>a=="send").Count==1,"retry backoff never performs page writes");
            now=now.AddMinutes(4);Steps(c,1);p.ReadTimeout=false;p.V.generating=false;p.V.response=true;p.V.responseSignature="finished while read channel was unavailable";
            now=now.AddSeconds(21);Steps(c,1);now=now.AddSeconds(11);Steps(c,1);
            Check(c.CandidateReady&&c.Attempt==1,"recovered completed response becomes collectable without resubmitting");
            c.CancelForRecovery();Steps(c,3);Check(!c.CandidateReady&&c.CandidateUrl==null&&c.Finished&&!c.Running&&c.Attempt==1&&p.Actions.FindAll(a=>a=="send").Count==1,"conversation repair invalidates candidate without resending or resetting attempt count");
            p=new FakePage {ReadTimeout=true};c=new RetryController(p,()=>now);c.Start("hello",0,false);Steps(c,1);c.Pause("manual");p.ReadTimeout=false;now=now.AddMinutes(2);Steps(c,5);
            Check(!c.Running&&p.Actions.Count==0,"manual pause prevents recovery from starting a task");
            c.Resume();Steps(c,4);Check(c.Running&&c.Attempt==1,"manual resume checks immediately and continues preserved progress");
            p=new FakePage {SendTimeout=true};c=new RetryController(p,()=>now);c.Start("hello",0,false);Steps(c,4);now=now.AddMinutes(3);Steps(c,5);
            Check(!c.Running&&p.Actions.FindAll(a=>a=="send").Count==1,"send timeout is never treated as a retryable read failure");
        }
        static void AutomaticTerms() {
            var now=DateTime.UtcNow;var p=new FakePage();var c=new RetryController(p,()=>now);
            c.Start("hello",0,false);Steps(c,3);p.V.termsPending=true;p.V.blocker="terms";Steps(c,1);
            Check(c.Running&&c.Phase=="confirm"&&p.Actions.FindAll(a=>a=="terms").Count==1,"first-use terms are accepted automatically while preserving pending submission");
            Steps(c,3);Check(c.Running&&c.Attempt==1&&p.Actions.FindAll(a=>a=="send").Count==1,"consent completion resumes without a second send or manual continue");
            p=new FakePage();p.V.termsPending=true;p.KeepTerms=true;c=new RetryController(p,()=>now);c.Start("hello",0,false);Steps(c,8);
            Check(c.Running&&p.Actions.Count==1,"slow consent response is awaited without repeated clicks");
            now=now.AddSeconds(21);Steps(c,1);Check(!c.Running&&p.Actions.Count==1,"unconfirmed consent pauses after timeout without blind retries");
            p=new FakePage();p.V.termsPending=true;p.AmbiguousTerms=true;c=new RetryController(p,()=>now);c.Start("hello",0,false);Steps(c,1);c.Resume();Steps(c,4);
            Check(c.Running&&p.Actions.FindAll(a=>a=="terms").Count==1&&c.Attempt==1,"ambiguous consent outcome is read back on resume rather than clicked again");
            p=new FakePage();p.V.termsPending=true;c=new RetryController(p,()=>now);c.Start("hello",0,false);c.Pause("manual");Steps(c,2);
            Check(p.Actions.Count==0,"manual pause prevents automatic consent");
            p=new FakePage();p.RestoreDraftAfterTerms=true;c=new RetryController(p,()=>now);c.Start("hello",1,false);Steps(c,3);p.V.termsPending=true;Steps(c,2);
            Check(c.Running&&p.Actions.FindAll(a=>a=="send").Count==1,"consent-restored draft is observed before any retry");
            now=now.AddSeconds(6);Steps(c,3);
            Check(c.Running&&c.Attempt==1&&p.Actions.FindAll(a=>a=="send").Count==2&&p.Actions.FindAll(a=>a=="terms").Count==1,"confirmed unsent draft after consent is submitted once without consuming an extra attempt");
            p.V.conversation=p.V.generating=p.V.promptConfirmed=false;p.V.url="https://arena.ai/agent";Steps(c,1);now=now.AddSeconds(21);Steps(c,2);
            Check(!c.Running&&p.Actions.FindAll(a=>a=="send").Count==2,"consent recovery cannot repeat after another uncertain send");
        }
        static void CooldownWorkflow() {
            var now=DateTime.UtcNow;var p=new FakePage();var c=new RetryController(p,()=>now);c.Start("hello",1,false);Steps(c,3);
            p.V.conversation=p.V.generating=p.V.promptConfirmed=false;p.V.url="https://arena.ai/agent";p.V.draft="hello";p.V.rateLimitId=1;p.V.rateLimitRetryAt=now.AddSeconds(640);Steps(c,1);
            Check(c.Phase=="cooldown"&&!c.Running&&c.Finished&&c.CooldownSeconds==0,"429 stops the current task without a countdown");
            Check(c.Message.Contains("切换到不同 IP 成功后自动重试"),"429 explains the network-switch recovery path");
            int sends=p.Actions.FindAll(a=>a=="send").Count;now=now.AddMinutes(30);Steps(c,20);
            Check(p.Actions.FindAll(a=>a=="send").Count==sends,"stopped 429 never submits again after time passes");
            c.Resume();Steps(c,10);Check(!c.Running&&p.Actions.FindAll(a=>a=="send").Count==sends,"continue cannot restart a finished rate-limited round");
            Check(c.CanRetryAfterNetworkChange&&c.RetryAfterNetworkChange(),"successful network change reopens only the rate-limited task");
            Steps(c,4);Check(c.Running&&c.Attempt==1&&p.Actions.FindAll(a=>a=="send").Count==sends+1,"network recovery retries the failed submission exactly once");
            Check(!c.RetryAfterNetworkChange(),"network recovery cannot duplicate a running retry");
            p=new FakePage();c=new RetryController(p,()=>now);c.Start("hello",0,false);Steps(c,3);p.V.conversation=p.V.generating=p.V.promptConfirmed=false;p.V.url="https://arena.ai/agent";p.V.rateLimitId=1;Steps(c,1);
            Check(!c.Running&&c.Finished&&c.CooldownSeconds==0,"429 without Retry-After also stops without a countdown");
            sends=p.Actions.FindAll(a=>a=="send").Count;now=now.AddMinutes(30);Steps(c,20);
            Check(p.Actions.FindAll(a=>a=="send").Count==sends,"429 without Retry-After never schedules a retry");
            p=new FakePage();c=new RetryController(p);c.Start("hello",0,false);c.Pause("manual");
            Check(!c.CanRetryAfterNetworkChange&&!c.RetryAfterNetworkChange(),"ordinary manual pause is never resumed by an IP switch");
        }
        static void UnlimitedRounds() {
            var now=DateTime.UtcNow;var p=new FakePage();var c=new RetryController(p,()=>now);
            Check(c.Limit==0,"new controller defaults to unlimited attempts");
            c.Start("hello",0,false);Steps(c,4);
            for(int round=0;round<120;round++) {
                int attempt=c.Attempt;p.V.thinking=true;
                for(int step=0;step<15&&c.Attempt==attempt;step++)Steps(c,1);
                if(c.Attempt!=attempt+1||!c.Running)throw new Exception("unlimited rejection stopped at "+attempt);
            }
            Check(c.Attempt==121&&c.Rejected==120,"120 Thinking rejections continue beyond both former 10 and 100 caps");
            int sends=p.Actions.FindAll(a=>a=="send").Count;c.Pause("manual");Steps(c,20);
            Check(!c.Running&&p.Actions.FindAll(a=>a=="send").Count==sends,"unlimited run still obeys manual pause without extra sends");
            c.Resume();Steps(c,1);Check(c.Running&&c.Attempt==121,"resume preserves unlimited progress");
            p=new FakePage();c=new RetryController(p,()=>now);c.Start("hello",0,false);Steps(c,4);
            for(int round=0;round<120;round++) {
                Steps(c,1);p.V.generating=false;p.V.response=true;p.V.responseSignature="candidate "+round;
                Steps(c,1);now=now.AddSeconds(11);Steps(c,1);
                if(!c.CandidateReady)throw new Exception("candidate missing at "+round);
                c.AfterCollection(true);Steps(c,5);
            }
            Check(c.Collected==120&&c.Attempt==121&&c.Running,"120 collected candidates continue without an attempt cap");
            p.V.generating=false;p.V.response=true;p.V.responseSignature="last";Steps(c,1);now=now.AddSeconds(11);Steps(c,1);c.AfterCollection(false);
            Check(c.Finished&&!c.Running&&c.Collected==121,"unlimited mode still respects collect-once preference");
            p=new FakePage();c=new RetryController(p);bool invalid=false;
            try{c.Start("hello",-1,false);}catch(ArgumentException){invalid=true;}
            Check(invalid&&!c.Running,"negative limit rejected before any task starts");
        }
    }
}
