using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ArenaCompanion {
    public sealed class AccountData {
        public string Email { get; set; }
        public string Password { get; set; }
        public string Name { get; set; }
        public bool Verified { get; set; }
        public string MailboxBeforeRefresh {get;set;}
        public bool MailboxRefreshRequested {get;set;}
        public bool MailboxChangeConfirmed {get;set;}
        public string[] ExcludedEmails {get;set;}
    }
    public sealed class AccountStore {
        readonly string path;
        public AccountStore(string directory) { path = Path.Combine(directory, "account.dpapi"); }
        public AccountData Load() {
            if (!File.Exists(path)) return new AccountData {Name="Kai"};
            byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            return new JavaScriptSerializer().Deserialize<AccountData>(Encoding.UTF8.GetString(bytes));
        }
        public void EnsurePassword(string defaultsDirectory = null) {
            AccountData account=Load();
            if(!String.IsNullOrEmpty(account.Password))return;
            throw new InvalidOperationException("请先填写账号密码；独立分发版不提供默认密码");
        }
        public void Save(AccountData account) {
            byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(account));
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
}
