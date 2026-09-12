const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const {parseHTML} = require('linkedom');
const source = fs.readFileSync(__dirname + '/PageBridge.js', 'utf8');
function setup(html) {
  const {window, document} = parseHTML('<html><body>' + html + '</body></html>');
  window.__arenaRequiredAttachments=[];
  window.HTMLElement.prototype.getClientRects = function() {return this.closest('[hidden]') ? [] : [1];};
  const context = {window,document,location:{href:'https://arena.ai/agent'},getComputedStyle:()=>({visibility:'visible'})};
  vm.runInNewContext(source, context);
  return {api:window.__arenaCompanion,document,window};
}
test('new page ignores the stopped conversation retained in a hidden activity', () => {
  const h=setup('<main><section hidden><div role="log">hello\nOld response\nStopped<button>Thinking...</button></div><div contenteditable="true">old draft</div></section><div contenteditable="true">hello</div><button aria-label="Send message"></button></main>');
  const v=h.api.read('hello');
  assert.equal(v.conversation,false);assert.equal(v.failed,false);assert.equal(v.thinking,false);
  assert.equal(v.draft,'hello');assert.equal(v.promptConfirmed,false);
});
test('visible conversation is selected after hidden cached logs', () => {
  const h=setup('<main><section hidden><div role="log">old prompt<button>Thinking...</button></div></section><div role="log">hello\nCurrent answer is generating.<button>Thought for 2 seconds</button></div><button aria-label="Stop generating"></button><div contenteditable="true"></div></main>');
  const v=h.api.read('hello');assert.equal(v.promptConfirmed,true);assert.equal(v.thinking,true);assert.equal(v.generating,true);
});
test('first-use terms are reported instead of being mistaken for a stalled send', () => {
  const h=setup('<main><div contenteditable="true">hello</div><button aria-label="Send message"></button></main><div role="dialog">Terms of Use &amp; Privacy Policy<button>Agree</button></div>');
  assert.match(h.api.read('hello').blocker,/使用条款/);
  assert.equal(h.api.read('hello').termsPending,true);
  assert.throws(()=>h.api.action('send','hello'),/使用条款/);
});

test('terms action clicks only the unique Agree inside the known dialog',()=>{
  const h=setup('<main></main><button id="unrelated">Agree</button><div role="dialog">Terms of Use &amp; Privacy Policy<button id="consent">Agree</button></div>');
  let clicked=0;h.document.querySelector('#consent').onclick=()=>{clicked++;h.document.querySelector('[role=dialog]').remove();};
  h.document.querySelector('#unrelated').onclick=()=>{throw Error('wrong Agree');};
  h.api.action('terms','hello');assert.equal(clicked,1);assert.equal(h.api.read('hello').termsPending,false);
  assert.throws(()=>h.api.action('terms','hello'),/使用条款/);assert.equal(clicked,1);
});
test('unknown consent, disabled and duplicate Agree never permit automatic consent',()=>{
  for(const html of ['<div role="dialog">Subscribe<button>Agree</button></div>','<div role="dialog">Terms of Use &amp; Privacy Policy<button disabled>Agree</button></div>','<div role="dialog">Terms of Use &amp; Privacy Policy<button>Agree</button><button>Agree</button></div>']) {
    const h=setup('<main></main>'+html);if(h.document.querySelector('[disabled]'))h.document.querySelector('[disabled]').disabled=true;
    assert.equal(h.api.read('hello').termsPending,false);assert.throws(()=>h.api.action('terms','hello'),/使用条款/);
  }
});
test('human verification takes priority even when the terms dialog appears first',()=>{
  const h=setup('<main></main><div role="dialog">Terms of Use &amp; Privacy Policy<button>Agree</button></div><div role="dialog">Security Verification</div>');
  assert.equal(h.api.read('hello').termsPending,false);assert.match(h.api.read('hello').blocker,/人机/);assert.throws(()=>h.api.action('terms','hello'),/使用条款/);
});
test('bound attachments require exact visible composer chips and no pending upload', () => {
  const h=setup('<main><div role="log"><button aria-label="Remove old.png"></button></div><button aria-label="Remove ref.png"></button><div contenteditable="true"></div></main>');
  assert.equal(h.api.attachmentsReady(['ref.png']),true);
  assert.equal(h.api.attachmentsReady(['missing.png']),false);
  h.document.querySelector('main').insertAdjacentHTML('beforeend','<span role="progressbar"></span>');
  assert.equal(h.api.attachmentsReady(['ref.png']),false);
});
test('unexpected extra attachment prevents silently changing the request', () => {
  const h=setup('<main><button aria-label="Remove ref.png"></button><button aria-label="Remove extra.png"></button></main>');
  assert.equal(h.api.attachmentsReady(['ref.png']),false);
  assert.equal(h.api.attachmentsReady(['ref.png','extra.png']),true);
});
test('the final send action checks attachments in the same DOM operation', () => {
  const h=setup('<main><div contenteditable="true">hello</div><button aria-label="Send message"></button></main>');
  h.window.__arenaRequiredAttachments=['ref.png'];
  assert.throws(()=>h.api.action('send','hello'),/附件未确认/);
});

test('reply signature never splits an emoji at the 160-unit boundary',()=>{
  const answer='🌱'+'x'.repeat(159);
  const h=setup('<main><div role="log">hello\n'+answer+'</div></main>');
  const signature=h.api.read('hello').responseSignature;
  assert.equal(signature.isWellFormed(),true);
  assert.ok(signature.includes('🌱'));
});
test('Open sidebar label is supported',()=>{
  const h=setup('<main></main><button aria-label="Open sidebar"></button>');
  assert.equal(h.api.read('hello').canExpand,true);
  let clicks=0;h.document.querySelector('button').onclick=()=>clicks++;
  h.api.action('expand','hello');assert.equal(clicks,1);
});
test('known rate-limit retry can submit through a retained rate-limit alert only',()=>{
  const main='<main><div contenteditable="true">hello</div><button aria-label="Send message"></button></main>';
  const alert='<div role="alert">Too many requests</div>';
  const h=setup(main+alert);let clicks=0;h.document.querySelector('button').onclick=()=>clicks++;
  assert.throws(()=>h.api.action('send','hello'),/限流/);
  h.api.action('retrySend','hello');assert.equal(clicks,1);
  for(const extra of ['<div role="dialog">Security Verification</div>','<button>Log In</button>','<div role="dialog">Terms of Use &amp; Privacy Policy<button>Agree</button></div>']) {
    const blocked=setup(main+alert+extra);assert.throws(()=>blocked.api.action('retrySend','hello'));
  }
});
test('rate-limit retry still rejects changed draft, active conversation and missing attachment',()=>{
  const h=setup('<main><div contenteditable="true">changed</div><button aria-label="Send message"></button></main><div role="alert">Rate limit</div>');
  assert.throws(()=>h.api.action('retrySend','hello'),/页面状态改变/);
  h.document.querySelector('[contenteditable]').textContent='hello';h.window.__arenaRequiredAttachments=['ref.png'];
  assert.throws(()=>h.api.action('retrySend','hello'),/附件未确认/);
  h.window.__arenaRequiredAttachments=[];h.document.querySelector('main').insertAdjacentHTML('beforeend','<div role="log">hello accepted response</div>');
  assert.throws(()=>h.api.action('retrySend','hello'),/页面状态改变/);
});
