using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    public sealed class WebPage : IArenaPage {
        readonly WebView2 browser;
        readonly string bridge;
        readonly BrowserScript scripts;
        readonly RateLimitTracker rateLimit;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        public bool Demo { get; set; }
        public WebPage(WebView2 browser) {
            this.browser = browser;
            scripts=BrowserScript.For(browser.CoreWebView2);
            rateLimit=new RateLimitTracker(Path.GetDirectoryName(browser.CoreWebView2.Environment.UserDataFolder));
            browser.CoreWebView2.WebResourceResponseReceived+=(s,e)=>{
                if(e.Response.StatusCode!=429||!RateLimitTracker.Matches(e.Request.Uri))return;
                try{var headers=e.Response.Headers;rateLimit.Observe(e.Request.Uri,e.Response.StatusCode,headers.Contains("Retry-After")?headers.GetHeader("Retry-After"):null,headers.Contains("Date")?headers.GetHeader("Date"):null,DateTime.UtcNow);}catch(IOException){}
            };
            bridge = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "PageBridge.js"));
        }
        public bool IsAllowed(string url) {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == "https" &&
                (u.Host == "arena.ai" || (Demo && u.Host == "arena-demo.local")) &&
                (u.AbsolutePath == "/agent" || u.AbsolutePath.StartsWith("/agent/"));
        }
        Task<string> Execute(string js) {return scripts.Execute(js);}
        public async Task<PageState> Read(string prompt) {
            string url = browser.Source == null ? "" : browser.Source.AbsoluteUri;
            if (!IsAllowed(url)) return new PageState { url = url };
            string value = await Execute(bridge + "\nwindow.__arenaCompanion.read(" + json.Serialize(prompt) + ");");
            PageState state = json.Deserialize<PageState>(value);
            if (state == null) throw new InvalidOperationException("读取页面失败");
            rateLimit.Apply(state);
            return state;
        }
        public async Task Act(string name, string prompt) {
            if (!IsAllowed(browser.Source.AbsoluteUri)) throw new InvalidOperationException("页面地址发生改变");
            string code = "(()=>{try {return window.__arenaCompanion.action(" + json.Serialize(name) + "," + json.Serialize(prompt) + ");}catch(e){return {error:e.message};}})()";
            var answer = json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(await Execute(code));
            if (answer == null || answer.ContainsKey("error")) throw new InvalidOperationException(answer == null ? "操作结果未确认" : Convert.ToString(answer["error"]));
        }
        public async Task<object> Inspect() {
            if(browser.Source==null||!IsAllowed(browser.Source.AbsoluteUri))return new {allowed=false};
            string script=@"(()=>{const main=document.querySelector('main');const visible=e=>e.getClientRects().length>0;return {
                path:location.origin+location.pathname,
                text:main?.innerText.slice(-6000)||'',
                editors:[...document.querySelectorAll('main [contenteditable],main textarea')].map(e=>({tag:e.tagName,role:e.getAttribute('role'),editable:e.getAttribute('contenteditable'),visible:visible(e)})),
                buttons:[...document.querySelectorAll('button')].filter(visible).map(e=>({label:e.getAttribute('aria-label')||e.textContent.trim(),disabled:e.disabled})),
                dialogs:[...document.querySelectorAll('[role=dialog],[role=alert]')].filter(visible).map(e=>e.innerText.slice(0,1000)),
                logs:[...document.querySelectorAll('[role=log]')].map(e=>({withinMain:!!e.closest('main'),visible:visible(e),length:e.innerText.length}))};})()";
            return json.DeserializeObject(await Execute(script));
        }
        public async Task ReplaceDraft(string expected,string replacement) {
            if(browser.Source==null||!IsAllowed(browser.Source.AbsoluteUri))throw new InvalidOperationException("请先打开 Arena");
            string code=bridge+"\n(()=>{try{return window.__arenaCompanion.replaceDraft("+json.Serialize(expected)+","+json.Serialize(replacement)+");}catch(e){return {error:e.message};}})()";
            var result=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(await Execute(code));
            if(result==null||result.ContainsKey("error"))throw new InvalidOperationException(result==null?"草稿修改未确认":Convert.ToString(result["error"]));
        }
    }
}
