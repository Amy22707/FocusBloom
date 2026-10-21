import { isAllowed } from './rules.mjs';
import { applyTheme, palettes } from './theme.mjs';

const byId = id => document.getElementById(id);
const blocked = document.body.classList.contains('blocked');
const workspace = !blocked && new URLSearchParams(location.search).get('view') === 'page';
const originalUrl = blocked ? location.hash.slice(1) : '';
let latest;
let originalDomain = '';
let websiteSelection = '';
let preferenceSelection = '';

function showPane(name) {
  for (const pane of ['focus', 'rules', 'preferences']) byId(pane + 'Pane').hidden = pane !== name;
  for (const button of document.querySelectorAll('[data-pane]')) {
    if (button.dataset.pane === name) button.setAttribute('aria-current', 'page');
    else button.removeAttribute('aria-current');
  }
  window.scrollTo(0,0);
}

function updateSetup() {
  const countdown = byId('timerMode').value === 'Countdown';
  byId('durationLabel').hidden = !countdown;
  byId('quickSetup').textContent = countdown ? `倒计时 · ${byId('durationMinutes').value} 分钟 · 调整` : '正计时 · 调整';
  if (latest?.state.canConfigure) byId('project').textContent = byId('projectName').value.trim() || latest.state.project;
}

if (blocked) byId('blockedAddress').textContent = originalUrl;
if (workspace) { document.body.className = 'workspace'; byId('open').hidden = true; byId('fullscreen').hidden = false; }
if (!blocked) byId('palette').replaceChildren(...palettes.map(palette => {
  const option = document.createElement('option'); option.value = palette.id; option.textContent = palette.name; return option;
}));

async function message(payload) {
  try {
    latest = await chrome.runtime.sendMessage(payload);
    render(latest);
    return latest;
  } catch (error) { byId('error').textContent = error.message; return { error:error.message }; }
}

