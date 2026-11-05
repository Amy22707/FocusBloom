import { createRules, isAllowed } from './rules.mjs';
import { createModel, settle, start, pause, resume, finish, isActive, nextDeadline, getView, normalizeWebsite, validatePreferences } from './focus.mjs';
import { cloudClient } from './cloud.mjs';
import { prepareSync,applySync,captureSettings,syncStatus,resolveConflict,isAutomaticSyncDue,recordSyncAttempt } from './sync.mjs';
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
let identity=null;
let verification;
let cloud;
let syncing=false;
let authenticating=false;
let syncMessage='未登录，记录保存在此浏览器。';
let accountConfigured=false;
const profileKey=()=>identity?'focusProfile:'+identity.userId:'focusModel';

const ready = (async()=>{
  await api.storage.local.setAccessLevel?.({accessLevel:'TRUSTED_CONTEXTS'});
  const account=await api.storage.local.get('focusAccount'); identity=account.focusAccount||null;
  const key=profileKey(),result=await api.storage.local.get(key);
  model = result[key] || createModel(); model.projects ||= []; model.records ||= [];
  try {
    const response=await fetch(api.runtime.getURL('cloud-config.json'));
    if(response.ok) {const options=await response.json();accountConfigured=Boolean(options.environmentId&&options.publishableKey);if(accountConfigured) cloud=cloudClient(options);}
  } catch { /* 未配置账号服务时继续本机使用。 */ }
  if(identity) syncMessage=identity.expired?'登录已失效，请重新登录。本机数据已保留。':'已登录，等待同步。';
})();

function run(action = () => {}, checkExistingTabs = false) {
  const result = queue.then(async () => {
    await ready;
    if (settle(model, Date.now())) { if(identity) prepareSync(model);await save(); }
    await action();
    await synchronize(checkExistingTabs);
    return view;
  });
  queue = result.catch(() => {});
  return result;
}

async function save() { await api.storage.local.set({ [profileKey()]:model }); }

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
  view = { ...getView(model, Date.now()), error, account:{configured:accountConfigured,email:identity?.email||'',name:identity?.profile?.name||'',picture:identity?.expired?'':identity?.profile?.picture||'',signedIn:Boolean(identity&&!identity.expired),needsLogin:Boolean(identity?.expired),busy:syncing||authenticating,status:syncMessage,
    lastSync:model.lastSync||null,...syncStatus(model)} };
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
    case 'signOut': {
      if(authenticating) throw new Error('正在处理登录，请稍候。');
      if(isActive(model.session)||model.session?.state==='Resting') throw new Error('请先结束本次专注和休息，再退出账号。');
      await save();const guest=await api.storage.local.get('focusModel');
      await api.storage.local.set({focusAccount:null});identity=null;model=guest.focusModel||createModel();
      syncMessage='已退出，账号数据保留在此浏览器。';error='';ruleSelection='';return;
    }
    case 'resolveSyncConflict': resolveConflict(candidate,fields.key,Boolean(fields.keepLocal));break;
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
  if(['start','savePreferences'].includes(message.type)) captureSettings(candidate,['timer.mode','timer.durationMinutes','pauseMinutes','restMinutes']);
  if(['setWebsiteMode','upsertWebsite','deleteWebsite'].includes(message.type)) captureSettings(candidate,['restrictions.websiteMode','restrictions.websites','restrictions.websites.blacklist']);
  if(identity) prepareSync(candidate);
  await api.storage.local.set({ [profileKey()]:candidate });
  model = candidate;
  error = '';
}

async function authenticate(message) {
  await ready;
  if(!cloud) throw new Error('账号服务尚未配置。');
  if(authenticating) throw new Error('正在处理登录，请稍候。');
  authenticating=true;
  const fields=message.fields||{};
  try {
    let pending;
    await run(()=>{
      if(identity&&!identity.expired) throw new Error('请先退出当前账号。');
      if(message.type==='sendCode') {
        if(verification&&Date.now()-verification.sentAt<60000) throw new Error('请等待60秒后再发送验证码。');
      } else {
        if(isActive(model.session)||model.session?.state==='Resting') throw new Error('请先结束本次专注和休息，再切换账号。');
        if(message.type==='signIn') {
          if(!verification||verification.email!==String(fields.email).trim()) throw new Error('请先为这个邮箱获取验证码。');
          if(!/^\d{6}$/.test(String(fields.code).trim())) throw new Error('请输入6位邮箱验证码。');
          pending=verification;
        } else if(!fields.password) throw new Error('请输入密码。');
      }
    });
    if(message.type==='sendCode') {
      const next=await cloud.sendCode(String(fields.email).trim());
      await run(()=>{verification=next;syncMessage='验证码已发送，请查看邮箱。';error='';});
    } else {
      const session=message.type==='signIn'?await cloud.signIn(pending,String(fields.code).trim()):await cloud.signInWithPassword(String(fields.email).trim(),String(fields.password));
      await run(async()=>{
        if(isActive(model.session)||model.session?.state==='Resting') throw new Error('请先结束本次专注和休息，再切换账号。');
        await save();
        const key='focusProfile:'+session.userId,stored=await api.storage.local.get([key,'focusModel']);
        const next=stored[key]||createModel();
        if(fields.mergeGuest&&stored.focusModel) {
          const guest=stored.focusModel;next.projects ||= [];next.records ||= [];
          for(const project of guest.projects||[]) if(!next.projects.some(p=>p.id===project.id)) next.projects.push(structuredClone(project));
          for(const record of guest.records||[]) if(!next.records.some(r=>r.id===record.id)) next.records.push(structuredClone(record));
          if(!stored[key]) {next.preferences={...guest.preferences};next.lists=structuredClone(guest.lists);next.websiteMode=guest.websiteMode;}
        }
        next.preferences.palette=model.preferences.palette;next.preferences.appearance=model.preferences.appearance;
        if(!next.sharedSettings) captureSettings(next);
        await api.storage.local.set({focusAccount:session,[key]:next});identity=session;model=next;verification=null;
        syncMessage='登录成功。';error='';ruleSelection='';
      });
    }
  } finally {authenticating=false;await run();}
  if(message.type!=='sendCode') await cloudSync(true);
  return view;
}

