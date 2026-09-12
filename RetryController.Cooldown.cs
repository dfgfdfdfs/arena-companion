using System;
namespace ArenaCompanion {
    public sealed partial class RetryController {
        int seenRateLimit;
        bool retryFailedAttempt;
        bool rateLimitResubmitting;
        string cooldownUrl;
        const int RateLimitRetrySeconds=5;
        DateTime nextCooldownRetry;
        public DateTime? RetryAt {get;private set;}
        public int CooldownSeconds {get{return RetryAt.HasValue?(int)Math.Min(Int32.MaxValue,Math.Max(0,Math.Ceiling((RetryAt.Value-clock()).TotalSeconds))):0;}}
        bool HandleCooldown(PageState v) {
            if(v.rateLimitId>seenRateLimit) {
                seenRateLimit=v.rateLimitId;
                bool pending=Attempt>0&&(Phase=="confirm"||Phase=="observe");
                if(!pending&&Phase!="cooldown"&&v.rateLimitRetryAt<=clock())return false;
                RetryAt=v.rateLimitRetryAt==DateTime.MinValue?(DateTime?)null:v.rateLimitRetryAt;
                retryFailedAttempt|=pending;cooldownUrl=v.url;nextCooldownRetry=clock().AddSeconds(RateLimitRetrySeconds);
                termsSubmissionPending=false;rateLimitResubmitting=false;Move("cooldown");
            }
            if(Phase!="cooldown")return false;
            if(v.url!=cooldownUrl){Pause("冷却期间页面已切换，已停止自动续交；当前草稿保留");return true;}
            if(!String.IsNullOrEmpty(v.blocker)&&!v.blocker.Contains("限流")){Pause(v.blocker+"；冷却时间保留，处理后点继续");return true;}
            if(clock()<nextCooldownRetry) {
                var time=TimeSpan.FromSeconds(CooldownSeconds);
                string serverTime=RetryAt.HasValue?"网站提示剩余 "+((int)time.TotalHours>0?((int)time.TotalHours)+"小时 ":"")+time.Minutes.ToString("00")+"分 "+time.Seconds.ToString("00")+"秒":"网站未提供恢复时间";
                Say("网站限流（HTTP 429）\n"+serverTime+"；"+(int)Math.Ceiling((nextCooldownRetry-clock()).TotalSeconds)+" 秒后重试，成功后自动继续");return true;
            }
            if(!v.main||v.termsPending){Pause("限流重试前网页尚未就绪；处理后点击继续");return true;}
            if(v.conversation||v.generating) {
                if(!v.promptConfirmed){Pause("限流重试前出现其他对话，已停止自动续交");return true;}
                RetryAt=null;sawGeneration=v.generating;expectedUrl=v.url;Move("observe");Say("回答已经开始，继续观察，不重复发送");return true;
            }
            if(!v.editor||(!String.IsNullOrEmpty(v.draft)&&v.draft!=Prompt)){Pause("限流重试前草稿已改变；已保留草稿，未自动提交");return true;}
            if(!v.sendReady&&v.draft==Prompt){Pause("限流重试前发送按钮未就绪；稍后点击继续");return true;}
            if(retryFailedAttempt&&Attempt>0)Attempt--;
            retryFailedAttempt=false;RetryAt=null;sawGeneration=false;signature=null;expectedUrl=v.url;
            rateLimitResubmitting=true;
            Move(v.draft==Prompt?(preparation==null||!preparation.Required?"send":"prepare"):"fill");Say("已等待 5 秒，正在重试当前问题；收到新限流回复后更新倒计时");return false;
        }
    }
}
