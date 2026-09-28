const sharp=require('C:/Users/99wil/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp'),fs=require('fs');
const R=__dirname,ink='#293b38',paper='#fff3d3',muted='#59645d',line='#aa9f78';
const acts=[['Meadow Lanterns','Folk',40,40,20,80],['Barnstorm Circuit','Rock',55,55,35,65],['Orchard Chorus','Folk',75,70,45,90],['Field Frequency','Electronic',85,75,60,75],['Copper Static','Rock',90,80,80,70],['Neon Postcards','Pop',110,90,90,85]];
function txt(x,y,t,n=14,c=ink){return `<text x="${x}" y="${y}" font-family="Arial" font-size="${n}" fill="${c}">${t}</text>`}
function box(x,y,w,h,c,b='none'){return `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="${c}" stroke="${b}"/>`}
function star(x,y,i,score,col,id){let pts=[];for(let j=0;j<10;j++){let a=-Math.PI/2+j*Math.PI/5,r=j%2?3.4:8;pts.push(`${x+8+Math.cos(a)*r},${y+8+Math.sin(a)*r}`)}let p=pts.join(' '),units=Math.round(score/10),fraction=Math.max(0,Math.min(1,units/2-i));return `<defs><clipPath id="${id}">${box(x,y,16*fraction,17,'white')}</clipPath></defs><polygon points="${p}" fill="none" stroke="${col}" stroke-width="1"/><polygon points="${p}" fill="${col}" clip-path="url(#${id})"/>`}
async function render(w,h,filtered=false){
const pw=w-32,ph=Math.min(h-138,660),tableW=w===1280?824:1160,gap=24,tx=36,sx=tx+tableW+gap,sw=w-sx-36;
let s=box(0,0,w,h,'#737f68')+box(0,0,w,58,ink)+txt(18,27,'Lower Wittering · PREPARATION',18,paper)+txt(490,27,'Money £780     Festival clock: not started     32 expected',15,paper)+txt(w-150,27,'Alerts 0    Menu',14,paper);
s+=box(16,72,pw,ph,paper,line)+txt(36,109,'Prepare the festival',25)+txt(w-150,106,'Collapse ▴')+txt(36,143,'Overview',14)+txt(138,143,'Programme',14)+txt(259,143,'Staff',14)+txt(327,143,'Equipment',14)+txt(439,143,'Stock',14)+txt(515,143,'Site &amp; water',14)+box(130,151,99,3,ink);
s+=txt(tx,184,'Available bands',20)+box(tx,198,190,34,'#fff6df',line)+txt(tx+10,220,filtered?'Rock ▾':'All genres ▾')+txt(tx+206,220,(filtered?'2':'6')+' of 6 bands · Price ↑ · select a row, then a set',14);
let widths=w===1280?[188,96,68,136,136,200]:[260,130,100,170,180,320],xs=[tx];for(let a of widths)xs.push(xs.at(-1)+a);
s+=box(tx,246,tableW,48,'#eaddb7');let labels=['Band ↕','Genre ↕','Price ↑','Popularity ↕','Ego ↕','Professionalism ↕'];labels.forEach((a,i)=>s+=txt(xs[i]+8,266,a));s+=txt(xs[3]+8,285,'Audience appeal',12,muted)+txt(xs[4]+8,285,'Demandingness',12,'#795336')+txt(xs[5]+8,285,'Softens ego reaction',12,muted);
let data=filtered?acts.filter(a=>a[1]==='Rock'):acts;
data.forEach((a,r)=>{let y=294+r*42,sel=a[0]==='Barnstorm Circuit';s+=box(tx,y,tableW,42,sel?'#d3e8df':r%2?'#f7eaca':'#fff6df',line)+txt(xs[0]+8,y+18,a[0],14)+txt(xs[0]+8,y+34,sel?'Selected · drag to a set':a[0]==='Meadow Lanterns'?'Assigned · Set 1':a[4]>=70?'Expects to headline':'Available',12,muted)+txt(xs[1]+8,y+25,a[1])+txt(xs[2]+8,y+25,'£'+a[2]);for(let k=0;k<3;k++){for(let i=0;i<5;i++)s+=star(xs[k+3]+8+i*18,y+13,i,a[k+3],k===1?'#795336':ink,`s${r}-${k}-${i}`);s+=txt(xs[k+3]+103,y+26,String(Math.round(a[k+3]/10)/2),13,k===1?'#795336':ink)}});
s+=txt(tx,570,'High ego = more demanding / headline-sensitive. Stars show intensity, not universal quality.',13)+txt(tx,592,'Selected: Barnstorm Circuit · choose an empty set, or replace / swap an assigned act.',13);
s+=txt(sx+54,184,'Main stage',21)+txt(sx,219,'Elapsed festival time · mm:ss',13)+box(sx+54,246,sw-54,300,'#eee2be');
let starts=[15,115,215],t=[['00:15','01:30'],['01:55','03:10'],['03:35','04:50']];for(let i=0;i<3;i++){let y=246+starts[i];s+=txt(sx,y+5,t[i][0],12)+txt(sx,y+77,t[i][1],12)+box(sx+60,y,sw-66,75,i===0?'#d3e8df':'#fff6df',line)+txt(sx+70,y+24,i===0?'Meadow Lanterns':'Set '+(i+1),14)+txt(sx+70,y+46,i===0?'Folk · assigned':'Drop / assign here',13)+txt(sx+70,y+65,t[i].join('–'),12);if(i<2)s+=txt(sx+70,y+92,'Changeover · 25s',12,muted)}
s+=txt(sx,552,'05:00',12)+txt(sx+60,578,'Remove selected act',13);
let foot=72+ph-50;s+=box(36,foot,w-72,1,line)+txt(36,foot+25,'1 / 3 sets filled · Lineup £40 · Paid at Start',15)+txt(36,foot+47,'Before Start: choose 2 more acts. Staff readiness stays visible here.',13)+box(w-222,foot+10,182,38,'#eee2be',line)+txt(w-195,foot+36,'Start festival',16,muted);
s+=txt(18,h-23,'Preparation ▾     Rotate view',14,paper)+txt(w-407,h-23,'DESIGN CONCEPT · illustrative selection state',12,paper);
let svg=`<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}">${s}</svg>`,name=`booking-${w}${filtered?'-filtered':''}`;fs.writeFileSync(`${R}/${name}.svg`,svg);await sharp(Buffer.from(svg)).png().toFile(`${R}/${name}.png`);
}
(async()=>{await render(1280,720);await render(1920,1080);await render(1280,720,true)})();
