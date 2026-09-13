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
                List<string> recent=IpCycle.ParseRecent(File.Exists(statePath)?File.ReadAllText(statePath):"");
                List<V2rayNNode> distinct=IpCycle.DistinctConfiguredNodes(snapshot);
                V2rayNNode current=IpCycle.Current(snapshot);
                string publicIp=await PublicIpReader.Read();
                int currentPosition=current==null?-1:distinct.FindIndex(n=>String.Equals(n.ResolvedAddress,current.ResolvedAddress,StringComparison.OrdinalIgnoreCase));
                ipStatus.Text=NetworkStatusFormatter.Format(publicIp,current,currentPosition,snapshot.Nodes.Count,distinct.Count,recent.Count,DateTime.Now);
            } catch(Exception ex) {
                ipStatus.Text="网络信息读取失败："+ex.Message+Environment.NewLine+"只读刷新会在 5 秒后重试；当前代理不会被切换。";
            } finally {refreshingNetwork=false;}
        }

        async Task<string> SwitchToNextIp() {
            if(switchingIp)return ipStatus.Text;
            bool retryRateLimitedTask=controller!=null&&controller.CanRetryAfterNetworkChange;
            switchingIp=true;switchIp.Enabled=false;
            lastIpSwitchOk=false;
            string statePath=Path.Combine(InstanceContext.Root,"v2rayn-ip-cycle.txt");
            try {
                ipStatus.Text="正在实时读取 v2rayN 节点…";
                V2rayNControlClient client=await V2rayNControlClient.Discover();
                V2rayNSnapshot snapshot=await client.ReadProfiles();
                List<string> recent=IpCycle.ParseRecent(File.Exists(statePath)?File.ReadAllText(statePath):"");
                ipStatus.Text="正在解析服务器 IP、核对地区并优先选择日本节点…";
                var targets=await IpCycle.SelectTargets(snapshot,recent);
                V2rayNNode current=IpCycle.Current(snapshot);
                string beforeEndpoint=current==null?"未知":(String.IsNullOrEmpty(current.ResolvedAddress)?current.Address:current.ResolvedAddress);
                string beforePublic=await PublicIpReader.Read();
                Exception latest=null;
                if(targets.Count==0)throw new InvalidOperationException("没有满足条件的节点：排除中国大陆、香港、台湾、美国、当前 IP 和最近 4 次已用 IP；地区无法确认的节点不会冒充可用节点");
                foreach(V2rayNNode target in targets) {
                    try {
                        ipStatus.Text="当前 "+beforeEndpoint+" · 正在切换到 "+target.ResolvedAddress+"（"+target.CountryCode+"，"+(target.Delay>0?target.Delay+" ms":"延迟未测")+"）";
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
                        recent=IpCycle.RecordSuccess(recent,target.ResolvedAddress);
                        Directory.CreateDirectory(InstanceContext.Root);File.WriteAllLines(statePath,recent,new UTF8Encoding(false));
                        ipStatus.Text="当前 IP："+(afterPublic==""?target.ResolvedAddress:afterPublic)+" · 节点 "+target.ResolvedAddress+"（"+target.CountryCode+"，"+(target.Delay>0?target.Delay+" ms":"延迟未测")+"）· 切换成功";
                        AddIpLog("从 "+beforeEndpoint+" 切换到 "+target.ResolvedAddress+"（"+target.CountryCode+"，"+(target.Delay>0?target.Delay+" ms":"延迟未测")+"）"+(afterPublic==""?"；公网出口暂未读到":"；公网出口 "+afterPublic));
                        lastIpSwitchOk=true;
                        if(retryRateLimitedTask&&controller.RetryAfterNetworkChange()) {
                            browserTabs.SelectedIndex=0;
                            AddIpLog("网络切换已确认，自动重试刚才因 429 停止的任务");
                        }
                        return ipStatus.Text;
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
