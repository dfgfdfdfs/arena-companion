using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ArenaCompanion {
    public interface IAuthPages {
        Task<Dictionary<string, object>> Read(string target);
        Task Act(string target, string action, AccountData data);
        void Navigate(string target, string url);
    }
    public sealed class AuthFlow {
        readonly IAuthPages pages;
        readonly AccountStore store;
        readonly Func<DateTime> clock;
        readonly bool existingOnly;
        AccountData account;
        bool busy;
        int epoch;
        DateTime deadline, changedAt;
        string previousStage = "";
        public bool Running { get; private set; }
        public string Phase { get; private set; }
        public string Message { get; private set; }
        public string Email { get { return account == null ? "" : account.Email; } }
        public event Action Changed;
        public AuthFlow(IAuthPages pages, AccountStore store, Func<DateTime> clock = null,bool existingOnly=false) {
            this.pages=pages; this.store=store; this.clock=clock ?? (()=>DateTime.UtcNow);
            this.existingOnly=existingOnly;
            Phase="idle"; Message="自动登录尚未开始";
        }
        public static string Value(Dictionary<string,object> data,string key) {
            object value; return data.TryGetValue(key,out value) ? Convert.ToString(value) : "";
        }
        void Say(string phase,string message) {
            Phase=phase; Message=message; changedAt=clock(); if(Changed!=null)Changed();
        }
        public void Start() {
            if(busy || Running) throw new InvalidOperationException("登录流程正在运行");
            account=store.Load();
            if(String.IsNullOrEmpty(account.Email)&&!account.MailboxChangeConfirmed)account.MailboxRefreshRequested=false;
            if(String.IsNullOrEmpty(account.Password)) throw new InvalidOperationException("请先设置登录密码");
            if(existingOnly&&String.IsNullOrWhiteSpace(account.Email))throw new InvalidOperationException("保存项中没有账号");
            epoch++; Running=true; deadline=clock().AddMinutes(8); previousStage="";
            pages.Navigate("arena","https://arena.ai/agent"); Say("inspect","正在检查 Arena 登录状态");
        }
        public void Stop(string reason) { epoch++; Running=false; Say("paused",reason); }
        async Task Action(string target,string action) { await pages.Act(target,action,account); }
        public async Task Tick() {
            if(!Running || busy)return;
            busy=true; int generation=epoch;
            try {
                if(clock()>deadline) {Stop("登录超时，保留账号；可再次自动登录");return;}
                string target=(Phase=="mailbox"||Phase=="mail"||Phase=="verify"||Phase=="passwordFilled"||Phase=="passwordSubmitted")?"auth":"arena";
                var state=await pages.Read(target);
                if(!Running||generation!=epoch)return;
                string stage=Value(state,"stage");
                if(Value(state,"blocker")!="") {Stop(Value(state,"blocker"));return;}
                if(stage=="invalid") {Stop("邮箱确认链接无效，请在 Arena 重新发送确认邮件后重试");return;}
                if((clock()-changedAt).TotalSeconds>60 && stage==previousStage && Phase!="mail") {Stop("页面未进入下一步，保留当前页面，请检查后重试");return;}
                previousStage=stage;
                await Advance(target,stage,state);
            } catch(Exception ex) {Stop("登录步骤未确认（"+Phase+"）："+(ex is InvalidOperationException?ex.Message:ex.GetType().Name)+"；已停止重复提交");}
            finally {busy=false;}
        }
        async Task Advance(string target,string stage,Dictionary<string,object> state) {
            if(stage=="authenticated") {
                string actual=Value(state,"account");
                if(!String.IsNullOrEmpty(account.Email)&&!actual.Equals(account.Email,StringComparison.OrdinalIgnoreCase)) {Stop("当前页面登录了其他账号，请先确认账号");return;}
                if(target=="auth") { pages.Navigate("arena","https://arena.ai/agent"); Say("return","密码已提交，正在确认 Arena 主页面登录状态");return; }
                account.Email=actual; account.Verified=true; store.Save(account); Running=false;
                pages.Navigate("auth","about:blank"); Say("complete","已确认登录："+actual);return;
            }
            if(stage=="expand") {await Action(target,"expand");return;}
            if(Phase=="mailbox") {await AcquireMailbox(stage,state);return;}
            if(Phase=="mail") {await ReadMail(stage,state);return;}
            if(Phase=="verify" && stage=="setPassword") {await Action("auth","password");Say("passwordFilled","已填写密码，等待提交");return;}
            if(Phase=="passwordFilled") {Say("passwordSubmitted","已提交设置密码，等待确认");await Action("auth","submitPassword");return;}
            if(Phase=="passwordSubmitted"||Phase=="return")return;
            await ArenaStep(stage);
        }
        async Task AcquireMailbox(string stage,Dictionary<string,object> state) {
            if(stage!="mail")return;
            string email=Value(state,"mailbox");
            if(!System.Text.RegularExpressions.Regex.IsMatch(email,@"^[^\s@]+@[^\s@]+\.[^\s@]+$"))return;
            if(!account.MailboxRefreshRequested) {
                if(Value(state,"canChangeMailbox")!="True")return;
                account.MailboxBeforeRefresh=email;account.MailboxRefreshRequested=true;store.Save(account);
                Say("mailbox","已请求更改邮箱地址，正在等待新地址确认");await Action("auth","refreshMail");return;
            }
            if(!account.MailboxChangeConfirmed&&Value(state,"canConfirmMailboxChange")=="True") {
                account.MailboxChangeConfirmed=true;store.Save(account);Say("mailbox","已确认更换邮箱，等待地址变化");await Action("auth","confirmRefreshMail");return;
            }
            if(String.Equals(email,account.MailboxBeforeRefresh,StringComparison.OrdinalIgnoreCase))return;
            if(account.ExcludedEmails!=null&&Array.Exists(account.ExcludedEmails,e=>String.Equals(e,email,StringComparison.OrdinalIgnoreCase))) {Stop("邮箱地址已被其他实例使用，未提交注册");return;}
            account.Email=email;account.Verified=false;store.Save(account);
            Say("inspect","已确认邮箱地址更换，正在打开注册");
        }
        async Task ReadMail(string stage,Dictionary<string,object> state) {
            if(stage!="mail")return;
            if(!String.Equals(Value(state,"mailbox"),account.Email,StringComparison.OrdinalIgnoreCase)) {Stop("临时邮箱已变化，保留原账号，请检查邮箱");return;}
            string url=Value(state,"verifyUrl");
            if(url!="") {
                Uri uri;
                if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="arena.ai"||uri.AbsolutePath!="/nextjs-api/callback/email") {Stop("确认邮件的链接地址不匹配");return;}
                Say("verify","已收到确认邮件，正在打开 Arena 设置密码页面");pages.Navigate("auth",url);return;
            }
            if(Value(state,"mailAvailable")=="True")await Action("auth","openMail");
        }
        async Task ArenaStep(string stage) {
            if(existingOnly&&(stage=="create"||stage=="verification")) {Stop("网站要求注册或额外验证，请在当前页面检查已保存账号后重试登录");return;}
            if(stage=="loggedOut") {await Action("arena","openLogin");return;}
            if(stage=="email") {
                if(String.IsNullOrEmpty(account.Email)) {pages.Navigate("auth","https://10minutemail.one/zh");Say("mailbox","正在获取临时邮箱");return;}
                if(Phase=="emailFilled") {Say("emailSubmitted","已提交邮箱，等待网站判断登录或注册");await Action("arena","submitEmail");}
                else if(Phase!="emailSubmitted") {await Action("arena","email");Say("emailFilled","已填写邮箱");}
            } else if(stage=="create") {
                if(Phase=="nameFilled") {Say("createSubmitted","已提交注册，等待确认邮件");await Action("arena","create");}
                else if(Phase!="createSubmitted") {await Action("arena","name");Say("nameFilled","已填写姓名");}
            } else if(stage=="verification") {
                pages.Navigate("auth","https://10minutemail.one/zh");Say("mail","正在等待 Arena 确认邮件");
            } else if(stage=="loginPassword") {
                if(Phase=="loginFilled") {Say("loginSubmitted","已提交登录密码，等待确认");await Action("arena","submitPassword");}
                else if(Phase!="loginSubmitted") {await Action("arena","password");Say("loginFilled","已填写已保存账号的密码");}
            }
        }
    }
}
