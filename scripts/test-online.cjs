// Run against a local server: dotnet run --project src/MerciTantine.Server --urls http://127.0.0.1:5189
const assert=require('node:assert/strict');
const {randomUUID}=require('node:crypto');
const base=process.argv[2]||'http://127.0.0.1:5189';
let checks=0;
async function req(path,method='GET',body,token,status=200){
 const r=await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:body?JSON.stringify(body):undefined});
 assert.equal(r.status,status,`${method} ${path}: ${await r.clone().text()}`);checks++;return r.json();
}
(async()=>{
 await req('/health');
 const a=await req('/rooms','POST',{name:'Alice',humans:2,bots:1,difficulty:'Hard'});
 let s=await req(`/rooms/${a.code}`,'GET',null,a.token);assert.equal(s.started,false);
 await req(`/rooms/${a.code}`,'GET',null,null,401);
 await req(`/rooms/${a.code}/commands`,'POST',{requestId:randomUUID(),expectedRevision:0,kind:'end'},a.token,400);
 const b=await req(`/rooms/${a.code}/join`,'POST',{name:'Bob'});
 await req(`/rooms/${a.code}/join`,'POST',{name:'Intrus'},null,400);
 s=await req(`/rooms/${a.code}`,'GET',null,a.token);assert.equal(s.started,true);
 await req(`/rooms/${a.code}/commands`,'POST',{requestId:randomUUID(),expectedRevision:s.revision,kind:'explore',room:'salon',first:'Object',second:'Object'},b.token,400);
 const command={requestId:randomUUID(),expectedRevision:s.revision,kind:'explore',room:'salon',first:'Object',second:'Paper'};
 const after=await req(`/rooms/${a.code}/commands`,'POST',command,a.token);assert.equal(after.view.hidden.length,1);assert.equal(after.view.hand.length,1);
 const duplicate=await req(`/rooms/${a.code}/commands`,'POST',command,a.token);assert.equal(duplicate.revision,after.revision);assert.deepEqual(duplicate.view,after.view);
 const bob=await req(`/rooms/${a.code}`,'GET',null,b.token);
 assert.equal(bob.view.hidden.length,0);assert.equal(bob.view.hand.length,0);assert.equal(bob.view.opponents[0].hiddenCount,1);
 assert.ok(!('hidden' in bob.view.opponents[0]));assert.ok(!('hand' in bob.view.opponents[0]));assert.ok(!('randomState' in bob));assert.ok(!('cards' in bob));
 assert.ok(!bob.view.events.some(e=>e.privateTo===0));
 await req(`/rooms/${a.code}/commands`,'POST',{requestId:randomUUID(),expectedRevision:0,kind:'end'},a.token,409);
 const ended=await req(`/rooms/${a.code}/commands`,'POST',{requestId:randomUUID(),expectedRevision:after.revision,kind:'end'},a.token);assert.equal(ended.actingSeat,1);
 const reconnect=await req(`/rooms/${a.code}`,'GET',null,a.token);assert.equal(reconnect.revision,ended.revision);assert.equal(reconnect.view.hand.length,1);
 // Two clients race with the same expected revision: only one exploration may commit.
 const race=()=>fetch(`${base}/rooms/${a.code}/commands`,{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+b.token},body:JSON.stringify({requestId:randomUUID(),expectedRevision:ended.revision,kind:'explore',room:'bureau',first:'Object',second:'Object'})});
 const raced=await Promise.all([race(),race()]);assert.deepEqual(raced.map(r=>r.status).sort(),[200,409]);checks+=2;
 if(process.argv[3]){
   const before=await req(`/rooms/${a.code}`,'GET',null,a.token);
   require('node:child_process').execFileSync('docker',['restart',process.argv[3]],{stdio:'ignore'});
   for(let i=0;i<30;i++){try{const r=await fetch(base+'/health');if(r.ok)break;}catch{}await new Promise(r=>setTimeout(r,1000));}
   const afterRestart=await req(`/rooms/${a.code}`,'GET',null,a.token);
   assert.deepEqual(afterRestart,before);checks++;
 }
 console.log(`${checks} échanges HTTP vérifiés : authentification, lobby, confidentialité, autorité, idempotence, concurrence, reconnexion.`);
})().catch(e=>{console.error(e);process.exitCode=1;});
