using System;

namespace ArenaCompanion {
    public static class NetworkStatusFormatter {
        public static string Format(string publicIp,V2rayNNode current,V2rayNNode next,int currentPosition,int nextPosition,int nodeCount,int distinctCount,DateTime now) {
            string currentIp=current==null?"未知":Endpoint(current);
            string nextIp=next==null?"暂无":Endpoint(next);
            return "当前公网 IP："+(String.IsNullOrEmpty(publicIp)?"暂未读到（节点信息正常）":publicIp)+"    每 5 秒刷新 · "+now.ToString("HH:mm:ss")+Environment.NewLine+
                "当前节点："+Name(current)+" | "+Protocol(current)+" | "+currentIp+Port(current)+" | "+Delay(current)+Environment.NewLine+
                "下一个待切换："+Name(next)+" | "+Protocol(next)+" | "+nextIp+Port(next)+" | "+Delay(next)+Environment.NewLine+
                "轮换位置：当前 "+Position(currentPosition,distinctCount)+" → 下次 "+Position(nextPosition,distinctCount)+" | 共 "+nodeCount+" 个节点 / "+distinctCount+" 个不同 IP";
        }
        static string Endpoint(V2rayNNode node) {return String.IsNullOrEmpty(node.ResolvedAddress)?node.Address:node.ResolvedAddress;}
        static string Name(V2rayNNode node) {return node==null||String.IsNullOrWhiteSpace(node.Name)?"未命名节点":node.Name;}
        static string Protocol(V2rayNNode node) {return node==null||String.IsNullOrWhiteSpace(node.Type)?"协议未知":node.Type.ToUpperInvariant();}
        static string Port(V2rayNNode node) {return node==null||node.Port<=0?"":" : "+node.Port;}
        static string Delay(V2rayNNode node) {return node==null||node.Delay<=0?"延迟未测":("延迟 "+node.Delay+" ms");}
        static string Position(int index,int count) {return index<0||count<=0?"未知":((index+1)+"/"+count);}
    }
}
