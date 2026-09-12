(() => {
  const visible = el => !!el && !!el.getClientRects().length && getComputedStyle(el).visibility !== 'hidden';
  const label = el => (el.getAttribute('aria-label') || el.textContent || '').trim().replace(/\s+/g, ' ');
  const buttons = scope => [...scope.querySelectorAll('button')].filter(visible);
  const find = (name, scope = document) => buttons(scope).find(e => label(e) === name);
  const sidebarOpener = () => find('Expand sidebar') || find('Open sidebar');
  const dialogs = () => [...document.querySelectorAll('[role="dialog"]')].filter(visible);
  const termsDialog = () => {const matches=dialogs().filter(e=>/Terms of Use & Privacy Policy/.test(e.innerText));return matches.length===1?matches[0]:null;};
  const termsButton = () => {const d=termsDialog();const matches=d?buttons(d).filter(e=>label(e)==='Agree'&&!e.disabled):[];return matches.length===1?matches[0]:null;};
  const input = () => [...document.querySelectorAll('main div[contenteditable="true"]')].find(visible);
  const stagedNames = main => main ? buttons(main).filter(e=>!e.closest('[role="log"]')).map(label).filter(t=>t.startsWith('Remove ')).map(t=>t.slice(7)) : [];
  const writeDraft = value => {
    const el=input();if(!el)throw new Error('输入框尚未就绪');el.focus();
    const range=document.createRange();range.selectNodeContents(el);
    const selection=window.getSelection();selection.removeAllRanges();selection.addRange(range);
    if(!document.execCommand('insertText',false,value))throw new Error('未能填入提示词');
  };
  const view = prompt => {
    const main = [...document.querySelectorAll('main')].find(visible);
    const log = main && [...main.querySelectorAll('[role="log"]')].find(visible);
    const text = log?.innerText.trim() || '';
    const dialog = dialogs()[0];
    const challenge = dialogs().some(e=>/Security Verification|人机身份验证/.test(e.innerText));
    const alerts = [...document.querySelectorAll('[role="alert"]')].filter(visible).map(e => e.innerText).join('\n');
    const response = text.replace(prompt, '').trim();
    const blocked = challenge ? '需要人机验证' :
      find('Log In') || (dialog && /Log In to your account|Log In or Create Account/.test(dialog.innerText)) ? '请先登录 Arena' :
      /too many requests|rate limit|try again later|quota exceeded|limit reached/i.test(alerts) ? '网站限流，请稍后继续' :
      termsDialog() ? '正在处理网站首次使用条款' : '';
    return {
      url: location.href, main: !!main, conversation: !!text, promptConfirmed: !!prompt && text.includes(prompt),
      thinking: !!log && buttons(log).some(e => /^(Thinking\b|Thought\b|思考|已思考)/i.test(label(e))),
      generating: !!main && !!find('Stop generating', main),
      failed: /(?:^|\n)(?:Stopped|Generation stopped|Error|Something went wrong)(?:\n|$)/i.test(response),
      response: response.length > 20 && !/^(finding|waiting|initializ|starting)/i.test(response),
      responseSignature: response.length + ':' + Array.from(response).slice(-160).join(''),
      draft: input()?.innerText.trim() || '', editor: !!input(), blocker: blocked,
      termsPending: !!termsButton() && blocked==='正在处理网站首次使用条款',
      sendReady: !!main && !!find('Send message', main) && !find('Send message', main).disabled,
      newLinks: [...document.querySelectorAll('a[href="/agent"]')].filter(e => visible(e) && label(e) === 'New Chat').length,
      canExpand: !!sidebarOpener(),
      attachmentNames: stagedNames(main),
      conversationAttachments: log ? [...log.querySelectorAll('img')].filter(visible).map(e=>e.alt).filter(Boolean) : [],
    };
  };
  window.__arenaCompanion = {
    read: view,
    attachmentsReady: names => {
      if(!names.length)return true;
      const main=[...document.querySelectorAll('main')].find(visible);
      if(!main)return false;
      const attached=stagedNames(main);
      const busy=[...main.querySelectorAll('[role="progressbar"],.animate-spin')].some(visible);
      return !busy&&attached.length===names.length&&names.every(name=>attached.includes(name));
    },
    replaceDraft: (expected,replacement) => {
      const v=view(replacement);
      if(!v.main||v.conversation||v.generating||!v.editor||v.draft!==expected)throw new Error('草稿与预期不同，已保留');
      writeDraft(replacement);return {ok:true};
    },
    action: (name, prompt) => {
      const v = view(prompt);
      if(name==='terms'||name==='dismissTerms') {
        if(name==='terms'&&!v.termsPending)throw new Error('当前不能确认使用条款');
        const dialog=termsDialog();
        const b=name==='terms'?termsButton():dialog&&find('Close',dialog);
        if(!b)throw new Error('当前没有待处理的使用条款窗口');b.click();return {ok:true};
      }
      const rateLimitRetry=name==='retryFill'||name==='retrySend';
      if(rateLimitRetry&&termsDialog())throw new Error('使用条款尚未确认');
      if (v.blocker&&!(rateLimitRetry&&v.blocker==='网站限流，请稍后继续')) throw new Error(v.blocker);
      if(rateLimitRetry)name=name==='retryFill'?'fill':'send';
      if (!v.main) throw new Error('页面尚未就绪');
      if (name === 'expand') {
        const b = sidebarOpener(); if (b) b.click();
      } else if (name === 'stop') {
        const b = find('Stop generating', document.querySelector('main'));
        if (b) b.click();
      } else if (name === 'new') {
        if (v.generating) throw new Error('请先停止当前生成');
        const links = [...document.querySelectorAll('a[href="/agent"]')].filter(e => visible(e) && label(e) === 'New Chat');
        if (links.length !== 1) throw new Error('请展开左侧栏，显示 New Chat 按钮');
        links[0].click();
      } else if (name === 'fill') {
        if (v.conversation || v.generating || !v.editor) throw new Error('当前不是可填写的新对话');
        if (v.draft && v.draft !== prompt) throw new Error('发现不同草稿，已保留，请自行处理');
        if (!v.draft) {
          writeDraft(prompt);
        }
      } else if (name === 'send') {
        if(!window.__arenaCompanion.attachmentsReady(window.__arenaRequiredAttachments||[]))throw new Error('发送瞬间附件未确认，已暂停');
        if (v.conversation || v.generating || v.draft !== prompt || !v.sendReady) throw new Error('发送前页面状态改变，已暂停');
        find('Send message', document.querySelector('main')).click();
      } else throw new Error('未知操作');
      return {ok:true};
    },
  };
})();
