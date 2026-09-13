using System;

namespace ArenaCompanion {
    public static class NetworkStatusFormatter {
        public static string Format(string publicIp,V2rayNNode current,int currentPosition,int nodeCount,int distinctCount,int recentCount,DateTime now) {
            string currentIp=current==null?"未知":Endpoint(current);
            return "当前公网 IP："+(String.IsNullOrEmpty(publicIp)?"暂未读到（节点信息正常）":publicIp)+"    每 5 秒刷新 · "+now.ToString("HH:mm:ss")+Environment.NewLine+
                "当前节点："+Name(current)+" | "+Protocol(current)+" | "+currentIp+Port(current)+" | "+Delay(current)+Environment.NewLine+
                "下一个待切换：点击时实时核对地区和延迟，不提前指定"+Environment.NewLine+
                "选择规则：排除大陆/HK/TW/US | 日本优先 | 最近 4 次不重复（已记录 "+recentCount+"）| 同地区优先最低延迟 | 当前 "+Position(currentPosition,distinctCount)+" | 共 "+nodeCount+" 个节点 / "+distinctCount+" 个不同地址（点击时按 IP 去重）";
        }
        static string Endpoint(V2rayNNode node) {return String.IsNullOrEmpty(node.ResolvedAddress)?node.Address:node.ResolvedAddress;}
        static string Name(V2rayNNode node) {return node==null||String.IsNullOrWhiteSpace(node.Name)?"未命名节点":node.Name;}
        static string Protocol(V2rayNNode node) {return node==null||String.IsNullOrWhiteSpace(node.Type)?"协议未知":node.Type.ToUpperInvariant();}
        static string Port(V2rayNNode node) {return node==null||node.Port<=0?"":" : "+node.Port;}
        static string Delay(V2rayNNode node) {return node==null||node.Delay<=0?"延迟未测":("延迟 "+node.Delay+" ms");}
        static string Position(int index,int count) {return index<0||count<=0?"未知":((index+1)+"/"+count);}
    }
}
