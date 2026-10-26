// Renders the Bobin Kumanda logo to PNGs with headless Edge over DevTools (transparent background).
// usage: node render.js <out-dir> <job> [<job> ...]
//   name.svg:size     the drawing at size×size pixels   (bobinkumanda.svg:1024, bobinkumanda-small.svg:16)
//   wide:WxH          the mark with the name beside it, on the channel's navy
//   wide-light:WxH    the same without the navy plate, for light backgrounds
// The wide logo uses Fira Sans Condensed (OFL, Google Fonts); FONT_DIR is the folder holding its .ttf files.
const {spawn} = require('child_process'), fs = require('fs'), path = require('path'), os = require('os');
const dir = __dirname, out = path.resolve(process.argv[2] || path.join(dir, 'out'));
fs.mkdirSync(out, {recursive: true});
const read = f => fs.readFileSync(path.join(dir, f), 'utf8').replace(/\r\n/g, '\n');
const fontDir = process.env.FONT_DIR || path.join(dir, 'fonts');
const font = f => 'file:///' + path.join(fontDir, f).replace(/\\/g, '/');
const exe = [process.env['ProgramFiles(x86)'] + '\\Microsoft\\Edge\\Application\\msedge.exe', process.env.ProgramFiles + '\\Microsoft\\Edge\\Application\\msedge.exe'].find(f => fs.existsSync(f));
const inner = s => s.replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '');

function wide(w, h, dark) {
  const name = dark ? '#FFFFFF' : '#0A0F1E', sub = dark ? '#A9B4C8' : '#4A5570';
  const plate = dark ? '<rect width="2000" height="600" rx="120" fill="#0A0F1E"/>' : '';
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 2000 600" width="${w}" height="${h}">
  <style>@font-face{font-family:FSC;src:url(${font('Fira-Sans-Condensed-700.ttf')})}@font-face{font-family:FSC6;src:url(${font('Fira-Sans-Condensed-600.ttf')})}</style>
  ${plate}
  <g transform="translate(40 40) scale(.5078)">${inner(read('bobinkumanda.svg'))}</g>
  <text x="600" y="320" font-family="FSC" font-size="184" fill="${name}" letter-spacing="1">Bobin <tspan fill="${dark ? '#22D3EE' : '#0E9CBF'}">Kumanda</tspan></text>
  <path d="M648 378L618 430H638L626 472L666 412H646L664 378Z" fill="#FFD60A"/>
  <text x="692" y="440" font-family="FSC6" font-size="70" fill="${sub}" letter-spacing="10">BOBİN KARDEŞLER</text>
</svg>`;
}

const jobs = process.argv.slice(3).map(j => {
  const [name, size] = j.split(':');
  if (name === 'wide' || name === 'wide-light') {
    const [w, h] = size.split('x').map(Number);
    return [`bobinkumanda-${name}-${w}.png`, wide(w, h, name === 'wide'), w, h];
  }
  const s = Number(size);
  const svg = read(name).replace(/width="1024" height="1024"/, `width="${s}" height="${s}"`);
  return [name.replace('.svg', '') + '-' + s + '.png', svg, s, s];
});

const sleep = ms => new Promise(r => setTimeout(r, ms));
const port = 9333 + Math.floor(Math.random() * 500), prof = fs.mkdtempSync(path.join(os.tmpdir(), 'logo-'));
const proc = spawn(exe, ['--headless=new', '--disable-gpu', `--remote-debugging-port=${port}`, `--user-data-dir=${prof}`, '--allow-file-access-from-files', 'about:blank'], {stdio: 'ignore'});
(async () => {
  let target;
  for (let i = 0; i < 50 && !target; i++) { await sleep(300); try { target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); } catch (e) {} }
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise(r => ws.addEventListener('open', r));
  let id = 0;
  const pending = new Map();
  ws.addEventListener('message', e => { const m = JSON.parse(e.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result); pending.delete(m.id); } });
  const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({id: i, method, params})); });
  await send('Emulation.setDefaultBackgroundColorOverride', {color: {r: 0, g: 0, b: 0, a: 0}});
  for (const [name, s, w, h] of jobs) {
    const html = path.join(os.tmpdir(), 'bk-logo-' + name + '.html');
    fs.writeFileSync(html, `<!doctype html><html><head><meta charset="utf-8"><style>html,body{margin:0;background:transparent}svg{display:block}</style></head><body>${s}</body></html>`);
    await send('Emulation.setDeviceMetricsOverride', {width: w, height: h, deviceScaleFactor: 1, mobile: false});
    await send('Page.navigate', {url: 'file:///' + html.replace(/\\/g, '/')});
    await sleep(1200);
    const shot = await send('Page.captureScreenshot', {format: 'png', clip: {x: 0, y: 0, width: w, height: h, scale: 1}});
    fs.writeFileSync(path.join(out, name), Buffer.from(shot.data, 'base64'));
    fs.rmSync(html, {force: true});
    console.log(name, fs.statSync(path.join(out, name)).size);
  }
  ws.close();
})().catch(e => console.log('HATA', e.message)).finally(() => { proc.kill(); setTimeout(() => { try { fs.rmSync(prof, {recursive: true, force: true}); } catch (e) {} process.exit(0); }, 800); });
