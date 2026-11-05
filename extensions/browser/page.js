import { isAllowed } from './rules.mjs';
import { applyTheme, palettes } from './theme.mjs';
import { formatTime } from './focus.mjs';
import { pageLabels, translator } from './language.mjs';

const api = globalThis.browser ?? globalThis.chrome;
const byId = id => document.getElementById(id);
const blocked = document.body.classList.contains('blocked');
const workspace = !blocked && new URLSearchParams(location.search).get('view') === 'page';
const originalUrl = blocked ? location.hash.slice(1) : '';
let latest;
let language;
let t = translator('zh-CN');
const updateLabels = pageLabels(document);
let originalDomain = '';
let websiteSelection = '';
let preferenceSelection = '';
let paneName='focus';
let historyPageIndex=0;
let historyVersion='';
let conflictSelection='';
let projectSelection='';
let resendUntil=0;
let expiredSelection=false;

function showPane(name) {
  paneName=name;
  for (const pane of ['focus', 'rules', 'preferences', 'history']) byId(pane + 'Pane').hidden = pane !== name;
  for (const button of document.querySelectorAll('[data-pane]')) {
    if (button.dataset.pane === name) button.setAttribute('aria-current', 'page');
    else button.removeAttribute('aria-current');
  }
  window.scrollTo(0,0);
  if(name==='history') loadHistory();
}
async function loadHistory() {
  const result=await api.runtime.sendMessage({type:'getHistory',date:byId('historyDate').value,page:historyPageIndex});
  if(result.error) {byId('error').textContent=result.error;return;}
  byId('historySummary').textContent=t('{0} 次专注 · 合计 {1}', result.total, formatTime(result.totalMs));
  byId('historyEmpty').hidden=result.total>0;
  byId('historyPrevious').disabled=historyPageIndex===0;byId('historyNext').disabled=(historyPageIndex+1)*50>=result.total;
  byId('historyList').replaceChildren(...result.records.map(record=>{
    const row=document.createElement('section');row.className='historyCard';
    const title=document.createElement('h3');title.textContent=record.project;
    const duration=document.createElement('strong');duration.textContent=formatTime(record.focusMs);
    const timing=document.createElement('p');timing.className='muted hint';timing.textContent=new Date(record.startedAt).toLocaleString()+' — '+new Date(record.endedAt).toLocaleTimeString();
    const mode=document.createElement('p');mode.className='muted hint';mode.textContent=({CountUp:'正计时',Countdown:'倒计时',Pomodoro:'番茄循环'}[record.mode]||'专注')+' · '+({Manual:'手动结束',CountdownElapsed:'到点结束',DurationLimit:'24小时上限',ApplicationExit:'退出软件',Interrupted:'中断后保存'}[record.endReason]||'已完成');
    row.append(duration,title,timing,mode);return row;
  }));
}

function updateSetup() {
  const countdown = byId('timerMode').value === 'Countdown';
  byId('durationLabel').hidden = !countdown;
  byId('quickSetup').textContent = countdown ? t('倒计时 · {0} 分钟 · 调整', byId('durationMinutes').value) : '正计时 · 调整';
  if (latest?.state.canConfigure) byId('project').textContent = byId('projectName').value.trim() || latest.state.project;
}

if (blocked) byId('blockedAddress').textContent = originalUrl;
if (workspace) { document.body.className = 'workspace'; byId('open').hidden = true; byId('fullscreen').hidden = false; }
function updateLanguage(next) {
  if (language === next) return;
  language = next;
  t = translator(next);
  document.documentElement.lang = next;
  updateLabels(t);
  if (blocked) return;
  byId('palette').replaceChildren(...[...palettes, { id:'custom', name:'自定义配色' }].map(palette => {
    const option = document.createElement('option');
    option.value = palette.id;
    option.textContent = t(palette.name);
    return option;
  }));
  websiteSelection = historyVersion = conflictSelection = projectSelection = '';
}

