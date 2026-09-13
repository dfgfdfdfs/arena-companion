using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ArenaCompanion {
    static class IpCycleTests {
        static void Check(bool value,string name) {if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
        static Task<Dictionary<string,string>> Country(IEnumerable<string> addresses) {
            var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(string address in addresses)if(address!="9.9.9.9")result[address]=address=="1.2.3.4"?"CN":address=="5.5.5.5"?"JP":"US";
            return Task.FromResult(result);
        }
        static void Main() {
            var snapshot=new V2rayNSnapshot {CurrentIndexId="b"};
            snapshot.Nodes.Add(new V2rayNNode {IndexId="a",Address="207.57.125.79",Delay=47});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="b",Address="66.154.107.15",Delay=218,Current=true});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="b2",Address="66.154.107.15",Delay=19});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="c-slow",Address="3.112.208.186",Delay=120});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="c-fast",Address="3.112.208.186",Delay=80});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="d",Address="4.4.4.4",Delay=30});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="e",Address="5.5.5.5",Delay=20});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="cn",Address="1.2.3.4",Delay=5});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="unknown",Address="9.9.9.9",Delay=1});

            List<V2rayNNode> distinct=IpCycle.DistinctNodes(snapshot).GetAwaiter().GetResult();
            Check(distinct.Count==7,"duplicate server IPs are collapsed");
            Check(distinct.Single(n=>n.ResolvedAddress=="3.112.208.186").IndexId=="c-fast","lowest measured latency wins within one IP");
            Check(distinct.Single(n=>n.ResolvedAddress=="66.154.107.15").IndexId=="b2","duplicate selection is deterministic even when the faster profile is not marked current");

            List<V2rayNNode> targets=IpCycle.SelectTargets(snapshot,new string[0],Country).GetAwaiter().GetResult();
            Check(targets.Select(n=>n.ResolvedAddress).SequenceEqual(new[]{"5.5.5.5","4.4.4.4","207.57.125.79","3.112.208.186"}),"eligible non-mainland targets are ordered by current measured latency");
            Check(targets.All(n=>n.ResolvedAddress!="1.2.3.4"),"mainland China IP is excluded even when it is fastest");
            Check(targets.All(n=>n.ResolvedAddress!="9.9.9.9"),"unknown country is excluded instead of being treated as non-mainland");
            Check(targets.All(n=>n.ResolvedAddress!="66.154.107.15"),"current server IP is excluded");

            targets=IpCycle.SelectTargets(snapshot,new[]{"5.5.5.5","4.4.4.4","207.57.125.79","unused"},Country).GetAwaiter().GetResult();
            Check(targets.Count==1&&targets[0].ResolvedAddress=="3.112.208.186","none of the last four successful IPs can repeat");
            targets=IpCycle.SelectTargets(snapshot,new[]{"5.5.5.5","4.4.4.4","3.112.208.186","unused","207.57.125.79"},Country).GetAwaiter().GetResult();
            Check(targets.Count==1&&targets[0].ResolvedAddress=="207.57.125.79","an IP older than the four-entry window can be selected again");

            snapshot.Nodes.Single(n=>n.IndexId=="e").Delay=90;
            targets=IpCycle.SelectTargets(snapshot,new string[0],Country).GetAwaiter().GetResult();
            Check(targets[0].ResolvedAddress=="4.4.4.4","each click uses the latest snapshot latency instead of a preselected target");

            List<string> history=IpCycle.RecordSuccess(new[]{"a","b","c","d"},"e");
            Check(history.SequenceEqual(new[]{"e","a","b","c"}),"successful switch keeps only the newest four IPs");
            history=IpCycle.RecordSuccess(history,"b");
            Check(history.SequenceEqual(new[]{"b","e","a","c"}),"recording a repeated older IP moves it to the front without duplicates");
            Check(IpCycle.ParseRecent("a\r\nb\r\na\r\nc\r\nd\r\ne").SequenceEqual(new[]{"a","b","c","d"}),"history parser is backward compatible and enforces four entries");
        }
    }
}
