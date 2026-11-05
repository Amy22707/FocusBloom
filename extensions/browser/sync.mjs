export const settingKeys=['timer.mode','timer.durationMinutes','pauseMinutes','restMinutes','restrictions.websites','restrictions.websites.blacklist','restrictions.websiteMode'];
export function captureSettings(model,keys=settingKeys) {
  model.sharedSettings ||= {};
  const values={
    'timer.mode':model.preferences.timerMode,'timer.durationMinutes':String(model.preferences.durationMinutes),
    pauseMinutes:String(model.preferences.pauseMinutes),restMinutes:String(model.preferences.restMinutes),
    'restrictions.websites':JSON.stringify(model.lists.Whitelist.map(r=>({Domain:r.domain,IncludeSubdomains:r.includeSubdomains}))),
    'restrictions.websites.blacklist':JSON.stringify(model.lists.Blacklist.map(r=>({Domain:r.domain,IncludeSubdomains:r.includeSubdomains}))),
    'restrictions.websiteMode':model.websiteMode
  };
  for(const key of keys) model.sharedSettings[key]=values[key];
}
function canonical(kind,payload) {
  if(kind==='project') return {id:payload.id,name:payload.name,category:payload.category,color:payload.color,deleted:Boolean(payload.deleted)};
  return payload;
}
function stage(model) {
  model.projects ||= []; model.records ||= []; model.sync ||= {cursor:0,documents:{}};
  model.sync.localRevision ??= 0; model.sync.changedAt ??= Date.now();
  let changed=false;
  if(!model.sharedSettings) captureSettings(model);
  const sources=[...model.projects.map(p=>['project',p.id,canonical('project',p)]),...model.records.map(r=>['record',r.id,r]),...Object.entries(model.sharedSettings).filter(([key])=>settingKeys.includes(key)).map(([key,value])=>['setting',key,value])];
  for(const [kind,id,payload] of sources) {
    const key=kind+':'+id,old=model.sync.documents[key];
    if(!old) {model.sync.documents[key]={kind,id,payload:structuredClone(payload),version:0,revision:1,pending:true};changed=true;}
    else if(JSON.stringify(canonical(kind,old.payload))!==JSON.stringify(payload)) {Object.assign(old,{payload:structuredClone(payload),revision:old.revision+1,pending:true,conflict:null});changed=true;}
  }
  if(changed) markLocalChange(model);
}
function markLocalChange(model) {
  model.sync.localRevision=(model.sync.localRevision??0)+1;
  model.sync.changedAt=Date.now();
}
export function isAutomaticSyncDue(model, hasPending, now=Date.now()) {
  const lastAttempt=model.sync.lastAttempt??model.lastSync;
  return lastAttempt==null||now-lastAttempt>=7*24*60*60*1000||
    hasPending&&model.sync.localRevision>(model.sync.attemptedRevision??-1)&&now-model.sync.changedAt>=120000;
}
export function recordSyncAttempt(model, now=Date.now()) {
  model.sync.lastAttempt=now;
  model.sync.attemptedRevision=model.sync.localRevision;
}
export function prepareSync(model) {
  stage(model);
  const changes=Object.values(model.sync.documents).filter(d=>d.pending&&!d.conflict).sort((a,b)=>['project','record','setting'].indexOf(a.kind)-['project','record','setting'].indexOf(b.kind)).slice(0,50)
    .map(d=>({kind:d.kind,id:d.id,baseVersion:d.version,localRevision:d.revision,payload:structuredClone(d.payload)}));
  return {cursor:model.sync.cursor,changes};
}
function apply(model,document) {
  const {kind,id,version}=document,payload=canonical(kind,document.payload);
  if(kind==='project') { const index=model.projects.findIndex(p=>p.id===id); if(index<0) model.projects.push(payload); else model.projects[index]=payload; }
  else if(kind==='record') { if(!model.records.some(record=>record.id===id)) model.records.push(payload); }
  else if(kind==='setting'&&settingKeys.includes(id)) {
    model.sharedSettings[id]=payload;
    if(id==='timer.mode'&&['CountUp','Countdown'].includes(payload)) model.preferences.timerMode=payload;
    for(const [key,field] of [['timer.durationMinutes','durationMinutes'],['pauseMinutes','pauseMinutes'],['restMinutes','restMinutes']]) if(id===key) model.preferences[field]=Number(payload);
    if(id==='restrictions.websiteMode') model.websiteMode=payload;
    for(const [key,list] of [['restrictions.websites','Whitelist'],['restrictions.websites.blacklist','Blacklist']]) if(id===key) model.lists[list]=JSON.parse(payload).map(r=>({domain:r.Domain,includeSubdomains:r.IncludeSubdomains}));
  } else throw new Error('同步数据类型不受支持。');
  const key=kind+':'+id;
  model.sync.documents[key]={kind,id,version,payload:structuredClone(payload),revision:model.sync.documents[key]?.revision||1,pending:false,conflict:null};
}
export function applySync(model,sent,response) {
  stage(model);
  for(const accepted of response.accepted) {
    const change=sent.changes.find(d=>d.kind===accepted.kind&&d.id===accepted.id),local=model.sync.documents[accepted.kind+':'+accepted.id];
    if(!change||!local) throw new Error('同步确认内容不匹配。');
    local.version=accepted.version; local.pending=local.revision!==change.localRevision; local.conflict=null;
  }
  for(const document of [...response.documents,...response.conflicts].sort((a,b)=>['project','record','setting'].indexOf(a.kind)-['project','record','setting'].indexOf(b.kind))) {
    const local=model.sync.documents[document.kind+':'+document.id];
    if(local&&document.version<=local.version) continue;
    if(local?.pending&&document.kind!=='record') local.conflict=structuredClone(document);
    else apply(model,document);
  }
  model.sync.cursor=response.cursor;
}
export function syncStatus(model) {
  const documents=Object.values(model.sync?.documents||{});
  return {pending:documents.filter(d=>d.pending).length,conflicts:documents.filter(d=>d.conflict).map(d=>({key:d.kind+':'+d.id,kind:d.kind,id:d.id,local:d.payload,remote:d.conflict.payload}))};
}
export function resolveConflict(model,key,keepLocal) {
  const document=model.sync.documents[key];
  if(!document?.conflict) throw new Error('这项冲突已处理。');
  if(keepLocal) {document.version=document.conflict.version;document.conflict=null;document.pending=true;markLocalChange(model);}
  else apply(model,document.conflict);
}
