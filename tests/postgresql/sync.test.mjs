import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {database, call} from './sync-fixture.mjs';
const setup=await readFile(new URL('../../cloud/postgresql/setup.sql',import.meta.url),'utf8');
const project={id:'7d5b0b51-c8f5-4b89-8ba6-50dfec8a0074',name:'阅读',category:'学习',color:'#627F90',deleted:false};
const change=(kind,id,payload,baseVersion=0)=>({kind,id,payload,baseVersion,localRevision:1});
const record=(id,seconds=60)=>({id,projectId:project.id,mode:'CountUp',startedAt:'2026-10-24T08:00:00Z',endedAt:`2026-10-24T08:${seconds===60?'01:00':'00:'+String(seconds).padStart(2,'0')}Z`,endReason:'Manual',strict:true,todoId:null,interceptionCount:null,
  intervals:[{kind:'Focus',startedAt:'2026-10-24T08:00:00Z',endedAt:`2026-10-24T08:${seconds===60?'01:00':'00:'+String(seconds).padStart(2,'0')}Z`}]});
test('workspace upgrade preserves existing data, separates old clients and strips local paths',async()=>{
  const db=await database();try {
    await call(db,'one',[change('project',project.id,project)]);
    await db.exec("ALTER TABLE public.focusbloom_sync_documents DROP CONSTRAINT focusbloom_sync_documents_kind_check; ALTER TABLE public.focusbloom_sync_documents ADD CONSTRAINT focusbloom_sync_documents_kind_check CHECK (kind IN ('project', 'record', 'setting'))");
    await db.exec(setup);
    const date=change('workspace','date_events/a',{id:'a',payload:{title:'截止',coverPath:'local.png'}});
    await assert.rejects(call(db,'one',[date]),/同步内容格式错误/);
    await assert.rejects(call(db,'anon',[],0,'anon',true),/permission denied/i);
    await assert.rejects(call(db,'one',[change('workspace','settings/account.session',{key:'account.session',value:'private'})],0,'authenticated',true),/不参与/);
    await call(db,'one',[date],0,'authenticated',true);
    assert.deepEqual((await call(db,'one')).documents.map(d=>d.kind),['project']);
    const workspace=await call(db,'one',[],0,'authenticated',true);
    assert.equal(workspace.documents[0].payload.payload.coverPath,null);
    assert.equal((await call(db,'two',[],0,'authenticated',true)).documents.length,0);
  } finally {await db.close();}
});
test('setup can be rerun and only signed-in users can call the sync function',async()=>{
  const db=await database();try {
    await db.exec(setup);
    await assert.rejects(call(db,'anon',[],0,'anon'),/permission denied/i);
    await assert.rejects(call(db,'',[],0),/邮箱/);
    const first=await call(db,'one',[change('project',project.id,project)]);assert.equal(first.accepted[0].version,1);
    const other=await call(db,'two');assert.equal(other.documents.length,0);
    await db.transaction(async tx=>{
      await tx.exec('SET LOCAL ROLE authenticated');await tx.query("SELECT set_config('request.jwt.claims',$1,true)",[JSON.stringify({sub:'one',role:'authenticated'})]);
      await assert.rejects(tx.query('SELECT * FROM public.focusbloom_sync_documents'),/permission denied/i);
    }).catch(error=>{if(!/transaction is aborted/i.test(error.message))throw error;});
  } finally {await db.close();}
});
test('records are immutable, retries deduplicate, and invalid batches roll back',async()=>{
  const db=await database();try {
    const saved=record('a1ad5235-e460-4f77-b76b-1dbbaebd435f');
    const changes=[change('record',saved.id,saved),change('project',project.id,project)];
    const result=await call(db,'one',changes);assert.equal(result.accepted.length,2);assert.equal(result.cursor,2);
    const retry=await call(db,'one',changes);assert.deepEqual(retry.accepted,result.accepted);
    const immutable=await call(db,'one',[change('record',saved.id,{...saved,endReason:'Interrupted'},2)],2);assert.equal(immutable.conflicts.length,1);
    await assert.rejects(call(db,'one',[change('setting','pauseMinutes','7'),change('record','short',record('short',9))],2),/10秒/);
    const after=await call(db,'one',[],2);assert.equal(after.documents.length,0);
    assert.equal((await db.query('SELECT count(*)::int AS count FROM public.focusbloom_sync_documents')).rows[0].count,2);
  } finally {await db.close();}
});
test('offline edits use versions and project deletion remains a tombstone',async()=>{
  const db=await database();try {
    await call(db,'one',[change('project',project.id,project)]);
    const updated=await call(db,'one',[change('project',project.id,{...project,name:'新名称'},1)],1);assert.equal(updated.accepted[0].version,2);
    const stale=await call(db,'one',[change('project',project.id,{...project,name:'离线名称'},1)],2);assert.equal(stale.conflicts[0].payload.name,'新名称');
    const keep=await call(db,'one',[change('project',project.id,{...project,name:'离线名称'},2)],2);assert.equal(keep.accepted[0].version,3);
    await call(db,'one',[change('project',project.id,{...project,deleted:true},3)],3);
    const removed=await call(db,'one',[],3);assert.equal(removed.documents[0].payload.deleted,true);
    await assert.rejects(call(db,'one',[change('setting','focus.media','private')]),/不支持/);
    assert.equal((await call(db,'two')).documents.length,0);
  } finally {await db.close();}
});
test('download pagination includes record project dependencies and numeric boundaries agree with clients',async()=>{
  const db=await database();try {
    await call(db,'one',[change('project',project.id,project),change('setting','timer.durationMinutes',String(10/60))]);
    await call(db,'one',[change('setting','timer.durationMinutes','1440',2)]);
    await assert.rejects(call(db,'one',[change('setting','pauseMinutes','0')]),/计时偏好/);
    await assert.rejects(call(db,'one',[change('setting','timer.durationMinutes','1441')]),/计时偏好/);
    for(let batch=0;batch<3;batch++) {
      const changes=Array.from({length:45},(_,i)=>{const id=`record-${batch*45+i}`;return change('record',id,record(id));});await call(db,'one',changes);
    }
    const page=await call(db,'one',[],3);assert.equal(page.hasMore,true);assert.equal(page.documents.filter(d=>d.kind==='record').length,100);assert.equal(page.documents.filter(d=>d.kind==='project').length,1);
    const last=await call(db,'one',[],page.cursor);assert.equal(last.hasMore,false);assert.equal(last.documents.filter(d=>d.kind==='record').length,35);
  } finally {await db.close();}
});
