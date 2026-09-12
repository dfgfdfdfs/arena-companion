using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ArenaCompanion {
    public sealed class CandidateCollector {
        readonly CandidateStore store;
        readonly CandidatePage page;
        readonly HtmlImageRenderer renderer;
        public CandidateCollector(CandidateStore store,CandidatePage page,HtmlImageRenderer renderer) {this.store=store;this.page=page;this.renderer=renderer;}
        public async Task<CandidateRecord> Collect(string url,string profile,string email,string prompt,Func<Task> verify) {
            page.ExpectedUrl=url;
            try {return await CollectChecked(url,profile,email,prompt,verify);}
            finally {page.ExpectedUrl=null;}
        }
        async Task<CandidateRecord> CollectChecked(string url,string profile,string email,string prompt,Func<Task> verify) {
            await verify();var record=store.ForConversation(url,profile,email,prompt);
            var names=await HtmlNames();
            if(names.Length==0)throw new InvalidOperationException("这条回答尚未找到可读取的 HTML 文件，未收集空白图片");
            foreach(string name in names) {
                await verify();
                if(record.DiscardedNames!=null&&record.DiscardedNames.Contains(name))continue;
                if(record.Images.Any(i=>i.Name==name&&File.Exists(Path.Combine(store.DirectoryPath,i.File))))continue;
                record.Images.RemoveAll(i=>i.Name==name);
                string html=await page.DownloadHtml(name);await verify();
                string path=ImagePath(record.Id,name);
                var image=await renderer.Render(html,path,name);await verify();
                record.Images.Add(image);store.Save(record);
            }
            await verify();
            try {await page.Rename(record.Title);record.Renamed=true;record.RenameError=null;}
            catch(Exception ex){record.Renamed=false;record.RenameError=ex.Message;}
            store.Save(record);return record;
        }
        async Task<string[]> HtmlNames() {
            for(int n=0;n<21;n++) {
                var state=await page.State();
                var names=((System.Collections.IEnumerable)state["files"]).Cast<object>().Select(Convert.ToString).ToArray();
                if(names.Length>0)return names;
                if(n==0)await page.Action("openHtmlArtifact","");
                await Task.Delay(200);
            }
            return new string[0];
        }
        string ImagePath(string id,string name) {
            using(var sha=SHA256.Create()) {
                string hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(name))).Replace("-","").ToLowerInvariant();
                return Path.Combine(store.DirectoryPath,id+"-"+hash+".png");
            }
        }
    }
}
