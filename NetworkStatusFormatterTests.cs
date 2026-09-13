using System;

namespace ArenaCompanion {
    static class NetworkStatusFormatterTests {
        static void Check(bool value,string name) {if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
        static void Main() {
            var current=new V2rayNNode {Name="东京线路",Type="VLESS",Address="node.example",ResolvedAddress="66.154.107.15",Port=17040,Delay=218};
            string text=NetworkStatusFormatter.Format("66.154.107.15",current,2,9,3,4,new DateTime(2026,9,12,12,34,56));
            Check(text.Contains("当前公网 IP：66.154.107.15"),"public IP is visible");
            Check(text.Contains("东京线路 | VLESS | 66.154.107.15 : 17040 | 延迟 218 ms"),"current node details are visible");
            Check(text.Contains("点击时实时核对地区和延迟，不提前指定"),"next target is deliberately selected at click time");
            Check(text.Contains("非中国大陆 | 最近 4 次不重复（已记录 4）| 优先最低延迟"),"selection rules are visible");
            Check(text.Contains("当前 3/3 | 共 9 个节点 / 3 个不同地址（点击时按 IP 去重）"),"current position and counts are visible");
            Check(text.Contains("每 5 秒刷新 · 12:34:56"),"refresh interval and timestamp are visible");
        }
    }
}
