import { createRules, isAllowed } from './rules.mjs';
import { createModel, settle, start, pause, resume, finish, isActive, nextDeadline, getView, normalizeWebsite, validatePreferences } from './focus.mjs';
import { historyPage } from './history.mjs';
import { palettes } from './theme.mjs';

const api = globalThis.browser ?? globalThis.chrome;
const blockedPage = api.runtime.getURL('blocked.html');
const alarmName = 'focus-transition';
let model;
let view;
let error = '';
let queue = Promise.resolve();
let ruleSelection = '';
let viewSelection = '';
let scheduledDeadline;
let transitionTimeout;
const ready = (async()=>{
  await api.storage.local.setAccessLevel?.({accessLevel:'TRUSTED_CONTEXTS'});
  const stored = await api.storage.local.get(['focusAccount', 'focusModel']);
  const previousKey = stored.focusAccount?.userId ? 'focusProfile:' + stored.focusAccount.userId : null;
  const previous = previousKey ? (await api.storage.local.get(previousKey))[previousKey] : null;
  model = previous || stored.focusModel || createModel();
  model.projects ||= [];
  model.records ||= [];
  if (previous && stored.focusModel) {
    for (const collection of ['projects', 'records']) {
      const ids = new Set(model[collection].map(item => item.id));
      for (const item of stored.focusModel[collection] || []) {
        if (!ids.has(item.id)) { model[collection].push(item); ids.add(item.id); }
      }
    }
  }
  await save();
  await api.storage.local.remove('focusAccount');
  await api.alarms.clear('focus-sync');
})();

function run(action = () => {}, checkExistingTabs = false) {
  const result = queue.then(async () => {
    await ready;
    if (settle(model, Date.now())) { await save(); }
    await action();
    await synchronize(checkExistingTabs);
    return view;
  });
  queue = result.catch(() => {});
  return result;
}

async function save() { await api.storage.local.set({ focusModel:model }); }

async function checkTabs() {
  const websites = view.state.sessionWebsites;
  for (const tab of await api.tabs.query({})) {
    const address = tab.pendingUrl || tab.url;
    if (!address || !tab.id) continue;
    try {
      if (address.startsWith(blockedPage + '#')) {
        const original = address.slice(blockedPage.length + 1);
        if (isAllowed(original, view.effective, websites, view.state.websiteMode)) await api.tabs.update(tab.id, { url:original });
      } else if (!isAllowed(address, view.effective, websites, view.state.websiteMode)) {
        await api.tabs.update(tab.id, { url:blockedPage + '#' + address });
      }
    } catch { /* 标签页可能已由用户关闭。 */ }
  }
}

async function synchronize(checkExistingTabs) {
  view = { ...getView(model, Date.now()), error, historyVersion:JSON.stringify([model.records.length, model.records.at(-1)?.id]) };
  const selection = JSON.stringify([view.effective, view.state.websiteMode, view.effective ? view.state.sessionWebsites : []]);
  if (selection !== ruleSelection) {
    const oldRules = await api.declarativeNetRequest.getDynamicRules();
    try {
      await api.declarativeNetRequest.updateDynamicRules({ removeRuleIds:oldRules.map(rule => rule.id),
        addRules:createRules(view.effective, view.state.sessionWebsites, blockedPage, view.state.websiteMode) });
      ruleSelection = selection;
      checkExistingTabs = true;
    } catch (failure) {
      view.error = '网站规则未能生效：' + failure.message;
      view.effective = false;
    }
  }
  if (checkExistingTabs) await checkTabs();
  const deadline = nextDeadline(model.session);
  if (deadline !== scheduledDeadline) {
    clearTimeout(transitionTimeout);
    scheduledDeadline = deadline;
    if (deadline !== null) {
      await api.alarms.create(alarmName, { when:deadline });
      transitionTimeout = setTimeout(() => run().catch(reportError), Math.min(2147483647, Math.max(0, deadline - Date.now())));
    } else await api.alarms.clear(alarmName);
  }
  const published = JSON.stringify(view);
  if (published !== viewSelection) {
    await api.storage.session.set({ view });
    viewSelection = published;
  }
  await api.action.setBadgeText({ text:view.effective ? '专注' : model.session?.state === 'Resting' ? '休息' : '' });
  await api.action.setBadgeBackgroundColor({ color:'#627f90' });
}

