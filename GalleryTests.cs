using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    static class GalleryTests {
        static int passed;
        static void Check(bool value,string text){if(!value)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
        [STAThread] static int Main() {
            Application.EnableVisualStyles();Environment.SetEnvironmentVariable("ARENA_BACKGROUND","1");
            var host=new Form {Opacity=0,ShowInTaskbar=false};int result=0;
            host.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){Console.WriteLine(ex);result=1;}finally{host.Close();}};
            Application.Run(host);return result;
        }
        static async Task Run() {
            string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","qa","gallery-local",DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            var store=new CandidateStore(Path.Combine(root,"collection"));
            var renderer=new HtmlImageRenderer(Path.Combine(root,"browser"));
            string url="https://arena.ai/agent/11111111-1111-4111-8111-111111111111";
            var first=store.ForConversation(url,"profile-a","local-test-a@example.invalid","local test");first.Title="本地测试 01 · 长页面";
            string html="<!doctype html><style>*{margin:0}body{background:#182e48;color:white;font:32px sans-serif}header{padding:30px}section{height:1800px;background:linear-gradient(#182e48,#366a8d)}footer{height:100px;background:#00ff00}</style><header>Local render test / full page</header><section></section><footer></footer>";
            var im=await renderer.Render(html,Path.Combine(store.DirectoryPath,first.Id+".png"),"long.html");
            Check(im.Height>1800&&im.Width==640,"full-page PNG exceeds viewport and uses consistent comparison width");
            using(var bitmap=new Bitmap(Path.Combine(store.DirectoryPath,im.File))) {
                Color pixel=bitmap.GetPixel(100,bitmap.Height-10);Check(pixel.G>240&&pixel.R<10&&pixel.B<10,"long-page bottom is present rather than cropped at viewport");
            }
            first.Images.Add(im);first.Renamed=true;store.Save(first);
            var restored=new CandidateStore(store.DirectoryPath).ForConversation(url,"PROFILE-A","local-test-a@example.invalid","changed prompt");
            Check(restored.Id==first.Id&&restored.Images.Count==1,"restart and repeated collection preserve conversation identity");
            var second=store.ForConversation(url,"profile-b","local-test-b@example.invalid","local test");second.Title="本地测试 02 · 卡片";
            second.Images.Add(await renderer.Render("<!doctype html><style>body{margin:0;background:#ecdfc9;font:20px sans-serif;display:grid;place-content:center;min-height:100vh}.card{padding:36px;background:#fffaf0;border:2px solid #183d35;border-radius:26px;box-shadow:12px 12px #adbea8}h1{color:#183d35}</style><div class=card><h1>HTML → PNG</h1><p>Only the image is retained.</p></div>",Path.Combine(store.DirectoryPath,second.Id+".png"),"card.html"));
            store.Save(second);Check(second.Id!=first.Id&&store.All().Count==2,"different account profiles remain separate in the same gallery");
            Check(Directory.GetFiles(store.DirectoryPath,"*.html").Length==0,"collection retains images and mapping metadata only");
            CandidateRecord clicked=null;bool canEdit=true;
            using(var gallery=new GalleryWindow(store,r=>clicked=r,()=>canEdit)) {
                gallery.Show();for(int n=0;n<60&&!gallery.Ready;n++)await Task.Delay(200);
                if(!gallery.Ready)throw new Exception("Gallery failed to initialize: "+gallery.Text);
                await Task.Delay(800);
                string inspect=new JavaScriptSerializer().Serialize(await gallery.Inspect());File.WriteAllText(Path.Combine(root,"gallery-state.json"),inspect);
                Check(inspect.Contains("\"cards\":2")&&inspect.Contains("\"complete\":true"),"gallery renders the stored records and images");
                var layout=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(inspect);
                Check(Convert.ToString(((System.Collections.Generic.Dictionary<string,object>)layout["ui"])["columns"]).Split(' ').Length>=7,"default gallery layout fits at least seven columns at its normal window width");
                await gallery.ClickImage(second.Id);await Task.Delay(300);
                Check(clicked==null&&new JavaScriptSerializer().Serialize(await gallery.Inspect()).Contains("\"selected\":[{"),"left click selects an image without opening its conversation");
                await gallery.ContextAction(second.Id,second.Images[0].File,"open");await Task.Delay(300);
                Check(clicked!=null&&clicked.Id==second.Id&&clicked.Profile=="profile-b"&&clicked.Url==url,"right-click menu opens the exact conversation and source profile");
                Check(new JavaScriptSerializer().Serialize(await gallery.Inspect()).Contains(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory).Replace("\\","\\\\")),"export location defaults to the desktop");
                second.Profile=Path.Combine(root,"export-account");Directory.CreateDirectory(second.Profile);
                new AccountStore(second.Profile).Save(new AccountData {Email=second.Email,Password="Fixture-Only-Password",Name="Local"});store.Save(second);
                string destination=Path.Combine(root,"saved-export");Directory.CreateDirectory(destination);gallery.SetDestination(destination);await Task.Delay(250);
                await gallery.ContextAction(second.Id,second.Images[0].File,"save");await Task.Delay(400);
                string folder=gallery.LastSavedFolder;
                string accountRoot=Path.Combine(destination,"账号");
                Check(folder!=null&&Path.GetDirectoryName(folder)==accountRoot&&File.Exists(Path.Combine(folder,"打开对话.lnk")),"save menu creates the account folder and launch shortcut under the selected destination");
                File.Delete(Path.Combine(second.Profile,"account.dpapi"));
                Check(SavedConversationStore.Load(folder).Url==url&&new AccountStore(folder).Load().Email==second.Email,"saved account and conversation survive loss of the original account file");
                string originalFolder=folder,renamedFolder=Path.Combine(accountRoot,"这是任意重命名后的文件夹");Directory.Move(originalFolder,renamedFolder);folder=renamedFolder;
                Check(SavedConversationStore.ResolveById(accountRoot,second.Id)==renamedFolder&&SavedConversationStore.Load(renamedFolder).Url==url,"saved conversation resolves by stable id after its folder is renamed");
                Check(SavedConversationStore.ResolveLegacyFolder(originalFolder)==renamedFolder,"legacy absolute-path shortcut also recovers a renamed folder by its id suffix");
                Check(Directory.GetFiles(folder,"*.png").Length==0&&!File.ReadAllText(Path.Combine(folder,"conversation.json")).Contains("Fixture-Only-Password"),"export contains account and conversation metadata, no image or plaintext password");
                Check(new SavedConversationStore(Path.Combine(root,"gallery-export-directory.txt")).Destination==destination,"selected export location survives restart");
                await gallery.CaptureImage(Path.Combine(root,"gallery-preview.png"));
                canEdit=false;await gallery.ContextAction(second.Id,second.Images[0].File,"discard");await Task.Delay(250);
                Check(store.Get(second.Id).Images.Count==1,"active collection blocks conflicting gallery edits");canEdit=true;
                await gallery.ContextAction(second.Id,second.Images[0].File,"discard");await Task.Delay(300);
                Check(store.Get(second.Id).Images.Count==0&&store.Get(first.Id).Images.Count==1&&File.Exists(Path.Combine(store.DirectoryPath,first.Images[0].File)),"discard menu removes only the selected image and retains other candidates");
                string profileMarker=Path.Combine(second.Profile,"profile-marker.txt"),exportMarker=Path.Combine(root,"export-marker.txt");File.WriteAllText(profileMarker,"keep profile");File.WriteAllText(exportMarker,"keep export");
                store.Clear();gallery.Reload();await Task.Delay(250);
                Check(store.All().Count==0&&Directory.GetFiles(store.DirectoryPath,"*.png").Length==0&&!Directory.Exists(Path.Combine(store.DirectoryPath,"_discarded")),"clear removes current candidate records, images and discarded cache");
                Check(File.Exists(profileMarker)&&File.Exists(exportMarker),"clear does not touch account profiles or exported files");
            }
            await TestCollector(root);
            File.WriteAllText(Path.Combine(root,"result.json"),new JavaScriptSerializer().Serialize(new {passed,online=false,root}));
            Console.WriteLine("ALL "+passed+" PASSED; "+root);
        }
        static async Task TestCollector(string root) {
            using(var form=new Form {Opacity=0,ShowInTaskbar=false,ClientSize=new Size(900,700)})
            using(var view=new WebView2 {Dock=DockStyle.Fill}) {
                form.Controls.Add(view);form.Show();await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"fixture-browser")));
                // Local virtual host only. No Arena network requests or account state.
                string assets=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","qa","gallery-fixture-assets");Directory.CreateDirectory(assets);
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","candidate-fixture.html"),Path.Combine(assets,"index.html"),true);
                view.CoreWebView2.SetVirtualHostNameToFolderMapping("arena.ai",assets,CoreWebView2HostResourceAccessKind.DenyCors);
                var loaded=new TaskCompletionSource<bool>();view.CoreWebView2.NavigationCompleted+=(s,e)=>loaded.TrySetResult(e.IsSuccess);
                view.CoreWebView2.Navigate("https://arena.ai/index.html");
                if(await Task.WhenAny(loaded.Task,Task.Delay(10000))!=loaded.Task||!await loaded.Task)throw new Exception("Local fixture failed");
                var store=new CandidateStore(Path.Combine(root,"collector-fixture"));var cp=new CandidatePage(view);
                var collector=new CandidateCollector(store,cp,new HtmlImageRenderer(Path.Combine(root,"collector-renderer")));
                string url=view.Source.AbsoluteUri;
                var record=await collector.Collect(url,"fixture-profile","local@example.invalid","local test",()=>Task.FromResult(0));
                Check(record.Renamed&&record.Images.Count==2&&File.Exists(Path.Combine(store.DirectoryPath,record.Images[0].File)),"local end-to-end download, rendering, storage, rename and readback succeed for multiple HTML files");
                var again=await collector.Collect(url,"fixture-profile","local@example.invalid","local test",()=>Task.FromResult(0));
                Check(again.Id==record.Id&&store.All().Count==1&&Directory.GetFiles(store.DirectoryPath,"*.png").Length==2,"collector retry is idempotent after a completed collection");
                string untouched=Path.Combine(store.DirectoryPath,record.Images[1].File),bytes=Convert.ToBase64String(File.ReadAllBytes(untouched));
                File.Delete(Path.Combine(store.DirectoryPath,record.Images[0].File));
                var repaired=await collector.Collect(url,"fixture-profile","local@example.invalid","local test",()=>Task.FromResult(0));
                Check(repaired.Images.Count==2&&repaired.Images[0].File!=repaired.Images[1].File&&bytes==Convert.ToBase64String(File.ReadAllBytes(untouched)),"repairing a missing image never overwrites another HTML file's image");
                string discardedName=repaired.Images[0].Name;store.Discard(repaired.Id,repaired.Images[0].File);
                var recollected=await collector.Collect(url,"fixture-profile","local@example.invalid","local test",()=>Task.FromResult(0));
                Check(recollected.Images.Count==1&&recollected.DiscardedNames.Contains(discardedName),"recollection does not resurrect a discarded image");
                await view.CoreWebView2.ExecuteScriptAsync("window.captionedArtifact()");
                var captionStore=new CandidateStore(Path.Combine(root,"caption-fixture"));
                var captionCollector=new CandidateCollector(captionStore,cp,new HtmlImageRenderer(Path.Combine(root,"caption-renderer")));
                var captionRecord=await captionCollector.Collect(url,"fixture-profile","local@example.invalid","local test",()=>Task.FromResult(0));
                Check(captionRecord.Renamed&&captionRecord.Images.Count==1&&captionRecord.Images[0].Name=="sample.html","Chinese HTML caption opens preview and downloads the verified filename automatically");
                await view.CoreWebView2.ExecuteScriptAsync("window.dualPreviewArtifact()");
                var dualStore=new CandidateStore(Path.Combine(root,"dual-preview-fixture"));
                var dualCollector=new CandidateCollector(dualStore,cp,new HtmlImageRenderer(Path.Combine(root,"dual-renderer")));
                var dualRecord=await dualCollector.Collect(url,"fixture-profile","local@example.invalid","local test",()=>Task.FromResult(0));
                Check(dualRecord.Renamed&&dualRecord.Images.Count==1&&dualRecord.Images[0].Name=="sample.html","inline and workspace previews coexist without losing the HTML filename");
                Check(await view.CoreWebView2.ExecuteScriptAsync("window.openClicks||0")=="0","already-open matching preview is downloaded without clicking another artifact");
                await view.CoreWebView2.ExecuteScriptAsync("window.closedPreviewArtifact()");
                var openStore=new CandidateStore(Path.Combine(root,"open-file-fixture"));
                var openCollector=new CandidateCollector(openStore,cp,new HtmlImageRenderer(Path.Combine(root,"open-renderer")));
                var openRecord=await openCollector.Collect(url,"fixture-profile","local@example.invalid","local test",()=>Task.FromResult(0));
                Check(openRecord.Images.Count==1&&await view.CoreWebView2.ExecuteScriptAsync("window.openClicks")=="1","Open file selects the named HTML artifact and ignores a non-HTML file");
                await view.CoreWebView2.ExecuteScriptAsync("window.closedPreviewArtifact();window.openClicks=0;window.linkClicks=0;var link=document.createElement('button');link.textContent='sample.html';link.onclick=()=>window.linkClicks++;document.querySelector('[role=log]').append(link)");
                var linkedStore=new CandidateStore(Path.Combine(root,"linked-file-fixture"));
                var linkedCollector=new CandidateCollector(linkedStore,cp,new HtmlImageRenderer(Path.Combine(root,"linked-renderer")));
                var linkedRecord=await linkedCollector.Collect(url,"fixture-profile","local@example.invalid","local test",()=>Task.FromResult(0));
                Check(linkedRecord.Images.Count==1&&await view.CoreWebView2.ExecuteScriptAsync("window.openClicks===1&&window.linkClicks===0")=="true","same HTML tool entry plus reply filename link downloads and collects exactly once");
                await view.CoreWebView2.ExecuteScriptAsync("window.closedPreviewArtifact();var entry=[...document.querySelectorAll('[role=log] [role=button]')].find(e=>e.textContent.includes('sample.html'));entry.parentElement.append(entry.cloneNode(true))");
                bool ambiguous=false;try{await cp.Action("file","sample.html");}catch(InvalidOperationException){ambiguous=true;}
                Check(ambiguous,"two distinct tool entries with the same filename remain ambiguous");
                cp.ExpectedUrl="https://arena.ai/agent/22222222-2222-4222-8222-222222222222";bool rejected=false;
                try{await cp.Action("renameMenu","");}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"navigation mismatch blocks rename before any action");
                File.WriteAllText(Path.Combine(root,"collector-result.json"),new JavaScriptSerializer().Serialize(new {passed,online=false,record}));
            }
        }
    }
}
