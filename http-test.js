'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {spawn}=require('node:child_process');
const {once}=require('node:events');
const net=require('node:net');
const http=require('node:http');
const delay=ms=>new Promise(r=>setTimeout(r,ms));
async function freePort(){const server=net.createServer();server.listen(0,'127.0.0.1');await once(server,'listening');const port=server.address().port;await new Promise(r=>server.close(r));return port;}
async function main(){
 const port=await freePort(),base='http://127.0.0.1:'+port;
 const root=fs.mkdtempSync(path.join(__dirname,'http-test-data-'));
 const child=spawn(path.join(__dirname,'bin','NetWatch.exe'),['--headless','--port='+port,'--data='+root],{windowsHide:true,stdio:'ignore'});
 let token='',checks=0;
 const check=(v,s)=>{assert.ok(v,s);checks++;console.log('PASS '+s);};
 async function req(route,body,extra={}){return fetch(base+route,{method:body===undefined?'GET':'POST',headers:{'X-NetWatch-Token':token,...(body===undefined?{}:{'Content-Type':'application/json'}),...extra},body:body===undefined?undefined:JSON.stringify(body)});}
 try{
  for(let i=0;i<50;i++){try{let r=await fetch(base+'/api/session');token=(await r.json()).Token;if(token)break;}catch{}await delay(100);}
  check(token.length===64,'service starts without admin URL reservations');
  const advertised=await (await fetch(base+'/api/session',{headers:{Host:'127.0.0.1:'+port}})).json();check(advertised.Version==='1.4.0'&&advertised.AccessUrl,'session advertises LAN access address');
  const index=await fetch(base+'/');check(index.status===200&&(await index.text()).includes('NetWatch'),'embedded dashboard HTML');
  check(index.headers.get('Content-Security-Policy').includes("frame-ancestors 'none'"),'browser isolation headers');
  check((await fetch(base+'/api/dashboard')).status===403,'API rejects missing token');
  const hostStatus=await new Promise((resolve,reject)=>{const request=http.request({host:'127.0.0.1',port,path:'/api/dashboard',headers:{Host:'attacker.test:'+port,'X-NetWatch-Token':token}},r=>{r.resume();resolve(r.statusCode);});request.on('error',reject);request.end();});
  check(hostStatus===403,'host validation prevents DNS rebinding');
  check((await req('/api/demo',{}, {'Origin':'https://attacker.test'})).status===403,'cross-origin write rejected');
  check((await req('/api/demo',{}, {'X-NetWatch-Token':'bad'})).status===403,'invalid mutation token rejected');
  check((await req('/api/demo',{})).status===200,'demo devices are added through API');
  let dash=await (await req('/api/dashboard')).json();check(dash.Devices.length===3&&dash.Devices.every(d=>d.Config.Demo),'demo labels exposed for every simulated device');
  check(Array.isArray(dash.Trend)&&Array.isArray(dash.Links)&&dash.Links.length>0,'dashboard returns real trend and interface rows');
  const id=dash.Devices[0].Config.Id;
  let detail=await (await req('/api/device?id='+id)).json();check(detail.History.length>=120&&detail.Current.Interfaces.length===8,'device detail returns history and ports');
  let csv=await (await req('/api/export?id='+id)).text();check(csv.includes('时间(UTC)')&&csv.split('\n').length>=120,'CSV history export');
  const config={Name:'API test',Address:'127.0.0.1',Community:'test-only-not-a-real-secret',Port:await freePort(),Enabled:false};
  let saved=await (await req('/api/save',config)).json();check(saved.Id&&saved.HasCommunity&&!JSON.stringify(saved).includes(config.Community),'create device encrypts and redacts credential');
  check((await fetch(base+'/api/topology',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({Id:saved.Id,X:25,Y:70})})).status===403,'topology save rejects missing token');
  check((await req('/api/topology',{Id:saved.Id,X:25,Y:70})).status===200,'topology position saves through API');
  dash=await (await req('/api/dashboard')).json();check(dash.Topology[saved.Id].x===25&&dash.Topology[saved.Id].y===70&&fs.existsSync(path.join(root,'topology.json')),'dashboard returns persisted topology position');
  check((await req('/api/topology',{Id:saved.Id,X:150,Y:70})).status===400,'topology API rejects unsafe coordinates');
  check((await req('/api/topology/reset',{})).status===200,'topology layout reset through API');
  dash=await (await req('/api/dashboard')).json();check(Object.keys(dash.Topology).length===0,'topology reset is reflected in dashboard');
  check((await req('/api/save',config)).status===400,'duplicate address and port rejected');
  check((await req('/api/save',{...config,Address:'224.0.0.1'})).status===400,'multicast target rejected');
  check((await req('/api/save',{...config,Address:'not-an-ip'})).status===400,'invalid IP rejected');
  check((await req('/api/save',{...config,Id:saved.Id,Community:'',Name:'Edited via API'})).status===200,'edit keeps community when blank');
  check((await req('/api/delete',{Id:saved.Id})).status===200,'remove device stops management');
  check((await req('/api/device?id='+saved.Id)).status===400,'removed device is inaccessible');
  const raw=fs.readFileSync(path.join(root,'devices.json'),'utf8');check(!raw.includes(config.Community),'no plaintext community written to disk');
  console.log('ALL '+checks+' HTTP CHECKS PASSED');
 } finally {child.kill();await once(child,'exit').catch(()=>{});}
}
main().catch(e=>{console.error(e);process.exitCode=1;});
