using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace ArenaCompanion {
    public sealed class BoundAttachment {
        public string Name {get;set;}
        public string Path {get;set;}
        public string Sha256 {get;set;}
        public long Bytes {get;set;}
    }
    public sealed class TaskSettings {
        public string Prompt {get;set;}
        public bool ContinueCollecting {get;set;}
        public List<BoundAttachment> Attachments {get;set;}
        public TaskSettings() {Attachments=new List<BoundAttachment>();}
    }
    public sealed class TaskSettingsStore {
        readonly string directory;
        readonly string config;
        public TaskSettingsStore(string directory) {
            this.directory=directory;Directory.CreateDirectory(directory);config=System.IO.Path.Combine(directory,"task-settings.json");
        }
        public TaskSettings Load(string defaultPrompt) {
            if(!File.Exists(config))return new TaskSettings {Prompt=defaultPrompt,ContinueCollecting=true};
            var settings=new JavaScriptSerializer().Deserialize<TaskSettings>(File.ReadAllText(config));
            if(settings==null)throw new InvalidDataException("任务设置无法读取");
            if(settings.Attachments==null)settings.Attachments=new List<BoundAttachment>();
            return settings;
        }
        public void Save(TaskSettings settings) {
            string temporary=config+".tmp";File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(settings));
            if(File.Exists(config))File.Replace(temporary,config,null);else File.Move(temporary,config);
        }
        public BoundAttachment Import(string source) {
            string full=System.IO.Path.GetFullPath(source);var info=new FileInfo(full);
            if(!info.Exists||info.Length==0)throw new InvalidDataException("附件不存在或为空："+info.Name);
            string hash=Hash(full);string folder=System.IO.Path.Combine(directory,"Attachments",hash);
            Directory.CreateDirectory(folder);string destination=System.IO.Path.Combine(folder,info.Name);
            if(!File.Exists(destination))File.Copy(full,destination);
            if(Hash(destination)!=hash)throw new InvalidDataException("附件副本校验失败");
            return new BoundAttachment {Name=info.Name,Path=destination,Sha256=hash,Bytes=info.Length};
        }
        public static string Hash(string path) {
            using(var stream=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
        public static void Verify(IEnumerable<BoundAttachment> files) {
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var file in files) {
                if(!names.Add(file.Name))throw new InvalidDataException("附件名称重复，请使用不同名称");
                if(!File.Exists(file.Path)||new FileInfo(file.Path).Length!=file.Bytes||Hash(file.Path)!=file.Sha256)throw new InvalidDataException("绑定附件已丢失或改变："+file.Name);
            }
        }
    }
}
