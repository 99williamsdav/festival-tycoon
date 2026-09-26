const fs = require('fs');
const path = require('path');
const sharp = require('C:/Users/99wil/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const crypto = require('crypto');

const root = __dirname;
const source = path.join(root, 'assets/source/ui');
const runtime = path.join(root, 'assets/runtime/ui');
const review = path.join(root, 'art/reviews/R0.05a/tap-flow-icons-v1');
fs.mkdirSync(runtime, { recursive: true });
fs.mkdirSync(review, { recursive: true });
const states = ['low', 'normal', 'boosted'];

(async () => {
  for (const state of states) {
    const stem = `lwf_tap_flow_${state}_v1`;
    for (const size of [32, 48, 128]) {
      const name = size === 128 ? `${stem}.png` : `${stem}_${size}px.png`;
      await sharp(path.join(source, `${stem}.svg`), { density: 288 })
        .resize(size, size).png().toFile(path.join(runtime, name));
    }
  }
  const width = 1200, height = 740;
  const backdrop = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}">
    <rect width="1200" height="740" fill="#F3E8C9"/>
    <g font-family="Arial, sans-serif" fill="#39453F">
      <text x="48" y="55" font-size="28">TAP FLOW · ICON TRIO V1</text>
      <text x="48" y="89" font-size="17">Selected-tap context · transparent icons on the game's paper surface</text>
      <text x="48" y="194" font-size="15">160 px</text>
      <text x="240" y="320" font-size="23" text-anchor="middle">LOW FLOW</text>
      <text x="600" y="320" font-size="23" text-anchor="middle">NORMAL FLOW</text>
      <text x="960" y="320" font-size="23" text-anchor="middle">BOOSTED FLOW</text>
      <text x="240" y="348" font-size="16" text-anchor="middle">Two separated drops</text>
      <text x="600" y="348" font-size="16" text-anchor="middle">One continuous stream</text>
      <text x="960" y="348" font-size="16" text-anchor="middle">Three broad strands</text>
      <text x="48" y="430" font-size="15">48 px · actual size</text>
      <text x="48" y="518" font-size="15">32 px · actual size</text>
      <text x="48" y="604" font-size="15">32 px · one ink</text>
      <text x="48" y="687" font-size="17">Shape and stream amount distinguish all three states; labels remain game text.</text>
      <text x="48" y="714" font-size="15">Icons show resulting flow, not modifier identity. Council sharing and tower effects stay in adjacent text.</text>
    </g>
    <g stroke="#AAA48C" stroke-width="1"><path d="M48 108H1152M48 371H1152M48 647H1152"/></g>
  </svg>`);
  const layers = [];
  for (let i = 0; i < states.length; i++) {
    const cx = [240, 600, 960][i];
    const stem = `lwf_tap_flow_${states[i]}_v1`;
    layers.push({ input: await sharp(path.join(source, `${stem}.svg`)).resize(160, 160).png().toBuffer(), left: cx - 80, top: 130 });
    layers.push({ input: path.join(runtime, `${stem}_48px.png`), left: cx - 24, top: 398 });
    layers.push({ input: path.join(runtime, `${stem}_32px.png`), left: cx - 16, top: 495 });
    const mono = fs.readFileSync(path.join(source, `${stem}.svg`), 'utf8').replaceAll('#36747B', '#39453F');
    layers.push({ input: await sharp(Buffer.from(mono)).resize(32, 32).png().toBuffer(), left: cx - 16, top: 581 });
  }
  await sharp(backdrop).composite(layers).png().toFile(path.join(review, '01-contact-sheet.png'));
  for (const state of states) {
    for (const size of [32, 48, 128]) {
      const filename = `lwf_tap_flow_${state}_v1${size === 128 ? '' : `_${size}px`}.png`;
      const meta = await sharp(path.join(runtime, filename)).metadata();
      const stats = await sharp(path.join(runtime, filename)).stats();
      const alpha = stats.channels[3];
      if (meta.width !== size || meta.height !== size || !meta.hasAlpha || alpha.min !== 0 || alpha.max !== 255) {
        throw new Error(`Invalid transparent asset: ${filename}`);
      }
    }
  }
  const files = [...fs.readdirSync(source).map(name => path.join(source, name)), ...fs.readdirSync(runtime).map(name => path.join(runtime, name))];
  fs.writeFileSync(path.join(review, 'technical.json'), JSON.stringify({
    version: 'tap-flow-icons-v1', approval: 'pending coordinator and user',
    sourceViewBox: [0, 0, 64, 64], runtimeSizes: [32, 48, 128], transparentAlphaVerified: true,
    colours: { tap: '#39453F', water: '#36747B' },
    states: { low: 'two separated drops', normal: 'one narrow continuous stream', boosted: 'broad three-strand continuous stream' },
    files: files.map(file => ({ path: path.relative(root, file).replaceAll('\\', '/'), sha256: crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex') }))
  }, null, 2) + '\n');
  process.stdout.write('Rendered trio at 32/48/128px, contact sheet, and verified alpha.\n');
})().catch(error => { process.stderr.write(error.stack + '\n'); process.exitCode = 1; });
