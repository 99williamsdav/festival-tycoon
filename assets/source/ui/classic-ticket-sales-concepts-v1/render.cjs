const {chromium}=require('C:/Users/99wil/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const path=require('path');
(async()=>{const browser=await chromium.launch({headless:true,executablePath:"C:/Program Files/Google/Chrome/Application/chrome.exe"});const page=await browser.newPage({viewport:{width:1280,height:720},deviceScaleFactor:1});
for(const [query,name] of [['','01-band-booking-1280.png'],['?state=sales','02-sales-to-site-prep-1280.png'],['?state=rebook','03-rebook-confirmation-1280.png']]){await page.goto('file:///'+path.join(__dirname,'screen.html').replaceAll('\\','/')+query);await page.evaluate(()=>document.fonts.ready);await page.screenshot({path:path.join(__dirname,name)});}
await page.setViewportSize({width:1920,height:1080});await page.goto('file:///'+path.join(__dirname,'screen.html').replaceAll('\\','/'));await page.evaluate(()=>document.fonts.ready);await page.screenshot({path:path.join(__dirname,'04-band-booking-1920.png')});await browser.close();console.log('Four concept screenshots rendered.');})();


