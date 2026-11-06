import test from 'node:test';
import assert from 'node:assert/strict';
import {createModel,start,pause,resume,finish,settle} from '../browser/focus.mjs';
import {historyPage,focusMilliseconds} from '../browser/history.mjs';

test('history persists complete focus intervals, excludes pauses and short sessions, and has stable IDs',()=>{
  const model=createModel();start(model,{project:'阅读'},0);pause(model,6000);resume(model,66000);finish(model,70000);
  const saved=structuredClone(model);assert.equal(saved.records.length,1);assert.equal(focusMilliseconds(saved.records[0]),10000);
  assert.equal(saved.records[0].intervals.length,3);assert.equal(historyPage(saved).totalMs,10000);
  start(saved,{},80000);finish(saved,89999);assert.equal(saved.records.length,1);
  start(saved,{timerMode:'Countdown',durationMinutes:1},100000);settle(saved,200000);assert.equal(saved.records.length,2);
  assert.equal(saved.records[1].endedAt,new Date(160000).toISOString());assert.notEqual(saved.records[0].id,saved.records[1].id);
});
test('same-named projects in different folders are selected by ID',()=>{
  const model=createModel();model.projects=[{id:'one',name:'阅读',category:'学习',deleted:false},{id:'two',name:'阅读',category:'工作',deleted:false}];
  start(model,{project:'阅读',projectId:'two'},0);finish(model,20000);
  assert.equal(model.records[0].projectId,'two');assert.equal(model.session.project,'阅读');
  start(model,{project:'阅读',projectId:null},30000);finish(model,50000);
  const project=model.projects.find(p=>p.id===model.records[1].projectId);assert.equal(project.category,'浏览器');
});