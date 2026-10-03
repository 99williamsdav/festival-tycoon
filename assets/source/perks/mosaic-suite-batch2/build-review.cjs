// Batch 2 follows the suite-v1 review pipeline; native game text is not baked into art.
const fs=require('fs'),path=require('path'),crypto=require('crypto');
const {chromium}=require('C:/Users/99wil/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const root=__dirname,m=JSON.parse(fs.readFileSync(path.join(root,'manifest.json'),'utf8'));
const catalogue=fs.readFileSync('C:/Projects/festival-tycoon/src/Festival.Simulation/Perks.cs','utf8');
const constants=Object.fromEntries([...catalogue.matchAll(/(\w+)\s*=\s*"([a-z-]+)"/g)].map(x=>[x[1],x[2]]));
const definitions=[...catalogue.matchAll(/new\(("[^"]+"|\w+),\s*"([^"]+)",\s*"([^"]+)"\)/g)].map(x=>({id:x[1].startsWith('"')?JSON.parse(x[1]):constants[x[1]],name:x[2],effect:x[3]}));
for(const c of m.cards){
 if(!definitions.some(d=>d.id===c.id&&d.name===c.name&&d.effect===c.effect))throw Error('Catalogue mismatch '+c.id);
 const b=fs.readFileSync(path.join(root,c.artwork));
 if(b.toString('hex',0,8)!=='89504e470d0a1a0a'||b.readUInt32BE(16)!==1536||b.readUInt32BE(20)!==1024)throw Error('PNG size/format '+c.id);
 c.sha256=crypto.createHash('sha256').update(b).digest('hex');
}
const esc=s=>s.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('"','&quot;');
const art=c=>`<img src="${c.artwork}" alt="${esc(c.name)}">`;
const card=c=>`<article class="card" id="card-${c.id}"><div class="category">FESTIVAL PERK</div><h2>${esc(c.name)}</h2><div class="art">${art(c)}</div><p class="effect">${esc(c.effect)}</p></article>`;
const owned=c=>`<article class="owned" id="owned-${c.id}"><header><div class="thumb">${art(c)}</div><h2>${esc(c.name)}</h2></header><p>${esc(c.effect)}</p></article>`;
const html=`<!doctype html><html lang="en"><meta charset="utf-8"><title>Mosaic perks · batch 2 review</title><style>
*{box-sizing:border-box}body{margin:0;padding:24px;background:#d8d6bd;color:#2d3a37;font:16px/1.35 Arial,sans-serif}h1{font:28px Georgia,serif;margin:0 0 8px}.intro{margin:0 0 20px}.sheet{display:grid;grid-template-columns:repeat(3,248px);gap:24px;width:max-content}.unit>small{display:block;font-size:11px;margin:8px 0 0}.card{width:248px;height:358px;padding:14px;border:2px solid #596450;border-radius:10px;background:#fff4d6;position:relative}.card:before{content:'';position:absolute;inset:5px;border:1px solid #bcad86;border-radius:5px;pointer-events:none}.category{font-size:10px;letter-spacing:.13em;line-height:12px}.card h2{font:500 25px/1.04 Georgia,serif;height:56px;margin:5px 0 8px}.art{width:216px;height:145px;padding:10px;background:#e8e5cc;border-radius:4px;display:flex;align-items:center;justify-content:center}.art img{width:100%;height:100%;object-fit:contain}.effect{font:16px/1.3 Arial,sans-serif;margin:10px 0 0;padding-top:10px;border-top:1px solid #bbae89}.owned-sheet{display:grid;grid-template-columns:repeat(3,250px);gap:24px;width:max-content}.owned{width:250px;height:142px;padding:9px;background:#fff4d6;border-radius:5px}.owned header{display:flex;gap:8px}.thumb{width:46px;height:46px;flex:none;display:flex;align-items:center;justify-content:center}.thumb img{width:46px;height:46px;object-fit:contain}.owned h2{font:500 19px/1.1 Georgia,serif;width:174px;min-height:46px;margin:0}.owned p{font:14px/1.2 Arial,sans-serif;margin:6px 0 0;width:232px;min-height:66px}.section{font:24px Georgia,serif;margin:30px 0 15px}footer{margin-top:20px;font-size:13px}
h1{line-height:34px}.intro{line-height:22px}.unit>small{line-height:16px}.section{line-height:28px}
</style><h1>Festival perks · mosaic suite · batch 2</h1><p class="intro">Six new perks · exact current catalogue copy · artwork is text-free</p><main class="sheet">${m.cards.map(c=>`<section class="unit">${card(c)}<small>${c.id}</small></section>`).join('')}</main><h2 class="section">Suite-v1 owned-size review · 250 × 142</h2><section class="owned-sheet">${m.cards.map(owned).join('')}</section><footer>Review compositions follow the suite-v1 pipeline, not a running-game screenshot. Runtime PNGs have no titles or effect text.</footer></html>`;
fs.writeFileSync(path.join(root,'review.html'),html);
for(const d of ['cards','small'])fs.mkdirSync(path.join(root,d),{recursive:true});
(async()=>{
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 const checks=[];
 try{
 const page=await browser.newPage({viewport:{width:870,height:1200},deviceScaleFactor:1});
 await page.goto('file:///'+path.join(root,'review.html').replaceAll('\\','/'));
 await page.locator('img').evaluateAll(xs=>Promise.all(xs.map(x=>x.decode())));
 for(const c of m.cards){
  const el=page.locator('#card-'+c.id);
  await el.screenshot({path:path.join(root,c.draftPreview)});
  await page.locator('#owned-'+c.id).screenshot({path:path.join(root,c.ownedPreview)});
  checks.push(await el.evaluate(el=>{let p=el.querySelector('.effect'),r=p.getBoundingClientRect(),b=el.getBoundingClientRect();return{id:el.id,textFits:r.bottom<=b.bottom-8,width:b.width,height:b.height}}));
 }
 if(checks.some(c=>!c.textFits))throw Error('Effect overflow '+JSON.stringify(checks));
 await page.screenshot({path:path.join(root,'contact-sheet.png'),fullPage:true});
 await page.locator('.owned-sheet').screenshot({path:path.join(root,'owned-size-sheet.png')});
 const large=await browser.newPage({viewport:{width:870,height:1200},deviceScaleFactor:3});
 await large.goto('file:///'+path.join(root,'review.html').replaceAll('\\','/'));
 await large.locator('img').evaluateAll(xs=>Promise.all(xs.map(x=>x.decode())));
 for(const c of m.cards)await large.locator('#card-'+c.id).screenshot({path:path.join(root,c.fullReview)});
 for(const c of m.cards)for(const [key,width,height] of [['fullReview',744,1074],['draftPreview',248,358],['ownedPreview',250,142]]){
  const b=fs.readFileSync(path.join(root,c[key]));
  if(b.readUInt32BE(16)!==width||b.readUInt32BE(20)!==height)throw Error('Preview dimensions '+c.id+' '+key);
 }
 fs.writeFileSync(path.join(root,'manifest.json'),JSON.stringify(m,null,2)+'\n');
 fs.writeFileSync(path.join(root,'checks.json'),JSON.stringify({catalogue:'6 exact current matches',pngMasters:'6 PNGs, each 1536x1024',checks},null,2)+'\n');
 console.log('PASS: six exact catalogue matches, correct master sizes, fitting review copy, full/draft/owned previews.');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
