import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import * as focus from '../browser/focus.mjs';
import * as rules from '../browser/rules.mjs';
import { historyPage } from '../browser/history.mjs';
import { palettes } from '../browser/theme.mjs';

const source = (await readFile(new URL('../browser/background.js', import.meta.url), 'utf8')).replace(/^import .*;\r?\n/gm, '');
async function loadBackground(data) {
  let listener;
  const cleared = [];
  const event = { addListener() {} };
  const api = {
    runtime:{ id:'offline-test', getURL:name=>'chrome-extension://offline-test/'+name,
      onMessage:{ addListener(value) { listener=value; } }, onStartup:event, onInstalled:event },
    storage:{local:{
      async get(keys) { return Object.fromEntries((Array.isArray(keys)?keys:[keys]).map(key=>[key,structuredClone(data[key])])); },
      async set(values) { Object.assign(data, structuredClone(values)); },
      async remove(key) { delete data[key]; }, async setAccessLevel() {}
    },session:{async set() {}}},
    alarms:{async clear(name) { cleared.push(name); },async create() {},onAlarm:event},
    tabs:{async query() {return [];},onUpdated:event,onCreated:event},
    declarativeNetRequest:{async getDynamicRules() {return [];},async updateDynamicRules() {},
      async getSessionRules() {return [];},async updateSessionRules() {}},
    action:{async setBadgeText() {},async setBadgeBackgroundColor() {}}
  };
  vm.runInNewContext(source, { ...focus,...rules,historyPage,palettes,chrome:api,structuredClone,
    setTimeout,clearTimeout,fetch(){throw new Error('Offline extension attempted a network request');} });
  const command = message=>new Promise(resolve=>listener(message,{id:api.runtime.id},resolve));
  const view=await command({type:'getView'});
  return {view,command,cleared};
}

test('offline background keeps existing local data, has no account API, and cancels sync alarms',async()=>{
  const model=focus.createModel();model.preferences.palette='pine';
  const data={focusModel:model};
  const {view,command,cleared}=await loadBackground(data);
  assert.equal(view.preferences.palette,'pine');
  assert.equal(view.account,undefined);
  assert.ok(cleared.includes('focus-sync'));
  const rejected=await command({type:'signIn',fields:{email:'test@example.com'}});
  assert.equal(rejected.error,'未知操作。');
  assert.equal(data.focusAccount,undefined);
});

test('offline upgrade preserves active account and guest history and stays stable after restart',async()=>{
  const guest=focus.createModel();focus.start(guest,{project:'Guest reading'},0);focus.finish(guest,20000);
  const previous=focus.createModel();focus.start(previous,{project:'Course notes'},30000);focus.finish(previous,50000);
  previous.preferences.palette='sage';
  const data={focusAccount:{userId:'saved-user',accessToken:'old-token'},focusModel:guest,'focusProfile:saved-user':previous};
  const {view}=await loadBackground(data);
  assert.equal(view.preferences.palette,'sage');
  assert.equal(data.focusModel.records.length,2);
  assert.equal(data.focusModel.projects.length,2);
  assert.equal(historyPage(data.focusModel).totalMs,40000);
  assert.equal(data['focusProfile:saved-user'].records.length,1);
  assert.equal(data.focusAccount,undefined);
  await loadBackground(data);
  assert.equal(data.focusModel.records.length,2);
});

test('offline popup has no login form and the package excludes cloud modules',async()=>{
  const html=await readFile(new URL('../browser/popup.html',import.meta.url),'utf8');
  assert.doesNotMatch(html,/accountForm|syncNow|登录|注册|验证码/);
  const script=await readFile(new URL('../../scripts/publish-store-extension.ps1',import.meta.url),'utf8');
  assert.doesNotMatch(script,/'cloud\.mjs'|'sync\.mjs'|'cloud-config\.json'/);
});
