using System;
using System.IO;
namespace ArenaCompanion {
    static class AccountReplacementTests {
        static int count;
        static void Check(bool value,string text){if(!value)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
        static void Main(){
            string root=Path.Combine(Path.GetTempPath(),"ArenaReplacement-"+Guid.NewGuid().ToString("N")),source=Path.Combine(root,"Original");Directory.CreateDirectory(source);
            new AccountStore(source).Save(new AccountData {Email="original@example.invalid",Password="FixtureOnlyPassword",Name="Kai",Verified=true});
            string file=Path.Combine(source,"reference.png");File.WriteAllText(file,"fixture bytes");
            var store=new TaskSettingsStore(source);var settings=new TaskSettings {Prompt="independent task"};settings.Attachments.Add(store.Import(file));store.Save(settings);
            Directory.CreateDirectory(Path.Combine(source,"Browser"));File.WriteAllText(Path.Combine(source,"Browser","marker"),"old session");
            string gallery=Path.Combine(source,"候选图集");Directory.CreateDirectory(Path.Combine(gallery,"_discarded","old"));
            File.WriteAllText(Path.Combine(gallery,"candidate.json"),"{\"Profile\":\"original\"}");File.WriteAllText(Path.Combine(gallery,"candidate.png"),"image");File.WriteAllText(Path.Combine(gallery,"_discarded","old","discarded.png"),"discarded");
            File.WriteAllText(Path.Combine(source,"gallery-export-directory.txt"),"C:\\saved-gallery");
            string originalAccount=TaskSettingsStore.Hash(Path.Combine(source,"account.dpapi")),originalSettings=TaskSettingsStore.Hash(Path.Combine(source,"task-settings.json"));
            string name=AccountReplacement.Create(root,source,"Original"),target=InstanceContext.DirectoryFor(root,name);var account=new AccountStore(target).Load();
            Check(String.IsNullOrEmpty(account.Email)&&!account.Verified&&Array.IndexOf(account.ExcludedEmails,"original@example.invalid")>=0,"replacement starts without an account and excludes the previous email");
            Check(TaskSettingsStore.Hash(Path.Combine(source,"account.dpapi"))==originalAccount&&TaskSettingsStore.Hash(Path.Combine(source,"task-settings.json"))==originalSettings,"replacement preserves the old account and task files byte for byte");
            var copied=new TaskSettingsStore(target).Load("");Check(copied.Prompt==settings.Prompt&&copied.Attachments[0].Path!=settings.Attachments[0].Path&&copied.Attachments[0].Sha256==settings.Attachments[0].Sha256&&!Directory.Exists(Path.Combine(target,"Browser")),"attachment is copied independently while old browser session is not copied");
            Check(File.ReadAllText(Path.Combine(target,"候选图集","candidate.json")).Contains("original")&&File.Exists(Path.Combine(target,"候选图集","candidate.png"))&&File.Exists(Path.Combine(target,"候选图集","_discarded","old","discarded.png")),"replacement inherits candidate records, images and discarded state");
            Check(File.ReadAllText(Path.Combine(target,"gallery-export-directory.txt"))=="C:\\saved-gallery","replacement inherits the gallery export destination");
            Check(AccountReplacement.Create(root,source,"Original")!=name,"separate replacement requests allocate distinct instance directories");
            Console.WriteLine("ALL "+count+" PASSED");
        }
    }
}
