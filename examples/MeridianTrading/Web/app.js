'use strict';
const content=document.querySelector('#content'), drawer=document.querySelector('#drawer'),lightbox=document.querySelector('#lightbox');
let sequence=0, page='overview', offset=0, query='', overview=null, generation=0;
let navigation=0, gridRequest=0, detailRequest=0, orderCardRequest=0, sort='', descending=false;
let orderView='table';
let customerOutlineSerial=0;
const customerOutlineRequests=new Map();
let customerHydrationOptions=null;
const customerHydration={initialized:false,enabled:false,mode:'date',dateFrom:0,dateThrough:0,recentCount:10,minimumTotal:0,maximumTotal:0};
let customerHydrationCueTimer=0;
let generationTestCancel=false, generationCancelSent=false, generationProgressSeen=false;
let generationIndexProgressSeen=false;
let datasetAuditProgress=null, datasetAuditResult=null, datasetAuditProgressSeen=false;
let runtimeView='recordbase',runtimeMetrics=null,runtimeMetricsBusy=false;
let searchSchema=null,searchWorkbenchSerial=0;
let searchNodeSerial=2;
let gridFilterAsYouType=false,gridSearchDebounce=0;
let activeMeridianSelect=null,meridianSelectSerial=0;
const searchWorkbench={dataset:'orders',resultMode:'object',root:{kind:'group',id:'group-1',combinator:'all',children:[{kind:'clause',id:'clause-2',field:'Number',operator:'contains',value:'SO-',value2:'',ignoreCase:true}]},exampleId:'',exampleTitle:'',exampleModified:false};
const searchExamples=[
  {id:'exact-order',category:'Everyday work',title:'Find one order exactly',dataset:'orders',intent:'Locate a known business document without treating its number as a storage identity.',showcase:'Exact text semantics, case policy, and an attached LibraDex source index.',root:{combinator:'all',children:[{field:'Number',operator:'exact',value:'SO-0100001',value2:'',ignoreCase:true}]}},
  {id:'open-high-value',category:'Everyday work',title:'Open high-value orders',dataset:'orders',intent:'Find financially significant orders that still require operational attention.',showcase:'A numeric threshold combined with a nested Any group over two indexed statuses.',root:{combinator:'all',children:[{field:'Total',operator:'greaterOrEqual',value:'25000',value2:'',ignoreCase:true},{combinator:'any',children:[{field:'Status',operator:'exact',value:'Pending',value2:'',ignoreCase:true},{field:'Status',operator:'exact',value:'Processing',value2:'',ignoreCase:true}]}]}},
  {id:'recent-shipments',category:'Everyday work',title:'Recent shipped orders',dataset:'orders',intent:'Review shipments placed inside an inclusive business date window.',showcase:'Typed dates, inclusive Between semantics, and an indexed status conjunction.',root:{combinator:'all',children:[{field:'OrderedAt',operator:'between',value:'2026-06-01',value2:'2026-09-01',ignoreCase:true},{field:'Status',operator:'exact',value:'Shipped',value2:'',ignoreCase:true}]}},
  {id:'restock-risk',category:'Everyday work',title:'Inventory at restock risk',dataset:'inventory',intent:'Find stock positions already at the reorder boundary with units committed.',showcase:'Integer comparisons and a mixed indexed/evaluated condition.',root:{combinator:'all',children:[{field:'OnHand',operator:'lessOrEqual',value:'30',value2:'',ignoreCase:true},{field:'Reserved',operator:'greater',value:'0',value2:'',ignoreCase:true}]}},
  {id:'regional-credit',category:'Business rules',title:'Western trading accounts',dataset:'customers',intent:'Find western customers whose natural-language name contains the whole word Trading.',showcase:'Unicode whole-word boundaries combined with a regional business rule.',root:{combinator:'all',children:[{field:'Region',operator:'exact',value:'West',value2:'',ignoreCase:true},{field:'Name',operator:'whole',value:'Trading',value2:'',ignoreCase:true}]}},
  {id:'catalog-band',category:'Business rules',title:'Beverages in a price band',dataset:'products',intent:'Locate products in one category that fit an inclusive merchandising range.',showcase:'Text equality plus a typed decimal Between condition.',root:{combinator:'all',children:[{field:'Category',operator:'exact',value:'Beverages',value2:'',ignoreCase:true},{field:'Price',operator:'between',value:'100',value2:'250',ignoreCase:true}]}},
  {id:'line-economics',category:'Business rules',title:'Material line-item exposure',dataset:'lines',intent:'Find meaningful quantities whose extended value exceeds a review threshold.',showcase:'Two numeric types in one condition and a mixed source-index route.',root:{combinator:'all',children:[{field:'Quantity',operator:'between',value:'10',value2:'20',ignoreCase:true},{field:'Total',operator:'greaterOrEqual',value:'1000',value2:'',ignoreCase:true}]}},
  {id:'mail-domain',category:'Business rules',title:'Customers by email domain',dataset:'customers',intent:'Find contacts belonging to a supplied mail domain without wildcard syntax.',showcase:'A no-case suffix condition routed through LibraDex’s maintained folded-reversed email projection.',root:{combinator:'all',children:[{field:'Email',operator:'ends',value:'@example.test',value2:'',ignoreCase:true}]}},
  {id:'nested-operations',category:'Developer wishlist',title:'Nested regional operations',dataset:'orders',intent:'Express two independent alternatives while retaining one value constraint.',showcase:'A real All root containing two Any groups—not a flattened approximation.',root:{combinator:'all',children:[{combinator:'any',children:[{field:'Region',operator:'exact',value:'West',value2:'',ignoreCase:true},{field:'Region',operator:'exact',value:'Northeast',value2:'',ignoreCase:true}]},{combinator:'any',children:[{field:'Status',operator:'exact',value:'Pending',value2:'',ignoreCase:true},{field:'Status',operator:'exact',value:'Processing',value2:'',ignoreCase:true}]},{field:'Total',operator:'between',value:'5000',value2:'15000',ignoreCase:true}]}},
  {id:'sku-regex',category:'Developer wishlist',title:'Validated SKU shape',dataset:'lines',intent:'Find line items whose SKU conforms to an anchored operational pattern.',showcase:'Explicit bounded regular-expression semantics carried as data, never browser code.',root:{combinator:'all',children:[{field:'SKU',operator:'regex',value:'^MRD-00[0-9]{4}$',value2:'',ignoreCase:false}]}},
  {id:'customer-code-scan',category:'Developer wishlist',title:'Customer code scan, one value out',dataset:'customers',resultMode:'value',intent:'Audit a bounded identifier pattern that cannot use a maintained LibraDex key route.',showcase:'A true Fractal/Inheto regex scan whose result reader decodes only Customer.Name—no Customer object is hydrated.',warning:'The condition deliberately performs a RecordBase scan; the one-property result shape keeps result materialization narrow.',root:{combinator:'all',children:[{field:'Code',operator:'regex',value:'^CUS-00[0-9]{4}$',value2:'',ignoreCase:false}]}},
  {id:'inventory-cross-field-scan',category:'Developer wishlist',title:'Cross-field inventory scan, small object out',dataset:'inventory',resultMode:'projection',intent:'Compare two stored properties on every inventory position without inventing a calculated index.',showcase:'A full Fractal/Inheto property-to-property scan projected directly into SKU, OnHand, and Reserved—never the complete InventoryPosition.',warning:'No single LibraDex key can answer OnHand > Reserved; the scan is intentional and its reduced result materialization is separately measured.',root:{combinator:'all',children:[{field:'OnHand',operator:'propGreater',value:'Reserved',value2:'',ignoreCase:true}]}},
  {id:'impossible-state',category:'Developer wishlist',title:'Impossible-state audit',dataset:'orders',intent:'Prove that no order simultaneously carries two mutually exclusive statuses.',showcase:'A zero-result invariant expressed with ordinary conditions and exact native count evidence.',root:{combinator:'all',children:[{field:'Status',operator:'exact',value:'Delivered',value2:'',ignoreCase:true},{field:'Status',operator:'exact',value:'Cancelled',value2:'',ignoreCase:true}]}}
];
const gridSearchPolicy={
  orders:{field:'Number',mode:'contains',ignoreCase:true},customers:{field:'Name',mode:'contains',ignoreCase:true},
  products:{field:'Name',mode:'contains',ignoreCase:true},inventory:{field:'SKU',mode:'contains',ignoreCase:true},lines:{field:'SKU',mode:'contains',ignoreCase:true}
};
const gridSearchFields={orders:[['Number','Order number'],['Status','Status'],['Region','Region']],customers:[['Name','Name'],['Code','Customer code'],['Email','Email'],['Region','Region']],products:[['Name','Product name'],['SKU','SKU'],['Category','Category']],inventory:[['SKU','SKU'],['Warehouse','Warehouse']],lines:[['SKU','SKU']]};
const pending=new Map(), diagnostics=[];
const currencyFormat=new Intl.NumberFormat('en-US',{style:'currency',currency:'USD',minimumFractionDigits:2,maximumFractionDigits:2});
const integerFormat=new Intl.NumberFormat('en-US');
const money=n=>currencyFormat.format(Number(n));
const number=n=>integerFormat.format(Number(n));
const duration=milliseconds=>Number(milliseconds)<1000?`${Number(milliseconds).toFixed(1)} ms`:`${(Number(milliseconds)/1000).toFixed(3)} s`;
const binarySize=value=>{const size=Number(value);if(!Number.isFinite(size))return '—';if(size>=1073741824)return `${(size/1073741824).toFixed(2)} GiB`;if(size>=1048576)return `${(size/1048576).toFixed(1)} MiB`;if(size>=1024)return `${(size/1024).toFixed(1)} KiB`;return `${number(size)} B`;};
const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const labels={overview:'Overview',orders:'Orders',customers:'Customers',products:'Catalog',inventory:'Inventory',lines:'Order lines',search:'Search & lookup',analysis:'Analysis',dataset:'Runtime metrics'};
function rpc(command,payload={}){
  const id=String(++sequence),started=performance.now();
  const action=MeridianActivity.start(command);
  return new Promise((resolve,reject)=>{pending.set(id,{resolve,reject,started,action,command});try{window.chrome.webview.postMessage({id,command,payload,view:navigation});}catch(error){pending.delete(id);MeridianActivity.complete(action,{ok:false},started);reject(error);}});
}

// Uses the same allowlisted bridge without creating or replacing a user-action timing record.
function quietRpc(command,payload={}){
  const id=String(++sequence),started=performance.now();
  return new Promise((resolve,reject)=>{pending.set(id,{resolve,reject,started,action:null,command});try{window.chrome.webview.postMessage({id,command,payload,view:navigation});}catch(error){pending.delete(id);reject(error);}});
}

// Paints the persistent compact runtime line independently from the last-action measurement above it.
function paintRuntimeFooter(){
  const target=document.querySelector('#runtime-footer');if(!target)return;
  if(!runtimeMetrics){target.textContent='Sampling .NET host memory and GC counters…';return;}
  const metrics=runtimeMetrics,generations=metrics.generations||[],collections=generations.slice(0,3).map(item=>number(item.collections||0)).join(' / ');
  const entries=[['Host',binarySize(metrics.workingSetBytes)],['Managed',binarySize(metrics.managedBytes)],['SOH / LOH',`${binarySize(metrics.sohBytes)} / ${binarySize(metrics.lohBytes)}`],['GC 0 / 1 / 2',collections],['Alloc',metrics.allocationRateBytesPerSecond==null?'sampling…':`${binarySize(metrics.allocationRateBytesPerSecond)}/s`]];
  target.replaceChildren(...entries.map(([label,value])=>{const item=document.createElement('span'),strong=document.createElement('b');strong.textContent=`${label} `;item.append(strong,document.createTextNode(value));return item;}));
  target.title=`${metrics.scope}. ${metrics.note}`;
}

// Refreshes one non-collecting host sample; overlapping intervals coalesce and failures leave user-action telemetry untouched.
async function refreshRuntimeMetrics(){
  if(runtimeMetricsBusy)return runtimeMetrics;
  runtimeMetricsBusy=true;
  try{
    runtimeMetrics=await quietRpc('runtime-metrics');paintRuntimeFooter();
    if(page==='dataset'&&runtimeView==='dotnet')paintRuntimeInstance();
    return runtimeMetrics;
  }catch(error){const target=document.querySelector('#runtime-footer');if(target&&!runtimeMetrics)target.textContent='Runtime metrics unavailable · retrying';return runtimeMetrics;}
  finally{runtimeMetricsBusy=false;}
}
window.chrome.webview.addEventListener('message',event=>{
  const data=event.data;
  if(data.kind==='operation-phase'){MeridianActivity.phase(pending.get(data.id)?.action,data.phase);return;}
  if(data.kind==='generation-progress'){
    generationProgressSeen=true;
    const s=data.state,bar=document.querySelector('.progress i'),label=document.querySelector('#generation-status');
    if(bar){const determinate=s.phase==='Generating orders';bar.parentElement.classList.toggle('indeterminate',!determinate&&s.phase!=='Ready');bar.style.width=determinate?`${Math.min(100,s.completedOrders/s.requestedOrders*100)}%`:s.phase==='Ready'?'100%':'35%';}
    if(s.phase.startsWith('Indexing')&&bar?.parentElement.classList.contains('indeterminate'))generationIndexProgressSeen=true;
    MeridianActivity.phase([...pending.values()].find(p=>p.command==='generate-profile')?.action,`${s.phase} · ${number(s.completedOrders)} / ${number(s.requestedOrders)} orders · ${number(s.completedLines)} lines`);
    if(label)label.textContent=`${s.phase} · ${number(s.completedOrders)} / ${number(s.requestedOrders)} orders · ${number(s.completedLines)} lines · ${(s.elapsedMs/1000).toFixed(1)}s`;
    if(generationTestCancel&&!generationCancelSent&&s.completedOrders>=256){generationCancelSent=true;document.querySelector('#cancel-generation').click();}
    return;
  }
  if(data.kind==='dataset-audit-progress'){
    const item=pending.get(data.id);if(!item)return;
    datasetAuditProgressSeen=true;datasetAuditProgress=data.state;
    MeridianActivity.phase(item.action,`${data.state.phase} · ${data.state.detail}`);
    updateDatasetAuditProgress();return;
  }
  if(data.kind==='diagnostic'){
    diagnostics.unshift(data);diagnostics.length=Math.min(diagnostics.length,20);
    document.querySelector('#diagnostic-rows').innerHTML=diagnostics.map(d=>`<div class="diagnostic">${escape(d.command)} · Abraxas ${(d.timing?.databaseMs||0).toFixed(1)} ms · App/calc ${(d.timing?.calculationMs||0).toFixed(1)} ms · queue ${(d.timing?.queueMs||0).toFixed(1)} ms · JSON ${d.serializationMs.toFixed(1)} ms · ${number(d.bytes)} bytes${d.returnedRows!=null?' · '+number(d.returnedRows)+' rows':''}</div>`).join('');return;
  }
  if(data.kind==='progress'){const p=document.querySelector('.progress i');if(p)p.style.width=`${Math.min(100,data.count/generation*100)}%`;const s=document.querySelector('#generation-status');if(s)s.textContent=`${number(data.count)} of ${number(generation)} orders written`;return;}
  const item=pending.get(data.id);if(!item)return;pending.delete(data.id);
  MeridianActivity.complete(item.action,data,item.started);
  if(data.ok)item.resolve(data.result);else {const error=new Error(data.error);if(data.superseded)error.name='AbortError';item.reject(error);}
});
function heading(title,subtitle,action=''){return `<div class="page-heading"><div><p class="eyebrow">MERIDIAN / OPERATIONS</p><h1>${title}</h1><p class="subtitle">${subtitle}</p></div><div class="heading-actions">${action}</div></div>`;}
function badge(status){return `<span class="badge ${escape(status.toLowerCase())}">${escape(status)}</span>`;}
function toast(message){if(message==='Request superseded.')return;const el=document.querySelector('#toast');el.textContent=message;el.hidden=false;setTimeout(()=>el.hidden=true,3500);}
// Opens the shared modal evidence surface used by every Meridian feature that reveals developer code or technical detail.
function openLightbox(title,eyebrow,html,source=null){
  lightbox.dataset.returnFocus=source?.id||'';document.querySelector('#lightbox-title').textContent=title;document.querySelector('#lightbox-eyebrow').textContent=eyebrow;document.querySelector('#lightbox-content').innerHTML=html;lightbox.hidden=false;lightbox.querySelector('.lightbox-close').focus();
}
function closeLightbox(){if(lightbox.hidden)return;lightbox.hidden=true;const source=lightbox.dataset.returnFocus&&document.getElementById(lightbox.dataset.returnFocus);if(source)source.focus();}

