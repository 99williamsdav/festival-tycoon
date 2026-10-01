const fs=require('fs'),path=require('path'),crypto=require('crypto');
const root=__dirname,m=JSON.parse(fs.readFileSync(path.join(root,'manifest.json'))),errors=[];
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const check=(ok,n)=>{if(!ok)errors.push(n)};
for(const [name,a] of Object.entries(m.assets)){
 const file=path.join(root,'runtime',a.file),b=fs.readFileSync(file),jl=b.readUInt32LE(12),j=JSON.parse(b.subarray(20,20+jl)),start=20+jl+8;
 check(hash(file)===a.sha256,name+' GLB hash');check(hash(path.join(root,'source',a.source))===a.source_sha256,name+' source hash');
 check(j.meshes.length===a.meshes&&j.materials.length===a.materials,name+' counts');
 check(!j.animations?.length&&!j.skins?.length&&!j.cameras?.length,name+' unintended rig/review objects');
 check(j.meshes.reduce((n,mesh)=>n+mesh.primitives.reduce((s,p)=>s+j.accessors[p.indices].count/3,0),0)===a.triangles,name+' triangle count');
 for(const mesh of j.meshes)check(mesh.primitives.length===1,name+' single surface per mesh');
 for(const mat of j.materials)check((mat.alphaMode||'OPAQUE')==='OPAQUE',name+' opaque');
 for(const image of j.images)check(image.bufferView!==undefined,name+' embedded palette');
 const names=j.nodes.map(n=>n.name);
 const body=name.includes('cleanup_');
 check(a.meshes===(body?9:1),name+' expected mesh count');check(a.materials===(body?2:1),name+' expected material count');
 if(body){
  for(const node of ['LowerBody','UpperPivot','UpperBody','FittedVest','LeftUpperPivot','RightUpperPivot','LeftForePivot','RightForePivot','LeftHandPivot','RightHandPivot'])check(names.includes(node),name+' missing '+node);
  const upper=j.nodes.find(n=>n.name==='UpperPivot'),vestIndex=names.indexOf('FittedVest');check(upper.children.includes(vestIndex),name+' vest parent');
  for(const n of j.nodes.filter(n=>n.mesh!==undefined)){
   const p=j.meshes[n.mesh].primitives[0],mat=j.materials[p.material],ti=mat.pbrMetallicRoughness.baseColorTexture.index,im=j.images[j.textures[ti].source],bv=j.bufferViews[im.bufferView],offset=start+(bv.byteOffset||0);
   const dims=[b.readUInt32BE(offset+16),b.readUInt32BE(offset+20)];check(dims[0]===(n.name==='FittedVest'?128:96)&&dims[1]===8,name+' palette '+n.name);
  }
 }else{
  const socket=j.nodes.find(n=>n.name===(name.includes('picker')?'PickerJawSocket':'BagMouthSocket'));
  const expected=name.includes('picker')?[0,-.8,0]:[-.175,0,0];check(socket&&socket.translation.every((x,i)=>Math.abs(x-expected[i])<1e-6),name+' socket');
 }
}
for(const source of Object.values(m.source_authority)){check(hash(source.body)===source.body_sha256,'body original changed');check(hash(source.vest)===source.vest_sha256,'vest original changed')}
const native=JSON.parse(fs.readFileSync(path.join(root,'godot-verification.json')));check(native.passed,'native proof');
for(const test of native.checks)check(test.unreachable_samples.length===0,'unreachable '+test.sex);
for(const cap of native.captures)check(fs.existsSync(path.join(root,'review',cap+'.png')),'missing capture '+cap);
m.reference_pose={file:'godot-check/cleanup_pose.gd',sha256:hash(path.join(root,'godot-check/cleanup_pose.gd')),phase:'walking=-1; ground contact .45-.5; lift .5-.85; bag hold .85-1; optional post-completion cosmetic recovery1-1.15',target_contract:'cleanup-child yaw faces actual contact; canonical target X0,Z-negative,Y-.015..+.10; radius<=.428554m; actor/litter world position unchanged',hip_bend_max_degrees:32,tool_grip_to_jaw_m:.8,bag_grip_y:{male:.93,female:.89},reach_grip_x:{male:.18,female:.17},reach_grip_z_formula:'-.14-.25*horizontal_target_radius',transfer_grip_x:{male:.26,female:.24},transfer_grip_z:-.34,hand_grip_offset_y:-.044,elbow_pole:'(side*.2,-.2,-1)'};
m.approved_concept='../steward-cleanup-concepts-v1/steward-cleanup-board.png';m.status=errors.length?'QA_FAILED':'PRODUCTION_ASSET_COMPLETE';m.verification={passed:errors.length===0,errors,scope:'native analytic contact/arm reach/planted lowerbody, GLB source hashes/nodes/palette compatibility/resources/sockets and preserved current source authority'};
fs.writeFileSync(path.join(root,'verification.json'),JSON.stringify(m.verification,null,2));fs.writeFileSync(path.join(root,'manifest.json'),JSON.stringify(m,null,2));
console.log(JSON.stringify({verification:m.verification,manifest_sha256:hash(path.join(root,'manifest.json')),reference_sha256:m.reference_pose.sha256,assets:Object.fromEntries(Object.entries(m.assets).map(([k,a])=>[k,{triangles:a.triangles,meshes:a.meshes,surfaces:a.surfaces,materials:a.materials,sha256:a.sha256}]))},null,2));if(errors.length)process.exit(1);
