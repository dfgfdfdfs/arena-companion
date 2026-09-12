using System;
using System.Net.Mail;
namespace ArenaCompanion {
    public static class AccountSetup {
        public const string PasswordHint="密码至少 8 位，至少包含一个大写字母（A–Z）和一个符号（如 ! @ #）。";
        public static string Validate(string email,string password,string confirmation) {
            email=(email??"").Trim();
            if(email.Length>0){try{var address=new MailAddress(email);if(address.Address!=email)return "请输入有效的邮箱地址，或留空自动获取新邮箱";}catch(FormatException){return "请输入有效的邮箱地址，或留空自动获取新邮箱";}}
            bool upper=false,symbol=false;
            foreach(char c in password??""){upper|=c>='A'&&c<='Z';symbol|=Char.IsPunctuation(c)||Char.IsSymbol(c);}
            if(String.IsNullOrEmpty(password)||password.Length<8||!upper||!symbol)return PasswordHint;
            if(password!=confirmation)return "两次输入的密码不一致";
            return null;
        }
        public static void Save(AccountStore store,string email,string password,string confirmation) {
            string error=Validate(email,password,confirmation);if(error!=null)throw new ArgumentException(error);
            var account=store.Load();string normalized=(email??"").Trim();
            if(!String.Equals(account.Email??"",normalized,StringComparison.OrdinalIgnoreCase))account=new AccountData {Name="Kai"};
            account.Email=normalized;account.Password=password;store.Save(account);
        }
    }
}
