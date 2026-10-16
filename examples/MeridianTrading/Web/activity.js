'use strict';
// One interaction can issue several sequential requests (overview, then recent orders).
// Requests retain their originating action; obsolete completions never replace the latest status.
const MeridianActivity=(()=>{
  let serial=0,latest=null,closing=false;
  const active=new Set(), status=document.querySelector('#activity'), label=document.querySelector('#activity-label'), values=document.querySelector('#request-footer');
  const ms=n=>n>=1000?`${(n/1000).toFixed(2)} s`:`${n.toFixed(1)} ms`;
  function begin(name){
    latest={id:++serial,name,started:performance.now(),pending:0,roundTrip:0,database:0,calculation:0,queue:0,json:0,rows:0,hasRows:false,requests:0,revision:0,phase:'Preparing request',failed:false,intervals:[],done:false};
    const action=latest;
    requestAnimationFrame(()=>requestAnimationFrame(()=>{if(action.requests===0){action.done=true;action.total=performance.now()-action.started;action.ui=action.total;action.other=0;action.overlap=0;if(action===latest)paint();}}));
    return latest;
  }
  function start(command){
    if(['invalidate-reads','request-stats','runtime-metrics','capture-scene','cancel-generation','new-workspace'].includes(command))return null;
    if(!latest||latest.done)begin(command);
    const action=latest;action.done=false;action.pending++;action.requests++;action.revision++;action.phase='Sending '+command;active.add(action);paint();return action;
  }
  function phase(action,text){if(action){action.phase=text;if(action===latest)paint();}}
  function complete(action,data,started){
    if(!action)return;
    const end=performance.now(), t=data.timing||{};
    action.intervals.push([started,end]);action.roundTrip+=end-started;
    action.database+=t.databaseMs||0;action.calculation+=t.calculationMs||0;action.queue+=t.queueMs||0;action.json+=t.serializationMs||0;
    if(data.returnedRows!=null){action.rows+=data.returnedRows;action.hasRows=true;}
    if(data.result?.orders!=null&&data.result?.lines!=null&&typeof data.result.orders!=='object')action.facts=`Dataset: ${Number(data.result.orders).toLocaleString()} orders / ${Number(data.result.lines).toLocaleString()} lines`;
    if(data.result?.recordsScanned!=null)action.facts=`Hydrated: ${Number(data.result.recordsScanned).toLocaleString()} objects · ${Number(data.result.orders?.count||0).toLocaleString()} orders / ${Number(data.result.lines?.count||0).toLocaleString()} lines`;
    action.failed ||= !data.ok&&!data.superseded;action.pending--;
    if(action.pending===0){
      active.delete(action);const revision=action.revision;
      requestAnimationFrame(()=>requestAnimationFrame(()=>{
        if(action.pending||action.revision!==revision)return;
        action.total=performance.now()-action.started;
        // Merge overlapping RPC wait intervals. CPU/backend sums may overlap for test bursts;
        // report that explicitly rather than pretending parallel durations add to wall time.
        const ranges=action.intervals.slice().sort((a,b)=>a[0]-b[0]);let wait=0,last=-Infinity;
        for(const [from,to]of ranges){wait+=Math.max(0,to-Math.max(from,last));last=Math.max(last,to);}
        action.ui=Math.max(0,action.total-wait);action.overlap=Math.max(0,action.roundTrip-wait);
        action.other=Math.max(0,wait-action.database-action.calculation);action.done=true;
        if(action===latest){paint();window.dispatchEvent(new CustomEvent('meridian-action-complete',{detail:{...action,intervals:undefined}}));}
      }));
    }
    paint();
  }
  function paint(){
    if(!latest)return;
    const a=latest,busy=closing||active.size>0||!a.done;
    status.dataset.busy=String(busy);document.querySelector('#content').setAttribute('aria-busy',String(busy));
    label.textContent=closing?'Finishing the active storage operation before closing…':a.pending?`${a.name} · ${a.phase} · ${ms(performance.now()-a.started)}`:active.size?`Earlier operation finishing · ${[...active][0].phase}`:a.done?`${a.name} · ${a.failed?'Failed':'Complete'}`:'Rendering…';
    if(!a.done){values.textContent=`Elapsed ${ms(performance.now()-a.started)} · ${a.pending} pending request${a.pending===1?'':'s'} · measuring`;return;}
    const parts=[['Abraxas',a.database],['App/calc',a.calculation],['UI / paint wait',a.ui],['Queue / JSON / bridge',a.other],['Total',a.total]];
    values.replaceChildren(...parts.map(([name,time])=>{const s=document.createElement('span');s.textContent=`${name}: ${ms(time)}`;return s;}));
    if(a.hasRows){const s=document.createElement('span');s.textContent=`${a.rows.toLocaleString()} rows returned · ${a.requests} requests`;values.append(s);}
    if(a.facts){const s=document.createElement('span');s.textContent=a.facts;values.append(s);}
    if(a.overlap>1){const s=document.createElement('span');s.textContent='Overlapping requests: component work is not additive';values.append(s);}
    values.title=`Queue ${ms(a.queue)} · response JSON ${ms(a.json)}. UI includes frame wait, not GPU time. Returned-row counts may include repeated rows across requests.`;
  }
  document.addEventListener('click',e=>{const control=e.target.closest('button,[data-order],[data-edit]');if(control&&!control.closest('#diagnostics')&&!['diagnostics-button','close-diagnostics'].includes(control.id))begin(control.textContent.trim().slice(0,70)||'Open record');},true);
  document.addEventListener('change',e=>{if(e.target.matches('select'))begin('Change '+(e.target.id||'selection'));},true);
  document.addEventListener('submit',()=>begin('Save changes'),true);
  document.addEventListener('keydown',e=>{if(e.key==='Enter'&&e.target.matches('input,[data-order],[data-edit]'))begin('Search / open selection');},true);
  setInterval(()=>{if(active.size||closing)paint();},250);
  begin('Open workspace');
  return {begin,start,phase,complete,current:()=>latest,closing:()=>{closing=true;paint();}};
})();