// Presents native Abraxas display/canonical projections separately from Meridian's illustrative reconstruction of fluent C#.
function conditionEvidenceHtml(condition){
  return `<div class="condition-provenance"><strong>These semantic views come directly from the completed Abraxas condition.</strong><span>Meridian does not reconstruct or reinterpret either string.</span></div><section class="condition-native-block"><small>ABRAXAS DEVELOPER VIEW · condition.ToDisplayString(new ConditionTextOptions { Audience = Developer, Layout = Outline })</small><pre><code>${escape(condition.developerDisplay)}</code></pre></section><section class="canonical-block"><small>ABRAXAS CANONICAL DIAGNOSTIC · condition.ToCanonicalString()</small><code>${escape(condition.canonical)}</code></section><section class="condition-code-block"><small>EQUIVALENT FLUENT C# · GENERATED BY MERIDIAN FROM THE EDITABLE UI MODEL</small><pre><code>${escape(condition.code)}</code></pre><p>This sample code demonstrates how a developer can construct the same semantics; it is not emitted by Abraxas.</p></section>`;
}

// Explains the independently selected result-reading strategy without implying that ConditionBuilder controls materialization.
function resultShapeEvidenceHtml(shape){
  return `<section class="condition-code-block result-shape-code"><small>SELECTED RESULT SHAPE · ${escape(shape.label)}</small><p>${escape(shape.description)}</p><pre><code>${escape(shape.code)}</code></pre></section>`;
}

// Closes the one floating Meridian listbox and optionally returns keyboard focus to its trigger.
function closeMeridianSelect(restoreFocus=false){
  if(!activeMeridianSelect)return;
  const state=activeMeridianSelect;activeMeridianSelect=null;
  state.trigger.setAttribute('aria-expanded','false');state.wrapper.classList.remove('is-open');state.list.remove();
  if(restoreFocus&&state.trigger.isConnected)state.trigger.focus();
}

// Positions a floating listbox outside card and pill overflow boundaries while retaining the trigger width.
function positionMeridianSelect(state){
  const rect=state.trigger.getBoundingClientRect(),width=Math.min(innerWidth-16,Math.max(rect.width,190));
  state.list.style.width=`${width}px`;state.list.style.left=`${Math.max(8,Math.min(rect.left,innerWidth-width-8))}px`;
  state.list.style.top=`${rect.bottom+5}px`;
  const listRect=state.list.getBoundingClientRect();
  if(listRect.bottom>innerHeight-8&&rect.top>innerHeight-rect.bottom)state.list.style.top=`${Math.max(8,rect.top-listRect.height-5)}px`;
}

// Enhances one native select with an accessible Meridian trigger and listbox while keeping the select as the authoritative value source.
function enhanceSelect(select){
  if(select.dataset.meridianSelect==='true'){select._meridianSelectSync?.();return;}
  select.dataset.meridianSelect='true';select.classList.add('meridian-select-source');select.tabIndex=-1;
  const wrapper=document.createElement('span'),trigger=document.createElement('button'),value=document.createElement('span'),list=document.createElement('span');
  const listID=`meridian-listbox-${++meridianSelectSerial}`;
  wrapper.className='meridian-select';trigger.type='button';trigger.className='meridian-select-trigger';trigger.setAttribute('aria-haspopup','listbox');trigger.setAttribute('aria-expanded','false');trigger.setAttribute('aria-controls',listID);
  value.className='meridian-select-value';trigger.append(value);list.id=listID;list.className='meridian-select-list';list.setAttribute('role','listbox');
  select.parentNode.insertBefore(wrapper,select);wrapper.append(select,trigger);
  const rebuild=()=>{
    list.replaceChildren(...Array.from(select.options,(option,index)=>{const item=document.createElement('button');item.type='button';item.className='meridian-select-option';item.setAttribute('role','option');item.setAttribute('aria-selected',String(option.selected));item.disabled=option.disabled;item.dataset.optionIndex=String(index);item.textContent=option.textContent;return item;}));
  };
  const sync=()=>{const option=select.selectedOptions[0]||select.options[0];value.textContent=option?.textContent||'';trigger.disabled=select.disabled;trigger.title=option?.textContent||'';rebuild();};
  const choose=item=>{const option=select.options[Number(item.dataset.optionIndex)];if(!option||option.disabled)return;select.value=option.value;sync();select.dispatchEvent(new Event('change',{bubbles:true}));closeMeridianSelect(true);};
  const open=()=>{
    if(trigger.disabled)return;if(activeMeridianSelect?.wrapper===wrapper){closeMeridianSelect(true);return;}closeMeridianSelect();rebuild();document.body.append(list);
    const state={wrapper,trigger,list};activeMeridianSelect=state;wrapper.classList.add('is-open');trigger.setAttribute('aria-expanded','true');positionMeridianSelect(state);
    const selected=list.querySelector('[aria-selected="true"]')||list.querySelector('button:not(:disabled)');selected?.focus();
  };
  trigger.onclick=open;trigger.onkeydown=event=>{if(event.key==='ArrowDown'||event.key==='ArrowUp'){event.preventDefault();open();}};
  list.onclick=event=>{const item=event.target.closest('.meridian-select-option');if(item)choose(item);};
  list.onkeydown=event=>{
    const options=Array.from(list.querySelectorAll('.meridian-select-option:not(:disabled)')),at=options.indexOf(document.activeElement);
    if(event.key==='Escape'){event.preventDefault();event.stopPropagation();closeMeridianSelect(true);return;}
    if(event.key==='Enter'||event.key===' '){event.preventDefault();choose(document.activeElement);return;}
    let next=null;if(event.key==='ArrowDown')next=options[Math.min(options.length-1,at+1)];else if(event.key==='ArrowUp')next=options[Math.max(0,at-1)];else if(event.key==='Home')next=options[0];else if(event.key==='End')next=options.at(-1);
    if(next){event.preventDefault();next.focus();}
  };
  select.addEventListener('change',sync);select.addEventListener('focus',()=>trigger.focus());select._meridianSelectSync=sync;sync();
}

// Applies the shared control treatment to dynamically rendered views without changing their native value and change-event contracts.
function enhanceSelects(root=document){if(root.matches?.('select'))enhanceSelect(root);root.querySelectorAll?.('select').forEach(enhanceSelect);}
function showError(error){if(error.name==='AbortError')return;content.innerHTML=`<div class="error"><h2>Unable to complete this request</h2><p>${escape(error.message)}</p><button class="secondary" id="retry">Try again</button></div>`;document.querySelector('#retry').onclick=()=>navigate(page);}
async function navigate(next){
  const ticket=++navigation;++detailRequest;++gridRequest;++orderCardRequest;++customerOutlineSerial;customerOutlineRequests.clear();
  clearTimeout(gridSearchDebounce);gridSearchDebounce=0;closeMeridianSelect();
  void rpc('invalidate-reads').catch(showError);
  page=next;offset=0;query='';sort='';descending=false;drawer.hidden=true;
  closeLightbox();
  document.querySelectorAll('nav button').forEach(b=>b.classList.toggle('active',b.dataset.page===page));
  document.querySelector('#crumb').textContent=labels[page];
  window.scrollTo({left:0,top:0,behavior:'auto'});
  content.classList.remove('has-docked-pager');
  content.innerHTML='<div class="loading">Loading workspace…</div>';
  try{
    if(page==='overview'){
      const state=await rpc('readiness');if(ticket!==navigation)return;
      if(state.recoveryMigrationRequired){
        page='dataset';runtimeView='recordbase';document.querySelectorAll('nav button').forEach(b=>b.classList.toggle('active',b.dataset.page===page));document.querySelector('#crumb').textContent=labels[page];
        const data=await rpc('dataset');if(ticket!==navigation)return;overview=data;renderDataset();return;
      }
      const data=await rpc(state.manifest.phase==='Ready'?'overview':'dataset');if(ticket!==navigation)return;overview=data;await renderOverview();
    }
    else if(page==='dataset'){const data=await rpc('dataset');if(ticket!==navigation)return;overview=data;renderDataset();}
    else if(page==='analysis')await renderAnalysis();
    else if(page==='search')await renderSearchWorkbench();
    else await renderGrid();
  }catch(error){if(ticket===navigation)showError(error);}
}
function metrics(){return `<div class="metrics">${[
 ['Booked revenue',money(overview.revenue),'Non-cancelled orders','↗'],
 ['Total orders',number(overview.orders),`${number(overview.processing)} processing`,'▤'],
 ['Customer accounts',number(overview.customers),'Across five regions','♧'],
 ['Low stock positions',number(overview.lowStock),'Fewer than 30 units on hand','▧']
 ].map(([label,value,note,icon])=>`<div class="metric"><div class="metric-label">${label}<span class="metric-icon">${icon}</span></div><div class="metric-value">${value}</div><div class="metric-note">${note}</div></div>`).join('')}</div>`;}
async function renderOverview(){
  const ticket=navigation;
  document.querySelector('#dataset-footer').textContent=`Abraxas connected · ${number(overview.lines)} normalized line records · ${overview.manifest.phase}`;
  if(overview.manifest.phase!=='Ready'){renderDataset();return;}
  const regions=overview.regions,max=Math.max(...regions.map(x=>Number(x.total)),1),statuses=overview.statuses;
  content.innerHTML=heading('Your business, in focus.','A clear view of your orders, customers and inventory.','<span class="period">Generated trading year · 2025–26</span>')+`<section class="overview-evidence" aria-label="Overview calculation source"><div><i></i><strong>LIVE ABRAXAS CALCULATIONS</strong></div><p>Recomputed from the current RecordStores whenever this page opens: grouped order count and value, indexed entity counts, and the indexed low-stock condition.</p><small>No generated totals or cached application snapshot.</small></section>`+metrics()+
   `<div class="dashboard-grid"><section class="card"><div class="card-head"><div><h2>Order value by region</h2><p class="card-sub">All order statuses · grouped from live records</p></div><span class="tag">USD</span></div><div class="bar-chart">${regions.map(r=>`<div class="bar-column"><span class="bar-value">${money(r.total)}</span><div class="bar" style="height:${Number(r.total)/max*145}px"></div><span class="bar-label">${escape(r.key)}</span></div>`).join('')}</div><div class="chart-foot">${number(overview.orders)} orders · Abraxas grouped aggregation</div></section>
   <section class="card"><div class="card-head"><div><h2>Order pipeline</h2><p class="card-sub">Where your orders stand</p></div><span class="tag">LIVE</span></div><div class="status-list">${statuses.map(s=>`<div class="status-item"><span>${escape(s.key)}</span><div class="track"><div class="fill" style="width:${Number(s.count)/Math.max(1,Number(overview.orders))*100}%"></div></div><b>${number(s.count)}</b></div>`).join('')}</div></section></div>
   <section class="card"><div class="section-heading"><div><h2>Orders at a glance</h2><p>Open an order to explore its customer and separately stored line items.</p></div><button class="text-button" id="all-orders">View all orders →</button></div><div id="recent-orders"></div></section>`;
  document.querySelector('#all-orders').onclick=()=>navigate('orders');
  const response=await rpc('page',{entity:'orders',offset:0,query:''});
  if(ticket!==navigation||page!=='overview'||!document.querySelector('#recent-orders'))return;
  document.querySelector('#recent-orders').innerHTML=orderTable(response.rows.slice(0,6));bindOrderRows();
  await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame);
}
function orderTable(rows){return `<div class="table-scroll"><table><thead><tr><th>Order</th><th>Region</th><th>Placed on</th><th>Status</th><th class="right">Order value</th><th></th></tr></thead><tbody>${rows.map(r=>`<tr class="clickable" tabindex="0" data-order="${escape(r.id)}"><td class="strong">${escape(r.value.number)}</td><td>${escape(r.value.region)}</td><td>${new Date(r.value.orderedAt).toLocaleDateString('en-US',{month:'short',day:'numeric',year:'numeric'})}</td><td>${badge(r.value.status)}</td><td class="strong">${money(r.value.total)}</td><td>↗</td></tr>`).join('')}</tbody></table></div>`;}
function bindOrderRows(){document.querySelectorAll('[data-order]').forEach(row=>{row.onclick=()=>openOrder(row.dataset.order);row.onkeydown=e=>{if(e.key==='Enter')openOrder(row.dataset.order);};});}

// Keeps high-scale Orders and Customers paging reachable while respecting the dynamically sized performance footer and centered content width.
function layoutDockedPager(){
  const pager=content.querySelector('.pager-docked'),performanceBar=document.querySelector('#performance-bar');
  if(!pager||!performanceBar){content.classList.remove('has-docked-pager');return;}
  const card=pager.closest('.card'),bounds=card.getBoundingClientRect(),footerHeight=Math.ceil(performanceBar.getBoundingClientRect().height);
  document.documentElement.style.setProperty('--performance-footer-height',`${footerHeight}px`);
  pager.style.left=`${Math.round(bounds.left)}px`;pager.style.width=`${Math.round(bounds.width)}px`;pager.style.bottom=`${footerHeight}px`;
  content.classList.add('has-docked-pager');
}

// Renders the same bounded, links-suppressed order page as cards; relationship hydration begins only when the user expands one card.
function orderObjectCards(rows,route){
  return `<div class="object-page-route"><span><b>Collapsed route</b> ${escape(route)}</span><span>50 bounded headers · no line objects loaded</span></div><div class="order-object-grid">${rows.map(r=>`<article class="order-object-card" data-order-card="${escape(r.id)}"><button class="order-object-summary" type="button" data-expand-order="${escape(r.id)}" aria-expanded="false" aria-controls="order-object-${escape(r.id)}"><div class="order-object-title"><p class="eyebrow">SALESORDER / NORMALIZED HEADER</p><h2>${escape(r.value.number)}</h2><span>${escape(r.value.region)}</span></div>${badge(r.value.status)}<div class="order-object-facts"><span><small>PLACED</small>${new Date(r.value.orderedAt).toLocaleDateString('en-US',{month:'short',day:'numeric',year:'numeric'})}</span><span><small>VALUE</small><strong>${money(r.value.total)}</strong></span></div><div class="order-object-cue"><i>+</i><span><b>Hydrate object graph</b><small>Orders.Get(id) · .Lines on demand</small></span></div></button><div class="order-object-detail" id="order-object-${escape(r.id)}" role="region" aria-live="polite" hidden></div></article>`).join('')}</div>`;
}

// Keeps the view choice while preserving the current query, sort and page, so both presentations describe the identical bounded result set.
async function switchOrderView(next){
  if(next!=='table'&&next!=='cards')throw new Error('Unknown order view.');
  if(orderView===next)return;
  ++orderCardRequest;orderView=next;
  await renderGrid();
}

// Binds one explicit hydration action per collapsed card; a fresh expansion always re-reads the live object graph instead of caching relationship state in the UI.
function bindOrderCards(){
  document.querySelectorAll('[data-expand-order]').forEach(button=>button.onclick=()=>toggleOrderCard(button));
}

