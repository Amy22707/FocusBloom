import test from 'node:test';
import assert from 'node:assert/strict';
import {createModel,start,pause,resume,finish,settle} from '../browser/focus.mjs';
import {historyPage,focusMilliseconds} from '../browser/history.mjs';
import {prepareSync,applySync,resolveConflict,syncStatus,captureSettings,isAutomaticSyncDue,recordSyncAttempt} from '../browser/sync.mjs';
import {cloudClient} from '../browser/cloud.mjs';
test('automatic sync batches new edits and does not retry an unchanged failed batch every minute',()=>{
  let model=createModel();
  const first=prepareSync(model); const at=model.sync.changedAt;
  assert.equal(isAutomaticSyncDue(model,first.changes.length>0,at),true);
  recordSyncAttempt(model,at);
  model=structuredClone(model);
  assert.equal(isAutomaticSyncDue(model,true,at+60000),false);
  assert.equal(isAutomaticSyncDue(model,false,at+7*86400000-1),false);
  assert.equal(isAutomaticSyncDue(model,false,at+7*86400000),true);
  model.preferences.pauseMinutes=17;captureSettings(model,['pauseMinutes']);prepareSync(model);
  const edited=model.sync.changedAt;
  assert.equal(isAutomaticSyncDue(model,true,edited+119999),false);
  assert.equal(isAutomaticSyncDue(model,true,edited+120000),true);
  // Manual sync can register an attempt immediately, without waiting for the batch delay.
  recordSyncAttempt(model,edited);
  assert.equal(isAutomaticSyncDue(model,true,edited+120000),false);
});
test('history persists complete focus intervals, excludes pauses and short sessions, and has stable IDs',()=>{
  const model=createModel();start(model,{project:'阅读'},0);pause(model,6000);resume(model,66000);finish(model,70000);
  const saved=structuredClone(model);assert.equal(saved.records.length,1);assert.equal(focusMilliseconds(saved.records[0]),10000);
  assert.equal(saved.records[0].intervals.length,3);assert.equal(historyPage(saved).totalMs,10000);
  start(saved,{},80000);finish(saved,89999);assert.equal(saved.records.length,1);
  start(saved,{timerMode:'Countdown',durationMinutes:1},100000);settle(saved,200000);assert.equal(saved.records.length,2);
  assert.equal(saved.records[1].endedAt,new Date(160000).toISOString());assert.notEqual(saved.records[0].id,saved.records[1].id);
});
test('upload confirmation preserves new local edits; conflicts support explicit local or cloud choices',()=>{
  const model=createModel();const sent=prepareSync(model);model.preferences.pauseMinutes=7;captureSettings(model);
  applySync(model,sent,{cursor:10,hasMore:false,accepted:sent.changes.map((c,i)=>({kind:c.kind,id:c.id,version:i+1})),documents:[],conflicts:[]});
  const next=prepareSync(model);assert.equal(next.changes.length,1);assert.equal(next.changes[0].payload,'7');
  const remote={kind:'setting',id:'pauseMinutes',version:20,payload:'9'};
  applySync(model,next,{cursor:20,accepted:[],documents:[],conflicts:[remote]});assert.equal(model.preferences.pauseMinutes,7);assert.equal(syncStatus(model).conflicts.length,1);
  resolveConflict(model,'setting:pauseMinutes',true);assert.equal(prepareSync(model).changes[0].baseVersion,20);
  applySync(model,next,{cursor:21,accepted:[],documents:[],conflicts:[{...remote,version:21}]});
  resolveConflict(model,'setting:pauseMinutes',false);assert.equal(model.preferences.pauseMinutes,9);assert.equal(syncStatus(model).pending,0);
});
test('downloaded desktop records and projects are not uploaded again or counted twice',()=>{
  const desktop=createModel();start(desktop,{project:'桌面项目'},0);finish(desktop,20000);
  const model=createModel();const sent=prepareSync(model);const documents=[{kind:'project',id:desktop.projects[0].id,version:1,payload:desktop.projects[0]},{kind:'record',id:desktop.records[0].id,version:2,payload:desktop.records[0]}];
  const response={cursor:2,accepted:[],conflicts:[],documents};applySync(model,sent,response);applySync(model,prepareSync(model),response);
  assert.equal(historyPage(model).total,1);assert.equal(historyPage(model).totalMs,20000);
  assert.ok(!prepareSync(model).changes.some(c=>c.kind==='record'||c.kind==='project'));
});
test('cloud authentication and refresh never use an expired access token for refresh',async()=>{
  const requests=[];const responses=[{verification_id:'code',is_user:false},{verification_token:'verified'},{sub:'user',access_token:'a',refresh_token:'r',expires_in:7200},{sub:'user',access_token:'b',refresh_token:'s',expires_in:7200}];
  const client=cloudClient({environmentId:'test-env',publishableKey:'public'},async(url,options)=>{requests.push({url,options});return {ok:true,json:async()=>responses.shift()};});
  const verification=await client.sendCode('test@example.com');let session=await client.signIn(verification,'123456');session=await client.refresh(session);
  assert.ok(requests[2].url.endsWith('/auth/v1/signup'));assert.equal(session.accessToken,'b');assert.equal(requests[3].options.headers.Authorization,undefined);
});
test('same-named projects in different folders are selected by ID',()=>{
  const model=createModel();model.projects=[{id:'one',name:'阅读',category:'学习',deleted:false},{id:'two',name:'阅读',category:'工作',deleted:false}];
  start(model,{project:'阅读',projectId:'two'},0);finish(model,20000);
  assert.equal(model.records[0].projectId,'two');assert.equal(model.session.project,'阅读');
  start(model,{project:'阅读',projectId:null},30000);finish(model,50000);
  const project=model.projects.find(p=>p.id===model.records[1].projectId);assert.equal(project.category,'浏览器');
});
test('sync calls PostgreSQL RPC with bearer authentication and p_cursor/p_changes parameters',async()=>{
  let sent;
  const client=cloudClient({environmentId:'test-env',publishableKey:'public'},async(url,options)=>{sent={url,options};return {ok:true,json:async()=>({cursor:3,hasMore:false,accepted:[],documents:[],conflicts:[]})};});
  const result=await client.synchronize({accessToken:'user-access'},{cursor:2,changes:[]});
  assert.equal(result.cursor,3);assert.ok(sent.url.endsWith('/v1/rdb/rest/rpc/focusbloom_sync'));
  assert.equal(sent.options.headers.Authorization,'Bearer user-access');assert.deepEqual(JSON.parse(sent.options.body),{p_cursor:2,p_changes:[]});
});
test('password login maps email to username and profile requests use the current user token',async()=>{
  const requests=[];const responses=[{sub:'one',access_token:'access',refresh_token:'refresh',expires_in:7200},{name:'专注者',picture:'data:image/jpeg;base64,abc'}];
  const client=cloudClient({environmentId:'test-env',publishableKey:'public'},async(url,options)=>{requests.push({url,options});return {ok:true,json:async()=>responses.shift()};});
  const session=await client.signInWithPassword('one@example.com','Example9!');const profile=await client.getProfile(session);
  assert.deepEqual(JSON.parse(requests[0].options.body),{username:'one@example.com',password:'Example9!'});
  assert.equal(requests[1].options.method,'GET');assert.equal(requests[1].options.headers.Authorization,'Bearer access');assert.equal(requests[1].options.body,undefined);assert.equal(profile.name,'专注者');
});
test('expired login errors are actionable instead of exposing token hash messages',async()=>{
  const client=cloudClient({environmentId:'test-env',publishableKey:'public'},async(_url,options)=>{
    assert.equal(options.headers.Authorization,undefined);
    return {ok:false,json:async()=>({error:'failed_precondition',error_description:'token hash not match'})};
  });
  await assert.rejects(client.refresh({refreshToken:'refresh'}),/登录状态已失效.*本机数据会保留/);
});
