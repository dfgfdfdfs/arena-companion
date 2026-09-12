using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArenaCompanion {
    public sealed partial class MainForm {
        readonly Button switchIp=new Button {Text="切换到下一个不同 IP",AutoSize=true,Height=36};
        readonly Label ipStatus=new Label {Text="正在读取当前网络信息…",AutoSize=false,Dock=DockStyle.Fill,Padding=new Padding(10,7,8,5),BorderStyle=BorderStyle.FixedSingle,BackColor=Color.White,ForeColor=Color.FromArgb(37,57,81)};
        readonly Timer networkTimer=new Timer();
        bool switchingIp;
        bool refreshingNetwork;
        bool lastIpSwitchOk;

        void ConfigureNetworkSwitch(FlowLayoutPanel bar) {
            bar.Controls.Add(switchIp);
            switchIp.Click+=async(s,e)=>await SwitchToNextIp();
            networkTimer.Interval=5000;
            networkTimer.Tick+=async(s,e)=>await RefreshNetworkInfo();
            browserTabs.SelectedIndexChanged+=async(s,e)=>{if(ready&&browserTabs.SelectedIndex==1)await RefreshNetworkInfo();};
        }

        async Task RefreshNetworkInfo() {
            if(refreshingNetwork||switchingIp||!ready||browserTabs.SelectedIndex!=1)return;
            refreshingNetwork=true;
            try {
                V2rayNControlClient client=await V2rayNControlClient.Discover();
                V2rayNSnapshot snapshot=await client.ReadProfiles();
                string statePath=Path.Combine(InstanceContext.Root,"v2rayn-ip-cycle.txt");
                string last=File.Exists(statePath)?File.ReadAllText(statePath).Trim():"";
                List<V2rayNNode> targets=await IpCycle.DistinctTargets(snapshot,last);
                V2rayNNode current=snapshot.Nodes.FirstOrDefault(n=>n.Current||n.IndexId==snapshot.CurrentIndexId);
                V2rayNNode next=targets.FirstOrDefault();
                string publicIp=await PublicIpReader.Read();
                var distinct=snapshot.Nodes.GroupBy(n=>n.ResolvedAddress,StringComparer.OrdinalIgnoreCase)
                    .Select(g=>g.OrderBy(n=>n.Delay>0?0:1).ThenBy(n=>n.Delay>0?n.Delay:Int32.MaxValue).First()).ToList();
                int currentPosition=current==null?-1:distinct.FindIndex(n=>String.Equals(n.ResolvedAddress,current.ResolvedAddress,StringComparison.OrdinalIgnoreCase));
                int nextPosition=next==null?-1:distinct.FindIndex(n=>String.Equals(n.ResolvedAddress,next.ResolvedAddress,StringComparison.OrdinalIgnoreCase));
                ipStatus.Text=NetworkStatusFormatter.Format(publicIp,current,next,currentPosition,nextPosition,snapshot.Nodes.Count,distinct.Count,DateTime.Now);
            } catch(Exception ex) {
                ipStatus.Text="网络信息读取失败："+ex.Message+Environment.NewLine+"只读刷新会在 5 秒后重试；当前代理不会被切换。";
            } finally {refreshingNetwork=false;}
        }

        async Task<string> SwitchToNextIp() {
            if(switchingIp)return ipStatus.Text;
            switchingIp=true;switchIp.Enabled=false;
            lastIpSwitchOk=false;
            string statePath=Path.Combine(InstanceContext.Root,"v2rayn-ip-cycle.txt");
            try {
                ipStatus.Text="正在实时读取 v2rayN 节点…";
                V2rayNControlClient client=await V2rayNControlClient.Discover();
                V2rayNSnapshot snapshot=await client.ReadProfiles();
                string last=File.Exists(statePath)?File.ReadAllText(statePath).Trim():"";
                var targets=await IpCycle.DistinctTargets(snapshot,last);
                V2rayNNode current=snapshot.Nodes.FirstOrDefault(n=>n.Current||n.IndexId==snapshot.CurrentIndexId);
                string beforeEndpoint=current==null?"未知":(String.IsNullOrEmpty(current.ResolvedAddress)?current.Address:current.ResolvedAddress);
                string beforePublic=await PublicIpReader.Read();
                Exception latest=null;
                foreach(V2rayNNode target in targets) {
                    try {
                        ipStatus.Text="当前 "+beforeEndpoint+" · 正在切换到 "+target.ResolvedAddress;
                        await client.Activate(target.IndexId);
                        string afterPublic="";
                        for(int attempt=0;attempt<5&&afterPublic=="";attempt++) {
                            await Task.Delay(attempt==0?1200:1800);afterPublic=await PublicIpReader.Read();
                        }
                        V2rayNSnapshot confirmed=await client.ReadProfiles();
                        if(confirmed.CurrentIndexId!=target.IndexId)throw new InvalidOperationException("v2rayN 没有确认目标节点为活动配置");
                        if(afterPublic=="")throw new InvalidOperationException("目标节点已激活，但无法连通公网 IP 检测服务");
                        if(beforePublic!=""&&afterPublic!=""&&String.Equals(beforePublic,afterPublic,StringComparison.OrdinalIgnoreCase)) {
                            latest=new InvalidOperationException("节点 "+target.ResolvedAddress+" 的公网出口仍是 "+afterPublic);
                            continue;
                        }
                        Directory.CreateDirectory(InstanceContext.Root);File.WriteAllText(statePath,target.ResolvedAddress,new UTF8Encoding(false));
                        ipStatus.Text="当前 IP："+(afterPublic==""?target.ResolvedAddress:afterPublic)+" · 节点 "+target.ResolvedAddress+" · 切换成功";
                        AddIpLog("从 "+beforeEndpoint+" 切换到 "+target.ResolvedAddress+(afterPublic==""?"；公网出口暂未读到":"；公网出口 "+afterPublic));
                        lastIpSwitchOk=true;return ipStatus.Text;
                    } catch(Exception ex) {latest=ex;AddIpLog("节点 "+target.ResolvedAddress+" 切换未通过验证："+ex.Message);}
                }
                if(current!=null) {
                    try {await client.Activate(current.IndexId);AddIpLog("所有候选均失败，已恢复原活动节点 "+beforeEndpoint);}
                    catch(Exception restore) {AddIpLog("恢复原活动节点失败："+restore.Message);}
                }
                throw new InvalidOperationException(latest==null?"没有可切换的不同 IP":"所有不同 IP 均未通过验证；最后结果："+latest.Message);
            } catch(Exception ex) {
                ipStatus.Text="IP 切换失败："+ex.Message;ShowError(ipStatus.Text);
            } finally {switchingIp=false;SetEnabled();}
            return ipStatus.Text;
        }

        void AddIpLog(string message) {
            string entry=DateTime.Now.ToString("HH:mm:ss")+"  IP切换  "+message;
            log.Items.Add(entry);if(log.Items.Count>200)log.Items.RemoveAt(0);log.TopIndex=log.Items.Count-1;
            try {File.AppendAllText(Path.Combine(DataDirectory,"运行记录.txt"),entry+Environment.NewLine);}catch(IOException){}
        }
    }

}
