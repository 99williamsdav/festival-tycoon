// Bounded text-only rerender of the existing Beer Festival HTML composition.
// Preserve other card copies, masters, source prompts and historical all-card checks.
const fs=require('fs'),path=require('path'),crypto=require('crypto');
const {chromium}=require('C:/Users/99wil/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const repo=path.resolve(__dirname,'../../..');
const root=path.join(repo,'assets/source/perks/mosaic-suite-batch2');
const catalogue=fs.readFileSync(path.join(repo,'src/Festival.Simulation/Perks.cs'),'utf8');
const expected=catalogue.match(/new\(BeerFestival,\s*"Beer Festival",\s*"([^"]+)"/)[1];
const card=JSON.parse(fs.readFileSync(path.join(root,'manifest.json'),'utf8')).cards.find(c=>c.id==='beer-festival');
if(card.effect!==expected)throw Error('Beer Festival manifest/catalogue mismatch');
const master=fs.readFileSync(path.join(root,card.artwork));
const sha=b=>crypto.createHash('sha256').update(b).digest('hex');
if(sha(master)!==card.sha256)throw Error('Artwork master changed');
if(sha(fs.readFileSync(path.join(repo,'game/assets/ui/perks/beer-festival.png')))!==card.sha256)throw Error('Runtime artwork mismatch');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
  const page=await browser.newPage({viewport:{width:870,height:1200},deviceScaleFactor:1});
  await page.goto('file:///'+path.join(root,'review.html').replaceAll('\\','/'));
  await page.locator('img').evaluateAll(xs=>Promise.all(xs.map(x=>x.decode())));
  const checks=[];
  for(const [id,selector,width,height] of [['card-beer-festival','.effect',248,358],['owned-beer-festival','p',250,142]]){
   const check=await page.locator('#'+id).evaluate((el,selector)=>{
    const text=el.querySelector(selector),bounds=el.getBoundingClientRect(),range=document.createRange();range.selectNodeContents(text);
    return{id:el.id,copy:text.textContent,width:bounds.width,height:bounds.height,textBottom:range.getBoundingClientRect().bottom-bounds.top,
     textFits:range.getBoundingClientRect().bottom<=bounds.bottom-8};
   },selector);
   if(check.copy!==expected||!check.textFits||check.width!==width||check.height!==height)throw Error('Copy/fit mismatch '+JSON.stringify(check));
   checks.push(check);
  }
  await page.locator('#card-beer-festival').screenshot({path:path.join(root,card.draftPreview)});
  await page.locator('#owned-beer-festival').screenshot({path:path.join(root,card.ownedPreview)});
  await page.screenshot({path:path.join(root,'contact-sheet.png'),fullPage:true});
  await page.locator('.owned-sheet').screenshot({path:path.join(root,'owned-size-sheet.png')});
  const large=await browser.newPage({viewport:{width:870,height:1200},deviceScaleFactor:3});
  await large.goto('file:///'+path.join(root,'review.html').replaceAll('\\','/'));
  await large.locator('img').evaluateAll(xs=>Promise.all(xs.map(x=>x.decode())));
  await large.locator('#card-beer-festival').screenshot({path:path.join(root,card.fullReview)});
  const previews=[];
  for(const [file,width,height] of [[card.fullReview,744,1074],[card.draftPreview,248,358],[card.ownedPreview,250,142]]){
   const bytes=fs.readFileSync(path.join(root,file));
   if(bytes.readUInt32BE(16)!==width||bytes.readUInt32BE(20)!==height)throw Error('Preview dimensions '+file);
   previews.push({file,width,height,sha256:sha(bytes)});
  }
  fs.writeFileSync(path.join(__dirname,'preview-checks.json'),JSON.stringify({expectedCopy:expected,masterSha256:card.sha256,checks,previews,
   note:'Existing HTML layout rendered with separate text. No artwork generation or running-game/EXE claim.'},null,2)+'\n');
  console.log('PASS: exact Beer Festival copy, draft/owned text fits, three preview sizes, unchanged artwork hashes.');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
