using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ArenaCompanion {
    public static class InstanceContext {
        public static string Name {get;private set;}
        public static string Root {get {return Environment.GetEnvironmentVariable("ARENA_INSTANCE_ROOT")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Arena筛选助手多开");}}
        public static string DirectoryFor(string root,string name) {
            if(String.IsNullOrWhiteSpace(name)||!Regex.IsMatch(name,@"^[\p{L}\p{N}_ -]{1,40}$")||name!=name.Trim())throw new ArgumentException("实例名请使用 1 至 40 个文字、数字、空格、下划线或短横线");
            return Path.Combine(Path.GetFullPath(root),name);
        }
        public static void Configure(string name) {
            string directory=DirectoryFor(Root,name);Directory.CreateDirectory(directory);Name=name;
            Environment.SetEnvironmentVariable("ARENA_DATA_DIRECTORY",directory);
            Environment.SetEnvironmentVariable("ARENA_SETTINGS_DIRECTORY",directory);
        }
        public static string MutexName(string directory) {
            string canonical=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
            using(var sha=SHA256.Create())return "ArenaInstance."+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-","");
        }
    }
}
