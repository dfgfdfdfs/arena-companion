using System;
using System.IO;
using System.Linq;
namespace ArenaCompanion {
    static class AccountDefaultsTests {
        static int passed;
        static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
        static void Main() {
            string root=Path.Combine(Path.GetTempPath(),"ArenaDefaultsQA-"+Guid.NewGuid().ToString("N"));
            string defaults=Path.Combine(root,"defaults"),fresh=Path.Combine(root,"fresh");
            Directory.CreateDirectory(defaults);Directory.CreateDirectory(fresh);
            new AccountStore(defaults).Save(new AccountData {Password="fixture-password",Email="must-not-copy@example.test",Verified=true});
            var store=new AccountStore(fresh);store.EnsurePassword(defaults);var account=store.Load();
            Check(account.Password=="fixture-password"&&String.IsNullOrEmpty(account.Email)&&!account.Verified,"fresh instance receives only password, never another account identity");
            byte[] initial=File.ReadAllBytes(Path.Combine(fresh,"account.dpapi"));store.EnsurePassword(defaults);
            Check(initial.SequenceEqual(File.ReadAllBytes(Path.Combine(fresh,"account.dpapi"))),"repeat startup leaves saved credentials byte-identical");
            account.Password="existing-password";account.Email="existing@example.test";account.Verified=true;store.Save(account);store.EnsurePassword(defaults);account=store.Load();
            Check(account.Password=="existing-password"&&account.Email=="existing@example.test"&&account.Verified,"existing password and login identity are preserved");
            account.Password=null;account.MailboxBeforeRefresh="before@example.test";store.Save(account);store.EnsurePassword(defaults);account=store.Load();
            Check(account.Password=="fixture-password"&&account.Email=="existing@example.test"&&account.MailboxBeforeRefresh=="before@example.test","missing password is filled without resetting pending email workflow");
            account.Password=null;store.Save(account);bool missing=false;
            try{store.EnsurePassword(Path.Combine(root,"absent"));}catch(InvalidOperationException){missing=true;}
            Check(missing&&String.IsNullOrEmpty(store.Load().Password),"missing configuration reports failure without inventing credentials");
            Check(PasswordPolicy.Error("short!")!=""&&PasswordPolicy.Error("lowercase!")!=""&&PasswordPolicy.Error("Uppercase1")!="","distribution password rejects missing length, uppercase or symbol");
            Check(PasswordPolicy.Error("Strong-pass1")=="","distribution password accepts uppercase and symbol");
            Console.WriteLine("ALL "+passed+" PASSED");
        }
    }
}
