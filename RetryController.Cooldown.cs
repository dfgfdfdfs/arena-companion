using System;
namespace ArenaCompanion {
    public sealed partial class RetryController {
        int seenRateLimit;
        bool rateLimitPendingSubmission;
        public DateTime? RetryAt {get;private set;}
        public int CooldownSeconds {get{return RetryAt.HasValue?(int)Math.Min(Int32.MaxValue,Math.Max(0,Math.Ceiling((RetryAt.Value-clock()).TotalSeconds))):0;}}
        bool HandleCooldown(PageState v) {
            if(v.rateLimitId>seenRateLimit) {
                seenRateLimit=v.rateLimitId;
                bool pending=Attempt>0&&(Phase=="confirm"||Phase=="observe");
                if(!pending&&v.rateLimitRetryAt<=clock())return false;
                RetryAt=null;termsSubmissionPending=false;rateLimitPendingSubmission=pending;Move("cooldown");
                Finished=true;
                Pause("检测到网站限流（HTTP 429），已转到账号页；切换到不同 IP 成功后自动重试");
                return true;
            }
            return Phase=="cooldown";
        }
    }
}
