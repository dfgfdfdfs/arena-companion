using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    public sealed class AuthPages : IAuthPages {
        readonly Func<string,WebView2> select;
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        readonly string bridge;
        public AuthPages(Func<string,WebView2> select) {
            this.select=select;
            bridge=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","AuthBridge.js");
        }
        async Task<Dictionary<string,object>> Execute(string target,string script) {
            WebView2 view=select(target);
            if(view.CoreWebView2==null)throw new InvalidOperationException("浏览器尚未就绪");
            string host=view.Source==null?"":view.Source.Host;
            if(host!="arena.ai"&&host!="10minutemail.one")return new Dictionary<string,object>{{"stage","loading"}};
            Task<string> task=view.CoreWebView2.ExecuteScriptAsync(File.ReadAllText(bridge)+"\n"+script);
            if(await Task.WhenAny(task,Task.Delay(15000))!=task)throw new TimeoutException("页面响应超时");
            var result=json.Deserialize<Dictionary<string,object>>(await task);
            if(result==null)throw new InvalidOperationException("页面操作结果未确认");
            if(result.ContainsKey("bridgeError"))throw new InvalidOperationException(Convert.ToString(result["bridgeError"]));
            return result;
        }
        public Task<Dictionary<string,object>> Read(string target) {return Execute(target,"window.__arenaAuth.read()");}
        public async Task Act(string target,string action,AccountData data) {
            string argument=json.Serialize(new {email=data.Email,password=data.Password,name=data.Name});
            await Execute(target,"(()=>{try{return window.__arenaAuth.act("+json.Serialize(action)+","+argument+");}catch(e){return {bridgeError:e.message};}})()");
        }
        public void Navigate(string target,string url) {
            Uri uri;
            bool allowed=url=="about:blank"||(Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&(uri.Host=="arena.ai"||uri.Host=="10minutemail.one"));
            if(!allowed)throw new InvalidOperationException("登录流程地址不受支持");
            select(target).CoreWebView2.Navigate(url);
            if(target=="auth"&&url=="about:blank")select("arena");
        }
    }
}
