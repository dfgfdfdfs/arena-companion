using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ArenaCompanion {
    public static class IpCycle {
        public const int NoRepeatWindow=4;

        public static async Task<List<V2rayNNode>> DistinctNodes(V2rayNSnapshot snapshot) {
            string[] addresses=snapshot.Nodes.Select(node=>node.Address).Where(value=>!String.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var resolved=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var pending=new List<string>();
            foreach(string address in addresses) {IPAddress parsed;if(IPAddress.TryParse(address,out parsed))resolved[address]=parsed.ToString();else pending.Add(address);}
            using(var gate=new SemaphoreSlim(24)) {
                Task<KeyValuePair<string,string>>[] lookups=pending.Select(async address=>{await gate.WaitAsync();try{return new KeyValuePair<string,string>(address,await Resolve(address));}finally{gate.Release();}}).ToArray();
                foreach(var pair in await Task.WhenAll(lookups))resolved[pair.Key]=pair.Value;
            }
            foreach(V2rayNNode node in snapshot.Nodes)node.ResolvedAddress=resolved.ContainsKey(node.Address)?resolved[node.Address]:node.Address.Trim().ToLowerInvariant();
            return BestPerAddress(snapshot.Nodes,n=>n.ResolvedAddress);
        }

        public static List<V2rayNNode> DistinctConfiguredNodes(V2rayNSnapshot snapshot) {
            foreach(V2rayNNode node in snapshot.Nodes)node.ResolvedAddress=node.Address.Trim().ToLowerInvariant();
            return BestPerAddress(snapshot.Nodes,n=>n.ResolvedAddress);
        }

        static List<V2rayNNode> BestPerAddress(IEnumerable<V2rayNNode> nodes,Func<V2rayNNode,string> key) {
            return nodes
                .Where(n=>!String.IsNullOrWhiteSpace(n.ResolvedAddress))
                .GroupBy(key,StringComparer.OrdinalIgnoreCase)
                .Select(g=>g.OrderBy(n=>n.Delay>0?0:1)
                    .ThenBy(n=>n.Delay>0?n.Delay:Int32.MaxValue)
                    .ThenBy(n=>n.Name??"",StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n=>n.IndexId??"",StringComparer.OrdinalIgnoreCase).First())
                .ToList();
        }

        public static async Task<List<V2rayNNode>> SelectTargets(V2rayNSnapshot snapshot,IEnumerable<string> recentAddresses,Func<IEnumerable<string>,Task<Dictionary<string,string>>> countryLookup=null) {
            List<V2rayNNode> distinct=await DistinctNodes(snapshot);
            V2rayNNode current=Current(snapshot);
            string currentAddress=current==null?"":current.ResolvedAddress;
            var recent=new HashSet<string>((recentAddresses??new string[0]).Take(NoRepeatWindow),StringComparer.OrdinalIgnoreCase);
            List<V2rayNNode> candidates=distinct.Where(node=>!String.Equals(node.ResolvedAddress,currentAddress,StringComparison.OrdinalIgnoreCase)&&!recent.Contains(node.ResolvedAddress)).ToList();
            Func<IEnumerable<string>,Task<Dictionary<string,string>>> lookup=countryLookup??IpCountryReader.ReadMany;
            Dictionary<string,string> countries=await lookup(candidates.Select(node=>node.ResolvedAddress));
            var eligible=new List<V2rayNNode>();
            foreach(V2rayNNode node in candidates) {
                string country;
                node.CountryCode=countries.TryGetValue(node.ResolvedAddress,out country)?(country??"").Trim().ToUpperInvariant():"";
                if(node.CountryCode.Length!=2||node.CountryCode=="CN"||node.CountryCode=="HK"||node.CountryCode=="TW"||node.CountryCode=="US")continue;
                eligible.Add(node);
            }
            return eligible.OrderBy(n=>n.CountryCode=="JP"?0:1)
                .ThenBy(n=>n.Delay>0?0:1)
                .ThenBy(n=>n.Delay>0?n.Delay:Int32.MaxValue)
                .ThenBy(n=>n.ResolvedAddress,StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static V2rayNNode Current(V2rayNSnapshot snapshot) {
            return snapshot.Nodes.FirstOrDefault(n=>n.Current||n.IndexId==snapshot.CurrentIndexId);
        }

        public static List<string> ParseRecent(string text) {
            return (text??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)
                .Select(value=>value.Trim()).Where(value=>value!="").Distinct(StringComparer.OrdinalIgnoreCase).Take(NoRepeatWindow).ToList();
        }

        public static List<string> RecordSuccess(IEnumerable<string> history,string address) {
            var result=new List<string>();
            if(!String.IsNullOrWhiteSpace(address))result.Add(address.Trim());
            foreach(string value in history??new string[0])if(!result.Contains(value,StringComparer.OrdinalIgnoreCase))result.Add(value);
            return result.Take(NoRepeatWindow).ToList();
        }

        static async Task<string> Resolve(string address) {
            IPAddress parsed;if(IPAddress.TryParse(address,out parsed))return parsed.ToString();
            try {
                Task<IPAddress[]> lookup=Dns.GetHostAddressesAsync(address);
                if(await Task.WhenAny(lookup,Task.Delay(4000))!=lookup)return address.Trim().ToLowerInvariant();
                IPAddress[] addresses=await lookup;
                IPAddress selected=addresses.FirstOrDefault(ip=>ip.AddressFamily==AddressFamily.InterNetwork)??addresses.FirstOrDefault();
                return selected==null?address.Trim().ToLowerInvariant():selected.ToString();
            } catch {return address.Trim().ToLowerInvariant();}
        }
    }

    public static class IpCountryReader {
        public static async Task<Dictionary<string,string>> ReadMany(IEnumerable<string> values) {
            var addresses=(values??new string[0]).Select(value=>{IPAddress parsed;return IPAddress.TryParse(value,out parsed)?parsed.ToString():"";})
                .Where(value=>value!="").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var countries=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            Task<Dictionary<string,string>>[] requests=Enumerable.Range(0,(addresses.Length+99)/100)
                .Select(batch=>ReadBatch(addresses.Skip(batch*100).Take(100).ToArray())).ToArray();
            foreach(Dictionary<string,string> result in await Task.WhenAll(requests))
                foreach(var pair in result)countries[pair.Key]=pair.Value;
            return countries;
        }

        static async Task<Dictionary<string,string>> ReadBatch(string[] addresses) {
            var countries=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if(addresses.Length==0)return countries;
            try {
                byte[] payload=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(addresses));
                var request=(HttpWebRequest)WebRequest.Create("https://api.country.is/");
                request.Method="POST";request.ContentType="application/json";request.ContentLength=payload.Length;
                request.UserAgent="ArenaCompanion/1.0";request.Timeout=7000;request.ReadWriteTimeout=7000;
                Task<Stream> opening=request.GetRequestStreamAsync();
                if(await Task.WhenAny(opening,Task.Delay(7500))!=opening)return countries;
                using(var stream=await opening)await stream.WriteAsync(payload,0,payload.Length);
                Task<WebResponse> response=request.GetResponseAsync();
                if(await Task.WhenAny(response,Task.Delay(7500))!=response)return countries;
                using(var result=await response)using(var reader=new StreamReader(result.GetResponseStream())) {
                    var rows=new JavaScriptSerializer().Deserialize<List<Dictionary<string,object>>>(await reader.ReadToEndAsync());
                    foreach(var row in rows??new List<Dictionary<string,object>>()) {
                        object ip,country;if(!row.TryGetValue("ip",out ip)||!row.TryGetValue("country",out country))continue;
                        string address=Convert.ToString(ip),code=Convert.ToString(country).Trim().ToUpperInvariant();
                        if(code.Length==2&&code.All(ch=>ch>='A'&&ch<='Z'))countries[address]=code;
                    }
                }
            } catch {}
            return countries;
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
