(() => {
  const key = '__arenaConversationRecovery';
  if (window[key]) return;
  const state = {plan:null};
  const visible = e => !!e.getClientRects().length;
  const draft = () => [...document.querySelectorAll('[contenteditable="true"]')].filter(visible).map(e=>e.innerText).join('\n');
  const context = () => {
    const log = [...document.querySelectorAll('[role="log"]')].find(visible);
    if (!log) return null;
    let f=log[Object.keys(log).find(k=>k.startsWith('__reactFiber'))], live, initial;
    for(let i=0;f&&i<64;i++,f=f.return) {
      const p=f.memoizedProps;
      if(Array.isArray(p?.initialMessages)) initial=p.initialMessages;
      if(Array.isArray(p?.value?.messages)) live=p.value;
    }
    return live&&initial?{live,initial}:null;
  };
  const fault = evidence => {
    if(location.origin!=='https://arena.ai'||!/^\/agent\/[0-9a-f-]{36}$/.test(location.pathname)) return null;
    const text=evidence||document.body.innerText;
    const m=/Assistant node "([0-9a-f-]{36})" not found in session "([0-9a-f-]{36})"/i.exec(text);
    if(!m||location.pathname!=='/agent/'+m[2])return null;
    if([...document.querySelectorAll('[role="dialog"]')].filter(visible).some(e=>/Security Verification|人机|Log In|使用条款/.test(e.innerText)))return null;
    const c=context();if(!c)return null;
    const {live,initial}=c, last=live.messages.at(-1), prior=initial.at(-1);
    if(live.id!==m[2]||!['ready','error'].includes(live.status)||live.agentFeedbackOperationLock?.activeOperation)return null;
    if(live.error&&(!live.error.message||!live.error.message.includes(m[0])))return null;
    if(typeof live.setMessages!=='function'||typeof live.clearError!=='function')return null;
    if(last?.id!==m[1]||last.role!=='assistant'||last.metadata?.nodeId!==last.id||last.metadata?.checkpointApplied!==true||last.metadata?.pending===true)return null;
    if(prior?.role!=='user'||prior.metadata?.error?.errorCategory!=='provider_timeout'||prior.metadata?.pending===true)return null;
    if(live.messages.length!==initial.length+1||initial.some((v,i)=>v.id!==live.messages[i]?.id||v.id===last.id))return null;
    return {c,nodeId:last.id,sessionId:m[2],evidence:m[0]};
  };
  window[key]={
    prepare(evidence) {
      const v=fault(evidence);if(!v)return {eligible:false};
      const {live,initial}=v.c;
      const token=crypto.randomUUID(), snapshot=JSON.stringify(live.messages), initialSnapshot=JSON.stringify(initial), savedDraft=draft();
      state.plan={token,nodeId:v.nodeId,evidence:v.evidence,url:location.href,snapshot,initialSnapshot,draft:savedDraft};
      return {eligible:true,token,nodeId:v.nodeId,url:location.href,backup:{capturedAt:new Date().toISOString(),url:location.href,evidence:v.evidence,messages:live.messages,initialMessages:initial,draft:savedDraft},text:live.messages.at(-1).parts?.filter(p=>p.type==='text').map(p=>p.text).join('\n')||''};
    },
    apply(token) {
      const p=state.plan;if(!p||p.token!==token||p.url!==location.href)return {applied:false,reason:'页面已改变'};
      const v=fault(p.evidence);
      if(!v||JSON.stringify(v.c.live.messages)!==p.snapshot||JSON.stringify(v.c.initial)!==p.initialSnapshot||draft()!==p.draft)return {applied:false,reason:'消息或草稿已改变'};
      // Remove only the confirmed orphan; preserve all other live metadata and the draft controller.
      v.c.live.setMessages(v.c.live.messages.slice(0,-1));v.c.live.clearError();
      return {applied:true,nodeId:p.nodeId};
    },
    verify(token) {
      const p=state.plan,c=context();
      return {repaired:!!p&&p.token===token&&p.url===location.href&&!!c&&!c.live.messages.some(m=>m.id===p.nodeId)};
    }
  };
})();