function render(view) {
  if (!view?.state) return;
  const state = view.state;
  applyTheme(view.preferences);
  byId('project').textContent = state.project;
  byId('project').title = state.project;
  byId('timer').textContent = state.timer;
  byId('state').textContent = state.stateText;
  byId('phase').textContent = state.phaseText;
  byId('error').textContent = view.error || '';
  byId('end').hidden = !['Focusing', 'Paused'].includes(state.state);
  if (blocked) {
    const websites = state.sessionWebsites;
    byId('blockReason').textContent = state.websiteMode === 'Blacklist' ? '此网站在本次黑名单中' : '此网站不在本次白名单中';
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
  byId('enabled').checked = view.enabled;
  byId('control').textContent = !view.enabled ? '网站限制已关闭' : view.effective
    ? `网站限制生效 · ${state.websiteMode === 'Blacklist' ? '黑名单' : '白名单'}` : '当前不限网站';
  for (const [id, visible] of [['start', state.canConfigure], ['pause', state.state === 'Focusing'], ['resume', state.state === 'Paused'], ['skipRest', state.state === 'Resting']]) byId(id).hidden = !visible;
  for (const control of byId('timerForm').elements) control.disabled = !state.canConfigure;
  byId('backToFocus').disabled = false;
  byId('quickSetup').hidden = !state.canConfigure;
  byId('preferenceNotice').textContent = state.canConfigure ? '' : '当前计时安排保持固定，结束后可调整。';
  const preferences = JSON.stringify(view.preferences);
  if (preferences !== preferenceSelection) {
    preferenceSelection = preferences;
    for (const key of ['timerMode', 'durationMinutes', 'pauseMinutes', 'restMinutes', 'palette', 'appearance']) byId(key).value = view.preferences[key];
    byId('projectName').value = view.preferences.project;
  }
  updateSetup();
  byId('listMode').disabled = !state.canEditRules;
  byId('listMode').value = state.savedWebsiteMode;
  byId('editHint').textContent = state.canEditRules ? '黑白列表分别保存在此浏览器中。' : '本次使用开始时的规则，结束后可以修改。';
  for (const control of byId('websiteForm').elements) control.disabled = !state.canEditRules;
  byId('addCurrent').disabled = !state.canEditRules;
  const selection = JSON.stringify([state.websites, state.savedWebsiteMode, state.canEditRules]);
  if (selection !== websiteSelection) {
    websiteSelection = selection;
    byId('websiteCount').textContent = `${state.websites.length} 个网站`;
    byId('emptyWebsites').hidden = state.websites.length > 0;
    byId('emptyWebsites').textContent = state.savedWebsiteMode === 'Whitelist'
      ? '尚未添加网站。开启限制后，普通网站均不可访问。' : '尚未添加网站，当前黑名单不会限制任何网站。';
    byId('websites').replaceChildren(...state.websites.map(rule => {
      const row = document.createElement('div'); row.className = 'website';
      const title = document.createElement('span'); title.textContent = rule.domain + (rule.includeSubdomains ? ' · 含子域名' : '');
      const actions = document.createElement('div'); actions.className = 'actions';
      const edit = document.createElement('button'); edit.textContent = '编辑'; edit.className = 'quiet'; edit.disabled = !state.canEditRules;
      edit.onclick = () => { originalDomain = rule.domain; byId('domain').value = rule.domain; byId('subdomains').checked = rule.includeSubdomains; };
      const remove = document.createElement('button'); remove.textContent = '删除'; remove.className = 'quiet'; remove.disabled = !state.canEditRules;
      remove.onclick = () => { if (window.confirm(`删除网站“${rule.domain}”？`)) message({ type:'deleteWebsite', fields:{ domain:rule.domain } }); };
      actions.append(edit, remove); row.append(title, actions); return row;
    }));
  }
}

byId('open').onclick = async () => {
  const url = chrome.runtime.getURL('popup.html?view=page');
  const [existing] = (await chrome.tabs.query({})).filter(tab => tab.url === url);
  if (existing) { await chrome.tabs.update(existing.id, { active:true }); await chrome.windows.update(existing.windowId, { focused:true }); }
  else await chrome.tabs.create({ url });
};
byId('end').onclick = () => message({ type:'end' });
if (!blocked) {
  for (const button of document.querySelectorAll('[data-pane]')) button.onclick = () => showPane(button.dataset.pane);
  byId('settings').onclick = byId('quickSetup').onclick = () => showPane('preferences');
  byId('backToFocus').onclick = () => showPane('focus');
  const fields = () => ({ project:byId('projectName').value, timerMode:byId('timerMode').value,
    durationMinutes:byId('durationMinutes').value, pauseMinutes:byId('pauseMinutes').value, restMinutes:byId('restMinutes').value });
  byId('start').onclick = () => {
    if (!byId('timerForm').checkValidity()) { showPane('preferences'); byId('timerForm').reportValidity(); return; }
    message({ type:'start', fields:fields() });
  };
  byId('timerForm').onsubmit = async event => {
    event.preventDefault();
    const result = await message({ type:'savePreferences', fields:fields() });
    if (!result.error) byId('preferenceNotice').textContent = '默认计时安排已保存。';
  };
  byId('timerForm').oninput = updateSetup;
  byId('timerMode').onchange = updateSetup;
  for (const action of ['pause', 'resume', 'skipRest']) byId(action).onclick = () => message({ type:action });
  byId('enabled').onchange = event => message({ type:'setEnabled', enabled:event.target.checked });
  byId('fullscreen').onclick = async () => {
    if (document.fullscreenElement) await document.exitFullscreen(); else await document.documentElement.requestFullscreen();
  };
  document.addEventListener('fullscreenchange', () => { byId('fullscreen').textContent = document.fullscreenElement ? '退出全屏' : '全屏'; });
  const setTheme = () => message({ type:'setTheme', fields:{ palette:byId('palette').value, appearance:byId('appearance').value } });
  byId('palette').onchange = setTheme; byId('appearance').onchange = setTheme;
  byId('listMode').onchange = async event => { originalDomain = ''; byId('websiteForm').reset(); await message({ type:'setWebsiteMode', fields:{ listMode:event.target.value } }); };
  byId('cancel').onclick = () => { originalDomain = ''; byId('websiteForm').reset(); };
  byId('websiteForm').onsubmit = async event => {
    event.preventDefault();
    const view = await message({ type:'upsertWebsite', fields:{ domain:byId('domain').value, includeSubdomains:byId('subdomains').checked, originalDomain } });
    if (!view.error) { originalDomain = ''; byId('websiteForm').reset(); }
  };
  byId('addCurrent').onclick = async () => {
    const [tab] = await chrome.tabs.query({ active:true, currentWindow:true });
    let address = tab?.url || '';
    if (address.startsWith(chrome.runtime.getURL('blocked.html') + '#')) address = address.slice(address.indexOf('#') + 1);
    try {
      const url = new URL(address); if (!['http:', 'https:'].includes(url.protocol)) throw new Error();
      await message({ type:'upsertWebsite', fields:{ domain:url.hostname, includeSubdomains:true } });
    } catch { byId('error').textContent = '请在普通网站的标签页中打开扩展，再添加当前网站。'; }
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
chrome.storage.onChanged.addListener((changes, area) => { if (area === 'session' && changes.view) { latest = changes.view.newValue; render(latest); } });
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => { if (latest) applyTheme(latest.preferences); });