// Expands one order by calling the ordinary detail route, whose Orders.Get materializes .Lines through the registered Abraxas/Inheto property link.
async function toggleOrderCard(button){
  const card=button.closest('.order-object-card'),detail=card.querySelector('.order-object-detail');
  if(button.getAttribute('aria-expanded')==='true'){
    ++orderCardRequest;button.setAttribute('aria-expanded','false');card.classList.remove('is-expanded');detail.hidden=true;detail.replaceChildren();
    button.querySelector('.order-object-cue b').textContent='Hydrate object graph';return;
  }
  document.querySelectorAll('.order-object-card.is-expanded').forEach(open=>{open.classList.remove('is-expanded');const control=open.querySelector('[data-expand-order]'),region=open.querySelector('.order-object-detail');control.setAttribute('aria-expanded','false');control.querySelector('.order-object-cue b').textContent='Hydrate object graph';region.hidden=true;region.replaceChildren();});
  const ticket=++orderCardRequest,id=button.dataset.expandOrder;
  const activity=MeridianActivity.begin('Hydrate SalesOrder.Lines');
  button.disabled=true;button.setAttribute('aria-expanded','true');button.querySelector('.order-object-cue b').textContent='Composing object graph';card.classList.add('is-expanded');detail.hidden=false;
  detail.innerHTML='<div class="object-card-loading"><i class="wait-spinner"></i><div><strong>Abraxas/Inheto is materializing SalesOrder.Lines</strong><span>Resolving packed identities against the separate order-lines store…</span></div></div>';
  try{
    const response=await rpc('detail',{id}),order=response.order;
    if(ticket!==orderCardRequest||page!=='orders'||orderView!=='cards'||!card.isConnected)return;
    const lines=order.lines||[];
    detail.innerHTML=`<div class="object-composition-route"><div><small>PARENT RECORDSTORE</small><strong>orders</strong><span>SalesOrder header + packed ordered line identities</span></div><b>ABRAXAS + INHETO <i>→</i></b><div><small>RELATED RECORDSTORE</small><strong>order-lines</strong><span>${number(lines.length)} OrderLine objects assigned to .Lines</span></div></div><div class="object-result-heading"><div><p class="eyebrow">COMPLETED APPLICATION OBJECT</p><h3>SalesOrder.Lines</h3><span>${escape(response.customer.name)} · separately requested customer projection</span></div><div class="object-card-timing">Measuring completed action…</div></div>${lines.length?`<div class="object-line-grid">${lines.map(line=>`<div><span><b>${escape(line.sku)}</b><small>Product ${escape(line.productID)}</small></span><span>${number(line.quantity)} × ${money(line.unitPrice)}</span><strong>${money(line.total)}</strong></div>`).join('')}</div>`:'<div class="empty">The completed .Lines collection is empty.</div>'}<div class="object-result-foot"><code>SalesOrder order = Orders.Get(id);</code><span>.Lines was complete before Get returned · no repository join, tracking context, or post-load mapper</span></div>`;
    button.querySelector('.order-object-cue b').textContent='Collapse object graph';
    await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame);
    const timing=MeridianActivity.current(),target=detail.querySelector('.object-card-timing');
    if(target&&timing.id===activity.id&&timing.done)target.innerHTML=`<b>${number(lines.length)} lines hydrated</b><span>Abraxas ${duration(timing.database)} · app/calc ${duration(timing.calculation)} · UI ${duration(timing.ui)} · total ${duration(timing.total)}</span>`;
  }catch(error){
    if(ticket!==orderCardRequest)return;
    detail.innerHTML=`<div class="error"><strong>Object graph could not be materialized.</strong><p>${escape(error.message)}</p></div>`;button.querySelector('.order-object-cue b').textContent='Retry object graph';
  }finally{if(button.isConnected)button.disabled=false;}
}

// Keeps every customer master row scalar and bounded; the adjacent hidden band is only a target for an explicit complete-object request.
function customerTable(rows){
  return `<div class="table-scroll customer-table-scroll"><table class="customer-master-table"><thead><tr><th class="outline-column"><span class="sr-only">Object graph</span></th><th>Code</th><th>Name</th><th>Region</th><th>Email</th><th class="right">Credit limit</th></tr></thead><tbody>${rows.map(r=>`<tr class="customer-master-row" tabindex="0" data-customer-row="${escape(r.id)}" data-edit="${escape(r.id)}"><td class="outline-cell"><button class="customer-outline-toggle" type="button" data-expand-customer="${escape(r.id)}" aria-expanded="false" aria-controls="customer-outline-${escape(r.id)}" aria-label="Expand ${escape(r.value.name)} object graph"><span>+</span></button></td><td class="strong">${escape(r.value.code)}</td><td>${escape(r.value.name)}</td><td>${escape(r.value.region)}</td><td>${escape(r.value.email)}</td><td class="strong right">${money(r.value.creditLimit)}</td></tr><tr class="customer-detail-row" data-customer-detail-row="${escape(r.id)}" hidden><td colspan="6"><div class="customer-outline-region" id="customer-outline-${escape(r.id)}" role="region" aria-live="polite"></div></td></tr>`).join('')}</tbody></table></div>`;
}

// Initializes filter controls once from live indexed dataset boundaries while retaining the user's choices across customer-page rerenders.
function initializeCustomerHydration(options){
  if(customerHydration.initialized)return;
  customerHydration.initialized=true;
  customerHydration.dateFrom=0;
  customerHydration.dateThrough=Math.max(0,Math.round((Date.parse(`${options.maximumDate}T00:00:00Z`)-Date.parse(`${options.minimumDate}T00:00:00Z`))/86400000));
  customerHydration.minimumTotal=Number(options.minimumTotal);
  customerHydration.maximumTotal=Number(options.maximumTotal);
  customerHydration.recentCount=Math.min(10,Number(options.recentMaximum));
}

// Converts one day offset into the invariant calendar date consumed by the bridge policy validator.
function customerHydrationDate(offset){const date=new Date(`${customerHydrationOptions.minimumDate}T00:00:00Z`);date.setUTCDate(date.getUTCDate()+Number(offset));return date.toISOString().slice(0,10);}

// Formats one dataset-relative date offset without allowing browser timezone conversion to move the calendar day.
function customerHydrationDateLabel(offset){return new Date(`${customerHydrationDate(offset)}T12:00:00Z`).toLocaleDateString('en-US',{month:'short',day:'numeric',year:'numeric',timeZone:'UTC'});}

// Produces the allow-listed immutable policy payload placed in ambient scope around one ordinary Customers.Get call.
function customerHydrationPayload(){return {enabled:customerHydration.enabled,mode:customerHydration.mode,fromDate:customerHydrationDate(customerHydration.dateFrom),throughDate:customerHydrationDate(customerHydration.dateThrough),recentCount:customerHydration.recentCount,minimumTotal:customerHydration.minimumTotal,maximumTotal:customerHydration.maximumTotal};}

// Describes the currently selected policy for status text and synchronized multi-row refresh actions.
function customerHydrationLabel(){
  if(!customerHydration.enabled)return 'Full history';
  if(customerHydration.mode==='date')return `${customerHydrationDateLabel(customerHydration.dateFrom)}–${customerHydrationDateLabel(customerHydration.dateThrough)}`;
  if(customerHydration.mode==='recent')return `Most recent ${number(customerHydration.recentCount)} orders`;
  return `${money(customerHydration.minimumTotal)}–${money(customerHydration.maximumTotal)}`;
}

// Renders the three policy choices separately from the scalar grid so a policy change can preserve and rehydrate every open outline.
function customerHydrationControl(){
  const options=customerHydrationOptions,days=Math.max(0,Math.round((Date.parse(`${options.maximumDate}T00:00:00Z`)-Date.parse(`${options.minimumDate}T00:00:00Z`))/86400000));
  const valueSpan=Math.max(1,Number(options.maximumTotal)-Number(options.minimumTotal));
  const valueStep=Math.max(1,Math.round(valueSpan/100/100)*100);
  const modeLabel={date:'Date window',recent:'Recent count',value:'Order value'}[customerHydration.mode];
  let sliders='';
  if(customerHydration.mode==='date')sliders=`<div class="hydration-range-pair"><label>From <output id="hydration-from-output">${escape(customerHydrationDateLabel(customerHydration.dateFrom))}</output><input id="hydration-date-from" type="range" min="0" max="${days}" step="1" value="${customerHydration.dateFrom}"></label><label>Through <output id="hydration-through-output">${escape(customerHydrationDateLabel(customerHydration.dateThrough))}</output><input id="hydration-date-through" type="range" min="0" max="${days}" step="1" value="${customerHydration.dateThrough}"></label></div>`;
  else if(customerHydration.mode==='recent')sliders=`<label class="hydration-single-range">Orders to hydrate <output id="hydration-recent-output">${number(customerHydration.recentCount)} newest</output><input id="hydration-recent" type="range" min="1" max="${Number(options.recentMaximum)}" step="1" value="${customerHydration.recentCount}"></label>`;
  else sliders=`<div class="hydration-range-pair"><label>Minimum <output id="hydration-minimum-output">${money(customerHydration.minimumTotal)}</output><input id="hydration-value-minimum" type="range" min="${Number(options.minimumTotal)}" max="${Number(options.maximumTotal)}" step="${valueStep}" value="${customerHydration.minimumTotal}"></label><label>Maximum <output id="hydration-maximum-output">${money(customerHydration.maximumTotal)}</output><input id="hydration-value-maximum" type="range" min="${Number(options.minimumTotal)}" max="${Number(options.maximumTotal)}" step="${valueStep}" value="${customerHydration.maximumTotal}"></label></div>`;
  return `<section id="customer-hydration-control" class="customer-hydration-control ${customerHydration.enabled?'is-filtered':'is-full'}"><div class="hydration-control-heading"><div><p class="eyebrow">AMBIENT RELATIONSHIP POLICY</p><h2>Shape <code>Customer.Orders</code> at hydration time</h2><span>The domain object and <code>Customers.Get(id)</code> call stay unchanged.</span></div><button id="hydration-enabled" class="hydration-state" type="button" aria-pressed="${!customerHydration.enabled}" aria-label="${customerHydration.enabled?'Restore full order history':'Full order history is active'}"><i></i><span>Full history</span><small>${customerHydration.enabled?'restore':'active'}</small></button></div><div class="hydration-control-body"><div class="hydration-mode-pill" role="group" aria-label="Customer order hydration policy"><button type="button" data-hydration-mode="date" aria-pressed="${customerHydration.enabled&&customerHydration.mode==='date'}">Date window</button><button type="button" data-hydration-mode="recent" aria-pressed="${customerHydration.enabled&&customerHydration.mode==='recent'}">Recent count</button><button type="button" data-hydration-mode="value" aria-pressed="${customerHydration.enabled&&customerHydration.mode==='value'}">Order value</button></div><div class="hydration-slider ${customerHydration.enabled?'':'is-standby'}">${sliders}</div></div><div class="hydration-control-foot"><span><b>Current scope</b> <span id="hydration-current-scope">${escape(customerHydrationLabel())}</span></span><span id="hydration-control-help">${customerHydration.enabled?'Changing policy or range rehydrates every expanded customer.':`Adjust a ${modeLabel} value or choose a policy to activate filtered hydration.`}</span></div></section>`;
}

// Replaces only the policy panel after a choice, leaving scalar and expanded rows intact.
function renderCustomerHydrationControl(){
  const current=document.querySelector('#customer-hydration-control');if(!current)return;
  current.outerHTML=customerHydrationControl();bindCustomerHydrationControls();
}

// Makes an edited filter visibly active without replacing its live range input while the user is dragging it.
function activateCustomerHydrationEdit(mode){
  const wasFull=!customerHydration.enabled;
  customerHydration.enabled=true;customerHydration.mode=mode;
  const control=document.querySelector('#customer-hydration-control'),full=document.querySelector('#hydration-enabled');
  control?.classList.remove('is-full');control?.classList.add('is-filtered');
  if(full){full.setAttribute('aria-pressed','false');full.setAttribute('aria-label','Restore full order history');full.querySelector('small').textContent='restore';}
  document.querySelectorAll('[data-hydration-mode]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.hydrationMode===mode)));
  document.querySelector('.hydration-slider')?.classList.remove('is-standby');
  const scope=document.querySelector('#hydration-current-scope'),help=document.querySelector('#hydration-control-help');
  if(scope)scope.textContent=customerHydrationLabel();
  if(help)help.textContent='Changing policy or range rehydrates every expanded customer.';
  if(wasFull)cueCustomerFullHistory();
}

// Briefly identifies the now-available Full history reset, with a static emphasis under reduced-motion preferences.
function cueCustomerFullHistory(){
  const button=document.querySelector('#hydration-enabled');if(!button)return;
  button.classList.remove('is-reset-cue');void button.offsetWidth;button.classList.add('is-reset-cue');
  clearTimeout(customerHydrationCueTimer);customerHydrationCueTimer=setTimeout(()=>button.isConnected&&button.classList.remove('is-reset-cue'),1800);
}

// Binds policy, range, and full-history controls; any filter edit activates its policy and commits one refresh on change.
function bindCustomerHydrationControls(){
  const enabled=document.querySelector('#hydration-enabled');if(!enabled)return;
  enabled.onclick=()=>{if(!customerHydration.enabled)return;customerHydration.enabled=false;return applyCustomerHydrationPolicy();};
  document.querySelectorAll('[data-hydration-mode]').forEach(button=>button.onclick=()=>{customerHydration.mode=button.dataset.hydrationMode;customerHydration.enabled=true;return applyCustomerHydrationPolicy(true);});
  const dateFrom=document.querySelector('#hydration-date-from'),dateThrough=document.querySelector('#hydration-date-through');
  if(dateFrom){dateFrom.oninput=()=>{customerHydration.dateFrom=Math.min(Number(dateFrom.value),customerHydration.dateThrough);dateFrom.value=customerHydration.dateFrom;document.querySelector('#hydration-from-output').textContent=customerHydrationDateLabel(customerHydration.dateFrom);activateCustomerHydrationEdit('date');};dateFrom.onchange=()=>applyCustomerHydrationPolicy(true);}
  if(dateThrough){dateThrough.oninput=()=>{customerHydration.dateThrough=Math.max(Number(dateThrough.value),customerHydration.dateFrom);dateThrough.value=customerHydration.dateThrough;document.querySelector('#hydration-through-output').textContent=customerHydrationDateLabel(customerHydration.dateThrough);activateCustomerHydrationEdit('date');};dateThrough.onchange=()=>applyCustomerHydrationPolicy(true);}
  const recent=document.querySelector('#hydration-recent');if(recent){recent.oninput=()=>{customerHydration.recentCount=Number(recent.value);document.querySelector('#hydration-recent-output').textContent=`${number(customerHydration.recentCount)} newest`;activateCustomerHydrationEdit('recent');};recent.onchange=()=>applyCustomerHydrationPolicy(true);}
  const minimum=document.querySelector('#hydration-value-minimum'),maximum=document.querySelector('#hydration-value-maximum');
  if(minimum){minimum.oninput=()=>{customerHydration.minimumTotal=Math.min(Number(minimum.value),customerHydration.maximumTotal);minimum.value=customerHydration.minimumTotal;document.querySelector('#hydration-minimum-output').textContent=money(customerHydration.minimumTotal);activateCustomerHydrationEdit('value');};minimum.onchange=()=>applyCustomerHydrationPolicy(true);}
  if(maximum){maximum.oninput=()=>{customerHydration.maximumTotal=Math.max(Number(maximum.value),customerHydration.minimumTotal);maximum.value=customerHydration.maximumTotal;document.querySelector('#hydration-maximum-output').textContent=money(customerHydration.maximumTotal);activateCustomerHydrationEdit('value');};maximum.onchange=()=>applyCustomerHydrationPolicy(true);}
}

// Applies one policy to every currently expanded master row without rerendering or collapsing the customer grid.
async function applyCustomerHydrationPolicy(cueFullHistory=false){
  renderCustomerHydrationControl();
  if(cueFullHistory&&customerHydration.enabled)cueCustomerFullHistory();
  const buttons=Array.from(document.querySelectorAll('[data-expand-customer][aria-expanded="true"]'));
  if(buttons.length===0)return;
  const activity=MeridianActivity.begin(`Apply ${customerHydrationLabel()} to ${buttons.length} expanded customer${buttons.length===1?'':'s'}`);
  await Promise.all(buttons.map(button=>hydrateCustomerOutline(button,activity)));
  await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame);paintCustomerOutlineTimings(activity);
}

