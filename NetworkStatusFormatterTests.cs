using System;

namespace ArenaCompanion {
    static class NetworkStatusFormatterTests {
        static void Check(bool value,string name) {if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
        static void Main() {
            var current=new V2rayNNode {Name="东京线路",Type="VLESS",Address="node.example",ResolvedAddress="66.154.107.15",Port=17040,Delay=218};
            var next=new V2rayNNode {Name="香港线路",Type="Trojan",Address="hk.example",ResolvedAddress="207.57.125.79",Port=443,Delay=47};
            string text=NetworkStatusFormatter.Format("66.154.107.15",current,next,2,0,9,3,new DateTime(2026,9,12,12,34,56));
            Check(text.Contains("当前公网 IP：66.154.107.15"),"public IP is visible");
            Check(text.Contains("东京线路 | VLESS | 66.154.107.15 : 17040 | 延迟 218 ms"),"current node details are visible");
            Check(text.Contains("香港线路 | TROJAN | 207.57.125.79 : 443 | 延迟 47 ms"),"next node details are visible before switching");
            Check(text.Contains("当前 3/3 → 下次 1/3 | 共 9 个节点 / 3 个不同 IP"),"cycle position and counts are visible");
            Check(text.Contains("每 5 秒刷新 · 12:34:56"),"refresh interval and timestamp are visible");
        }
    }
}
