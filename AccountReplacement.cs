using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ArenaCompanion {
    public static class AccountReplacement {
        public static string Create(string root,string source,string instance) {
            var previous=new AccountStore(source).Load();if(String.IsNullOrEmpty(previous.Password))throw new InvalidOperationException("请先通过自动注册 / 登录设置账号密码");
            string stem=instance.Length>14?instance.Substring(0,14):instance;
            string name=stem+" 新邮箱 "+DateTime.Now.ToString("MMddHHmmss")+" "+Guid.NewGuid().ToString("N").Substring(0,4);
            string folder=InstanceContext.DirectoryFor(root,name);Directory.CreateDirectory(folder);
            var excluded=new List<string>();
            foreach(string path in Directory.GetDirectories(root)) {
                if(!File.Exists(Path.Combine(path,"account.dpapi")))continue;
                string email=new AccountStore(path).Load().Email;if(!String.IsNullOrEmpty(email))excluded.Add(email);
            }
            if(!String.IsNullOrEmpty(previous.Email))excluded.Add(previous.Email);
            new AccountStore(folder).Save(new AccountData {Name=previous.Name,Password=previous.Password,ExcludedEmails=excluded.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()});
            var settings=new TaskSettingsStore(source).Load("");var target=new TaskSettingsStore(folder);
            settings.Attachments=settings.Attachments.Select(a=>target.Import(a.Path)).ToList();target.Save(settings);
            return name;
        }
    }
}
