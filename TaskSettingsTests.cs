using System;
using System.Collections.Generic;
using System.IO;

namespace ArenaCompanion {
    static class TaskSettingsTests {
        static int count;
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
        static void Main() {
            string root=Path.Combine(Path.GetTempPath(),"ArenaSettingsTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string source=Path.Combine(root,"reference.png");File.WriteAllText(source,"test image bytes");
            var store=new TaskSettingsStore(Path.Combine(root,"managed"));var file=store.Import(source);
            var defaults=store.Load("new default prompt");
            Check(defaults.Prompt=="new default prompt"&&defaults.Attachments.Count==0&&defaults.ContinueCollecting,"new task uses supplied default prompt, no attachments and continuous collection");
            Check(file.Path!=source&&File.ReadAllText(file.Path)==File.ReadAllText(source),"binding keeps independent byte-identical copy");
            store.Save(new TaskSettings {Prompt="exact prompt",Attachments=new List<BoundAttachment>{file}});
            File.Delete(source);
            var loaded=new TaskSettingsStore(Path.Combine(root,"managed")).Load("fallback");TaskSettingsStore.Verify(loaded.Attachments);
            Check(loaded.Prompt=="exact prompt"&&loaded.Attachments[0].Sha256==file.Sha256,"restart restores prompt and attachment after original removed");
            var again=store.Import(file.Path);Check(again.Path==file.Path,"rebinding same attachment reuses managed copy");
            bool duplicate=false;try{TaskSettingsStore.Verify(new[]{file,file});}catch(InvalidDataException){duplicate=true;}
            Check(duplicate,"duplicate filenames are rejected");
            File.AppendAllText(file.Path,"changed");bool changed=false;try{TaskSettingsStore.Verify(new[]{file});}catch(InvalidDataException){changed=true;}
            Check(changed,"changed bound file is rejected before upload");
            Console.WriteLine("ALL "+count+" PASSED");Directory.Delete(root,true);
        }
    }
}
