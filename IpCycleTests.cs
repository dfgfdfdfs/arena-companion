using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaCompanion {
    static class IpCycleTests {
        static void Check(bool value,string name) {if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
        static void Main() {
            var snapshot=new V2rayNSnapshot {CurrentIndexId="b"};
            snapshot.Nodes.Add(new V2rayNNode {IndexId="a",Address="207.57.125.79",Delay=47});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="b",Address="66.154.107.15",Delay=218,Current=true});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="b2",Address="66.154.107.15",Delay=219});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="c-slow",Address="3.112.208.186",Delay=0});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="c-fast",Address="3.112.208.186",Delay=80});
            List<V2rayNNode> targets=IpCycle.DistinctTargets(snapshot,"").GetAwaiter().GetResult();
            Check(targets.Count==2,"duplicate addresses are removed");
            Check(targets[0].ResolvedAddress=="3.112.208.186"&&targets[1].ResolvedAddress=="207.57.125.79","cycle continues after current address");
            Check(targets[0].IndexId=="c-fast","measured node is preferred within one address");
            snapshot.CurrentIndexId="a";foreach(var node in snapshot.Nodes)node.Current=node.IndexId=="a";
            targets=IpCycle.DistinctTargets(snapshot,"").GetAwaiter().GetResult();
            Check(targets[0].ResolvedAddress=="66.154.107.15"&&targets[1].ResolvedAddress=="3.112.208.186","cycle wraps only after later addresses");
        }
    }
}
