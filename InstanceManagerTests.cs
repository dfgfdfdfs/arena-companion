using System;
using System.IO;
using System.Threading;

namespace ArenaCompanion {
    static class InstanceManagerTests {
        static int passed;
        static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);passed++;Console.WriteLine("PASS "+name);}
        static void Main() {
            string root=Path.Combine(Path.GetTempPath(),"ArenaInstanceManagerTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try {
                string original=InstanceContext.DirectoryFor(root,"实例 A");Directory.CreateDirectory(Path.Combine(original,"Browser"));
                File.WriteAllText(Path.Combine(original,"Browser","login-state.txt"),"kept");
                Directory.CreateDirectory(Path.Combine(original,"候选图集"));string encoded=original.Replace("\\","\\\\");
                File.WriteAllText(Path.Combine(original,"task-settings.json"),"{\"Attachments\":[{\"Path\":\""+encoded+"\\\\Attachments\\\\hash\\\\image.png\"}]}");
                File.WriteAllText(Path.Combine(original,"候选图集","candidate.json"),"{\"Profile\":\""+encoded+"\",\"Url\":\"https://arena.ai/agent/test\"}");
                string linked=Path.Combine(root,"继承图集实例");Directory.CreateDirectory(Path.Combine(linked,"候选图集"));
                string linkedCandidate=Path.Combine(linked,"候选图集","shared.json");File.WriteAllText(linkedCandidate,"{\"Profile\":\""+encoded+"\"}");
                bool linkedCreated;using(var linkedMutex=new Mutex(true,InstanceContext.MutexName(linked),out linkedCreated)) {
                    bool blocked=false;try{InstanceManager.Rename(root,"实例 A","实例 B");}catch(InvalidOperationException){blocked=true;}
                    Check(linkedCreated&&blocked&&Directory.Exists(original),"rename waits while another running instance uses the linked candidate account");linkedMutex.ReleaseMutex();
                }
                string renamed=InstanceManager.Rename(root,"实例 A","实例 B");
                Check(renamed=="实例 B"&&!Directory.Exists(original),"rename removes the old directory name");
                Check(File.ReadAllText(Path.Combine(root,"实例 B","Browser","login-state.txt"))=="kept","rename preserves nested instance data");
                string target=Path.Combine(root,"实例 B"),targetEncoded=target.Replace("\\","\\\\");
                Check(File.ReadAllText(Path.Combine(target,"task-settings.json")).Contains(targetEncoded),"rename updates bound attachment paths");
                Check(File.ReadAllText(Path.Combine(target,"候选图集","candidate.json")).Contains(targetEncoded),"rename updates candidate account paths");
                Check(File.ReadAllText(linkedCandidate).Contains(targetEncoded),"rename updates inherited candidate paths in other instances");
                bool duplicate=false;Directory.CreateDirectory(Path.Combine(root,"实例 C"));try{InstanceManager.Rename(root,"实例 B","实例 C");}catch(IOException){duplicate=true;}
                Check(duplicate&&Directory.Exists(Path.Combine(root,"实例 B")),"rename refuses to overwrite another instance");
                bool created;string running=target;using(var mutex=new Mutex(true,InstanceContext.MutexName(running),out created)) {
                    bool blocked=false;try{InstanceManager.Delete(root,"实例 B",false);}catch(InvalidOperationException){blocked=true;}
                    Check(created&&blocked&&Directory.Exists(running),"running instance cannot be deleted");mutex.ReleaseMutex();
                }
                using(var mutex=new Mutex(true,InstanceContext.MutexName(running),out created)) {
                    bool blocked=false;try{InstanceManager.Rename(root,"实例 B","运行中重命名");}catch(InvalidOperationException){blocked=true;}
                    Check(created&&blocked&&Directory.Exists(running),"running instance cannot be renamed");mutex.ReleaseMutex();
                }
                InstanceManager.Delete(root,"实例 B",false);Check(!Directory.Exists(running),"stopped instance can be deleted");
                InstanceManager.Rename(root,"实例 C","实例 c");Check(Directory.Exists(Path.Combine(root,"实例 c")),"case-only rename is supported");
                InstanceManager.Delete(root,"实例 c",false);Check(!Directory.Exists(Path.Combine(root,"实例 c")),"renamed instance remains deletable");
                string recycled=Path.Combine(root,"移入回收站验证");Directory.CreateDirectory(recycled);File.WriteAllText(Path.Combine(recycled,"marker.txt"),"fixture");
                InstanceManager.Delete(root,"移入回收站验证",true);Check(!Directory.Exists(recycled),"confirmed deletion moves the instance out of its data root");
                Console.WriteLine("ALL "+passed+" PASSED");
            } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
