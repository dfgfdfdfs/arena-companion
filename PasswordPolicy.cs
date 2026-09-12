using System;

namespace ArenaCompanion {
    public static class PasswordPolicy {
        public static string Error(string password) {
            if(String.IsNullOrEmpty(password)||password.Length<8)return "密码至少需要 8 个字符";
            bool upper=false,symbol=false;
            foreach(char value in password) {
                if(Char.IsUpper(value))upper=true;
                if(!Char.IsLetterOrDigit(value))symbol=true;
            }
            if(!upper)return "密码至少需要一个大写字母";
            if(!symbol)return "密码至少需要一个符号";
            return "";
        }
    }
}