// Separates the outline control from the existing scalar editor gesture while retaining keyboard access to both actions.
function bindCustomerRows(){
  document.querySelectorAll('[data-customer-row]').forEach(row=>{
    row.onclick=event=>{if(!event.target.closest('[data-expand-customer]'))openEditor('customers',row.dataset.customerRow);};
    row.onkeydown=event=>{if(event.key==='Enter'&&!event.target.closest('[data-expand-customer]'))openEditor('customers',row.dataset.customerRow);};
  });
  document.querySelectorAll('[data-expand-customer]').forEach(button=>button.onclick=event=>{event.stopPropagation();return toggleCustomerOutline(button);});
}

// Expands or collapses one outline; expansion delegates to the same refreshable ambient-policy hydration path used by control changes.
async function toggleCustomerOutline(button){
  const id=button.dataset.expandCustomer,master=button.closest('[data-customer-row]'),detail=document.querySelector(`[data-customer-detail-row="${CSS.escape(id)}"]`),region=detail.querySelector('.customer-outline-region');
  if(button.getAttribute('aria-expanded')==='true'){
    customerOutlineRequests.set(id,++customerOutlineSerial);
    button.setAttribute('aria-expanded','false');button.querySelector('span').textContent='+';master.classList.remove('is-expanded');detail.hidden=true;region.replaceChildren();return;
  }
  const activity=MeridianActivity.begin(`Hydrate Customer.Orders · ${customerHydrationLabel()}`);
  await hydrateCustomerOutline(button,activity);
  await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame);paintCustomerOutlineTimings(activity);
}

// Materializes one fresh Customer under the current ambient policy; per-row tickets prevent stale concurrent refreshes from winning.
async function hydrateCustomerOutline(button,activity){
  const id=button.dataset.expandCustomer,master=button.closest('[data-customer-row]'),detail=document.querySelector(`[data-customer-detail-row="${CSS.escape(id)}"]`),region=detail.querySelector('.customer-outline-region');
  const ticket=++customerOutlineSerial;customerOutlineRequests.set(id,ticket);
  button.disabled=true;button.setAttribute('aria-expanded','true');button.querySelector('span').textContent='−';master.classList.add('is-expanded');detail.hidden=false;
  region.innerHTML=`<div class="customer-outline-loading"><i class="wait-spinner"></i><div><strong>Abraxas/Inheto is materializing Customer.Orders</strong><span>Ambient policy: ${escape(customerHydrationLabel())} · resolving indexed orders and packed line identities…</span></div></div>`;
  try{
    const response=await rpc('customer-graph',{id,hydration:customerHydrationPayload()}),customer=response.customer,orders=customer.orders||[];
    if(customerOutlineRequests.get(id)!==ticket||page!=='customers'||!detail.isConnected)return;
    const completion=response.policy.isFiltered?'POLICY-COMPLETE APPLICATION OBJECT':'COMPLETE APPLICATION OBJECT';
    region.innerHTML=`<div class="customer-relationship-route"><div><small>AMBIENT APPLICATION SCOPE</small><strong>CustomerOrderHydrationScope</strong><span>${escape(response.policy.label)} · Customers.Get(id)</span></div><i>→</i><div><small>ABRAXAS INDEX ROUTE</small><strong>${escape(response.policy.indexRoute)}</strong><span>orders store</span></div><i>→</i><div><small>PACKED IDENTITIES</small><strong>SalesOrder.Lines</strong><span>order-lines store</span></div><i>→</i><div><small>RETURNED OBJECT</small><strong>Customer.Orders</strong><span>${number(response.orderCount)} orders · ${number(response.lineCount)} lines</span></div></div><div class="customer-outline-heading"><div><p class="eyebrow">${completion}</p><h3>${escape(customer.name)} / Customer.Orders</h3><p>${escape(response.route)}</p></div><div class="customer-outline-timing" data-hydration-timing="${activity.id}" data-orders="${response.orderCount}" data-lines="${response.lineCount}">Measuring completed action…</div></div>${orders.length?`<div class="customer-outline-orders"><table><thead><tr><th>Order</th><th>Placed on</th><th>Status</th><th class="right">Lines</th><th class="right">Order value</th></tr></thead><tbody>${orders.map(order=>`<tr><td class="strong">${escape(order.number)}</td><td>${new Date(order.orderedAt).toLocaleDateString('en-US',{month:'short',day:'numeric',year:'numeric'})}</td><td>${badge(order.status)}</td><td class="right">${number((order.lines||[]).length)}</td><td class="strong right">${money(order.total)}</td></tr>`).join('')}</tbody></table></div>`:'<div class="empty">No orders match the active hydration policy.</div>'}<div class="customer-outline-proof"><div><small>WHAT MERIDIAN CODE DOES</small><code>using (CustomerOrderHydrationScope.Push(policy))<br>{ Customer customer = Customers.Get(id); }</code></div><p><b>No ORM merge or post-load filter.</b> The registered <code>.Orders</code> hook read the ambient policy before Abraxas selected children. Every returned order’s <code>.Lines</code> was ready before <code>Get</code> returned.</p></div>`;
  }catch(error){
    if(customerOutlineRequests.get(id)!==ticket)return;
    region.innerHTML=`<div class="error"><strong>Customer object graph could not be materialized.</strong><p>${escape(error.message)}</p></div>`;
    button.setAttribute('aria-expanded','false');button.querySelector('span').textContent='+';master.classList.remove('is-expanded');
  }finally{if(button.isConnected)button.disabled=false;}
}

// Writes one honest timing summary after every request in a single- or multi-row hydration action has completed and painted.
function paintCustomerOutlineTimings(activity){
  const timing=MeridianActivity.current();if(timing.id!==activity.id||!timing.done)return;
  document.querySelectorAll(`[data-hydration-timing="${activity.id}"]`).forEach(target=>target.innerHTML=`<b>${number(target.dataset.orders)} orders / ${number(target.dataset.lines)} lines</b><span>Abraxas ${duration(timing.database)} · app/calc ${duration(timing.calculation)} · UI ${duration(timing.ui)} · total ${duration(timing.total)}</span>`);
}

