using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ArenaCompanion {
    static class IpCycleTests {
        static void Check(bool value,string name) {if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
        static Task<Dictionary<string,string>> Country(IEnumerable<string> addresses) {
            var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(string address in addresses) {
                if(address=="9.9.9.9")continue;
                if(address=="1.2.3.4")result[address]="CN";
                else if(address=="207.57.125.79")result[address]="HK";
                else if(address=="3.112.208.186")result[address]="TW";
                else if(address=="6.6.6.6")result[address]="US";
                else if(address=="5.5.5.5"||address=="8.8.8.8")result[address]="JP";
                else if(address=="7.7.7.7")result[address]="FR";
                else if(address=="4.4.4.4")result[address]="DE";
                else result[address]="SG";
            }
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
            snapshot.Nodes.Add(new V2rayNNode {IndexId="us",Address="6.6.6.6",Delay=2});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="fr",Address="7.7.7.7",Delay=10});
            snapshot.Nodes.Add(new V2rayNNode {IndexId="jp2",Address="8.8.8.8",Delay=40});

            List<V2rayNNode> distinct=IpCycle.DistinctNodes(snapshot).GetAwaiter().GetResult();
            Check(distinct.Count==10,"duplicate server IPs are collapsed");
            Check(distinct.Single(n=>n.ResolvedAddress=="3.112.208.186").IndexId=="c-fast","lowest measured latency wins within one IP");
            Check(distinct.Single(n=>n.ResolvedAddress=="66.154.107.15").IndexId=="b2","duplicate selection is deterministic even when the faster profile is not marked current");

            List<V2rayNNode> targets=IpCycle.SelectTargets(snapshot,new string[0],Country).GetAwaiter().GetResult();
            Check(targets.Select(n=>n.ResolvedAddress).SequenceEqual(new[]{"5.5.5.5","8.8.8.8","7.7.7.7","4.4.4.4"}),"Japan is preferred before other allowed regions, then latency orders each group");
            Check(targets.All(n=>n.ResolvedAddress!="1.2.3.4"),"mainland China IP is excluded even when it is fastest");
            Check(targets.All(n=>n.ResolvedAddress!="207.57.125.79"),"Hong Kong IP is excluded");
            Check(targets.All(n=>n.ResolvedAddress!="3.112.208.186"),"Taiwan IP is excluded");
            Check(targets.All(n=>n.ResolvedAddress!="6.6.6.6"),"United States IP is excluded");
            Check(targets.All(n=>n.ResolvedAddress!="9.9.9.9"),"unknown country is excluded instead of being treated as non-mainland");
            Check(targets.All(n=>n.ResolvedAddress!="66.154.107.15"),"current server IP is excluded");

            targets=IpCycle.SelectTargets(snapshot,new[]{"5.5.5.5","8.8.8.8","7.7.7.7","4.4.4.4"},Country).GetAwaiter().GetResult();
            Check(targets.Count==0,"none of the last four successful IPs can repeat");
            targets=IpCycle.SelectTargets(snapshot,new[]{"5.5.5.5","8.8.8.8","7.7.7.7","unused","4.4.4.4"},Country).GetAwaiter().GetResult();
            Check(targets.Count==1&&targets[0].ResolvedAddress=="4.4.4.4","an IP older than the four-entry window can be selected again");

            snapshot.Nodes.Single(n=>n.IndexId=="e").Delay=90;
            targets=IpCycle.SelectTargets(snapshot,new string[0],Country).GetAwaiter().GetResult();
            Check(targets[0].ResolvedAddress=="8.8.8.8","each click uses the latest Japanese latency instead of a preselected target");

            List<string> history=IpCycle.RecordSuccess(new[]{"a","b","c","d"},"e");
            Check(history.SequenceEqual(new[]{"e","a","b","c"}),"successful switch keeps only the newest four IPs");
            history=IpCycle.RecordSuccess(history,"b");
            Check(history.SequenceEqual(new[]{"b","e","a","c"}),"recording a repeated older IP moves it to the front without duplicates");
            Check(IpCycle.ParseRecent("a\r\nb\r\na\r\nc\r\nd\r\ne").SequenceEqual(new[]{"a","b","c","d"}),"history parser is backward compatible and enforces four entries");
        }
    }
}
