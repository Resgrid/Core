import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import {fileURLToPath} from 'node:url';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const relative='Web/Resgrid.Web/wwwroot/js/app/internal/routes/resgrid.routes.new.js';
function exercise(dwell,radius,location=true,name='Example',linkedCall='') {
  const source=fs.readFileSync(path.join(root,relative),'utf8');
  const addStart=source.indexOf("$('#saveStopBtn').on('click'");
  const submitStart=source.indexOf("$('form').on('submit'");
  const submitEnd=source.indexOf("$('#PendingStopsJson').val(JSON.stringify(stopsToSubmit));",submitStart);
  assert.ok(addStart>0 && submitStart>addStart && submitEnd>submitStart);
  const values={'#stopName':name,'#stopLat':location?'41.85':'','#stopLng':location?'-87.69':'','#stopType':linkedCall?'1':'0','#linkedCallId':linkedCall,'#stopPriority':'0','#stopDwellMinutes':dwell,'#stopGeofenceRadius':radius,'#stopContactName':'Training contact','#stopContactNumber':'312-555-0110','#stopNotes':'Fictional exercise','#stopContactId':'','#stopAddress':'Chicago, IL','#stopPlannedArrival':'','#stopPlannedDeparture':''};
  const handlers={},alerts=[];
  const jq=selector=>({on(event,handler){handlers[selector+':'+event]=handler;},val(...args){if(args.length){values[selector]=args[0];return this;}return values[selector];},modal(){}});
  jq.trim=value=>String(value??'').trim();
  const context=vm.createContext({$:jq,pendingStops:[],stopIdCounter:0,stopEditor:{refresh(){}},alert:message=>alerts.push(message)});
  vm.runInContext(source.slice(addStart,source.indexOf('// ── Serialize stops',addStart)),context);
  vm.runInContext(source.slice(submitStart,source.indexOf('});',submitEnd)+3),context);
  handlers['#saveStopBtn:click']();
  handlers['form:submit']();
  return {payload:JSON.parse(values['#PendingStopsJson']),alerts};
}
const cases=[['10','75',10,75],['','',null,null],['0','0',0,0],['1e2','25',100,25]];
for(const [dwell,radius,expectedDwell,expectedRadius] of cases){
  const {payload,alerts}=exercise(dwell,radius,true,'Example','501');
  assert.equal(alerts.length,0);assert.equal(payload.length,1);
  assert.equal(payload[0].callId,501);
  assert.equal(payload[0].dwellMinutes,expectedDwell);
  assert.equal(payload[0].geofenceRadius,expectedRadius);
}
for(const bad of ['1.5','no','-1','Infinity','2147483648']) {
  const result=exercise(bad,'75');assert.equal(result.payload.length,0);assert.equal(result.alerts.length,1);
}
for(const [location,name] of [[false,'Example'],[true,'']]) {
  const result=exercise('10','75',location,name);assert.equal(result.payload.length,0);assert.equal(result.alerts.length,1);
}
console.log('Route Add Stop: numeric, blank, linked-call and invalid-input cases passed.');
