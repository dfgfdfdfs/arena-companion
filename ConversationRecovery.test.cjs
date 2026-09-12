const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const source=fs.readFileSync(__dirname+'/ConversationRecovery.js','utf8');
const session='01a09075-6027-7520-a0c8-d64be1b603f5', node='01a09096-19bc-709f-aaf3-8fdd293eda81';
function fixture(){
 const initial=[{id:'user-1',role:'user',metadata:{error:{errorCategory:'provider_timeout'}}}];
 const ghost={id:node,role:'assistant',metadata:{nodeId:node,checkpointApplied:true,pending:false},parts:[{type:'text',text:'saved reply 🌱'}]};
 const live={id:session,status:'ready',messages:[...initial,ghost],agentFeedbackOperationLock:{activeOperation:null},setMessages(v){this.messages=v;this.calls=(this.calls||0)+1;},clearError(){this.cleared=true;}};
 const log={getClientRects:()=>[1],__reactFiber_fixture:{memoizedProps:{value:live},return:{memoizedProps:{initialMessages:initial}}}};
 const editor={innerText:'user draft',getClientRects:()=>[1]};
 const error=`Assistant node "${node}" not found in session "${session}"`;
 const context={window:{},crypto:require('node:crypto'),location:{origin:'https://arena.ai',pathname:'/agent/'+session,href:'https://arena.ai/agent/'+session},document:{body:{innerText:error},querySelectorAll:s=>s==='[role="log"]'?[log]:s==='[contenteditable="true"]'?[editor]:[]}};
 vm.runInNewContext(source,context);return {api:context.window.__arenaConversationRecovery,context,live,initial,ghost,editor,error};
}
test('backs up exact history and removes only confirmed orphan, preserving draft and prior metadata',()=>{const f=fixture(),p=f.api.prepare('');assert.equal(p.eligible,true);assert.equal(p.backup.messages.length,2);assert.equal(p.backup.initialMessages.length,1);assert.equal(p.text,'saved reply 🌱');assert.equal(f.api.apply(p.token).applied,true);assert.equal(f.live.messages[0],f.initial[0]);assert.equal(f.editor.innerText,'user draft');assert.equal(f.api.verify(p.token).repaired,true);assert.equal(f.api.prepare('').eligible,false);});
for(const [name,change] of [
 ['no matching error',f=>f.context.document.body.innerText=''],
 ['different session',f=>f.live.id='other'],
 ['active generation',f=>f.live.status='streaming'],
 ['pending submission',f=>f.live.status='submitted'],
 ['another active error',f=>f.live.error={message:'rate limit'}],
 ['verification dialog',f=>{const query=f.context.document.querySelectorAll;f.context.document.querySelectorAll=s=>s==='[role="dialog"]'?[{getClientRects:()=>[1],innerText:'Security Verification'}]:query(s);}],
 ['feedback request still running',f=>f.live.agentFeedbackOperationLock.activeOperation='feedback'],
 ['normal non-checkpoint answer',f=>f.ghost.metadata.checkpointApplied=false],
 ['pending checkpoint',f=>f.ghost.metadata.pending=true],
 ['server failure other than provider timeout',f=>f.initial[0].metadata.error.errorCategory='rate_limit'],
 ['newer messages would be lost',f=>f.live.messages.splice(1,0,{id:'new-user',role:'user'})],
 ['reply exists in server history',f=>f.initial.push(f.ghost)],
 ['unknown website',f=>f.context.location.origin='https://other.invalid']
])test('does not repair '+name,()=>{const f=fixture();change(f);assert.equal(f.api.prepare('').eligible,false);assert.equal(f.live.calls,undefined);});
for(const [name,change] of [
 ['draft changed',f=>f.editor.innerText='new draft'],
 ['reply changed',f=>f.ghost.parts[0].text+=' changed'],
 ['new message arrived',f=>f.live.messages.push({id:'new'})],
 ['navigation',f=>f.context.location.href+='/other'],
 ['server snapshot changed',f=>f.initial[0].metadata.changed=true]
])test('cancels after backup when '+name,()=>{const f=fixture(),p=f.api.prepare('');change(f);assert.equal(f.api.apply(p.token).applied,false);assert.equal(f.live.calls,undefined);});
test('network evidence can detect a dismissed toast without relaxing state checks',()=>{const f=fixture();f.context.document.body.innerText='';assert.equal(f.api.prepare(f.error).eligible,true);assert.equal(f.api.apply('wrong token').applied,false);});
test('captured original incident has the same guarded repair result',{skip:!process.env.ARENA_RECOVERY_EVIDENCE},()=>{
 const evidence=JSON.parse(fs.readFileSync(process.env.ARENA_RECOVERY_EVIDENCE,'utf8'));
 const f=fixture();f.initial.splice(0,f.initial.length,...evidence.initialMessages);f.live.messages=evidence.messages;
 const p=f.api.prepare(f.error);assert.equal(p.eligible,true);assert.equal(p.backup.messages.length,10);assert.equal(p.backup.initialMessages.length,9);
 assert.equal(f.api.apply(p.token).applied,true);assert.equal(f.live.messages.length,9);assert.equal(f.api.verify(p.token).repaired,true);
});
