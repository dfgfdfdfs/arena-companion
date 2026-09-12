using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    public sealed class AttachmentUpload : IRequestPreparation {
        readonly WebView2 browser;
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        List<BoundAttachment> files=new List<BoundAttachment>();
        bool submitted;
        public bool Required {get {return files.Count>0;}}
        public AttachmentUpload(WebView2 browser) {this.browser=browser;}
        public void Configure(IEnumerable<BoundAttachment> files) {this.files=new List<BoundAttachment>(files);TaskSettingsStore.Verify(this.files);BeginRound();}
        public void BeginRound() {submitted=false;}
        public async Task<Dictionary<string,object>> Inspect() {
            string code=@"(()=>{const main=[...document.querySelectorAll('main')].find(e=>e.getClientRects().length);if(!main)return {};
                return {text:main.innerText.slice(0,2500),inputs:[...main.querySelectorAll('input[type=file]')].map(e=>({accept:e.accept,multiple:e.multiple,parentVisible:e.parentElement.getClientRects().length>0})),
                images:[...main.querySelectorAll('img')].map(e=>({alt:e.alt,title:e.title,visible:e.getClientRects().length>0})),
                buttons:[...main.querySelectorAll('button')].filter(e=>e.getClientRects().length).map(e=>({label:e.getAttribute('aria-label')||e.textContent.trim(),title:e.title,disabled:e.disabled})),
                fileNodes:[...main.querySelectorAll('[title]')].map(e=>({tag:e.tagName,title:e.title,text:e.textContent.slice(0,180)}))};})()";
            return json.Deserialize<Dictionary<string,object>>(await browser.CoreWebView2.ExecuteScriptAsync(code));
        }
        public async Task<bool> Check() {
            if(Required)EnsureArena();
            string code="(()=>{window.__arenaRequiredAttachments="+json.Serialize(files.Select(f=>f.Name).ToArray())+";return window.__arenaCompanion.attachmentsReady(window.__arenaRequiredAttachments);})()";
            return json.Deserialize<bool>(await browser.CoreWebView2.ExecuteScriptAsync(code));
        }
        public async Task<bool> Prepare() {
            if(await Check())return true;
            if(!submitted) {submitted=true;await Stage();}
            return false;
        }
        async Task<Dictionary<string,object>> Devtools(string method,object parameters) {
            string result=await browser.CoreWebView2.CallDevToolsProtocolMethodAsync(method,json.Serialize(parameters));
            return json.Deserialize<Dictionary<string,object>>(result);
        }
        public async Task Stage() {
            EnsureArena();
            TaskSettingsStore.Verify(files);
            if(!Required)return;
            string code=@"(()=>{const main=[...document.querySelectorAll('main')].find(e=>e.getClientRects().length);
                if(!main||[...main.querySelectorAll('[role=log]')].some(e=>e.getClientRects().length&&e.innerText.trim()))return 0;
                const inputs=[...main.querySelectorAll('input[type=file]')].filter(e=>e.parentElement.getClientRects().length);
                if(inputs.length!==1)return inputs.length;
                document.querySelectorAll('[data-arena-bound-upload]').forEach(e=>e.removeAttribute('data-arena-bound-upload'));
                inputs[0].setAttribute('data-arena-bound-upload','true');return 1;})()";
            int count=json.Deserialize<int>(await browser.CoreWebView2.ExecuteScriptAsync(code));
            if(count!=1)throw new InvalidOperationException("没有找到唯一可用的附件上传入口");
            var tree=await Devtools("DOM.getDocument",new {depth=0});
            var root=(Dictionary<string,object>)tree["root"];
            var found=await Devtools("DOM.querySelector",new {nodeId=root["nodeId"],selector="input[data-arena-bound-upload='true']"});
            if(Convert.ToInt32(found["nodeId"])==0)throw new InvalidOperationException("附件入口已变化");
            await Devtools("DOM.setFileInputFiles",new {nodeId=found["nodeId"],files=files.Select(f=>f.Path).ToArray()});
        }
        void EnsureArena() {
            Uri url=browser.Source;
            if(url==null||url.Scheme!="https"||url.Host!="arena.ai"||(url.AbsolutePath!="/agent"&&!url.AbsolutePath.StartsWith("/agent/")))throw new InvalidOperationException("仅可向 Arena Agent 页面上传绑定附件");
        }
    }
}