async function cloudSync(automatic=false) {
  await ready;
  if(syncing||!identity||identity.expired||!cloud) return;
  let session;
  try {
    await run(async()=>{
      if(syncing||!identity||identity.expired) return;
      const oldRevision=model.sync?.localRevision;
      const request=prepareSync(model);
      if(automatic&&!isAutomaticSyncDue(model,request.changes.length>0)) {
        if(oldRevision!==model.sync.localRevision) await save();
        return;
      }
      session=identity;syncing=true;
      recordSyncAttempt(model);await save();
      syncMessage='正在同步…';
    });
    if(!session) return;
    if(session.expiresAt<=Date.now()+60000) {
      const refreshed=await cloud.refresh(session);
      await run(async()=>{if(identity?.userId!==session.userId) throw new Error('账号已切换。');identity={...refreshed,profile:identity.profile};await api.storage.local.set({focusAccount:identity});});
      session=refreshed;
    }
    let response,sent;
    do {
      await run(async()=>{if(identity?.userId!==session.userId) throw new Error('账号已切换。');sent=prepareSync(model);await save();});
      response=await cloud.synchronize(session,sent);
      await run(async()=>{if(identity?.userId!==session.userId) throw new Error('账号已切换。');applySync(model,sent,response);await save();});
    } while(response.hasMore||response.accepted.length&&syncStatus(model).pending>syncStatus(model).conflicts.length);
    await run(async()=>{if(identity?.userId!==session.userId) throw new Error('账号已切换。');model.lastSync=Date.now();syncMessage=syncStatus(model).conflicts.length?'有两端修改的内容，请选择保留哪一份。':'同步完成。';await save();});
    try {
      const profile=await cloud.getProfile(session);
      await run(async()=>{if(identity?.userId===session.userId) {identity.profile=profile;await api.storage.local.set({focusAccount:identity});}});
    } catch(failure) {if(failure.requiresSignIn) throw failure;if(identity?.userId===session.userId) syncMessage+=' 个人资料暂时无法刷新。';}
  } catch(failure) {
    if(!session) throw failure;
    if(failure.requiresSignIn) await run(async()=>{
      if(identity?.userId!==session.userId||identity.accessToken!==session.accessToken) return;
      await save();identity={...identity,accessToken:'',refreshToken:'',expiresAt:0,expired:true};await api.storage.local.set({focusAccount:identity});
      syncMessage='登录已失效，请重新登录。本机数据已保留，可继续本机使用。';error='';
    });
    else if(identity?.userId===session.userId) syncMessage='同步未完成：'+failure.message;
  }
  finally {if(session) {syncing=false;await run();}}
}

api.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== api.runtime.id) return;
  if(['sendCode','signIn','passwordSignIn'].includes(message.type)) {
    authenticate(message).then(respond).catch(async failure=>{reportError(failure);respond(await run());});return true;
  }
  if(message.type==='getHistory') {
    run().then(()=>respond(historyPage(model,message.date||'',Math.max(0,Number(message.page)||0)))).catch(failure=>respond({error:failure.message}));return true;
  }
  if(message.type==='syncNow') {cloudSync().then(()=>respond(view)).catch(failure=>respond({...view,error:failure.message}));return true;}
  run(() => command(message)).then(respond).catch(async failure => {
    reportError(failure);
    try { respond(await run()); } catch { respond({ ...view, error:failure.message }); }
  });
  return true;
});
api.alarms.onAlarm.addListener(alarm => { if (alarm.name === alarmName) run().catch(reportError);if(alarm.name==='focus-sync') cloudSync(true).catch(reportError); });
api.runtime.onStartup.addListener(() => run(undefined, true).catch(reportError));
api.runtime.onInstalled.addListener(() => run(undefined, true).catch(reportError));
api.tabs.onUpdated.addListener((_id, change) => { if (change.url) run(undefined, true).catch(reportError); });
api.tabs.onCreated.addListener(() => run(undefined, true).catch(reportError));

run(async () => {
  const previousRules = await api.declarativeNetRequest.getSessionRules();
  if (previousRules.length) await api.declarativeNetRequest.updateSessionRules({ removeRuleIds:previousRules.map(rule => rule.id) });
}, true).catch(reportError);
ready.then(()=>accountConfigured && api.alarms.create('focus-sync',{periodInMinutes:1})).catch(reportError);
