const sharp=require('C:/Users/99wil/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const R=__dirname, poses=['relaxed','drink_hold','food_hold','drinking','drinking_soft','eating'];
function label(t,w){return Buffer.from(`<svg width="${w}" height="45"><text x="12" y="29" font-family="Arial" font-size="19" fill="#383c35">${t}</text></svg>`)}
(async()=>{
 for(const sex of ['male','female']){
  let layers=[];
  for(let i=0;i<poses.length;i++){
   let x=(i%3)*450,y=Math.floor(i/3)*495;
   layers.push({input:label(sex+' / '+poses[i],450),left:x,top:y},{input:await sharp(`${R}/${sex}-${poses[i]}-beauty.png`).resize(450,450).png().toBuffer(),left:x,top:y+45});
  }
  await sharp({create:{width:1350,height:990,channels:3,background:'#eee8da'}}).composite(layers).png().toFile(`${R}/review-${sex}.png`);
  layers=[];
  for(let i=1;i<poses.length;i++)for(let j=0;j<2;j++){
   let x=(i-1)*300,y=j*345;
   layers.push({input:label(poses[i]+(j?' side':''),300),left:x,top:y},{input:await sharp(`${R}/${sex}-${poses[i]}-contact${j?'-side':''}.png`).resize(300,300).png().toBuffer(),left:x,top:y+45});
  }
  await sharp({create:{width:1500,height:690,channels:3,background:'#eee8da'}}).composite(layers).png().toFile(`${R}/contact-${sex}.png`);
 }
 let layers=[];
 for(const [j,sex] of ['male','female'].entries())for(const [i,p] of poses.entries()){
  layers.push({input:label(sex+' '+p,300),left:i*300,top:j*265},{input:await sharp(`${R}/${sex}-${p}-game.png`).extract({left:810,top:430,width:300,height:220}).png().toBuffer(),left:i*300,top:j*265+45});
 }
 await sharp({create:{width:1800,height:530,channels:3,background:'#eee8da'}}).composite(layers).png().toFile(`${R}/gameplay-scale.png`);
})();
