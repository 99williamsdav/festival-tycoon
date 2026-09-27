const sharp=require('C:/Users/99wil/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const fs=require('fs'),path=require('path');const R=__dirname;
const ink='#293b38',paper='#fff3d3',muted='#59645d',line='#aa9f78';
const esc=s=>String(s).replaceAll('&','&amp;').replaceAll('<','&lt;');
function text(x,y,s,size=16,color=ink,serif=false){return `<text x="${x}" y="${y}" fill="${color}" font-family="${serif?'Georgia':'Arial'}" font-size="${size}">${esc(s)}</text>`}
function rect(x,y,w,h,fill,stroke='none',extra=''){return `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="${fill}" stroke="${stroke}" ${extra}/>`}
function rule(x,y,w){return `<path d="M${x} ${y}h${w}" stroke="${line}"/>`}
function svg(s,w=1100,h=800){return `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}">${s}</svg>`}
async function save(name,s){fs.writeFileSync(path.join(R,name+'.svg'),s);await sharp(Buffer.from(s)).png().toFile(path.join(R,name+'.png'));}
const acts=[['Meadow Lanterns','Folk','40',40],['Orchard Chorus','Folk','75',70],['Barnstorm Circuit','Rock','55',55],['Copper Static','Rock','90',80],['Neon Postcards','Pop','110',90],['Field Frequency','Electronic','85',75]];
function booking(filled){
 let s=rect(0,0,1100,800,ink)+rect(20,20,1060,760,paper)+text(46,62,'Prepare the festival',30,ink,true)+text(46,98,'Overview',16)+text(150,98,'Programme',16)+text(274,98,'Staff',16)+text(345,98,'Equipment',16)+text(470,98,'Stock',16)+text(551,98,'Site & water',16)+rule(46,112,1008)+rect(144,109,105,3,ink);
 s+=text(46,145,'Available bands',21)+text(590,145,'Main stage',23,ink,true)+text(46,173,'Drag a band, or select it and then choose a set.',14)+text(590,173,'Elapsed festival time · mm:ss',14,muted);
 for(let i=0;i<6;i++){
  const a=acts[i],y=193+i*76;
  s+=rect(46,y,418,67,'#fff6df',line)+text(58,y+21,a[0],17)+text(402,y+21,'£'+a[2],17)+text(58,y+41,a[1]+' · Popularity '+a[3]+'/100'+(filled&&i<2?' · Set '+(i+1):''),14)+text(58,y+58,'Ego —   ·   Professionalism —',13,muted);
 }
 s+=rect(584,193,450,420,'#eee2be');
 const start=[15,115,215],times=[['00:15','01:30'],['01:55','03:10'],['03:35','04:50']];
 s+=text(516,200,'00:00',13,muted);
 for(let i=0;i<3;i++){
  const y=193+start[i]*1.4,h=105,assigned=filled&&i===0,replace=filled&&i===1;
  s+=text(516,y+5,times[i][0],13)+text(516,y+h+5,times[i][1],13)+rect(592,y,434,h,replace?'#e5d5aa':assigned?'#d3e8df':'#fff6df',replace?'#806128':line,(!assigned&&!replace)?'stroke-dasharray="5 4"':'');
  s+=text(610,y+25,replace?'Replace Orchard Chorus?':assigned?'Meadow Lanterns':'Set '+(i+1),18);
  s+=text(610,y+50,replace?'Barnstorm Circuit · Rock · £55':assigned?'Folk · '+times[i].join('–'):'Drop a band here',16);
  s+=text(610,y+76,replace?'Release to replace · old band returns to list':assigned?'Assigned · move or remove':times[i].join('–')+' · 75s set',13,muted);
  if(i<2)s+=text(610,y+h+23,'Changeover · 25s · no drop',13,muted);
 }
 s+=text(516,619,'05:00',13,muted)+text(46,678,'Ego / professionalism values await the approved data contract.',13,muted)+rule(46,692,1008);
 s+=text(46,724,filled?'2 of 3 sets filled · Lineup £115 · Paid at Start':'0 of 3 sets filled · Lineup £0 · Paid at Start',17)+text(46,749,'Choose three different acts. Start also checks staff and preparation requirements.',13,muted)+rect(872,708,162,45,'#eee2be',line)+text(898,736,'Start festival',17,muted);
 s+=text(660,775,'DESIGN MOCKUP · NOT AN IN-GAME CAPTURE',12,muted);
 return svg(s);
}
function newspaper(){
 let s=rect(0,0,1100,800,ink)+rect(66,24,968,750,paper)+text(96,58,'LOCAL EDITION · FESTIVAL REVIEW',13)+text(750,58,'ILLUSTRATIVE DATA',13,muted)+rule(96,72,908);
 s+=text(128,127,'The Lower Wittering Gazette',43,ink,true)+rule(96,147,908)+text(126,203,'Festival hits the right note',38,ink,true)+text(126,242,'★★★★☆',31,ink)+text(325,239,'4 / 5 · Guest satisfaction 78%',19)+text(126,268,'Rating based on guest satisfaction, not financial performance.',14,muted)+rule(96,291,908);
 s+=text(126,330,'The crowd’s verdict',25,ink,true)+text(126,363,'The festival draws a warm reception.',18)+text(126,394,'A solid day of music, with room to improve.',16)+text(126,435,'Final guest satisfaction · 78%',17)+text(126,459,'32 eligible guests · example values only',13,muted);
 s+=rect(654,314,324,172,'#eee2be')+text(678,345,'Festival accounts',24,ink,true)+text(678,381,'Festival profit',16)+text(678,422,'+£126.00',33,ink,true)+text(678,453,'Profit shown separately from rating.',13,muted)+rule(96,508,908);
 s+=text(126,549,'Around the field',25,ink,true)+text(126,590,'12',30,ink,true)+text(126,617,'Beers finished',16)+text(126,640,'Not sales; pint unit unconfirmed',12,muted)+text(425,590,'2',30,ink,true)+text(425,617,'Fights',16)+text(425,640,'Distinct incidents',12,muted)+text(716,590,'Not recorded',25,ink,true)+text(716,617,'Near-death collapses',16)+text(716,640,'Requires defined medical metric',12,muted)+rule(96,665,908);
 s+=text(126,708,'Demo complete',23,ink,true)+text(126,735,'All guests have left. Thanks for playing.',15)+rect(790,692,188,49,ink)+text(818,723,'Return to menu',17,paper);
 return svg(s);
}
(async()=>{await save('01-booking-empty',booking(false));await save('02-booking-replacement',booking(true));await save('03-newspaper',newspaper());})();