function reportError(failure) { error = failure.message; }

async function command(message) {
  const candidate = structuredClone(model);
  const now = Date.now();
  const fields = message.fields || {};
  switch (message.type) {
    case 'getView': return;
    case 'ackLimitNotice':
      if (candidate.limitNotice === message.noticeAt) candidate.limitNotice = null;
      break;
    case 'setEnabled': candidate.enabled = Boolean(message.enabled); break;
    case 'start': start(candidate, fields, now); break;
    case 'pause': pause(candidate, now); break;
    case 'resume': resume(candidate, now); break;
    case 'end': finish(candidate, now); break;
    case 'skipRest':
      if (candidate.session?.state !== 'Resting') throw new Error('当前没有休息。');
      candidate.session.state = 'Ended'; candidate.session.deadline = null; break;
    case 'savePreferences':
      if (isActive(candidate.session) || candidate.session?.state === 'Resting') throw new Error('本次结束后可以保存新的计时偏好。');
      candidate.preferences = validatePreferences(fields, candidate.preferences); break;
    case 'setTheme': {
      const prefs = candidate.preferences;
      if (fields.language === 'en' || fields.language === 'zh-CN') prefs.language = fields.language;
      if (['System', 'Light', 'Dark'].includes(fields.appearance)) prefs.appearance = fields.appearance;
      if (/^#[0-9a-f]{6}$/i.test(fields.customColor)) prefs.customColor = fields.customColor;
      if (palettes.some(palette => palette.id === fields.palette) || fields.palette === 'custom' && prefs.customColor) prefs.palette = fields.palette;
      if ([80, 90, 100, 110, 120].includes(fields.timerScale)) prefs.timerScale = fields.timerScale;
      break;
    }
    case 'setWebsiteMode':
    case 'upsertWebsite':
    case 'deleteWebsite': {
      if (isActive(candidate.session)) throw new Error('请结束当前专注后修改网站规则。');
      if (message.type === 'setWebsiteMode') {
        if (!['Whitelist', 'Blacklist'].includes(fields.listMode)) throw new Error('请选择黑名单或白名单。');
        candidate.websiteMode = fields.listMode;
      } else {
        const domain = normalizeWebsite(fields.domain);
        const list = candidate.lists[candidate.websiteMode];
        candidate.lists[candidate.websiteMode] = list.filter(rule => rule.domain !== domain && rule.domain !== fields.originalDomain);
        if (message.type === 'upsertWebsite') candidate.lists[candidate.websiteMode].push({ domain, includeSubdomains:Boolean(fields.includeSubdomains) });
      }
      break;
    }
    default: throw new Error('未知操作。');
  }
  await api.storage.local.set({ focusModel:candidate });
  model = candidate;
  error = '';
}

api.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== api.runtime.id) return;
  if(message.type==='getHistory') {
    run().then(()=>respond(historyPage(model,message.date||'',Math.max(0,Number(message.page)||0)))).catch(failure=>respond({error:failure.message}));return true;
  }
  run(() => command(message)).then(respond).catch(async failure => {
    reportError(failure);
    try { respond(await run()); } catch { respond({ ...view, error:failure.message }); }
  });
  return true;
});
api.alarms.onAlarm.addListener(alarm => { if (alarm.name === alarmName) run().catch(reportError); });
api.runtime.onStartup.addListener(() => run(undefined, true).catch(reportError));
api.runtime.onInstalled.addListener(() => run(undefined, true).catch(reportError));
api.tabs.onUpdated.addListener((_id, change) => { if (change.url) run(undefined, true).catch(reportError); });
api.tabs.onCreated.addListener(() => run(undefined, true).catch(reportError));

run(async () => {
  const previousRules = await api.declarativeNetRequest.getSessionRules();
  if (previousRules.length) await api.declarativeNetRequest.updateSessionRules({ removeRuleIds:previousRules.map(rule => rule.id) });
}, true).catch(reportError);
