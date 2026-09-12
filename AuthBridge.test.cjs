const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const source = fs.readFileSync(__dirname + '/AuthBridge.js', 'utf8');

function setup({stage = 'create', ready = true} = {}) {
  const calls = [];
  class Input {
    constructor(type, name) { this.type = type; this.name = name; this.disabled = false; this._value = ''; }
    get value() { return this._value; }
    set value(value) { if (this.type === 'file') throw Error('file upload selected'); this._value = value; }
    getClientRects() { return [1]; }
    dispatchEvent(e) { calls.push(e.type); }
  }
  const element = text => ({textContent: text, getAttribute: () => '', getClientRects: () => [1], click() { calls.push(text); }});
  const name = new Input('text', 'name'), file = new Input('file', 'files');
  const password = new Input('password', 'password'), confirm = new Input('password', 'confirmPassword');
  const finish = element('Finish');
  const form = {getClientRects: () => [1], querySelectorAll: () => [finish], addEventListener: (_, handler) => { form.guard = handler; }};
  if (ready) form.__reactPropsTest = {};
  const inputs = stage === 'create' ? [file, name] : [password, confirm];
  const headings = [element(stage === 'create' ? 'Create Account' : 'Create password')];
  const document = {
    querySelector: selector => selector === 'form' ? form : null,
    querySelectorAll: selector => {
      if (selector === 'input') return inputs;
      if (selector === 'h1,h2,h3') return headings;
      if (selector === 'button') return [finish];
      return [];
    },
  };
  const context = {document, location: {host: 'arena.ai', origin: 'https://arena.ai', pathname: '/agent'},
    window: {}, HTMLInputElement: Input, Event: class {constructor(type) {this.type = type;}},
    getComputedStyle: () => ({visibility: 'visible'}), URL};
  vm.runInNewContext(source, context);
  return {api: context.window.__arenaAuth, name, file, password, confirm, form, calls};
}

test('registration fills the text field without touching the upload input behind the modal', () => {
  const s = setup(); s.api.act('name', {name: 'Kai'});
  assert.equal(s.name.value, 'Kai'); assert.equal(s.file.value, '');
});
test('unhydrated password form cannot be filled or submitted', () => {
  const s = setup({stage: 'password', ready: false});
  assert.equal(s.api.read().stage, 'loading');
  assert.throws(() => s.api.act('submitPassword', {}));
  assert.equal(s.calls.length, 0);
});
test('ready password form fills both fields and prevents native GET fallback', () => {
  const s = setup({stage: 'password'});
  s.api.act('password', {password: 'Test-only!'});
  assert.equal(s.password.value, 'Test-only!'); assert.equal(s.confirm.value, 'Test-only!');
  s.api.act('submitPassword', {});
  let prevented = false; s.form.guard({preventDefault() {prevented = true;}});
  assert.equal(prevented, true); assert.equal(s.calls.filter(c => c === 'Finish').length, 1);
});

test('mailbox change uses Change and its scoped confirmation, not inbox Refresh', () => {
  const calls=[];const button=text=>({textContent:text,disabled:false,getAttribute:()=>'',getClientRects:()=>[1],click(){calls.push(text)}});
  const change=button('更改'),refresh=button('刷新'),confirm=button('确定');
  const dialog={textContent:'更换邮箱地址 确定',getClientRects:()=>[1],querySelectorAll:()=>[confirm]};
  const document={querySelector:selector=>selector.includes('Email Address')?{value:'old@example.invalid'}:null,querySelectorAll:selector=>selector==='button'?[change,refresh,confirm]:selector==='[role="dialog"],[role="alertdialog"]'?[dialog]:[]};
  const context={document,window:{},location:{host:'10minutemail.one',origin:'https://10minutemail.one',pathname:'/zh'},getComputedStyle:()=>({visibility:'visible'}),URL};
  vm.runInNewContext(source,context);const api=context.window.__arenaAuth;
  assert.equal(api.read().canConfirmMailboxChange,true);api.act('refreshMail',{});api.act('confirmRefreshMail',{});assert.deepEqual(calls,['更改','确定']);
});

test('mailbox confirmation also works in the actual unlabelled modal structure', () => {
  const calls=[];const element=text=>({textContent:text,disabled:false,getAttribute:()=>'',getClientRects:()=>[1],click(){calls.push(text)}});
  const confirm=element('确定'),outside=element('确定'),cancel=element('取消'),heading=element('更换邮箱地址');
  const panel={textContent:'更换邮箱地址 确定要更换邮箱吗？ 取消 确定',outerHTML:'<div>mail change panel</div>',getClientRects:()=>[1],querySelectorAll:()=>[cancel,confirm]};heading.parentElement={parentElement:panel};
  const document={querySelector:()=>null,querySelectorAll:selector=>selector==='h1,h2,h3'?[heading]:selector==='button'?[outside,cancel,confirm]:[]};
  const context={document,window:{},location:{host:'10minutemail.one',origin:'https://10minutemail.one',pathname:'/zh'},getComputedStyle:()=>({visibility:'visible'}),URL};
  vm.runInNewContext(source,context);const api=context.window.__arenaAuth;assert.equal(api.read().canConfirmMailboxChange,true);api.act('confirmRefreshMail',{});assert.deepEqual(calls,['确定']);
});
