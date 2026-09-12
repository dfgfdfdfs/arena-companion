(()=>{
 const visible=e=>!!e&&e.getClientRects().length>0&&getComputedStyle(e).visibility!=='hidden';
 const label=e=>(e.getAttribute('aria-label')||e.textContent||'').trim();
 const all=(q,root=document)=>[...root.querySelectorAll(q)].filter(visible);
 const button=name=>all('button').filter(e=>label(e)===name);
 const isHtml=name=>/\.html?$/i.test(name);
 const previewName=()=>all('iframe[title="HTML preview"],iframe[title="File preview"]').length>0&&button('Switch file').length===1&&button('Download file').length===1?button('Switch file')[0].textContent.trim():'';
 const openFiles=()=>all('[role=log] button').filter(e=>label(e)==='Open file').flatMap(b=>{
  const row=b.closest('[role=button]')||b.parentElement;
  const names=[...new Set(all('span,code',row).filter(e=>!e.children.length).map(e=>e.textContent.trim()).filter(isHtml))];
  return names.length===1?[{name:names[0],button:b}]:[];
 });
 window.__arenaCandidate=(action,value)=>{try{
  if(action==='inspect')return {
   url:location.origin+location.pathname,
   frames:all('iframe').map(e=>({title:e.title,origin:e.src?new URL(e.src).origin:'',srcdocLength:e.srcdoc.length,rect:e.getBoundingClientRect().toJSON()})),
   files:all('button').filter(e=>/\.html?$/i.test(label(e))).map(e=>({label:label(e),html:e.outerHTML.slice(0,1200)})),
   htmlNodes:all('main *').filter(e=>!e.children.length&&/\.html?$/i.test(e.textContent.trim())).map(e=>({text:e.textContent,ancestors:[e.parentElement,e.parentElement?.parentElement].filter(Boolean).map(n=>({tag:n.tagName,role:n.getAttribute('role'),html:n.outerHTML.slice(0,2200)}))})),
   currentLinks:all('a[href]').filter(e=>new URL(e.href).pathname===location.pathname).map(e=>({text:label(e),parent:e.parentElement.outerHTML.slice(0,3500)})),
   inputs:all('input').map(e=>({type:e.type,typeAttribute:e.getAttribute('type'),placeholder:e.placeholder,value:e.type==='password'?'':e.value})),
   menus:all('[role=menu],[role=dialog]').map(e=>e.innerText),
   buttons:all('button,[role=menuitem]').map(label),
   artifactButtons:all('[role=log] button').filter(e=>/HTML|Open file/i.test(label(e))).map(e=>e.outerHTML.slice(0,2400)),
   code:all('pre,code,.cm-content,.view-lines,textarea,[role=tabpanel]').map(e=>({tag:e.tagName,cls:e.className,length:e.textContent.length,start:e.textContent.slice(0,200)}))
  };
  if(action==='button'){
   if(!['Raw source','Preview','Download file','Expand panel','Rename'].includes(value))throw Error('不支持的候选操作');
   const b=value==='Rename'?all('[role=menuitem]').filter(e=>label(e)==='Rename'):button(value);if(b.length!==1)throw Error('候选按钮不唯一或尚未出现：'+value);b[0].click();return {ok:true};
  }
  if(action==='file'){
   if(isHtml(value)&&previewName()===value)return {ok:true};
   // The Write tool and reply link can both expose the same file. Prefer the named tool entry.
   const entries=openFiles().filter(e=>e.name===value).map(e=>e.button);
   const b=[...new Set(entries.length?entries:button(value))];
   if(!isHtml(value)||b.length!==1)throw Error('未找到唯一匹配的 HTML 文件：'+value);b[0].click();return {ok:true};
  }
  if(action==='openHtmlArtifact'){
   const b=[...new Set([...all('[role=log] button').filter(e=>/^(查看\s*HTML\s*文件|View HTML file)$/i.test(label(e))),...openFiles().map(e=>e.button)])];
   if(b.length!==1)throw Error('这条回答尚未找到唯一的 HTML 文件入口');b[0].click();return {ok:true};
  }
  if(action==='renameFill'||action==='renameSave'||action==='renameCancel'){
   const ds=all('[role=dialog]').filter(e=>/^Rename chat\b/.test(e.innerText));
   if(ds.length!==1)throw Error('当前没有唯一的重命名对话框');const d=ds[0];
   if(action==='renameFill'){
    if(!value||value.length>100)throw Error('名称长度错误');const inputs=all('input',d).filter(e=>e.type==='text');
    if(inputs.length!==1)throw Error('名称输入框不唯一');
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(inputs[0],value);
    inputs[0].dispatchEvent(new Event('input',{bubbles:true}));return {ok:true};
   }
   const b=all('button',d).filter(e=>label(e)===(action==='renameSave'?'Rename':'Cancel'));
   if(b.length!==1||b[0].disabled)throw Error('重命名按钮不可用');b[0].click();return {ok:true};
  }
  if(action==='state')return {
   title:all('a[href]').filter(e=>new URL(e.href).pathname===location.pathname).map(label)[0]||'',
   renameDialog:all('[role=dialog]').some(e=>/^Rename chat\b/.test(e.innerText)),
   renameMenu:all('[role=menuitem]').some(e=>label(e)==='Rename'),
   files:[...new Set([...all('[role=log] button').map(label),...openFiles().map(e=>e.name),previewName()].filter(isHtml))],
   previewCount:all('iframe[title="File preview"]').length
  };
  if(action==='renameMenu'){
   const links=all('a[href]').filter(e=>new URL(e.href).pathname===location.pathname);
   if(links.length!==1)throw Error('当前对话在侧栏中不唯一');
   links[0].scrollIntoView({block:'nearest'});
   const b=all('button',links[0].parentElement).filter(e=>label(e)==='More options');
   if(b.length!==1)throw Error('未找到当前对话菜单');b[0].dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerType:'mouse',ctrlKey:false}));return {ok:true};
  }
  throw Error('未知候选操作');
 }catch(e){return {error:e.message};}};
})();