async function message(payload) {
  try {
    latest = await api.runtime.sendMessage(payload);
    render(latest);
    return latest;
  } catch (error) { byId('error').textContent = error.message; return { error:error.message }; }
}

function render(view) {
  if (!view?.state) return;
  updateLanguage(view.preferences.language || 'zh-CN');
  const state = view.state;
  applyTheme(view.preferences);
  byId('project').textContent = state.project;
  byId('project').title = state.project;
  byId('timer').textContent = state.timer;
  byId('state').textContent = state.stateText;
  byId('phase').textContent = state.phaseText;
  byId('error').textContent = t(view.error || '');
  byId('end').hidden = !['Focusing', 'Paused'].includes(state.state);
  if (blocked) {
    const websites = state.sessionWebsites;
    byId('blockReason').textContent = state.websiteMode === 'Blacklist' ? t('此网站在本次黑名单中') : t('此网站不在本次白名单中');
    byId('allowedTitle').hidden = state.websiteMode === 'Blacklist';
    const selection = JSON.stringify([websites, state.websiteMode]);
    if (selection !== websiteSelection) {
      websiteSelection = selection;
      byId('allowedLinks').replaceChildren(...(state.websiteMode === 'Blacklist' ? [] : websites).map(rule => {
        const link = document.createElement('a'); link.textContent = rule.domain; link.href = 'https://' + rule.domain; return link;
      }));
    }
    if (!view.effective || isAllowed(originalUrl, true, websites, state.websiteMode)) {
      if (/^https?:\/\//i.test(originalUrl)) location.replace(originalUrl);
    }
    return;
  }
  const account=view.account||{};
  byId('loginExpiredBanner').hidden=!account.needsLogin;
  if(account.needsLogin&&!expiredSelection) {byId('accountLogin').open=true;byId('accountEmailInput').value=account.email;byId('mergeGuest').checked=false;}
  expiredSelection=Boolean(account.needsLogin);byId('mergeGuest').closest('label').hidden=expiredSelection;
  const displayName=account.signedIn?(account.name||account.email.split('@')[0]):'未登录';
  byId('accountName').textContent=displayName;byId('avatarInitial').textContent=displayName.slice(0,1).toUpperCase();
  const avatar=byId('accountAvatar');if(account.picture) {if(avatar.getAttribute('src')!==account.picture) avatar.src=account.picture;} else avatar.removeAttribute('src');avatar.hidden=!account.picture;
  byId('accountEmail').textContent=account.signedIn?account.email:account.needsLogin?t('登录已失效'):t('访客 · 本机使用');
  byId('syncStatus').textContent=account.configured?t(account.status):t('账号服务尚未配置，本机功能可正常使用。');
  byId('syncPending').textContent=account.signedIn?t('待同步 {0} 项', account.pending || 0):'';
  byId('syncLast').textContent=account.lastSync?t('上次同步：{0}', new Date(account.lastSync).toLocaleString(language)):'';
  byId('syncNow').disabled=!account.signedIn||account.busy;byId('signOut').disabled=account.busy;byId('accountLogout').hidden=!account.signedIn;byId('accountLogin').hidden=Boolean(account.signedIn);
  byId('sendCode').disabled=account.busy||!account.configured||Date.now()<resendUntil;
  byId('accountForm').querySelector('button[type=submit]').disabled=account.busy||!account.configured;
  byId('sendCode').textContent=Date.now()<resendUntil?`${Math.ceil((resendUntil-Date.now())/1000)}秒后重发`:t('获取验证码');
  const visibleConflicts=account.signedIn?(account.conflicts||[]):[];const conflicts=JSON.stringify(visibleConflicts);
  if(conflicts!==conflictSelection) {
    conflictSelection=conflicts;
    function describe(value) {return typeof value==='string'?value:value.name+' · '+(value.deleted?t('已删除'):value.category);}
    byId('syncConflicts').replaceChildren(...visibleConflicts.map(conflict=>{
      const row=document.createElement('div');row.className='conflictCard';const title=document.createElement('h3');title.textContent=conflict.kind==='project'?'项目修改冲突':'通用设置修改冲突';
      const local=document.createElement('p');local.textContent='本机：'+describe(conflict.local);const remote=document.createElement('p');remote.textContent='云端：'+describe(conflict.remote);
      const actions=document.createElement('div');actions.className='actions';
      for(const [label,keepLocal] of [[t('保留本机'),true],[t('采用云端'),false]]) {const button=document.createElement('button');button.textContent=t(label);button.disabled=account.busy;button.onclick=()=>message({type:'resolveSyncConflict',fields:{key:conflict.key,keepLocal}});actions.append(button);}
      row.append(title,local,remote,actions);return row;
    }));
  }
  const recordsVersion=JSON.stringify([state.state,account.lastSync,account.email]);
  if(paneName==='history'&&historyVersion!==recordsVersion) {historyVersion=recordsVersion;loadHistory();}
  byId('enabled').checked = view.enabled;
  byId('control').textContent = !view.enabled ? t('网站限制已关闭') : view.effective
    ? t('网站限制生效 · {0}', t(state.websiteMode === 'Blacklist' ? '黑名单' : '白名单')) : '当前不限网站';
  for (const [id, visible] of [['start', state.canConfigure], ['pause', state.state === 'Focusing'], ['resume', state.state === 'Paused'], ['skipRest', state.state === 'Resting']]) byId(id).hidden = !visible;
  for (const control of byId('timerForm').elements) control.disabled = !state.canConfigure;
  byId('backToFocus').disabled = false;
  byId('quickSetup').hidden = !state.canConfigure;
  byId('preferenceNotice').textContent = state.canConfigure ? '' : '当前计时安排保持固定，结束后可调整。';
  const timerKeys = ['timerMode', 'durationMinutes', 'pauseMinutes', 'restMinutes', 'project', 'projectId'];
  const preferences = JSON.stringify(timerKeys.map(key => view.preferences[key]));
  byId('palette').value = view.preferences.palette;
  byId('appearance').value = view.preferences.appearance;
  byId('language').value = language;
  byId('customColor').value = view.preferences.customColor || '#487a8b';
  byId('timerScale').value = view.preferences.timerScale || 100;
  const projectOptions=JSON.stringify(view.projects||[]);
  if(projectOptions!==projectSelection) {
    projectSelection=projectOptions;
    const empty=document.createElement('option');empty.value='';empty.textContent='新建或输入专注名称';
    byId('focusProject').replaceChildren(empty,...(view.projects||[]).map(project=>{const option=document.createElement('option');option.value=project.id;option.textContent=project.category+' / '+project.name;return option;}));
    byId('focusProject').value=view.preferences.projectId||'';
  }
  if (preferences !== preferenceSelection) {
    preferenceSelection = preferences;
    for (const key of timerKeys.slice(0, 4)) byId(key).value = view.preferences[key];
    byId('projectName').value = view.preferences.project;
    byId('focusProject').value=view.preferences.projectId||'';
  }
  updateSetup();
  byId('listMode').disabled = !state.canEditRules;
  byId('listMode').value = state.savedWebsiteMode;
  byId('editHint').textContent = state.canEditRules ? t('黑白列表分别保存在此浏览器中。') : t('本次使用开始时的规则，结束后可以修改。');
  for (const control of byId('websiteForm').elements) control.disabled = !state.canEditRules;
  byId('addCurrent').disabled = !state.canEditRules;
  const selection = JSON.stringify([state.websites, state.savedWebsiteMode, state.canEditRules]);
  if (selection !== websiteSelection) {
    websiteSelection = selection;
    byId('websiteCount').textContent = t('{0} 个网站', state.websites.length);
    byId('emptyWebsites').hidden = state.websites.length > 0;
    byId('emptyWebsites').textContent = state.savedWebsiteMode === 'Whitelist'
      ? t('尚未添加网站。开启限制后，普通网站均不可访问。') : t('尚未添加网站，当前黑名单不会限制任何网站。');
    byId('websites').replaceChildren(...state.websites.map(rule => {
      const row = document.createElement('div'); row.className = 'website';
      const title = document.createElement('span'); title.textContent = rule.domain + (rule.includeSubdomains ? ' · 含子域名' : '');
      const actions = document.createElement('div'); actions.className = 'actions';
      const edit = document.createElement('button'); edit.textContent = '编辑'; edit.className = 'quiet'; edit.disabled = !state.canEditRules;
      edit.onclick = () => { originalDomain = rule.domain; byId('domain').value = rule.domain; byId('subdomains').checked = rule.includeSubdomains; };
      const remove = document.createElement('button'); remove.textContent = '删除'; remove.className = 'quiet'; remove.disabled = !state.canEditRules;
      remove.onclick = () => { if (window.confirm(t('删除网站“{0}”？', rule.domain))) message({ type:'deleteWebsite', fields:{ domain:rule.domain } }); };
      actions.append(edit, remove); row.append(title, actions); return row;
    }));
  }
}

byId('open').onclick = async () => {
  const url = api.runtime.getURL('popup.html?view=page');
  const [existing] = (await api.tabs.query({})).filter(tab => tab.url === url);
  if (existing) { await api.tabs.update(existing.id, { active:true }); await api.windows.update(existing.windowId, { focused:true }); }
  else await api.tabs.create({ url });
};
byId('end').onclick = () => message({ type:'end' });
if (!blocked) {
  byId('relogin').onclick=()=>{showPane('preferences');byId('accountLogin').open=true;byId('accountEmailInput').focus();};
  byId('loginMethod').onchange=()=>{const password=byId('loginMethod').value==='password';byId('verificationFields').hidden=password;byId('passwordFields').hidden=!password;byId('accountCode').required=!password;byId('accountPassword').required=password;};
  byId('historyDate').onchange=()=>{historyPageIndex=0;loadHistory();};
  byId('historyAll').onclick=()=>{byId('historyDate').value='';historyPageIndex=0;loadHistory();};
  byId('historyPrevious').onclick=()=>{historyPageIndex--;loadHistory();};
  byId('historyNext').onclick=()=>{historyPageIndex++;loadHistory();};
  byId('sendCode').onclick=async()=>{
    if(!byId('accountEmailInput').reportValidity())return;
    byId('sendCode').disabled=true;
    const result=await message({type:'sendCode',fields:{email:byId('accountEmailInput').value}});
    if(!result.error)resendUntil=Date.now()+60000;
  };
  byId('accountForm').onsubmit=async event=>{
    event.preventDefault();const password=byId('loginMethod').value==='password';const result=await message({type:password?'passwordSignIn':'signIn',fields:{email:byId('accountEmailInput').value,code:byId('accountCode').value,password:byId('accountPassword').value,mergeGuest:byId('mergeGuest').checked}});
    if(!result.error) {byId('accountCode').value='';byId('accountPassword').value='';byId('accountLogin').open=false;preferenceSelection='';}
  };
  byId('signOut').onclick=()=>message({type:'signOut'});
  byId('syncNow').onclick=()=>message({type:'syncNow'});
  for (const button of document.querySelectorAll('[data-pane]')) button.onclick = () => showPane(button.dataset.pane);
  byId('settings').onclick = byId('quickSetup').onclick = () => showPane('preferences');
  byId('backToFocus').onclick = () => showPane('focus');
  byId('focusProject').onchange=()=>{
    const project=latest.projects.find(project=>project.id===byId('focusProject').value);
    if(project)byId('projectName').value=project.name;updateSetup();
  };
  byId('projectName').addEventListener('input',()=>{byId('focusProject').value='';});
  const fields = () => ({ project:byId('projectName').value, projectId:byId('focusProject').value||null,timerMode:byId('timerMode').value,
    durationMinutes:byId('durationMinutes').value, pauseMinutes:byId('pauseMinutes').value, restMinutes:byId('restMinutes').value });
  byId('start').onclick = () => {
    if (!byId('timerForm').checkValidity()) { showPane('preferences'); byId('timerForm').reportValidity(); return; }
    message({ type:'start', fields:fields() });
  };
  byId('timerForm').onsubmit = async event => {
    event.preventDefault();
    const result = await message({ type:'savePreferences', fields:fields() });
    if (!result.error) byId('preferenceNotice').textContent = t('默认计时安排已保存。');
  };
  byId('timerForm').oninput = updateSetup;
  byId('timerMode').onchange = updateSetup;
  for (const action of ['pause', 'resume', 'skipRest']) byId(action).onclick = () => message({ type:action });
  byId('enabled').onchange = event => message({ type:'setEnabled', enabled:event.target.checked });
  byId('fullscreen').onclick = async () => {
    if (document.fullscreenElement) await document.exitFullscreen(); else await document.documentElement.requestFullscreen();
  };
  document.addEventListener('fullscreenchange', () => { byId('fullscreen').textContent = document.fullscreenElement ? t('退出全屏') : t('全屏'); });
  const setTheme = fields => message({ type:'setTheme', fields });
  byId('palette').onchange = event => setTheme({ palette:event.target.value, customColor:byId('customColor').value });
  byId('appearance').onchange = event => setTheme({ appearance:event.target.value });
  byId('language').onchange = event => setTheme({ language:event.target.value });
  byId('customColor').onchange = event => setTheme({ palette:'custom', customColor:event.target.value });
  byId('timerScale').onchange = event => setTheme({ timerScale:Number(event.target.value) });
  byId('listMode').onchange = async event => { originalDomain = ''; byId('websiteForm').reset(); await message({ type:'setWebsiteMode', fields:{ listMode:event.target.value } }); };
  byId('cancel').onclick = () => { originalDomain = ''; byId('websiteForm').reset(); };
  byId('websiteForm').onsubmit = async event => {
    event.preventDefault();
    const view = await message({ type:'upsertWebsite', fields:{ domain:byId('domain').value, includeSubdomains:byId('subdomains').checked, originalDomain } });
    if (!view.error) { originalDomain = ''; byId('websiteForm').reset(); }
  };
  byId('addCurrent').onclick = async () => {
    const [tab] = await api.tabs.query({ active:true, currentWindow:true });
    let address = tab?.url || '';
    if (address.startsWith(api.runtime.getURL('blocked.html') + '#')) address = address.slice(address.indexOf('#') + 1);
    try {
      const url = new URL(address); if (!['http:', 'https:'].includes(url.protocol)) throw new Error();
      await message({ type:'upsertWebsite', fields:{ domain:url.hostname, includeSubdomains:true } });
    } catch { byId('error').textContent = t('请在普通网站的标签页中打开扩展，再添加当前网站。'); }
  };
}
function showLimitNotice() {
  if (blocked || latest?.limitNotice == null || byId('limitNotice').open) return;
  const noticeAt = latest.limitNotice;
  const dialog = byId('limitNotice');
  dialog.addEventListener('close', () => message({type:'ackLimitNotice',noticeAt}), {once:true});
  dialog.showModal();
}
await message({ type:'getView' });
showLimitNotice();
window.addEventListener('focus', showLimitNotice);
const refresh = setInterval(() => message({ type:'getView' }), 1000);
window.addEventListener('pagehide', () => clearInterval(refresh));
api.storage.onChanged.addListener((changes, area) => { if (area === 'session' && changes.view) { latest = changes.view.newValue; render(latest); } });
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => { if (latest) applyTheme(latest.preferences); });
