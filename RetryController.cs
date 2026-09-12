using System;
using System.Threading.Tasks;

namespace ArenaCompanion {
    public interface IRequestPreparation {
        bool Required {get;}
        void BeginRound();
        Task<bool> Prepare();
        Task<bool> Check();
    }
    public sealed class PageState {
        public string url { get; set; }
        public bool main { get; set; }
        public bool conversation { get; set; }
        public bool promptConfirmed { get; set; }
        public bool thinking { get; set; }
        public bool generating { get; set; }
        public bool failed { get; set; }
        public bool response { get; set; }
        public string responseSignature { get; set; }
        public string draft { get; set; }
        public bool editor { get; set; }
        public string blocker { get; set; }
        public bool termsPending {get;set;}
        public int rateLimitId {get;set;}
        public DateTime rateLimitRetryAt {get;set;}
        public bool sendReady { get; set; }
        public int newLinks { get; set; }
        public bool canExpand { get; set; }
        public string[] attachmentNames {get;set;}
        public string[] conversationAttachments {get;set;}
    }
    public interface IArenaPage {
        Task<PageState> Read(string prompt);
        Task Act(string name, string prompt);
        bool IsAllowed(string url);
    }
    public sealed partial class RetryController {
        readonly IArenaPage page;
        readonly Func<DateTime> clock;
        readonly IRequestPreparation preparation;
        public bool Running { get; private set; }
        public bool Finished { get; private set; }
        public bool CandidateReady {get;private set;}
        public string CandidateUrl {get;private set;}
        public int Collected {get;private set;}
        public int Attempt { get; private set; }
        public int Limit { get; private set; }
        public int Rejected { get; private set; }
        public string Phase { get; private set; }
        public string Message { get; private set; }
        public string Prompt { get; private set; }
        public bool WaitingForVerification { get; private set; }
        public event Action Changed;
        bool busy, sawGeneration, adopt;
        bool termsAttempted, termsWaiting;
        bool termsSubmissionPending;
        DateTime termsSince,termsDraftSince;
        int epoch;
        int readFailures;
        DateTime nextRead,readFailureSince;
        string expectedUrl, signature;
        DateTime until, phaseSince, stableSince;
        public RetryController(IArenaPage page, Func<DateTime> clock = null,IRequestPreparation preparation = null) {
            this.page = page; this.clock = clock ?? (() => DateTime.UtcNow);
            this.preparation=preparation;
            Phase = "idle"; Limit = 0; Message = "先打开 Arena 并登录，或点击离线演示";
        }
        void Say(string text) { Message = text; if (Changed != null) Changed(); }
        void Move(string phase) { Phase = phase; phaseSince = clock(); until = phaseSince.AddMinutes(2); }
        public void Start(string prompt, int limit, bool useCurrent) {
            if (busy) throw new InvalidOperationException("上一操作仍在结束，请稍后重试");
            if (Running) return;
            if (String.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("请先填写提示词");
            if (limit < 0) throw new ArgumentException("次数不能为负数；0 表示不限次数");
            epoch++; Prompt = prompt.Trim(); Limit = limit; Attempt = 0; Rejected = 0;
            adopt = useCurrent; expectedUrl = null; signature = null; sawGeneration = false;
            termsAttempted=termsWaiting=false;
            termsSubmissionPending=false;termsDraftSince=DateTime.MinValue;
            seenRateLimit=0;RetryAt=null;
            readFailures=0;nextRead=DateTime.MinValue;
            WaitingForVerification=false;
            if(preparation!=null)preparation.BeginRound();
            Running = true; Finished = false; CandidateReady=false;CandidateUrl=null;Collected=0; until = clock().AddMinutes(2); Move("inspect");
            Say("正在检查页面，请稍候…");
        }
        public void Pause(string reason) {
            epoch++; Running = false; WaitingForVerification=false; Say(reason);
        }
        void PauseForVerification(string reason) {
            epoch++; Running=false; WaitingForVerification=true; nextRead=DateTime.MinValue;
            Say(reason+"，请在右侧完成；通过后将自动继续当前进度");
        }
        public void CancelForRecovery() {
            CandidateReady=false;CandidateUrl=null;Finished=true;
            Pause("正在恢复会话状态，本轮不重复提交；恢复后可重新开始筛选");
        }
        public void Resume() {
            if (busy) throw new InvalidOperationException("上一操作仍在结束，请稍后重试");
            if (Running || Finished || Phase == "idle") return;
            epoch++; Running = true; WaitingForVerification=false; until = clock().AddMinutes(2); phaseSince = clock();
            nextRead=DateTime.MinValue;
            Say("继续当前进度，不重复发送已提交的问题");
        }
        void Done(string message) { Finished = true; Running = false; Say(message); }
        bool LimitReached {get {return Limit > 0 && Attempt >= Limit;}}
        public void AfterCollection(bool keepGoing) {
            if(busy||!CandidateReady)throw new InvalidOperationException("当前不是等待收集的候选");
            CandidateReady=false;CandidateUrl=null;Collected++;
            if(!keepGoing||LimitReached){Done("候选已收集"+(LimitReached?"，已达到次数上限":"，可打开候选图集比较"));return;}
            epoch++;Running=true;Finished=false;until=clock().AddMinutes(2);Move("new");
            Say("候选已收集，继续筛选下一条");
        }
        bool Late(int seconds) { return (clock() - phaseSince).TotalSeconds >= seconds; }
        public async Task Tick() {
            if ((!Running&&!WaitingForVerification) || busy || clock()<nextRead) return;
            busy = true; int currentEpoch = epoch;
            try {
                PageState v;DateTime readingSince=clock();
                try {v=await page.Read(Prompt);}
                catch(TimeoutException) {
                    if((!Running&&!WaitingForVerification)||epoch!=currentEpoch)return;
                    if(readFailures==0)readFailureSince=readingSince;
                    readFailures++;signature=null;
                    int delay=Math.Min(20,3*(1<<Math.Min(3,readFailures-1)));nextRead=clock().AddSeconds(delay);
                    Say("暂时无法读取网页，"+delay+" 秒后自动检查；当前任务保留，不重复提交");return;
                }
                if ((!Running&&!WaitingForVerification) || epoch != currentEpoch) return;
                if(WaitingForVerification) {
                    if(!String.IsNullOrEmpty(v.blocker))return;
                    WaitingForVerification=false;Running=true;until=clock().AddMinutes(2);phaseSince=clock();nextRead=DateTime.MinValue;
                    Say("人机验证已完成，自动继续当前进度，不重复提交");return;
                }
                if(readFailures>0){var gap=clock()-readFailureSince;until=until.Add(gap);phaseSince=phaseSince.Add(gap);readFailures=0;nextRead=DateTime.MinValue;Say("页面读取已恢复，继续当前任务");}
                if (!page.IsAllowed(v.url)) { Pause("请在软件内打开 Arena Agent 页面"); return; }
                if(HandleCooldown(v))return;
                if (Phase != "observe" && clock() >= until) { Pause("当前页面操作超过 2 分钟，已暂停；点击“继续”保留进度"); return; }
                if(v.termsPending) {
                    if(!termsAttempted) {
                        termsAttempted=termsWaiting=true;termsSince=clock();
                        termsSubmissionPending=Attempt>0&&(Phase=="confirm"||Phase=="observe");
                        Say("正在自动同意网站首次使用条款，随后继续当前进度");
                        await page.Act("terms",Prompt);
                    } else if((clock()-termsSince).TotalSeconds>=20)Pause("网站条款确认尚未生效，已暂停，避免重复点击");
                    return;
                }
                if (!String.IsNullOrEmpty(v.blocker)) {
                    if(v.blocker.IndexOf("人机",StringComparison.OrdinalIgnoreCase)>=0||v.blocker.IndexOf("Security Verification",StringComparison.OrdinalIgnoreCase)>=0||v.blocker.IndexOf("Verify you are human",StringComparison.OrdinalIgnoreCase)>=0)PauseForVerification(v.blocker);
                    else Pause(v.blocker + "，处理后点击“继续”");
                    return;
                }
                if(termsWaiting) {termsWaiting=false;phaseSince=clock();until=clock().AddMinutes(2);Say("网站条款已确认，自动继续当前进度");}
                if(ContinueAfterTerms(v))return;
                if (!v.main) { if (Late(20)) Pause("页面尚未加载完成，请稍后继续"); return; }
                bool newRoute = Phase == "confirm" || Phase == "waitNew" || (expectedUrl != null && expectedUrl.EndsWith("/agent") && Phase == "observe" && v.promptConfirmed);
                if (expectedUrl != null && expectedUrl != v.url && !newRoute) { Pause("你已切换到其他页面。请点“重新开始”建立新一轮"); return; }
                if (Phase == "inspect") {
                    if (v.canExpand) { await page.Act("expand", Prompt); return; }
                    expectedUrl = v.url;
                    if (adopt && v.conversation) {
                        if(preparation!=null&&preparation.Required) {Pause("已绑定附件，请取消接着当前对话，从新对话开始");return;}
                        if (!v.promptConfirmed) { Pause("当前对话与提示词不同。取消“接着当前对话”后重新开始"); return; }
                        Attempt = 1; sawGeneration = v.generating; Move("observe");
                    } else if (v.conversation || v.generating) Move("reject");
                    else Move("fill");
                    Say("页面已连接，准备筛选"); return;
                }
                if (Phase == "observe" || Phase == "confirm") {
                    if (!v.promptConfirmed) { if (Late(20)) Pause("发送结果尚未确认，已暂停，不会自动重发"); return; }
                    expectedUrl = v.url;
                    if (Phase == "confirm") { Move("observe"); Say("第 " + Attempt + " 次：等待回答，正在检查 Thinking…"); }
                    if (v.thinking) { Rejected++; Move("reject"); Say("第 " + Attempt + " 次出现 Thinking，准备换下一次"); return; }
                    if (v.failed) { Pause("网页回答已停止或出错，不能作为候选；可重新开始"); return; }
                    sawGeneration |= v.generating;
                    if (sawGeneration && !v.generating && v.response) {
                        if (signature != v.responseSignature) { signature = v.responseSignature; stableSince = clock(); }
                        else if ((clock() - stableSince).TotalSeconds >= 10) {CandidateReady=true;CandidateUrl=v.url;Done("找到候选：回答完成，未观察到 Thinking，准备收集图像");}
                    } else signature = null;
                    return;
                }
                if (Phase == "reject") {
                    if (v.generating) { Move("waitStop"); await page.Act("stop", Prompt); return; }
                    if (LimitReached) { Done("已达到 " + Limit + " 次上限，"+(Collected>0?"本轮已收集 "+Collected+" 条候选":"未找到候选")); return; }
                    Move("new"); return;
                }
                if (Phase == "waitStop") {
                    if (!v.generating) Move("reject");
                    else if (Late(20)) Pause("停止生成尚未确认，请检查右侧页面后继续");
                    return;
                }
                if (Phase == "new") {
                    if (v.canExpand) { await page.Act("expand", Prompt); return; }
                    Move("waitNew"); expectedUrl = null;
                    if(preparation!=null)preparation.BeginRound();
                    await page.Act("new", Prompt); return;
                }
                if (Phase == "waitNew") {
                    if (!v.conversation && !v.generating && v.editor) { expectedUrl = v.url; Move("fill"); }
                    else if (Late(20)) Pause("未能打开新对话，请检查页面后继续");
                    return;
                }
                if (Phase == "fill") {
                    if (!v.editor || v.conversation || v.generating) { Pause("当前不是空白新对话，请先打开新对话再继续"); return; }
                    if (!String.IsNullOrEmpty(v.draft) && v.draft != Prompt) { Pause("右侧有不同的草稿，已保留；请自行处理后继续"); return; }
                    await page.Act("fill", Prompt);
                    if (Running && epoch == currentEpoch) {
                        Move(preparation==null?"send":"prepare");
                        if(preparation!=null&&preparation.Required)Say("正在检查并上传已绑定附件，确认后自动发送");
                    }
                    return;
                }
                if(Phase=="prepare") {
                    if(await preparation.Prepare()) {if(Running&&epoch==currentEpoch) {Move("send");if(preparation.Required)Say("附件已确认，准备提交第 "+(Attempt+1)+" 次");}}
                    else if(Late(60))Pause("附件上传尚未确认，已暂停，不会重复上传或无附件发送");
                    return;
                }
                if (Phase == "send") {
                    if(preparation!=null&&!await preparation.Check()) {Pause("发送前附件发生改变，请检查附件后重新开始");return;}
                    if(!Running||epoch!=currentEpoch)return;
                    if (v.draft != Prompt || v.conversation || v.generating) { Pause("发送前页面或草稿改变，已暂停"); return; }
                    if (!v.sendReady) { if (Late(20)) Pause("发送按钮暂不可用，请检查页面"); return; }
                    if (LimitReached) { Done("已达到次数上限"); return; }
                    Attempt++; sawGeneration = false; signature = null; Move("confirm");
                    Say("已提交第 " + Attempt + " 次，等待网页确认…");
                    await page.Act("send", Prompt); return;
                }
            } catch (Exception ex) { Pause("操作已暂停：" + ex.Message); }
            finally { busy = false; }
        }
        bool ContinueAfterTerms(PageState v) {
            if(!termsSubmissionPending)return false;
            if(!v.url.EndsWith("/agent")||v.thinking||v.response) {termsSubmissionPending=false;return false;}
            bool restored=v.main&&!v.conversation&&!v.generating&&v.editor&&v.draft==Prompt&&v.sendReady;
            if(!restored){termsDraftSince=DateTime.MinValue;return false;}
            if(termsDraftSince==DateTime.MinValue)termsDraftSince=clock();
            if((clock()-termsDraftSince).TotalSeconds<5)return true;
            // The known consent gate returned the unsent request to the empty composer.
            // Reuse this attempt once, rechecking its existing attachments before sending.
            termsSubmissionPending=false;Attempt--;sawGeneration=false;signature=null;expectedUrl=v.url;
            Move(preparation==null?"send":"prepare");
            Say("条款已同意，网站将未提交的问题退回草稿；正在继续提交同一条问题");
            return true;
        }
    }
}
