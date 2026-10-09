import { createRules, isAllowed } from './rules.mjs';
import { createModel, settle, start, pause, resume, finish, isActive, nextDeadline, getView, normalizeWebsite, validatePreferences } from './focus.mjs';

const blockedPage = chrome.runtime.getURL('blocked.html');
const alarmName = 'focus-transition';
let model;
let view;
let error = '';
let queue = Promise.resolve();
let ruleSelection = '';
let viewSelection = '';
let scheduledDeadline;
let transitionTimeout;

const ready = chrome.storage.local.get('focusModel').then(result => {
  model = result.focusModel || createModel();
});

function run(action = () => {}, checkExistingTabs = false) {
  const result = queue.then(async () => {
    await ready;
    if (settle(model, Date.now())) await save();
    await action();
    await synchronize(checkExistingTabs);
    return view;
  });
  queue = result.catch(() => {});
  return result;
}

async function save() { await chrome.storage.local.set({ focusModel:model }); }

async function checkTabs() {
  const websites = view.state.sessionWebsites;
  for (const tab of await chrome.tabs.query({})) {
    const address = tab.pendingUrl || tab.url;
    if (!address || !tab.id) continue;
    try {
      if (address.startsWith(blockedPage + '#')) {
        const original = address.slice(blockedPage.length + 1);
        if (isAllowed(original, view.effective, websites, view.state.websiteMode)) await chrome.tabs.update(tab.id, { url:original });
      } else if (!isAllowed(address, view.effective, websites, view.state.websiteMode)) {
        await chrome.tabs.update(tab.id, { url:blockedPage + '#' + address });
      }
    } catch { /* 标签页可能已由用户关闭。 */ }
  }
}

async function synchronize(checkExistingTabs) {
  view = { ...getView(model, Date.now()), error };
  const selection = JSON.stringify([view.effective, view.state.websiteMode, view.effective ? view.state.sessionWebsites : []]);
  if (selection !== ruleSelection) {
    const oldRules = await chrome.declarativeNetRequest.getDynamicRules();
    try {
      await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds:oldRules.map(rule => rule.id),
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
      await chrome.alarms.create(alarmName, { when:deadline });
      transitionTimeout = setTimeout(() => run().catch(reportError), Math.min(2147483647, Math.max(0, deadline - Date.now())));
    } else await chrome.alarms.clear(alarmName);
  }
  const published = JSON.stringify(view);
  if (published !== viewSelection) {
    await chrome.storage.session.set({ view });
    viewSelection = published;
  }
  await chrome.action.setBadgeText({ text:view.effective ? '专注' : model.session?.state === 'Resting' ? '休息' : '' });
  await chrome.action.setBadgeBackgroundColor({ color:'#627f90' });
}

function reportError(failure) { error = failure.message; }

async function command(message) {
  const candidate = structuredClone(model);
  const now = Date.now();
  const fields = message.fields || {};
  switch (message.type) {
    case 'getView': return;
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
    case 'setTheme':
      candidate.preferences.palette = fields.palette;
      candidate.preferences.appearance = fields.appearance; break;
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
  await chrome.storage.local.set({ focusModel:candidate });
  model = candidate;
  error = '';
}

chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== chrome.runtime.id) return;
  run(() => command(message)).then(respond).catch(async failure => {
    reportError(failure);
    try { respond(await run()); } catch { respond({ ...view, error:failure.message }); }
  });
  return true;
});
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === alarmName) run().catch(reportError); });
chrome.runtime.onStartup.addListener(() => run(undefined, true).catch(reportError));
chrome.runtime.onInstalled.addListener(() => run(undefined, true).catch(reportError));
chrome.tabs.onUpdated.addListener((_id, change) => { if (change.url) run(undefined, true).catch(reportError); });
chrome.tabs.onCreated.addListener(() => run(undefined, true).catch(reportError));

run(async () => {
  const previousRules = await chrome.declarativeNetRequest.getSessionRules();
  if (previousRules.length) await chrome.declarativeNetRequest.updateSessionRules({ removeRuleIds:previousRules.map(rule => rule.id) });
}, true).catch(reportError);
