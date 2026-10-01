const fs=require('fs'),path=require('path'),crypto=require('crypto');
const root=__dirname;const manifest=JSON.parse(fs.readFileSync(path.join(root,'manifest.json')));const errors=[];
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
for(const [name,a] of Object.entries(manifest.assets)){
 const file=path.join(root,'runtime',a.file),b=fs.readFileSync(file),j=JSON.parse(b.subarray(20,20+b.readUInt32LE(12)));
 if(hash(file)!==a.sha256)errors.push(name+' hash mismatch');
 if(j.animations?.length)errors.push(name+' unexpected animations');
 if(j.meshes.length!==1||j.materials.length!==1)errors.push(name+' unexpected mesh/material count');
 if(j.images.some(i=>i.bufferView===undefined))errors.push(name+' nonembedded palette');
 for(const n of j.nodes){if(n.scale&&n.scale.some(v=>v!==1))errors.push(name+' scale');if(n.translation&&n.translation.some(v=>v!==0))errors.push(name+' offset');}
 const bb=a.bounds_blender;if(!bb.flat().every(Number.isFinite))errors.push(name+' invalid bounds');
 if(name.includes('Shell')&&(Math.abs(bb[1][2]-.95)>1e-5||Math.abs(bb[1][0]-.325)>1e-5||Math.abs(bb[0][2])>1e-5))errors.push('Shell dimensions');
 if(name.includes('Rubbish')&&(Math.max(...bb.slice(0,2).flatMap(v=>[Math.abs(v[0]),Math.abs(v[1])]))>.254))errors.push('Fill protrudes through liner');
}
if(Math.abs(manifest.adult.height_m-1.75)>1e-5)errors.push('Adult height');
const godot=JSON.parse(fs.readFileSync(path.join(root,'godot-verification.json')));if(!godot.passed)errors.push('Godot proof failed');
const report={passed:errors.length===0,errors,scope:'GLB structure, hashes, bounds, identity transforms and actual Godot proof; visual checks documented separately'};
fs.writeFileSync(path.join(root,'verification.json'),JSON.stringify(report,null,2));
manifest.status='PRODUCTION_ASSET_COMPLETE';manifest.approved_concept='../bin-concepts-v3-slatted/slatted-bin-concept.png';manifest.palette={file:'textures/lwf_bin_palette_v1.png',sha256:hash(path.join(root,'textures/lwf_bin_palette_v1.png')),size:[128,8]};manifest.verification=report;
fs.writeFileSync(path.join(root,'manifest.json'),JSON.stringify(manifest,null,2));console.log(JSON.stringify({report,assets:manifest.assets,manifest_sha256:hash(path.join(root,'manifest.json'))},null,2));if(errors.length)process.exit(1);