async function renderGrid(){
  const current=page,ticket=++gridRequest;++orderCardRequest;++customerOutlineSerial;customerOutlineRequests.clear();
  if(current==='customers'&&!customerHydrationOptions){customerHydrationOptions=await rpc('customer-hydration-options');if(page!==current||ticket!==gridRequest)return;initializeCustomerHydration(customerHydrationOptions);}
  const policy=gridSearchPolicy[current];
  const response=await rpc('page',{entity:current,offset,query,sort,descending,searchField:policy.field,searchMode:policy.mode,ignoreCase:policy.ignoreCase});if(page!==current||ticket!==gridRequest)return;
  const rows=response.rows;offset=response.offset;
  const columns={customers:['code','name','region','email','creditLimit'],products:['sku','name','category','price','reorderPoint'],inventory:['sku','warehouse','onHand','reserved'],lines:['sku','quantity','unitPrice','total','productID']};
  let table=rows.length===0?'<div class="empty">No matching records. Try another search.</div>':current==='orders'&&orderView==='cards'?orderObjectCards(rows,response.route):current==='orders'?orderTable(rows):current==='customers'?customerTable(rows):`<div class="table-scroll"><table><thead><tr>${columns[current].map(c=>`<th>${escape(c.replace(/([A-Z])/g,' $1'))}</th>`).join('')}</tr></thead><tbody>${rows.map(r=>`<tr>${columns[current].map(c=>`<td>${['price','creditLimit','unitPrice','total'].includes(c)?money(r.value[c]):escape(r.value[c])}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`;
  const viewSwitch=current==='orders'?`<div class="view-switch" role="group" aria-label="Order presentation"><button type="button" data-order-view="table" aria-pressed="${orderView==='table'}">Table</button><button type="button" data-order-view="cards" aria-pressed="${orderView==='cards'}">Object graph</button></div>`:'';
  const hydrationControl=current==='customers'?customerHydrationControl():'';
  const subtitle=current==='orders'?'Browse fast scalar headers, then hydrate a natural cross-store object only when you expand it.':current==='customers'?'Keep the domain object unchanged while ambient state shapes its normalized relationship hydration.':'Explore this independently stored domain. Scalar pages keep large graphs out of the grid.';
  const dockPager=current==='orders'||current==='customers';
  const fieldOptions=gridSearchFields[current].map(([value,label])=>`<option value="${value}" ${value===policy.field?'selected':''}>${label}</option>`).join('');
  const conditionProof=response.search?`<section class="grid-condition-proof"><div><span>ABRAXAS CONDITION</span><strong>${escape(response.search.display)}</strong><small>${escape(response.search.route)}</small><div class="grid-search-evidence"><b>${number(response.search.matchCount)} matched · ${number(rows.length)} shown</b><span>Complete search ${duration(response.search.searchCompleteMs)}</span><span>Exact count ${duration(response.search.countMs)}</span><span>First result ${response.search.firstResultMs==null?'—':duration(response.search.firstResultMs)}</span><span>Page reader ${duration(response.search.readerMs)}</span><span>Condition build ${duration(response.search.conditionBuildMs)}</span></div></div><div class="grid-condition-actions"><button id="grid-developer-code" class="pill-button" type="button">〈/〉 Show condition evidence</button></div></section>`:'';
  content.innerHTML=heading(labels[current],subtitle,viewSwitch)+`<section class="card ${current==='orders'&&orderView==='cards'?'object-view-shell':''} ${current==='customers'?'customer-view-shell':''}"><div class="toolbar search-toolbar"><div class="search-policy-pill" aria-label="Search policy"><label class="search-term"><span>Search</span><input id="search" aria-label="Search value" placeholder="Enter a value…" value="${escape(query)}" autocomplete="off" spellcheck="false"></label><label><span>Field</span><select id="search-field">${fieldOptions}</select></label><label><span>Match</span><select id="search-mode"><option value="contains" ${policy.mode==='contains'?'selected':''}>Contains</option><option value="starts" ${policy.mode==='starts'?'selected':''}>Starts with</option><option value="ends" ${policy.mode==='ends'?'selected':''}>Ends with</option><option value="exact" ${policy.mode==='exact'?'selected':''}>Exact value</option><option value="whole" ${policy.mode==='whole'?'selected':''}>Whole word</option><option value="regex" ${policy.mode==='regex'?'selected':''}>Regex</option></select></label><label><span>Case</span><select id="search-case"><option value="ignore" ${policy.ignoreCase?'selected':''}>Ignore case</option><option value="match" ${policy.ignoreCase?'':'selected'}>Match case</option></select></label></div><div class="search-toolbar-actions"><button id="search-button" class="secondary">Search</button><button id="clear-search" class="pill-button" ${query?'':'disabled'}>Clear search</button><button id="filter-as-type" class="filter-type-toggle" type="button" role="switch" aria-checked="${gridFilterAsYouType}"><i></i><span>Filter as you type<small>500 ms debounce</small></span></button><span class="tag">50 ROWS / PAGE</span></div></div>${conditionProof}${hydrationControl}${table}<div class="pager ${dockPager?'pager-docked':''}" role="navigation" aria-label="${escape(labels[current])} pagination"><span>${rows.length?`${number(offset+1)}–${number(offset+rows.length)}`:'0'} shown · ${escape(response.route)}</span><div><button id="previous" class="secondary" ${offset===0?'disabled':''}>← Previous</button><button id="next" class="secondary" ${response.more?'':'disabled'}>Next →</button></div></div></section>`;
  if(dockPager)requestAnimationFrame(layoutDockedPager);else content.classList.remove('has-docked-pager');
  const gridCode=document.querySelector('#grid-developer-code');if(gridCode)gridCode.onclick=()=>openLightbox(`${labels[current]} search evidence`,'NATIVE ABRAXAS TEXT + EQUIVALENT C#',conditionEvidenceHtml(response.search),gridCode);
  const search=async restoreFocus=>{clearTimeout(gridSearchDebounce);gridSearchDebounce=0;query=document.querySelector('#search').value;policy.field=document.querySelector('#search-field').value;policy.mode=document.querySelector('#search-mode').value;policy.ignoreCase=document.querySelector('#search-case').value==='ignore';offset=0;await renderGrid();if(restoreFocus&&page===current){const input=document.querySelector('#search');input?.focus();input?.setSelectionRange(input.value.length,input.value.length);}};
  const scheduleSearch=()=>{if(!gridFilterAsYouType)return;clearTimeout(gridSearchDebounce);gridSearchDebounce=setTimeout(()=>void search(true).catch(showError),500);};
  const searchInput=document.querySelector('#search'),clearSearch=document.querySelector('#clear-search'),filterToggle=document.querySelector('#filter-as-type');
  document.querySelector('#search-button').onclick=()=>search(false).catch(showError);
  searchInput.onkeydown=event=>{if(event.key==='Enter')void search(false).catch(showError);};
  searchInput.oninput=()=>{clearSearch.disabled=searchInput.value.length===0;scheduleSearch();};
  clearSearch.onclick=()=>{clearTimeout(gridSearchDebounce);gridSearchDebounce=0;searchInput.value='';query='';offset=0;return renderGrid().catch(showError);};
  filterToggle.onclick=()=>{gridFilterAsYouType=!gridFilterAsYouType;filterToggle.setAttribute('aria-checked',String(gridFilterAsYouType));if(gridFilterAsYouType&&searchInput.value!==query)scheduleSearch();};
  document.querySelectorAll('#search-field,#search-mode,#search-case').forEach(control=>control.onchange=scheduleSearch);
  document.querySelector('#previous').onclick=()=>{offset=Math.max(0,offset-50);renderGrid().catch(showError);};document.querySelector('#next').onclick=()=>{offset+=50;renderGrid().catch(showError);};bindOrderRows();bindOrderCards();bindCustomerRows();bindCustomerHydrationControls();
  document.querySelectorAll('[data-order-view]').forEach(button=>button.onclick=()=>switchOrderView(button.dataset.orderView).catch(showError));
  const fields={orders:['Number','OrderedAt','Total'],customers:['Name','CreditLimit'],products:['Name','Price'],inventory:['SKU','OnHand'],lines:['SKU','Total']}[current];
  document.querySelector('.toolbar').insertAdjacentHTML('beforeend',`<label class="sort-label">Sort <select id="sort"><option value="">Store order</option>${fields.map(f=>`<option value="${f}" ${f===sort?'selected':''}>${f}${response.sortOptions.find(o=>o.field===f)?.indexed?' · indexed':' · unindexed'}</option>`).join('')}</select></label><button class="secondary" id="direction" aria-label="Toggle sort direction">${descending?'↓ Desc':'↑ Asc'}</button>`);
  enhanceSelects(content);
  if(sort&&!response.sortOptions.find(o=>o.field===sort)?.indexed){
    document.querySelector('.card').insertAdjacentHTML('beforeend','<div class="notice">This field is not indexed. Sorting covers all matching records and may take longer on large datasets.</div>');
  }
  document.querySelector('#sort').onchange=e=>{sort=e.target.value;offset=0;renderGrid().catch(showError);};
  document.querySelector('#direction').onclick=()=>{descending=!descending;if(!sort)sort=fields[0];offset=0;renderGrid().catch(showError);};
  if(['products','inventory'].includes(current))document.querySelectorAll('#content tbody tr').forEach((row,i)=>{row.classList.add('clickable');row.tabIndex=0;row.dataset.edit=rows[i].id;row.onclick=()=>openEditor(current,rows[i].id);row.onkeydown=e=>{if(e.key==='Enter')row.click();};});
}
async function openOrder(id){
  const ticket=++detailRequest;
  drawer.hidden=false;drawer.innerHTML='<div class="loading">Opening order…</div>';
  try{const detail=await rpc('detail',{id}),o=detail.order;
    if(ticket!==detailRequest)return;
    drawer.innerHTML=`<div class="drawer-top"><p class="eyebrow">ORDER DETAILS</p><button id="close-drawer" class="quiet" aria-label="Close details">✕</button></div><h2>${escape(o.number)}</h2>${badge(o.status)}<div class="detail-cards"><div><small>CUSTOMER</small>${escape(detail.customer.name)}<br>${escape(detail.customer.region)}</div><div><small>ORDER DATE</small>${new Date(o.orderedAt).toLocaleDateString()}<br>${o.lines.length} line items</div></div><h3>Order lines</h3><div class="table-scroll"><table><thead><tr><th>Product</th><th>Qty</th><th>Price</th><th>Total</th></tr></thead><tbody>${o.lines.map(l=>`<tr><td>${escape(l.sku)}</td><td>${l.quantity}</td><td>${money(l.unitPrice)}</td><td>${money(l.total)}</td></tr>`).join('')}</tbody></table></div><div class="total">${money(o.total)}</div><label>Order status<select id="order-status">${['Pending','Processing','Shipped','Delivered','Cancelled'].map(s=>`<option ${s===o.status?'selected':''}>${s}</option>`).join('')}</select></label><p><button class="primary" id="save-status">Save status</button></p><p class="route">${escape(detail.route)}<br><code>Orders.Get(id)</code> returned this order with <code>.Lines</code> already populated. Saving a status preserves the existing packed line IDs.</p>`;
    document.querySelector('#close-drawer').onclick=()=>{++detailRequest;drawer.hidden=true;};
    document.querySelector('#save-status').onclick=async()=>{const button=document.querySelector('#save-status');button.disabled=true;try{await rpc('save-edit',{entity:'orders',id,revision:detail.revision,fields:{status:document.querySelector('#order-status').value}});toast('Order status saved');if(ticket===detailRequest){if(page==='orders')await renderGrid();await openOrder(id);}}catch(error){toast(error.message);button.disabled=false;}};
    const customerLink=document.createElement('button');customerLink.className='text-button';customerLink.textContent='Open customer account →';customerLink.onclick=()=>openEditor('customers',o.customerID);drawer.querySelector('.detail-cards').after(customerLink);
  }catch(error){if(ticket!==detailRequest)return;drawer.innerHTML=`<p>${escape(error.message)}</p><button id="close-drawer" class="secondary">Close</button>`;document.querySelector('#close-drawer').onclick=()=>{++detailRequest;drawer.hidden=true;};}
}

async function openEditor(entity,id){
  const ticket=++detailRequest;drawer.hidden=false;drawer.innerHTML='<div class="loading">Opening record…</div>';
  try{
    const record=await rpc('edit-detail',{entity,id});if(ticket!==detailRequest)return;
    const v=record.value,fields={customers:[['name','Name','text'],['email','Email','email'],['creditLimit','Credit limit','number']],products:[['name','Product name','text'],['category','Category','text'],['price','Catalog price','number'],['reorderPoint','Reorder point','number']],inventory:[['onHand','Units on hand','number'],['reserved','Reserved units','number']]}[entity];
    drawer.innerHTML=`<div class="drawer-top"><p class="eyebrow">${escape(labels[entity])} / WORKBENCH</p><button id="close-drawer" class="quiet" aria-label="Close details">✕</button></div><h2>${escape(v.name||v.sku)}</h2><p class="subtitle">${escape(v.code||v.sku)} · ${escape(v.region||v.category||v.warehouse)}</p><form id="edit-form" class="editor-form">${fields.map(([key,label,type])=>`<label>${label}<input name="${key}" type="${type}" value="${escape(v[key])}" required ${type==='number'?`min="0" step="${['price','creditLimit'].includes(key)?'0.01':'1'}"`:''}></label>`).join('')}<p id="edit-error" class="edit-error" role="alert"></p><button class="primary" type="submit">Save changes</button></form><p class="route">Scalar edit · stored relationships preserved.<br>${entity==='products'?'Catalog changes do not rewrite historical order prices.':entity==='customers'?'Abraxas composes Customer.Orders from separate order records selected by indexed CustomerID.':'Reserved stock must not exceed units on hand.'}</p>${entity==='customers'?'<section class="related"><h3>Customer orders</h3><div id="customer-orders"></div></section>':''}`;
    document.querySelector('#close-drawer').onclick=()=>{++detailRequest;drawer.hidden=true;};
    document.querySelector('#edit-form').onsubmit=async event=>{
      event.preventDefault();const form=event.currentTarget,button=form.querySelector('button'),values={};
      fields.forEach(([key,,type])=>values[key]=type==='number'?Number(form.elements[key].value):form.elements[key].value);button.disabled=true;
      try{await rpc('save-edit',{entity,id,revision:record.revision,fields:values});toast('Changes saved');if(ticket===detailRequest){if(page===entity)await renderGrid();await openEditor(entity,id);}}
      catch(error){form.querySelector('#edit-error').textContent=error.message;button.disabled=false;}
    };
    if(entity==='customers')await showRelated('customer-orders',{id},0,ticket);
  }catch(error){if(ticket===detailRequest){drawer.innerHTML=`<p class="edit-error">${escape(error.message)}</p>`;toast('Press Escape to close details');}}
}

async function showRelated(command,payload,start,ticket){
  const response=await rpc(command,{...payload,offset:start});if(ticket!==detailRequest)return;
  const target=drawer.querySelector('#customer-orders');if(!target)return;
  target.innerHTML=(response.rows.length?`<div class="related-orders">${response.rows.map(r=>`<button class="related-order" data-order="${escape(r.id)}"><span><b>${escape(r.value.number)}</b><small>${escape(r.value.status)}</small></span><strong>${money(r.value.total)}</strong></button>`).join('')}</div>`:'<div class="empty">No orders in this relationship.</div>')+`<div class="pager"><span>${start+response.rows.length} shown</span><button id="related-prev" class="secondary" ${start===0?'disabled':''}>←</button><button id="related-next" class="secondary" ${response.more?'':'disabled'}>→</button></div>`;
  target.querySelector('#related-prev').onclick=()=>showRelated(command,payload,Math.max(0,start-50),ticket).catch(toast);
  target.querySelector('#related-next').onclick=()=>showRelated(command,payload,start+50,ticket).catch(toast);bindOrderRows();
}

async function openGroup(group,key){
  const ticket=++detailRequest;drawer.hidden=false;
  drawer.innerHTML=`<div class="drawer-top"><p class="eyebrow">ANALYSIS / DRILL-DOWN</p><button id="close-drawer" class="quiet" aria-label="Close details">✕</button></div><h2>${escape(key)}</h2><p class="subtitle">${escape(group)} equality · fifty records per page</p><div id="customer-orders"></div>`;
  document.querySelector('#close-drawer').onclick=()=>{++detailRequest;drawer.hidden=true;};
  try{await showRelated('group-orders',{group,key},0,ticket);}catch(error){toast(error.message);}
}

// Renders a typed semantic builder whose browser choices are translated only through the host's allow-listed ConditionBuilder surface.
async function renderSearchWorkbench(){
  const ticket=++searchWorkbenchSerial;
  if(!searchSchema)searchSchema=await rpc('search-schema');
  if(page!=='search'||ticket!==searchWorkbenchSerial)return;
  paintSearchWorkbench();
}

function searchDataset(){return searchSchema.datasets.find(item=>item.name===searchWorkbench.dataset)||searchSchema.datasets[0];}
function searchField(dataset,name){return dataset.fields.find(item=>item.name===name)||dataset.fields[0];}
function searchResultMode(dataset=searchDataset()){return dataset.resultModes.find(item=>item.name===searchWorkbench.resultMode)||dataset.resultModes[0];}
function wireName(name){return name.length>1&&name===name.toUpperCase()?name.toLowerCase():name.charAt(0).toLowerCase()+name.slice(1);}

function nextSearchNodeId(prefix){return `${prefix}-${++searchNodeSerial}`;}
function newSearchClause(dataset){const field=dataset.fields[0];return {kind:'clause',id:nextSearchNodeId('clause'),field:field.name,operator:field.operators[0].value,value:'',value2:'',ignoreCase:true};}
function newSearchGroup(dataset,combinator='all'){return {kind:'group',id:nextSearchNodeId('group'),combinator,children:[newSearchClause(dataset)]};}
function searchNodes(group){return [group,...group.children.flatMap(child=>child.kind==='group'?searchNodes(child):[child])];}
function searchClauseCount(){return searchNodes(searchWorkbench.root).filter(node=>node.kind==='clause').length;}
function searchGroupCount(){return searchNodes(searchWorkbench.root).filter(node=>node.kind==='group').length;}
function findSearchNode(id,group=searchWorkbench.root){if(group.id===id)return group;for(const child of group.children){if(child.id===id)return child;if(child.kind==='group'){const found=findSearchNode(id,child);if(found)return found;}}return null;}
function findSearchParent(id,group=searchWorkbench.root){if(group.children.some(child=>child.id===id))return group;for(const child of group.children){if(child.kind==='group'){const found=findSearchParent(id,child);if(found)return found;}}return null;}
function normalizeSearchGroup(dataset,group){group.children.forEach(child=>{if(child.kind==='group'){normalizeSearchGroup(dataset,child);return;}const field=searchField(dataset,child.field);child.field=field.name;if(!field.operators.some(item=>item.value===child.operator))child.operator=field.operators[0].value;if(child.operator.startsWith('prop')){const peer=field.compareFields.find(item=>item.name===child.value)||field.compareFields[0];if(peer)child.value=peer.name;else child.operator=field.operators[0].value;}});}
function serializeSearchGroup(group){return {kind:'group',combinator:group.combinator,children:group.children.map(child=>child.kind==='group'?serializeSearchGroup(child):{kind:'clause',field:child.field,operator:child.operator,value:child.value,value2:child.value2,ignoreCase:child.ignoreCase})};}

// Converts browser-owned example data into the exact detached tree shape produced by manual builder interaction.
function inflateSearchExampleNode(node){
  if(Array.isArray(node.children))return {kind:'group',id:nextSearchNodeId('group'),combinator:node.combinator,children:node.children.map(inflateSearchExampleNode)};
  return {kind:'clause',id:nextSearchNodeId('clause'),field:node.field,operator:node.operator,value:node.value,value2:node.value2||'',ignoreCase:node.ignoreCase!==false};
}

function searchExampleClauses(node){return Array.isArray(node.children)?node.children.flatMap(searchExampleClauses):[node];}
function searchExampleExpression(dataset,node){
  if(Array.isArray(node.children))return `(${node.children.map(child=>searchExampleExpression(dataset,child)).join(node.combinator==='all'?' AND ':' OR ')})`;
  const field=searchField(dataset,node.field),operation=field.operators.find(item=>item.value===node.operator)?.label||node.operator;
  const value=node.operator.startsWith('prop')?(field.compareFields.find(item=>item.name===node.value)?.label||node.value):JSON.stringify(node.value);
  return `${field.label} ${operation.toLowerCase()} ${value}${node.operator==='between'?` through ${JSON.stringify(node.value2)}`:''}${field.type==='text'?node.ignoreCase===false?' · match case':' · ignore case':''}`;
}
function searchExampleSourceSummary(example){
  const dataset=searchSchema.datasets.find(item=>item.name===example.dataset),clauses=searchExampleClauses(example.root),indexed=clauses.filter(clause=>searchField(dataset,clause.field).indexed).length;
  return indexed===clauses.length?`${indexed}/${clauses.length} clauses have attached source indexes`:indexed===0?`No clauses have attached source indexes`:`${indexed}/${clauses.length} clauses have attached source indexes`;
}
function markSearchExampleModified(){
  if(!searchWorkbench.exampleId||searchWorkbench.exampleModified)return;
  searchWorkbench.exampleModified=true;
  const status=document.querySelector('#loaded-example-status');if(status)status.textContent=`Modified from ${searchWorkbench.exampleTitle} · still an ordinary editable condition`;
}

// Opens a descriptive gallery; selecting a card only loads ordinary browser condition state and never invokes the host.
function showSearchExamples(){
  const source=document.querySelector('#query-examples'),categories=[...new Set(searchExamples.map(example=>example.category))];
  const html=`<div class="search-example-intro"><strong>Nothing here is a privileged query.</strong><span>Load an example, edit any condition or result-shape segment, inspect the generated C#, then choose when to run it. Only the visible workbench state crosses the WebView2 bridge.</span></div>${categories.map(category=>`<section class="search-example-section"><div class="search-example-section-title"><p class="eyebrow">${escape(category)}</p><span>${number(searchExamples.filter(example=>example.category===category).length)} examples</span></div><div class="search-example-grid">${searchExamples.filter(example=>example.category===category).map(example=>{const dataset=searchSchema.datasets.find(item=>item.name===example.dataset),clauses=searchExampleClauses(example.root),mode=dataset.resultModes.find(item=>item.name===(example.resultMode||'object'))||dataset.resultModes[0];return `<article class="search-example-card"><div class="search-example-card-head"><span>${escape(dataset.label)}</span><span>${number(clauses.length)} condition${clauses.length===1?'':'s'}</span></div><h3>${escape(example.title)}</h3><p>${escape(example.intent)}</p><div class="search-example-expression">${escape(searchExampleExpression(dataset,example.root).replace(/^\(|\)$/g,''))}</div><dl><div><dt>Shows off</dt><dd>${escape(example.showcase)}</dd></div><div><dt>Source availability</dt><dd>${escape(searchExampleSourceSummary(example))}</dd></div><div><dt>Result read</dt><dd>${escape(mode.label)} · ${escape(mode.columns.map(column=>column.label).join(' · '))}</dd></div></dl>${example.warning?`<div class="search-example-warning">${escape(example.warning)}</div>`:''}<button class="pill-button" type="button" data-load-search-example="${escape(example.id)}">Load editable example →</button></article>`;}).join('')}</div></section>`).join('')}`;
  openLightbox('Explore example conditions','ORDINARY CONDITIONS · EXPLAINED',html,source);
  document.querySelectorAll('[data-load-search-example]').forEach(button=>button.onclick=()=>{
    const example=searchExamples.find(item=>item.id===button.dataset.loadSearchExample);if(!example)return;
    closeLightbox();searchWorkbench.dataset=example.dataset;searchWorkbench.resultMode=example.resultMode||'object';searchWorkbench.root=inflateSearchExampleNode(example.root);searchWorkbench.exampleId=example.id;searchWorkbench.exampleTitle=example.title;searchWorkbench.exampleModified=false;paintSearchWorkbench();
    document.querySelector('#query-developer-code')?.focus();toast(`${example.title} loaded. Review or edit it before running.`);
  });
}

// Repaints controls from detached state so field-type changes cannot leave an invalid operator or stale second value in the DOM.
function paintSearchWorkbench(){
  const dataset=searchDataset(),identities=searchSchema.identityModels,mode=searchResultMode(dataset);
  searchWorkbench.dataset=dataset.name;
  searchWorkbench.resultMode=mode.name;
  normalizeSearchGroup(dataset,searchWorkbench.root);
  const datasetOptions=searchSchema.datasets.map(item=>`<option value="${item.name}" ${item.name===dataset.name?'selected':''}>${escape(item.label)}</option>`).join('');
  const resultModeButtons=dataset.resultModes.map(item=>`<button type="button" data-result-mode="${escape(item.name)}" aria-pressed="${item.name===mode.name}">${escape(item.label)}</button>`).join('');
  content.innerHTML=heading('Search & lookup','Turn user-selected semantics into typed Abraxas conditions, then compare indexed discovery with direct Fractal/Inheto evaluation.')+
  `<section class="search-route-map" aria-label="Abraxas search route"><div><small>USER SEMANTICS</small><strong>Field · operation · value</strong></div><i>→</i><div><small>ABRAXAS</small><strong>ConditionBuilder</strong></div><i>→</i><div><small>DISCOVERY</small><strong>LibraDex or Fractal/Inheto</strong></div><i>→</i><div><small>RESULT SHAPE</small><strong>${escape(mode.label)}</strong></div></section>
  <div class="search-workbench-grid"><section class="card query-builder-card"><div class="section-heading"><div><p class="eyebrow">SEMANTIC QUERY BUILDER</p><h2>Describe what should match.</h2><p>Values remain data. Nested groups become real ConditionBuilder groups—never executable browser code.</p></div><div class="query-builder-tools"><button class="pill-button" id="query-examples" type="button">✦ Explore example conditions</button><span class="tag">${searchSchema.maximumClauses} CONDITIONS · ${searchSchema.maximumGroupDepth} LEVELS</span></div></div><div class="query-topline"><div class="dataset-segment"><label>Dataset<select id="query-dataset">${datasetOptions}</select></label></div><div class="result-mode-control"><span>Return as</span><div class="result-mode-pill" role="group" aria-label="Result read shape">${resultModeButtons}</div><small>${escape(mode.columns.map(column=>column.label).join(' · '))}</small></div>${searchWorkbench.exampleId?`<div class="query-example-loaded"><small>EXAMPLE LOADED</small><strong id="loaded-example-status">${escape(searchWorkbench.exampleModified?`Modified from ${searchWorkbench.exampleTitle} · still ordinary editable state`:`${searchWorkbench.exampleTitle} · fully editable · not executed`)}</strong></div>`:''}</div><div id="query-tree" class="query-tree" tabindex="-1">${searchGroupHtml(dataset,searchWorkbench.root,1,'Where')}</div><div class="query-actions"><span>${number(searchClauseCount())} condition${searchClauseCount()===1?'':'s'} · ${number(searchGroupCount())} group${searchGroupCount()===1?'':'s'} · ${escape(mode.label.toLowerCase())}</span><div class="query-action-buttons"><button class="pill-button" id="query-developer-code" type="button">〈/〉 Show condition evidence</button><button class="primary" id="run-search">Run Abraxas search ↗</button></div></div><p id="query-preview-error" class="query-preview-error" role="alert"></p><p class="query-note">${escape(searchSchema.wordBoundary)} Regex is explicit and limited to 256 characters. Abraxas reports the actual plan; exact count and the selected bounded result read are timed separately.</p></section>
  <section class="card lookup-card"><div><p class="eyebrow">DIRECT IDENTITY LOOKUP</p><h2>Skip secondary discovery.</h2><p>Compare direct Fractal lookup plus Inheto materialization with the condition path above.</p></div><label>Record identity<input id="lookup-id" inputmode="numeric" pattern="[0-9]*" autocomplete="off" placeholder="Enter numeric record identity"><small>Enter digits directly, or choose Lookup → on a result.</small></label><button class="secondary" id="run-lookup">Lookup in ${escape(dataset.label)}</button><div id="lookup-result" class="lookup-result"><span>No identity selected.</span></div></section>
  <section class="card identity-model-card"><div class="identity-model-head"><div><p class="eyebrow">IDENTITY WITHOUT MODEL CONSTRAINTS</p><h2>Store-owned by default. Mirrored into an object only when useful.</h2><p>Both approaches are active Meridian design choices, not separate persistence layers.</p></div><button class="pill-button" id="identity-developer-code" type="button">〈/〉 Show identity code</button></div><div class="identity-models"><article><small>NO ID PROPERTY REQUIRED</small><strong>${identities.storeOwned.types.map(escape).join(' · ')}</strong><p>${escape(identities.storeOwned.description)}</p></article><article class="identity-opt-in"><small>OPTIONAL GENERATED-ID HOOK · ${identities.objectMirror.configured?'WRITE PATH ACTIVE':'NOT CONFIGURED'}</small><strong>${escape(identities.objectMirror.type)}.${escape(identities.objectMirror.property)}</strong><p>${escape(identities.objectMirror.description)}</p><em>${escape(identities.objectMirror.existingRecords)}</em></article></div></section></div><div id="search-results"></div>`;
  enhanceSelects(content);bindSearchWorkbench();
}

function searchGroupHtml(dataset,group,depth,connector){
  const directClauses=group.children.filter(child=>child.kind==='clause').length,totalClauses=searchClauseCount(),totalGroups=searchGroupCount();
  return `<section class="query-group" data-group-id="${group.id}" data-depth="${depth}"><div class="query-group-header">${depth>1?`<span class="clause-connector">${escape(connector)}</span>`:''}<div class="combine-pill" role="group" aria-label="Group match policy"><button type="button" data-group-combine="all" aria-pressed="${group.combinator==='all'}">All</button><button type="button" data-group-combine="any" aria-pressed="${group.combinator==='any'}">Any</button></div><span class="query-group-title">conditions in this group must match</span><div class="query-group-actions"><button class="pill-button" type="button" data-add-clause ${totalClauses>=searchSchema.maximumClauses?'disabled':''}>＋ Condition</button><button class="pill-button" type="button" data-add-group ${(totalClauses>=searchSchema.maximumClauses||totalGroups>=searchSchema.maximumGroups||depth>=searchSchema.maximumGroupDepth)?'disabled':''}>＋ Group</button>${depth>1?'<button class="quiet" type="button" data-remove-group aria-label="Remove group">✕</button>':''}</div></div><div class="query-children">${group.children.map((child,index)=>child.kind==='group'?searchGroupHtml(dataset,child,depth+1,index===0?'Where':group.combinator==='all'?'AND':'OR'):searchClauseHtml(dataset,child,index,index===0?'Where':group.combinator==='all'?'AND':'OR',directClauses)).join('')}</div></section>`;
}

function searchValueInput(dataset,field,clause,name,label){
  if(clause.operator.startsWith('prop')){
    const options=field.compareFields.map(item=>`<option value="${item.name}" ${item.name===clause.value?'selected':''}>${escape(item.label)}</option>`).join('');
    return `<label class="criteria-segment clause-value"><span>Compare with field</span><select ${name}>${options}</select></label>`;
  }
  const attributes=field.type==='date'?'type="date"':field.type==='decimal'?'type="number" inputmode="decimal" step="0.01"':field.type==='int32'?'type="number" inputmode="numeric" step="1"':field.type==='uint64'?'type="text" inputmode="numeric" pattern="[0-9]*"':'type="search" autocomplete="off" spellcheck="false"';
  const placeholder=field.type==='text'?'Enter text':field.type==='date'?'Choose date':field.type==='decimal'?'0.00':'Enter whole number';
  return `<label class="criteria-segment clause-value"><span>${label}</span><input ${name} ${attributes} value="${escape(name.includes('value2')?clause.value2:clause.value)}" placeholder="${placeholder}"></label>`;
}

function searchClauseHtml(dataset,clause,index,connector,directClauses){
  const field=searchField(dataset,clause.field),between=clause.operator==='between',text=field.type==='text';
  const fieldOptions=dataset.fields.map(item=>`<option value="${item.name}" ${item.name===field.name?'selected':''}>${escape(item.label)} · ${item.indexed?'indexed':'scan'}</option>`).join('');
  const operatorOptions=field.operators.map(item=>`<option value="${item.value}" ${item.value===clause.operator?'selected':''}>${escape(item.label)}</option>`).join('');
  return `<div class="query-clause" data-node-id="${clause.id}" data-index="${index}"><span class="clause-connector">${escape(connector)}</span><div class="criteria-pill ${between?'is-between':''}"><label class="criteria-segment"><span>Field</span><select data-clause-field>${fieldOptions}</select></label><label class="criteria-segment"><span>Operation</span><select data-clause-operator>${operatorOptions}</select></label>${searchValueInput(dataset,field,clause,'data-clause-value',between?'From':'Value')}${between?searchValueInput(dataset,field,clause,'data-clause-value2','Through'):''}${text?`<label class="criteria-segment"><span>Case</span><select data-clause-case><option value="ignore" ${clause.ignoreCase?'selected':''}>Ignore case</option><option value="match" ${clause.ignoreCase?'':'selected'}>Match case</option></select></label>`:`<div class="criteria-segment"><span>Value type</span><strong class="criteria-type">${escape(field.type)}</strong></div>`}</div><button class="quiet remove-clause" type="button" aria-label="Remove condition" ${directClauses<=1?'disabled':''}>✕</button></div>`;
}

function readSearchClauseState(){
  document.querySelectorAll('.query-clause').forEach(row=>{const clause=findSearchNode(row.dataset.nodeId);if(!clause)return;clause.field=row.querySelector('[data-clause-field]').value;clause.operator=row.querySelector('[data-clause-operator]').value;clause.value=row.querySelector('[data-clause-value]').value;clause.value2=row.querySelector('[data-clause-value2]')?.value||'';clause.ignoreCase=(row.querySelector('[data-clause-case]')?.value||'ignore')==='ignore';});
}

function bindSearchWorkbench(){
  document.querySelector('#query-dataset').onchange=event=>{searchWorkbench.dataset=event.target.value;searchWorkbench.resultMode='object';searchWorkbench.root=newSearchGroup(searchDataset());searchWorkbench.exampleId='';searchWorkbench.exampleTitle='';searchWorkbench.exampleModified=false;paintSearchWorkbench();};
  document.querySelectorAll('[data-result-mode]').forEach(button=>button.onclick=()=>{readSearchClauseState();searchWorkbench.resultMode=button.dataset.resultMode;markSearchExampleModified();paintSearchWorkbench();});
  document.querySelectorAll('[data-group-combine]').forEach(button=>button.onclick=()=>{readSearchClauseState();markSearchExampleModified();const group=findSearchNode(button.closest('[data-group-id]').dataset.groupId);group.combinator=button.dataset.groupCombine;paintSearchWorkbench();});
  document.querySelectorAll('[data-clause-field],[data-clause-operator],[data-clause-case]').forEach(select=>select.onchange=()=>{readSearchClauseState();markSearchExampleModified();paintSearchWorkbench();});
  document.querySelectorAll('[data-clause-value],[data-clause-value2]').forEach(control=>control.oninput=markSearchExampleModified);
  document.querySelectorAll('.remove-clause').forEach(button=>button.onclick=()=>{readSearchClauseState();markSearchExampleModified();const id=button.closest('.query-clause').dataset.nodeId,parent=findSearchParent(id);parent.children=parent.children.filter(child=>child.id!==id);paintSearchWorkbench();});
  document.querySelectorAll('[data-add-clause]').forEach(button=>button.onclick=()=>{readSearchClauseState();markSearchExampleModified();const group=findSearchNode(button.closest('[data-group-id]').dataset.groupId);group.children.push(newSearchClause(searchDataset()));paintSearchWorkbench();});
  document.querySelectorAll('[data-add-group]').forEach(button=>button.onclick=()=>{readSearchClauseState();markSearchExampleModified();const group=findSearchNode(button.closest('[data-group-id]').dataset.groupId);group.children.push(newSearchGroup(searchDataset(),group.combinator==='all'?'any':'all'));paintSearchWorkbench();});
  document.querySelectorAll('[data-remove-group]').forEach(button=>button.onclick=()=>{readSearchClauseState();markSearchExampleModified();const id=button.closest('[data-group-id]').dataset.groupId,parent=findSearchParent(id);parent.children=parent.children.filter(child=>child.id!==id);paintSearchWorkbench();});
  document.querySelector('#query-examples').onclick=showSearchExamples;
  document.querySelector('#run-search').onclick=runSearchWorkbench;
  document.querySelector('#query-developer-code').onclick=showSearchConditionCode;
  document.querySelector('#run-lookup').onclick=()=>runSearchLookup(document.querySelector('#lookup-id').value);
  const identityCode=document.querySelector('#identity-developer-code');identityCode.onclick=()=>{const identities=searchSchema.identityModels;openLightbox('Abraxas identity choices','STORE-OWNED + OPTIONAL OBJECT MIRROR',`<div class="identity-code-grid"><div><small>IDENTITY-FREE DOMAIN OBJECT</small><pre><code>${escape(identities.storeOwned.code)}</code></pre></div><div><small>OPT-IN OBJECT MIRROR</small><pre><code>${escape(identities.objectMirror.code)}</code></pre></div></div>`,identityCode);};
}

// Requests only the validated ConditionBuilder description so code can be inspected before any exact count or record materialization begins.
async function showSearchConditionCode(){
  readSearchClauseState();
  const button=document.querySelector('#query-developer-code'),error=document.querySelector('#query-preview-error');button.disabled=true;error.textContent='';
  try{
    const result=await rpc('describe-search',{dataset:searchWorkbench.dataset,resultMode:searchWorkbench.resultMode,root:serializeSearchGroup(searchWorkbench.root)});if(page!=='search')return;
    openLightbox(`${result.datasetLabel} condition evidence`,'DESCRIBED · NOT EXECUTED',`${conditionEvidenceHtml(result.condition)}${resultShapeEvidenceHtml(result.resultShape)}<div class="condition-preview-note"><strong>${escape(result.execution)}</strong><span>${escape(result.route)} · built in ${duration(result.conditionBuildMs)}</span></div>`,button);
  }catch(problem){error.textContent=problem.message;}
  finally{if(button.isConnected)button.disabled=false;}
}

// Brings the live progress/result region into view with its leading edge just above the viewport so the condition heading and initial result rows share the screen.
function revealSearchResults(){
  const target=document.querySelector('#search-results');if(!target)return;
  window.scrollTo({left:window.scrollX,top:Math.max(0,window.scrollY+target.getBoundingClientRect().top+16),behavior:'auto'});
}

async function runSearchWorkbench(){
  readSearchClauseState();
  const button=document.querySelector('#run-search'),target=document.querySelector('#search-results'),mode=searchResultMode();button.disabled=true;target.innerHTML=`<div class="loading"><i class="wait-spinner"></i> Abraxas is building the typed condition, reading the first ${number(50)} natural-order ${escape(mode.label.toLowerCase())} results, then completing the exact count…</div>`;
  revealSearchResults();
  try{const result=await rpc('search-records',{dataset:searchWorkbench.dataset,resultMode:searchWorkbench.resultMode,root:serializeSearchGroup(searchWorkbench.root)});if(page!=='search')return null;paintSearchResults(result);revealSearchResults();return result;}
  catch(error){target.innerHTML=`<div class="error"><strong>Condition not executed.</strong><p>${escape(error.message)}</p></div>`;revealSearchResults();}
  finally{if(button.isConnected)button.disabled=false;}
}

function paintSearchResults(result){
  const target=document.querySelector('#search-results'),columns=result.resultShape.columns,timings=result.timings,shape=result.resultShape,plan=result.plan;
  const rows=result.rows.length?`<div class="table-scroll"><table><thead><tr><th>Identity</th>${columns.map(field=>`<th>${escape(field.label)}</th>`).join('')}<th></th></tr></thead><tbody>${result.rows.map(row=>`<tr><td class="strong">${escape(row.id)}</td>${columns.map(field=>`<td>${formatSearchValue(searchRowValue(row.value,field.name),field.type)}</td>`).join('')}<td><button class="text-button" data-lookup-id="${escape(row.id)}">Lookup →</button></td></tr>`).join('')}</tbody></table></div>`:'<div class="empty">No records satisfy this condition.</div>';
  const materialized=`${number(shape.completeObjects)} complete · ${number(shape.reducedObjects)} reduced · ${number(shape.scalarValues)} scalar`;
  const indexes=plan.indexNames.length?plan.indexNames.join(' · '):'No LibraDex index selected';
  const countMethod=result.countStrategy==='parallel-fractal-scan'?'2-worker Fractal identity stream':'Native Abraxas aggregate';
  target.innerHTML=`<section class="card search-evidence"><div class="search-evidence-head"><div><p class="eyebrow">EXECUTED ABRAXAS CONDITION</p><h2>${escape(result.condition.display)}</h2><p>${escape(result.route)}</p><div class="actual-plan"><strong>${escape(plan.label)}</strong><span>${escape(plan.reason)}</span><small>${escape(indexes)}</small></div></div><div class="search-timing"><strong>${number(result.matchCount)} matched · ${number(result.rows.length)} shown</strong><span>Complete search<b>${duration(timings.searchCompleteMs)}</b></span><span>Exact match count<b>${duration(timings.countMs)} · ${escape(countMethod)}</b></span><span>First result<b>${timings.firstResultMs==null?'—':duration(timings.firstResultMs)}</b></span><span>${escape(shape.label)} read<b>${duration(timings.readerMs)}</b></span><span>Condition build<b>${duration(timings.conditionBuildMs)}</b></span><span>Materialized<b>${materialized}</b></span></div></div><div class="result-shape-proof"><div><small>INHETO RESULT SHAPE</small><strong>${escape(shape.label)}</strong><span>${escape(shape.description)}</span></div><b>${escape(columns.map(column=>column.label).join(' · '))}</b></div><div class="clause-routes">${result.clauses.map(item=>`<div class="${item.usedByIndex?'indexed':'scan'}"><small>${escape(item.fieldLabel)}</small><strong>${escape(item.routeLabel)}</strong><span>${escape(item.op)} · ${escape(item.type)}${item.indexName?` · ${escape(item.indexName)}`:''}</span><em>${escape(item.reason)}</em></div>`).join('')}</div><div class="search-code-action"><button id="search-developer-code" class="pill-button" type="button">〈/〉 Show condition + result code</button></div>${rows}<div class="pager"><span>${number(result.matchCount)} exact matches · first ${number(result.rows.length)} shown as ${escape(shape.label.toLowerCase())} · links suppressed · ${escape(result.countRoute)}</span></div></section>`;
  const codeButton=document.querySelector('#search-developer-code');codeButton.onclick=()=>openLightbox(`${result.datasetLabel} condition evidence`,'CONDITIONBUILDER + ACTUAL PLAN + RESULT SHAPE',`${conditionEvidenceHtml(result.condition)}${resultShapeEvidenceHtml(shape)}<div class="condition-preview-note"><strong>${escape(plan.label)}</strong><span>${escape(plan.reason)} · ${escape(indexes)}</span></div>`,codeButton);
  target.querySelectorAll('[data-lookup-id]').forEach(button=>button.onclick=()=>runSearchLookup(button.dataset.lookupId));
}

function searchRowValue(value,name){return value?.[wireName(name)]??value?.[name]??null;}
function formatSearchValue(value,type){if(value==null)return '—';if(type==='decimal')return money(value);if(type==='date')return new Date(value).toLocaleDateString();if(type==='int32'||type==='uint64')return number(value);return escape(value);}

async function runSearchLookup(id){
  const input=document.querySelector('#lookup-id'),target=document.querySelector('#lookup-result');input.value=id||input.value;if(!/^\d+$/.test(input.value)){target.innerHTML='<span class="edit-error">Enter a decimal record identity.</span>';return;}
  target.innerHTML='<span><i class="wait-spinner"></i> Direct lookup…</span>';
  try{const result=await rpc('lookup-record',{entity:searchWorkbench.dataset,id:input.value});if(page!=='search')return;target.innerHTML=result.found?`<strong>Object materialized in ${duration(result.elapsedMs)}</strong><span>${escape(result.route)}</span><div><button id="lookup-developer-code" class="pill-button" type="button">〈/〉 Code</button> <button id="lookup-object" class="pill-button" type="button">{ } Scalar object</button></div>`:`<strong>Identity not found</strong><span>${escape(result.route)}</span>`;if(result.found){const codeButton=document.querySelector('#lookup-developer-code'),objectButton=document.querySelector('#lookup-object');codeButton.onclick=()=>openLightbox('Direct identity lookup code','FRACTAL + INHETO',`<pre><code>${escape(result.code)}</code></pre>`,codeButton);objectButton.onclick=()=>openLightbox(`Scalar ${searchDataset().label} object`,'LINKS SUPPRESSED',`<pre><code>${escape(JSON.stringify(result.value,null,2))}</code></pre>`,objectButton);}}
  catch(error){target.innerHTML=`<span class="edit-error">${escape(error.message)}</span>`;}
}

async function renderAnalysis(){
  content.innerHTML=heading('Find the story in your data.','Group orders, compare totals and averages, then explore the records behind each result.')+`<section class="card"><div class="analysis-controls"><div class="analysis-dataset" aria-label="Dataset"><span class="analysis-control-label">Dataset</span><div class="analysis-dataset-value"><strong>Sales orders</strong><small>Current sample source</small></div></div><label>Group by<select id="group"><option value="Region">Region</option><option value="Status">Order status</option></select></label><button id="run-analysis" class="primary">Run analysis ↗</button></div><div id="analysis-results"></div></section><p class="card-sub">Count, sum and average are computed by Abraxas. Multi-key grouping, additional datasets and configurable measures remain upcoming.</p>`;
  const ticket=navigation;
  const run=async()=>{
    const button=document.querySelector('#run-analysis'),group=document.querySelector('#group').value;button.disabled=true;
    try{
      const started=performance.now(),results=await rpc('analyze',{group});if(ticket!==navigation)return;
      document.querySelector('#analysis-results').innerHTML=`<table><thead><tr><th>Group</th><th>Order count</th><th>Order value</th><th>Average order</th><th></th></tr></thead><tbody>${results.map((r,i)=>`<tr><td class="strong">${escape(r.key)}</td><td>${number(r.count)}</td><td>${money(r.total)}</td><td>${money(r.average)}</td><td><button class="text-button" data-group="${i}">Explore →</button></td></tr>`).join('')}</tbody></table><div class="pager">${results.length} groups · ${(performance.now()-started).toFixed(1)} ms round trip · all order statuses</div>`;
      document.querySelectorAll('[data-group]').forEach(b=>b.onclick=()=>openGroup(group,results[Number(b.dataset.group)].key));
    }catch(error){toast(error.message);}finally{button.disabled=false;}
  };document.querySelector('#run-analysis').onclick=run;await run();
}
function runtimeViewControl(){return `<div class="runtime-view-switch" role="group" aria-label="Runtime metrics view"><button type="button" data-runtime-view="recordbase" aria-pressed="${runtimeView==='recordbase'}">RecordBase</button><button type="button" data-runtime-view="dotnet" aria-pressed="${runtimeView==='dotnet'}">.NET runtime</button></div>`;}

// Keeps storage and process-memory semantics separate while making both views available under one observability destination.
function bindRuntimeViewControls(){
  document.querySelectorAll('[data-runtime-view]').forEach(button=>button.onclick=async()=>{runtimeView=button.dataset.runtimeView;if(runtimeView==='dotnet')await refreshRuntimeMetrics();if(page==='dataset')renderDataset();});
}

function renderDataset(){
  const state=overview.manifest.phase;
  content.innerHTML=heading('Runtime metrics','Inspect the RecordBase on disk and the live .NET host without conflating storage, residency, or managed heap.',runtimeViewControl());
  bindRuntimeViewControls();
  if(runtimeView==='dotnet'){renderRuntimeInstance();void refreshRuntimeMetrics();return;}
  content.insertAdjacentHTML('beforeend',
  (overview.recoveryMigrationRequired?`<div class="welcome recovery-migration"><p class="eyebrow">STORAGE RECOVERY UPGRADE</p><h2>Move this workspace to embedded recovery format v2.</h2><p>This legacy v${number(overview.indexCatalogFileFormatVersion)} LibraDex catalog remains readable and has not been silently changed. The explicit migration compacts the RecordBase, replaces the catalog through Abraxas, reopens every live store, and then prepares any pending Meridian indexes.</p><p>The operation can take time on a large workspace. Meridian will show the real active phase and an indeterminate wait animation because compaction does not expose a truthful item total.</p><button id="migrate-recovery" class="primary">Migrate workspace to v2 ↗</button></div>`:'')+
  (state==='Empty'?`<div class="welcome generation-welcome"><p class="eyebrow">WELCOME TO MERIDIAN</p><h2>Bring your trading workspace to life.</h2><p>Generate 1,000 orders, approximately 5,000 separately stored order lines, 100 customers, 100 products and 100 stock positions. The same seed produces the same business values. Your data stays on this machine.</p><button id="generate" class="primary">Generate sample workspace ↗</button><p id="generation-status"></p><div class="progress"><i></i></div></div>`:state!=='Ready'?`<div class="notice">Dataset state: ${escape(state)}. Generation did not finish. Open a new dedicated directory with --data to start another dataset. Partial data is retained for inspection.</div>`:'')+
  `<section class="card" style="margin-top:24px"><div class="section-heading"><h2>Dataset details</h2><span class="tag">${escape(state.toUpperCase())}</span></div><div class="dataset-info">Generator: ${escape(overview.manifest.version)} · seed ${overview.manifest.seed}<br>Records: ${number(overview.orders)} orders / ${number(overview.lines)} lines<br>Generation + indexing: ${(overview.manifest.generationMilliseconds/1000).toFixed(2)} seconds<br>Location: <code>${escape(overview.directory)}</code></div></section>`);
  renderGenerationControls(state);
  renderStorage();
  renderRelationshipArchitecture(state);
  renderDatasetAudit(state);
  const migration=document.querySelector('#migrate-recovery');
  if(migration)migration.onclick=async()=>{
    if(!window.confirm('Migrate this workspace to LibraDex embedded recovery format v2 now?'))return;
    migration.disabled=true;document.querySelectorAll('nav button,#new-workspace,#generate').forEach(element=>element.disabled=true);
    try{overview=await rpc('migrate-recovery-format');renderDataset();toast('Workspace migration to embedded recovery format v2 completed.');}
    catch(error){toast(error.message);if(migration.isConnected)migration.disabled=false;}
    finally{document.querySelectorAll('nav button').forEach(element=>element.disabled=false);}
  };
}

// Renders stable placeholders once; periodic samples update values in place so focus and scroll are preserved.
function renderRuntimeInstance(){
  content.insertAdjacentHTML('beforeend',`<section class="card runtime-instance" id="runtime-instance"><div class="runtime-summary-grid"><article class="runtime-summary"><small>HOST WORKING SET</small><strong data-runtime-field="working">—</strong><span>Resident pages in the Meridian .NET host</span></article><article class="runtime-summary"><small>CURRENT MANAGED ESTIMATE</small><strong data-runtime-field="managed">—</strong><span><code>GC.GetTotalMemory(false)</code> · no collection</span></article><article class="runtime-summary"><small>SOH AFTER LAST GC</small><strong data-runtime-field="soh">—</strong><span>Gen 0 + Gen 1 + Gen 2 segment sizes</span></article><article class="runtime-summary"><small>LOH AFTER LAST GC</small><strong data-runtime-field="loh">—</strong><span>Large-object segments; not current live objects</span></article></div><div class="runtime-memory-load" title="CLR memory load versus high-memory threshold"><i id="runtime-memory-load-fill"></i></div><div class="runtime-panels"><article class="runtime-panel"><header><div><h2>Managed generations after the last GC</h2><p>Segment sizes and fragmentation are CLR post-collection observations.</p></div></header><table class="runtime-generation-table"><thead><tr><th>Area</th><th>Size after</th><th>Fragmented after</th><th>Collections</th></tr></thead><tbody id="runtime-generations"></tbody></table></article><article class="runtime-panel"><header><div><h2>Host process + collector</h2><p>Current process counters beside the last completed GC snapshot.</p></div></header><dl class="runtime-facts"><div><dt>Private memory</dt><dd data-runtime-field="private">—</dd></div><div><dt>Peak working set</dt><dd data-runtime-field="peak">—</dd></div><div><dt>Total allocated</dt><dd data-runtime-field="allocated">—</dd></div><div><dt>Allocation rate</dt><dd data-runtime-field="allocation-rate">—</dd></div><div><dt>GC heap / committed</dt><dd data-runtime-field="gc-heap">—</dd></div><div><dt>Heap fragmentation</dt><dd data-runtime-field="fragmented">—</dd></div><div><dt>POH after last GC</dt><dd data-runtime-field="poh">—</dd></div><div><dt>Memory load / threshold</dt><dd data-runtime-field="memory-load">—</dd></div><div><dt>GC mode</dt><dd data-runtime-field="gc-mode">—</dd></div><div><dt>Last GC</dt><dd data-runtime-field="last-gc">—</dd></div><div><dt>Last GC pauses</dt><dd data-runtime-field="pauses">—</dd></div><div><dt>Pause time</dt><dd data-runtime-field="pause-percent">—</dd></div><div><dt>Pinned / finalization</dt><dd data-runtime-field="pinned">—</dd></div><div><dt>Promoted bytes</dt><dd data-runtime-field="promoted">—</dd></div><div><dt>Host CPU</dt><dd data-runtime-field="cpu">—</dd></div><div><dt>Threads / handles</dt><dd data-runtime-field="threads">—</dd></div><div><dt>Process uptime</dt><dd data-runtime-field="uptime">—</dd></div><div><dt>Runtime</dt><dd data-runtime-field="runtime">—</dd></div></dl></article></div><div class="runtime-footnote"><div><strong data-runtime-field="scope">Meridian .NET host process</strong><br><span id="runtime-note">Sampling without requesting a garbage collection.</span><br><small id="runtime-sampled-at">Waiting for the first sample…</small></div><button class="secondary" id="refresh-runtime">Refresh now</button></div></section>`);
  document.querySelector('#refresh-runtime').onclick=async event=>{const button=event.currentTarget;button.disabled=true;button.textContent='Sampling…';await refreshRuntimeMetrics();if(button.isConnected){button.disabled=false;button.textContent='Refresh now';}};
  paintRuntimeInstance();
}

// Updates the detailed view from one coherent sampler response without inventing point-in-time generation values.
function paintRuntimeInstance(){
  const root=document.querySelector('#runtime-instance'),metrics=runtimeMetrics;if(!root||!metrics)return;
  const set=(field,value)=>{const target=root.querySelector(`[data-runtime-field="${field}"]`);if(target)target.textContent=value;};
  const percent=value=>value==null?'—':`${Number(value).toFixed(2)}%`;
  set('working',binarySize(metrics.workingSetBytes));set('managed',binarySize(metrics.managedBytes));set('soh',binarySize(metrics.sohBytes));set('loh',binarySize(metrics.lohBytes));
  set('private',binarySize(metrics.privateMemoryBytes));set('peak',binarySize(metrics.peakWorkingSetBytes));set('allocated',binarySize(metrics.totalAllocatedBytes));set('allocation-rate',metrics.allocationRateBytesPerSecond==null?'Sampling…':`${binarySize(metrics.allocationRateBytesPerSecond)}/s`);
  set('gc-heap',`${binarySize(metrics.gcHeapSizeBytes)} / ${binarySize(metrics.totalCommittedBytes)}`);set('fragmented',binarySize(metrics.fragmentedBytes));set('poh',binarySize(metrics.pohBytes));set('memory-load',`${binarySize(metrics.memoryLoadBytes)} / ${binarySize(metrics.highMemoryLoadThresholdBytes)} · ${percent(metrics.memoryLoadPercent)}`);
  set('gc-mode',`${metrics.runtime.serverGc?'Server':'Workstation'} · ${metrics.runtime.latencyMode}`);set('last-gc',`#${number(metrics.lastGc.index)} · Gen ${number(metrics.lastGc.generation)} · ${metrics.lastGc.concurrent?'concurrent':'blocking'}${metrics.lastGc.compacted?' · compacted':''}`);
  set('pauses',metrics.lastGc.pauseDurationsMs.length?metrics.lastGc.pauseDurationsMs.map(value=>duration(value)).join(' + '):'No completed pause reported');set('pause-percent',percent(metrics.lastGc.pauseTimePercentage));set('pinned',`${number(metrics.pinnedObjects)} / ${number(metrics.finalizationPending)}`);set('promoted',binarySize(metrics.promotedBytes));
  set('cpu',`${metrics.processorPercent==null?'Sampling…':percent(metrics.processorPercent)} · ${duration(metrics.processorTimeMs)} total`);set('threads',`${number(metrics.threadCount)} / ${number(metrics.handleCount)}`);set('uptime',duration(metrics.uptimeMs));set('runtime',`.NET ${metrics.runtime.version} · ${metrics.runtime.is64Bit?'x64':'x86'} · ${number(metrics.runtime.processorCount)} logical CPUs`);set('scope',metrics.scope);
  root.querySelector('#runtime-generations').innerHTML=(metrics.generations||[]).map(generation=>`<tr><td>${escape(generation.name)}</td><td>${binarySize(generation.sizeAfterBytes)}</td><td>${binarySize(generation.fragmentationAfterBytes)}</td><td>${generation.collections==null?'—':number(generation.collections)}</td></tr>`).join('');
  root.querySelector('#runtime-memory-load-fill').style.width=`${Math.max(0,Math.min(100,Number(metrics.memoryLoadPercent)||0))}%`;
  root.querySelector('#runtime-note').textContent=metrics.note;root.querySelector('#runtime-sampled-at').textContent=`Sampled ${new Date(metrics.sampledAt).toLocaleTimeString()} · LOH compaction ${metrics.runtime.lohCompactionMode}`;
}

// Makes the application-facing contract explicit: normalized stores are composed during ordinary Abraxas/Inheto materialization, not by a separate ORM layer.
function renderRelationshipArchitecture(state){
  if(state!=='Ready')return;
  content.insertAdjacentHTML('beforeend',`<section class="card relationship-card" id="relationship-architecture"><div class="relationship-intro"><div><p class="eyebrow">NORMALIZED STORES → NATURAL OBJECTS</p><h2>Cross-store composition. No separate ORM.</h2><p>Meridian registers property-path relationship codecs once. Abraxas and Inheto invoke them while materializing each object, so application code receives the natural object graph directly.</p></div><span class="relationship-badge">ABRAXAS + INHETO</span></div><div class="relationship-flow" aria-label="Normalized stores composed into one runtime object"><div class="relationship-node stores"><small>NORMALIZED RECORDSTORES</small><strong>orders</strong><strong>order-lines</strong><strong>customers</strong></div><div class="relationship-arrow"><span>property-path links</span><b>→</b><small>materialize</small></div><div class="relationship-node object"><small>APPLICATION OBJECT</small><strong>SalesOrder</strong><code>{ CustomerID, Lines: List&lt;OrderLine&gt; }</code><span>.Lines is complete before Get returns</span></div></div><div class="relationship-patterns"><article><span class="pattern-number">01</span><div><h3>Parent-owned ordered membership</h3><p><code>SalesOrder.Lines</code> stores packed line identities. Its registered reader uses direct <code>GetMany</code> against the separate line store and preserves order.</p></div></article><article><span class="pattern-number">02</span><div><h3>Child-owned reverse relationship</h3><p><code>Customer.Orders</code> is derived from the customer record identity through the indexed <code>SalesOrder.CustomerID</code> property.</p></div></article></div><div class="relationship-consumer"><div><small>ORDINARY CONSUMER CODE</small><code>SalesOrder order = Orders.Get(orderID);<br>foreach (OrderLine line in order.Lines!) { … }</code></div><p><strong>No repository join, tracking context, or post-load mapper.</strong><br>Bulk grids and analytics can deliberately opt out with <code>ReadNoLinks</code> / <code>GetNoLinks</code> when only scalar data is needed.</p></div></section>`);
}

// Dataset storage is measured from the RecordBase files, never inferred from record counts.
function renderStorage(){
  const storage=overview.storage;if(!storage)return;
  const bytes=value=>`${(Number(value)/1048576).toFixed(2)} MiB · ${number(value)} bytes`;
  content.querySelector('.card').insertAdjacentHTML('beforebegin',`<section class="card" id="storage-details" style="margin-top:24px"><div class="section-heading"><h2>RecordBase storage</h2><button class="secondary" id="refresh-storage">Refresh sizes</button></div><div class="dataset-info"><p><strong>Total: ${bytes(storage.totalBytes)}</strong></p><p>LibraDex catalog format: <strong>v${number(overview.indexCatalogFileFormatVersion)}</strong> · ${overview.recoveryMigrationRequired?'legacy; explicit migration available':'embedded structural-publication recovery'}</p><table><thead><tr><th>Component</th><th>Files</th><th>Size</th></tr></thead><tbody>${storage.components.map(c=>`<tr><td>${escape(c.name)}</td><td>${number(c.count)}</td><td>${bytes(c.bytes)}</td></tr>`).join('')}<tr><td>Inheto · serialized payloads</td><td colspan="2">Embedded in Fractal data; no separate file</td></tr></tbody></table><details><summary>Individual files</summary><table><tbody>${storage.files.map(f=>`<tr><td>${escape(f.name)}</td><td>${bytes(f.bytes)}</td></tr>`).join('')}</tbody></table></details><p>${escape(storage.note)}</p><small>Measured ${escape(new Date(storage.measuredAt).toLocaleString())}</small></div></section>`);
  document.querySelector('#refresh-storage').onclick=async event=>{const button=event.currentTarget;button.disabled=true;button.textContent='Measuring…';try{overview.storage=await rpc('storage');if(page==='dataset'&&runtimeView==='recordbase'){document.querySelector('#storage-details')?.remove();renderStorage();}}catch(error){toast(error.message);}finally{if(button.isConnected){button.disabled=false;button.textContent='Refresh sizes';}}};
}

// A full-object audit is intentionally order-independent: OpenUnordered avoids paying for an order the workflow never consumes.
function renderDatasetAudit(state){
  if(state!=='Ready')return;
  content.insertAdjacentHTML('beforeend',`<section class="card audit-card" id="dataset-audit"><div class="section-heading"><div><h2>Full-object hydration + relationship audit</h2><p>Hydrate every normalized order and line, then validate indexed master references and exact packed line membership.</p></div><button class="primary" id="run-dataset-audit">Run audit ↗</button></div><div class="dataset-info"><p><strong>Routes:</strong> unordered object readers · complete scalar indexes · direct identity probes · registered link hydration</p><p id="dataset-audit-status" role="status">Ready to test ${number(overview.orders)} orders and ${number(overview.lines)} order lines.</p><div class="progress" id="dataset-audit-progress"><i></i></div><div id="dataset-audit-result"></div></div></section>`);
  updateDatasetAuditProgress();
  const button=document.querySelector('#run-dataset-audit');
  button.onclick=async()=>{
    datasetAuditProgress={phase:'Starting audit',completedOrders:0,completedLines:0,totalOrders:overview.orders,totalLines:overview.lines,determinate:false,phaseCompleted:0,phaseTotal:0,phaseUnit:'tests',detail:'Preparing measured audit tests',elapsedMs:0};datasetAuditResult=null;datasetAuditProgressSeen=false;updateDatasetAuditProgress();
    button.disabled=true;document.querySelectorAll('nav button,#refresh-storage,#new-workspace').forEach(element=>element.disabled=true);
    try{datasetAuditResult=await rpc('audit-dataset');updateDatasetAuditProgress();toast(datasetAuditResult.passed?'Full-object hydration audit passed.':'Hydration audit found inconsistencies.');return datasetAuditResult;}
    catch(error){toast(error.message);return null;}
    finally{document.querySelectorAll('nav button,#refresh-storage,#new-workspace').forEach(element=>element.disabled=false);if(button.isConnected)button.disabled=false;}
  };
}

// Updates only the live audit region so progress messages never replace unrelated Dataset state or controls.
function updateDatasetAuditProgress(){
  const status=document.querySelector('#dataset-audit-status'),bar=document.querySelector('#dataset-audit-progress'),fill=bar?.querySelector('i'),result=document.querySelector('#dataset-audit-result');
  if(!status||!bar||!fill||!result)return;
  if(datasetAuditProgress){
    const s=datasetAuditProgress,completed=Number(s.phaseCompleted),total=Number(s.phaseTotal),percent=total?Math.min(100,completed/total*100):100;
    bar.classList.toggle('indeterminate',!s.determinate||!total);fill.style.width=s.determinate&&total?`${percent}%`:'35%';
    status.textContent=`${s.phase} · ${s.detail} · ${(s.elapsedMs/1000).toFixed(1)}s`;
  }else{
    bar.classList.remove('indeterminate');fill.style.width='0';status.textContent=`Ready to test ${number(overview.orders)} orders and ${number(overview.lines)} order lines.`;
  }
  if(!datasetAuditResult){result.replaceChildren();return;}
  const r=datasetAuditResult;
  const timing=check=>{const total=Math.max(Number(check.elapsedMs),0.0001),database=Number(check.databaseMs),calculation=Number(check.calculationMs),rate=check.rateRelevant?`<span>${number(Number(check.workItems)/(total/1000))} ${escape(check.workUnit)}/s</span>`:'';return `<div class="audit-timing"><strong>${duration(total)} total</strong><span>Abraxas ${duration(database)} · ${(database/total*100).toFixed(1)}%</span><span>App/calc ${duration(calculation)} · ${(calculation/total*100).toFixed(1)}%</span>${rate}</div>`;};
  result.innerHTML=`<div class="audit-summary ${r.passed?'passed':'failed'}"><strong>${r.passed?'Audit passed':'Review findings'}</strong><span>${number(r.recordsScanned)} base objects scanned</span><span>${number(r.hydratedObjects)} total objects hydrated</span><span>${r.checks.length} tests · ${duration(r.elapsedMs)} native</span></div><div class="audit-reconciliation">Measured tests ${duration(r.measuredTestMs)} · unassigned orchestration ${duration(r.unattributedMs)} · exact membership buffer ${(Number(r.packedMembership.temporaryBytes)/1048576).toFixed(2)} MiB</div><div class="audit-table-wrap"><table class="audit-tests"><thead><tr><th>Test</th><th>Route + workload</th><th>Timing</th><th>Result + measured evidence</th></tr></thead><tbody>${r.checks.map(check=>`<tr><td class="strong">${escape(check.name)}</td><td><code>${escape(check.route)}</code><span class="audit-work">${number(check.workItems)} ${escape(check.workUnit)}</span></td><td>${timing(check)}</td><td><span class="badge ${check.passed?'':'cancelled'}">${check.passed?'Passed':'Finding'}</span><span class="audit-findings">${number(check.findings)} findings</span><p>${escape(check.detail)}</p></td></tr>`).join('')}</tbody></table></div><p class="card-sub">${escape(r.route)} · identity fingerprints: orders ${escape(r.orders.identityXor)}, lines ${escape(r.lines.identityXor)}</p>`;
}

function renderGenerationControls(state){
  const maximumOrders=overview.maximumGeneratedOrders??104857;
  const welcome=document.querySelector('.generation-welcome');
  if(welcome)welcome.innerHTML=`<p class="eyebrow">CHOOSE YOUR WORKLOAD</p><h2>From a quick tour to a substantial corpus.</h2><p>Normalized records, deterministic values, bounded write checkpoints. Each workspace is generated once.</p><div class="profile-grid">${[['Smoke','1,000 orders','5,000 lines'],['Standard','200,000 orders','1 million lines'],['Large','2 million orders','10 million lines'],['Custom','Your order count','3–7 lines per order']].map(([name,orders,lines])=>`<label class="profile-card"><input type="radio" name="profile" value="${name}" ${name==='Smoke'?'checked':''} ${(name==='Standard'&&maximumOrders<200000)||(name==='Large'&&maximumOrders<2000000)?'disabled':''}><b>${name}</b><span>${orders}</span><small>${lines}</small></label>`).join('')}</div><label>Custom orders <input id="custom-orders" type="number" min="1" max="${maximumOrders}" value="10000"></label><p>This edition supports up to ${number(maximumOrders)} generated orders. Standard/Large require larger compiled capacity. Large profiles can take substantial time and disk space. Cancellation retains completed chunks; it does not roll back. Index builds finish their current index before stopping.</p><button id="generate" class="primary">Generate workspace ↗</button> <button id="cancel-generation" class="secondary" hidden>Cancel generation</button><p id="generation-status" role="status"></p><div class="progress"><i></i></div>`;
  const info=document.querySelector('.dataset-info');
  info.innerHTML=`Profile: ${escape(overview.manifest.profile||'Legacy smoke')} · generator ${escape(overview.manifest.version)} · seed ${overview.manifest.seed}<br>Records: ${number(overview.orders)} orders / ${number(overview.lines)} lines<br>Ingestion: ${((overview.manifest.ingestionMilliseconds||0)/1000).toFixed(2)}s · indexing: ${((overview.manifest.indexingMilliseconds||0)/1000).toFixed(2)}s · total: ${(overview.manifest.generationMilliseconds/1000).toFixed(2)}s<br>LibraDex format: v${number(overview.indexCatalogFileFormatVersion)} · ${overview.recoveryMigrationRequired?'migration required':'embedded recovery enabled'}<br>Location: <code>${escape(overview.directory)}</code><p>New datasets open in a separate window. This workspace and any partial records remain unchanged.</p><button class="secondary" id="new-workspace">New workspace window ↗</button>`;
  document.querySelector('#new-workspace').onclick=()=>rpc('new-workspace').catch(error=>toast(error.message));
  if(state==='Ready')info.insertAdjacentHTML('beforeend','<p>Catalog-price and stock-filter indexes are prepared by the application. Experimental covering-index construction remains a separate diagnostic, not part of normal startup.</p>');
  if(overview.manifest.indexTimings?.length)info.insertAdjacentHTML('beforeend',`<h3>Index build timings</h3><table><tbody>${overview.manifest.indexTimings.map(t=>`<tr><td>${escape(t.name)}</td><td>${(t.milliseconds/1000).toFixed(3)}s</td></tr>`).join('')}</tbody></table>`);
  const button=document.querySelector('#generate');if(!button)return;
  document.querySelector('#cancel-generation').onclick=async event=>{event.currentTarget.disabled=true;await rpc('cancel-generation');toast('Cancellation requested; finishing the current safe unit.');};
  button.onclick=async()=>{
    const profile=document.querySelector('[name="profile"]:checked').value,orders=Number(document.querySelector('#custom-orders').value);
    if(profile==='Custom'&&(!Number.isInteger(orders)||orders<1||orders>maximumOrders)){toast(`Custom orders must be 1–${number(maximumOrders)} for this edition.`);return;}
    button.disabled=true;document.querySelector('#cancel-generation').hidden=false;
    document.querySelectorAll('nav button,#new-workspace,[name="profile"],#custom-orders').forEach(el=>el.disabled=true);
    try{await rpc('generate-profile',{profile,orders});await navigate('overview');}
    catch(error){toast(error.message);await navigate('dataset');}
    finally{document.querySelectorAll('nav button').forEach(el=>el.disabled=false);}
  };
}
document.querySelectorAll('[data-page]').forEach(button=>button.onclick=()=>navigate(button.dataset.page));
document.querySelector('#diagnostics-button').onclick=()=>document.querySelector('#diagnostics').hidden=!document.querySelector('#diagnostics').hidden;
document.querySelector('#close-diagnostics').onclick=()=>document.querySelector('#diagnostics').hidden=true;
document.querySelectorAll('[data-close-lightbox]').forEach(button=>button.onclick=closeLightbox);
document.addEventListener('pointerdown',event=>{if(activeMeridianSelect&&!activeMeridianSelect.wrapper.contains(event.target)&&!activeMeridianSelect.list.contains(event.target))closeMeridianSelect();});
document.addEventListener('keydown',event=>{if(event.key==='Escape'){closeMeridianSelect();closeLightbox();++detailRequest;drawer.hidden=true;document.querySelector('#diagnostics').hidden=true;}});
window.addEventListener('resize',()=>{closeMeridianSelect();requestAnimationFrame(layoutDockedPager);});
window.addEventListener('scroll',()=>closeMeridianSelect(),true);
new MutationObserver(mutations=>mutations.forEach(mutation=>{if(mutation.type==='attributes')mutation.target._meridianSelectSync?.();else mutation.addedNodes.forEach(enhanceSelects);})).observe(document.body,{subtree:true,childList:true,attributes:true,attributeFilter:['disabled']});
enhanceSelects(document);
new ResizeObserver(()=>requestAnimationFrame(layoutDockedPager)).observe(document.querySelector('#performance-bar'));
navigate('overview');
void refreshRuntimeMetrics();
setInterval(()=>{if(!document.hidden)void refreshRuntimeMetrics();},2000);
document.addEventListener('visibilitychange',()=>{if(!document.hidden)void refreshRuntimeMetrics();});

