const fs=require('fs'),path=require('path'),crypto=require('crypto');
const sharp=require('C:/Users/99wil/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const root=__dirname,manifest=JSON.parse(fs.readFileSync(path.join(root,'manifest.json'))),errors=[];
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const assert=(ok,msg)=>{if(!ok)errors.push(msg)};
const palette=['D3E5DF','A9BCB7','BA604F','E3D8BC','687B59','AF8960','C6A77B','E9C34B','303532','DFE6D8'];
async function run(){
 for(const [name,a] of Object.entries(manifest.assets)){
  const file=path.join(root,'runtime',a.file),b=fs.readFileSync(file),jsonLength=b.readUInt32LE(12),j=JSON.parse(b.subarray(20,20+jsonLength)),binStart=20+jsonLength+8;
  assert(hash(file)===a.sha256,name+' hash');assert(j.meshes.length===1&&j.materials.length===1,name+' resources');
  assert(!j.animations?.length&&!j.skins?.length&&!j.cameras?.length,name+' unexpected components');
  assert(j.meshes[0].primitives.length===1,name+' surfaces');
  assert((j.materials[0].alphaMode||'OPAQUE')==='OPAQUE'&&j.materials[0].doubleSided,name+' material');
  assert(j.images.length===1&&j.images[0].bufferView!==undefined,name+' embedded palette');
  for(const n of j.nodes){assert(!n.translation||n.translation.every(v=>v===0),name+' offset');assert(!n.scale||n.scale.every(v=>v===1),name+' scale')}
  const prim=j.meshes[0].primitives[0],acc=j.accessors[prim.attributes.POSITION],view=j.bufferViews[acc.bufferView];
  assert(j.accessors[prim.indices].count/3===a.triangles,name+' tris');
  const pos=[];for(let i=0;i<acc.count;i++){const off=binStart+(view.byteOffset||0)+(acc.byteOffset||0)+i*(view.byteStride||12);pos.push([b.readFloatLE(off),b.readFloatLE(off+4),b.readFloatLE(off+8)])}
  const rotation=(v,q)=>{const [x,y,z,w]=q,[vx,vy,vz]=v;const tx=2*(y*vz-z*vy),ty=2*(z*vx-x*vz),tz=2*(x*vy-y*vx);return [vx+w*tx+y*tz-z*ty,vy+w*ty+z*tx-x*tz,vz+w*tz+x*ty-y*tx]};
  for(const [label,p] of Object.entries(a.ground_poses)){const yMin=Math.min(...pos.map(v=>rotation(v,p.quaternion_godot_xyzw)[1]))+p.ground_offset_godot_y;assert(Math.abs(yMin)<1e-6,name+' ground contact '+label)}
  a.bytes=b.length;a.meshes=1;a.surfaces=1;a.materials=1;
 }
 const p=path.join(root,manifest.palette),{data,info}=await sharp(p).raw().toBuffer({resolveWithObject:true});
 assert(info.width===80&&info.height===8,'palette dimensions');
 for(let i=0;i<10;i++){const expected=[0,2,4].map(k=>parseInt(palette[i].slice(k,k+2),16));assert(expected.every((v,k)=>Math.abs(v-data[(i*8+4)*info.channels+k])<=1),'palette colour '+i)}
 const native=JSON.parse(fs.readFileSync(path.join(root,'godot-verification.json')));assert(native.passed,'native proof');
 for(const c of native.captures)assert(fs.existsSync(path.join(root,'review',c.file)),'capture '+c.file);
 assert(hash(path.join(root,'source/lwf_litter_and_wasp_v1.blend'))===manifest.source_sha256,'source hash');
 const binRoot=path.join(root,'../bin-slatted-asset-v1');const bin=JSON.parse(fs.readFileSync(path.join(binRoot,'manifest.json')));
 for(const a of Object.values(bin.assets))assert(hash(path.join(binRoot,'runtime',a.file))===a.sha256,'original bin changed '+a.file);
 manifest.palette_sha256=hash(p);manifest.verification={passed:errors.length===0,errors,scope:'GLB resources/hashes/triangles, ground contact after rotations, palette sRGB values, original bin hashes and native Godot proof'};
 manifest.status=errors.length?'QA_FAILED':'PRODUCTION_ASSET_COMPLETE';
 fs.writeFileSync(path.join(root,'verification.json'),JSON.stringify(manifest.verification,null,2));fs.writeFileSync(path.join(root,'manifest.json'),JSON.stringify(manifest,null,2));
 console.log(JSON.stringify({verification:manifest.verification,manifest_sha256:hash(path.join(root,'manifest.json')),assets:Object.fromEntries(Object.entries(manifest.assets).map(([k,a])=>[k,{triangles:a.triangles,bytes:a.bytes,sha256:a.sha256}]))},null,2));if(errors.length)process.exitCode=1;
}
run().catch(e=>{console.error(e);process.exitCode=1});
