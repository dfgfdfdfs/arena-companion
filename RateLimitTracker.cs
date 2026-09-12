using System;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
namespace ArenaCompanion {
    public sealed class RateLimitRecord {
        public int Sequence {get;set;}
        public string RetryUntilUtc {get;set;}
        public string RequestUrl {get;set;}
    }
    public sealed class RateLimitTracker {
        readonly string path;
        RateLimitRecord record=new RateLimitRecord();
        public RateLimitTracker(string directory) {
            path=Path.Combine(directory,"rate-limit.json");
            try{if(File.Exists(path)){var saved=new JavaScriptSerializer().Deserialize<RateLimitRecord>(File.ReadAllText(path));if(saved!=null&&Matches(saved.RequestUrl))record=saved;}}catch(IOException){}catch(ArgumentException){}
        }
        public static bool Matches(string url){Uri u;return Uri.TryCreate(url,UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Host=="arena.ai"&&u.AbsolutePath=="/nextjs-api/stream/create-chat";}
        public static DateTime? Parse(string header,string serverDate,DateTime now) {
            header=(header??"").Trim();
            long seconds;
            if(Int64.TryParse(header,NumberStyles.Integer,CultureInfo.InvariantCulture,out seconds)) {
                if(seconds<0||seconds>Int32.MaxValue)return null;return now.AddSeconds(Math.Max(1,seconds));
            }
            DateTimeOffset target,server;
            if(!DateTimeOffset.TryParse(header,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out target))return null;
            if(DateTimeOffset.TryParse(serverDate,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out server))return now.AddSeconds(Math.Max(1,(target-server).TotalSeconds));
            return target.UtcDateTime>now?target.UtcDateTime:now.AddSeconds(1);
        }
        public void Observe(string url,int status,string header,string serverDate,DateTime now) {
            if(status!=429||!Matches(url))return;
            DateTime? until=Parse(header,serverDate,now);record=new RateLimitRecord {Sequence=record.Sequence+1,RequestUrl=url,RetryUntilUtc=until.HasValue?until.Value.ToString("o"):null};
            string temporary=path+".tmp";File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(record));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
        }
        public void Apply(PageState state) {
            state.rateLimitId=record.Sequence;DateTime parsed;
            state.rateLimitRetryAt=DateTime.TryParse(record.RetryUntilUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out parsed)?parsed.ToUniversalTime():DateTime.MinValue;
        }
    }
}
