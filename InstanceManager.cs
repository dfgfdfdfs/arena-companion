using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.VisualBasic.FileIO;

namespace ArenaCompanion {
    internal static class InstanceManager {
        public static string Rename(string root,string currentName,string newName) {
            string source=InstanceContext.DirectoryFor(root,currentName);
            string target=InstanceContext.DirectoryFor(root,newName);
            if(!Directory.Exists(source))throw new DirectoryNotFoundException("选中的实例已经不存在，请刷新后重试");
            if(String.Equals(source,target,StringComparison.Ordinal))throw new InvalidOperationException("新名称与当前名称相同");
            EnsureStopped(source);
            EnsureReferenceOwnersStopped(root,source);
            if(!String.Equals(source,target,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(target))throw new IOException("已经存在同名实例，请换一个名称");
            MoveDirectory(source,target);
            try {RewriteStoredPaths(root,target,source,target);}
            catch {try {MoveDirectory(target,source);}catch {}throw;}
            return newName;
        }

        public static void Delete(string root,string instanceName,bool recycle) {
            string directory=InstanceContext.DirectoryFor(root,instanceName);
            if(!Directory.Exists(directory))throw new DirectoryNotFoundException("选中的实例已经不存在，请刷新后重试");
            EnsureStopped(directory);
            if(recycle)FileSystem.DeleteDirectory(directory,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);
            else Directory.Delete(directory,true);
        }

        static void EnsureStopped(string directory) {
            if(IsRunning(directory))throw new InvalidOperationException("这个实例正在运行，请先关闭该实例后再操作");
        }
        static bool IsRunning(string directory) {
            bool created;
            using(var mutex=new Mutex(true,InstanceContext.MutexName(directory),out created)) {
                if(created)mutex.ReleaseMutex();return !created;
            }
        }
        static void EnsureReferenceOwnersStopped(string root,string oldPath) {
            string oldToken=JsonToken(oldPath);
            foreach(string instance in Directory.GetDirectories(root)) {
                if(String.Equals(instance,oldPath,StringComparison.OrdinalIgnoreCase))continue;
                string gallery=Path.Combine(instance,"候选图集");if(!Directory.Exists(gallery))continue;
                bool referenced=false;
                foreach(string file in Directory.GetFiles(gallery,"*.json",System.IO.SearchOption.TopDirectoryOnly))if(File.ReadAllText(file).IndexOf(oldToken,StringComparison.OrdinalIgnoreCase)>=0){referenced=true;break;}
                if(referenced&&IsRunning(instance))throw new InvalidOperationException("实例“"+Path.GetFileName(instance)+"”正在使用引用该账号的候选图集，请先关闭它后再重命名");
            }
        }
        static void MoveDirectory(string source,string target) {
            if(String.Equals(source,target,StringComparison.OrdinalIgnoreCase)) {
                string temporary=Path.Combine(Path.GetDirectoryName(source),".__arena_rename_"+Guid.NewGuid().ToString("N"));
                Directory.Move(source,temporary);
                try {Directory.Move(temporary,target);}
                catch {if(Directory.Exists(temporary)&&!Directory.Exists(source))Directory.Move(temporary,source);throw;}
            } else Directory.Move(source,target);
        }
        static void RewriteStoredPaths(string root,string instanceDirectory,string oldPath,string newPath) {
            var files=new List<string>();string settings=Path.Combine(instanceDirectory,"task-settings.json");
            if(File.Exists(settings))files.Add(settings);
            foreach(string instance in Directory.GetDirectories(root)) {
                string gallery=Path.Combine(instance,"候选图集");if(Directory.Exists(gallery))files.AddRange(Directory.GetFiles(gallery,"*.json",System.IO.SearchOption.TopDirectoryOnly));
            }
            string oldToken=JsonToken(oldPath),newToken=JsonToken(newPath);var originals=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            try {
                foreach(string file in files) {
                    string original=File.ReadAllText(file);string changed=ReplaceInsensitive(original,oldToken,newToken);
                    if(changed==original)continue;originals[file]=original;WriteAtomic(file,changed);
                }
            } catch {
                foreach(var item in originals)try {File.WriteAllText(item.Key,item.Value);}catch {}
                throw;
            }
        }
        static string JsonToken(string value) {
            string quoted=new JavaScriptSerializer().Serialize(value);return quoted.Substring(1,quoted.Length-2);
        }
        static string ReplaceInsensitive(string value,string oldValue,string newValue) {
            int start=0,index;var result=new System.Text.StringBuilder();
            while((index=value.IndexOf(oldValue,start,StringComparison.OrdinalIgnoreCase))>=0){result.Append(value,start,index-start);result.Append(newValue);start=index+oldValue.Length;}
            if(start==0)return value;result.Append(value,start,value.Length-start);return result.ToString();
        }
        static void WriteAtomic(string path,string content) {
            string temporary=path+".rename.tmp";File.WriteAllText(temporary,content);
            try {File.Replace(temporary,path,null);}finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
