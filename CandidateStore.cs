using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace ArenaCompanion {
    public sealed class CandidateImage {
        public string Name {get;set;}
        public string File {get;set;}
        public int Width {get;set;}
        public int Height {get;set;}
    }
    public sealed class CandidateRecord {
        public string Id {get;set;}
        public string Title {get;set;}
        public string Url {get;set;}
        public string Profile {get;set;}
        public string Email {get;set;}
        public string Prompt {get;set;}
        public string CreatedAt {get;set;}
        public bool Renamed {get;set;}
        public string RenameError {get;set;}
        public List<CandidateImage> Images {get;set;}
        public List<string> DiscardedNames {get;set;}
        public CandidateRecord() {Images=new List<CandidateImage>();DiscardedNames=new List<string>();}
    }
    public sealed class CandidateStore {
        public string DirectoryPath {get;private set;}
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        public CandidateStore(string directory) {DirectoryPath=directory;Directory.CreateDirectory(directory);}
        public List<CandidateRecord> All() {
            return Directory.GetFiles(DirectoryPath,"*.json").Select(p=>json.Deserialize<CandidateRecord>(File.ReadAllText(p))).OrderByDescending(r=>r.CreatedAt).ToList();
        }
        public CandidateRecord Get(string id) {return All().Single(r=>r.Id==id);}
        public void Discard(string id,string file) {
            var record=Get(id);var image=record.Images.Single(i=>i.File==file);
            if(Path.GetFileName(file)!=file)throw new InvalidOperationException("无效的图片路径");
            string source=Path.Combine(DirectoryPath,file);
            string folder=Path.Combine(DirectoryPath,"_discarded",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string target=Path.Combine(folder,file);bool moved=File.Exists(source);
            if(moved)File.Move(source,target);
            record.Images.Remove(image);if(record.DiscardedNames==null)record.DiscardedNames=new List<string>();
            record.DiscardedNames.Add(image.Name);
            try {Save(record);}catch {if(moved)File.Move(target,source);throw;}
        }
        public CandidateRecord ForConversation(string url,string profile,string email,string prompt) {
            if(!CandidatePage.CandidateUrl(url))throw new ArgumentException("无效的原对话地址");
            var records=All();var existing=records.FirstOrDefault(r=>r.Url==url&&String.Equals(r.Profile,profile,StringComparison.OrdinalIgnoreCase));
            if(existing!=null)return existing;
            return new CandidateRecord {Id=Guid.NewGuid().ToString("N"),Title="候选 "+(records.Count+1).ToString("D4")+" · "+DateTime.Now.ToString("MM-dd HH:mm"),Url=url,Profile=profile,Email=email,Prompt=prompt,CreatedAt=DateTime.Now.ToString("o")};
        }
        public void Save(CandidateRecord record) {
            string target=Path.Combine(DirectoryPath,record.Id+".json"),temp=target+".tmp";
            File.WriteAllText(temp,json.Serialize(record));
            if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
        }
    }
}
