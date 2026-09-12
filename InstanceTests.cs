using System;
using System.IO;
using System.Threading;
namespace ArenaCompanion {
    static class InstanceTests {
        static int count;
        static void Check(bool value,string description){if(!value)throw new Exception(description);count++;Console.WriteLine("PASS "+description);}
        static void Main(){
            string root=Path.Combine(Path.GetTempPath(),"ArenaIsolation-"+Guid.NewGuid().ToString("N"));
            string a=InstanceContext.DirectoryFor(root,"A"),b=InstanceContext.DirectoryFor(root,"B");
            Check(a!=b&&InstanceContext.MutexName(a)!=InstanceContext.MutexName(b),"different instances have separate paths and locks");
            Check(InstanceContext.MutexName(a)==InstanceContext.MutexName(a.ToLowerInvariant()+"\\"),"equivalent Windows paths share one lock");
            bool rejected=false;try{InstanceContext.DirectoryFor(root,"..\\outside");}catch(ArgumentException){rejected=true;}
            Check(rejected,"instance name cannot escape its data root");
            Environment.SetEnvironmentVariable("ARENA_INSTANCE_ROOT",root);InstanceContext.Configure("A");
            Check(Environment.GetEnvironmentVariable("ARENA_SETTINGS_DIRECTORY")==a&&Environment.GetEnvironmentVariable("ARENA_DATA_DIRECTORY")==a,"all settings and account data follow the same instance");
            bool first,second;using(var ma=new Mutex(true,InstanceContext.MutexName(a),out first))using(var mb=new Mutex(true,InstanceContext.MutexName(b),out second))Check(first&&second,"independent instances can hold their locks concurrently");
            Console.WriteLine("ALL "+count+" PASSED");
        }
    }
}
