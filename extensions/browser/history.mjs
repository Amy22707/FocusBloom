export function focusMilliseconds(record) {
  return record.intervals.filter(interval=>interval.kind==='Focus').reduce((sum,interval)=>sum+Date.parse(interval.endedAt)-Date.parse(interval.startedAt),0);
}
export function localDate(at) {
  const date=new Date(at); return `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;
}
export function historyPage(model,date='',page=0) {
  const records=(model.records||[]).filter(r=>!date||localDate(r.endedAt)===date).sort((a,b)=>Date.parse(b.endedAt)-Date.parse(a.endedAt));
  return {total:records.length,totalMs:records.reduce((sum,r)=>sum+focusMilliseconds(r),0),page,records:records.slice(page*50,page*50+50).map(record=>({...record,project:model.projects?.find(p=>p.id===record.projectId)?.name||'未分类',focusMs:focusMilliseconds(record)}))};
}
