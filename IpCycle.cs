using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ArenaCompanion {
    public static class IpCycle {
        public static async Task<List<V2rayNNode>> DistinctTargets(V2rayNSnapshot snapshot,string lastAddress) {
            foreach(V2rayNNode node in snapshot.Nodes)node.ResolvedAddress=await Resolve(node.Address);
            var groups=snapshot.Nodes.GroupBy(n=>n.ResolvedAddress,StringComparer.OrdinalIgnoreCase)
                .Select(g=>g.OrderBy(n=>n.Delay>0?0:1).ThenBy(n=>n.Delay>0?n.Delay:Int32.MaxValue).First()).ToList();
            V2rayNNode current=snapshot.Nodes.FirstOrDefault(n=>n.Current||n.IndexId==snapshot.CurrentIndexId);
            string anchor=current==null?lastAddress:current.ResolvedAddress;
            int index=groups.FindIndex(n=>String.Equals(n.ResolvedAddress,anchor,StringComparison.OrdinalIgnoreCase));
            var ordered=new List<V2rayNNode>();
            for(int offset=1;offset<=groups.Count;offset++)ordered.Add(groups[(index+offset+groups.Count)%groups.Count]);
            if(index<0)ordered=groups;
            return ordered.Where(n=>!String.Equals(n.ResolvedAddress,anchor,StringComparison.OrdinalIgnoreCase)).ToList();
        }

        static async Task<string> Resolve(string address) {
            IPAddress parsed;if(IPAddress.TryParse(address,out parsed))return parsed.ToString();
            try {
                Task<IPAddress[]> lookup=Dns.GetHostAddressesAsync(address);
                if(await Task.WhenAny(lookup,Task.Delay(4000))!=lookup)return address.Trim().ToLowerInvariant();
                IPAddress selected=(await lookup).FirstOrDefault(ip=>ip.AddressFamily==AddressFamily.InterNetwork)??(await lookup).FirstOrDefault();
                return selected==null?address.Trim().ToLowerInvariant():selected.ToString();
            } catch {return address.Trim().ToLowerInvariant();}
        }
    }

    public static class PublicIpReader {
        static readonly string[] Services={"https://api.ipify.org","https://checkip.amazonaws.com","https://icanhazip.com"};
        public static async Task<string> Read() {
            foreach(string url in Services) {
                try {
                    var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="ArenaCompanion/1.0";request.Timeout=6000;request.ReadWriteTimeout=6000;
                    string proxy=Environment.GetEnvironmentVariable("ARENA_PUBLIC_IP_PROXY");
                    if(!String.IsNullOrWhiteSpace(proxy))request.Proxy=new WebProxy(proxy);
                    Task<WebResponse> response=request.GetResponseAsync();
                    if(await Task.WhenAny(response,Task.Delay(6500))!=response)continue;
                    using(var result=await response)using(var reader=new System.IO.StreamReader(result.GetResponseStream())) {
                        string text=(await reader.ReadToEndAsync()).Trim();IPAddress parsed;
                        if(IPAddress.TryParse(text,out parsed))return parsed.ToString();
                    }
                } catch {}
            }
            return "";
        }
    }
}
