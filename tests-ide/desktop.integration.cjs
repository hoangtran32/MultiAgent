'use strict';
// CI-only: open our packaged editor with an empty, isolated profile. Never send a model request.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
class Cdp {
  constructor(socket) {
    this.socket = socket; this.sequence = 0; this.pending = new Map();
    socket.addEventListener('message', event => {
      const message = JSON.parse(String(event.data)); const request = this.pending.get(message.id);
      if (!request) return; this.pending.delete(message.id); clearTimeout(request.timer);
      if (message.error) request.reject(Error(message.error.message)); else request.resolve(message.result);
    });
    socket.addEventListener('close', () => { for (const request of this.pending.values()) { clearTimeout(request.timer); request.reject(Error('Editor debug connection closed')); } this.pending.clear(); });
  }
  static async connect(url) {
    const socket = new WebSocket(url);
    await new Promise((resolve, reject) => { const timer = setTimeout(() => reject(Error('Editor debug connection timed out')), 8000); socket.addEventListener('open', () => { clearTimeout(timer); resolve(); }, { once:true }); socket.addEventListener('error', () => { clearTimeout(timer); reject(Error('Editor debug connection failed')); }, { once:true }); });
    return new Cdp(socket);
  }
  request(method, params = {}, sessionId) {
    const id = ++this.sequence;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(Error('Editor did not answer ' + method)); }, 8000);
      this.pending.set(id, { resolve, reject, timer }); this.socket.send(JSON.stringify({ id, method, params, sessionId }));
    });
  }
}
(async () => {
  if (process.env.GITHUB_ACTIONS !== 'true') throw Error('Desktop smoke test is restricted to GitHub CI.');
  const executable = path.resolve(process.argv[2]); const backend = path.resolve(process.argv[3]);
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'hub-desktop-ci-'));
  const project = path.join(temp, 'Project tiếng Việt'); const profile = path.join(temp, 'profile');
  for (const folder of [project, profile]) fs.mkdirSync(folder);
  fs.writeFileSync(path.join(project, 'README.md'), '# Isolated editor fixture\nNo agent requests.\n');
  const server = net.createServer(); await new Promise(resolve => server.listen(0, '127.0.0.1', resolve)); const port = server.address().port; await new Promise(resolve => server.close(resolve));
  const portable = path.join(temp, 'portable'); fs.mkdirSync(portable);
  fs.writeFileSync(path.join(portable, 'argv.json'), JSON.stringify({ 'remote-debugging-port':String(port), 'disable-hardware-acceleration':true }));
  const environment = { ...process.env, MULTI_AGENT_HUB_EXE:backend, MULTI_AGENT_HUB_PROFILE:profile, MULTI_AGENT_HUB_DATA_DIR:profile, MULTI_AGENT_HUB_IDE:'1', VSCODE_PORTABLE:portable };
  delete environment.NODE_OPTIONS; delete environment.ELECTRON_RUN_AS_NODE;
  const child = spawn(executable, ['--new-window', '--user-data-dir', path.join(temp, 'editor-data'), '--extensions-dir', path.join(temp, 'extensions'), '--skip-welcome', '--skip-sessions-welcome', '--skip-release-notes', '--disable-telemetry', '--disable-experiments', '--enable-smoke-test-driver', '--disable-workspace-trust', '--disable-gpu', '--remote-debugging-port=' + port, '--enable-logging=file', '--log-file=' + path.join(temp, 'electron.log'), project], { env:environment, shell:false, stdio:'ignore' });
  let closed = false, launchError, client;
  child.on('error', error => { launchError = error; }); child.on('close', () => { closed = true; });
  try {
    const deadline = Date.now() + 150000; let ready = false, lastError = '';
    while (Date.now() < deadline && !closed) {
      if (launchError) throw launchError;
      try {
        if (!client) { const response = await fetch(`http://127.0.0.1:${port}/json/version`, { signal:AbortSignal.timeout(2500) }); client = await Cdp.connect((await response.json()).webSocketDebuggerUrl); }
        const { targetInfos } = await client.request('Target.getTargets'); let workbench = false, agent;
        for (const target of targetInfos.filter(t => ['page','iframe','webview'].includes(t.type))) {
          let sessionId;
          try {
            ({ sessionId } = await client.request('Target.attachToTarget', { targetId:target.targetId, flatten:true }));
            const { result } = await client.request('Runtime.evaluate', { expression:`(() => {
              function find(w) {
                try {
                  const d = w.document;
                  if (d.getElementById('prompt') && d.getElementById('mode') && d.getElementById('workspace')) return { workspace:d.getElementById('workspace').textContent, mode:d.getElementById('mode').value, agents:Array.from(d.getElementById('agent').options).map(o => o.value), setup:!d.getElementById('setupNotice').hidden, sendDisabled:d.getElementById('send').disabled, error:d.getElementById('error').hidden ? '' : d.getElementById('error').textContent };
                  for (let i=0; i<w.frames.length; i++) { const result=find(w.frames[i]); if (result) return result; }
                } catch (_) {} return null;
              }
              return { title:document.title, workbench:!!document.querySelector('.monaco-workbench'), agent:find(window) };
            })()`, returnByValue:true }, sessionId);
            const value = result?.value;
            if (value?.workbench && value.title.includes('Hub Code')) workbench = true;
            if (value?.agent?.workspace) agent = value.agent;
          } finally { if (sessionId) await client.request('Target.detachFromTarget', { sessionId }).catch(() => {}); }
        }
        if (workbench && agent) {
          assert.equal(fs.realpathSync.native(agent.workspace).toLowerCase(), fs.realpathSync.native(project).toLowerCase());
          assert.equal(agent.mode, 'fast'); assert.equal(agent.error, ''); assert.ok(agent.agents.every(id => id === ''), 'Empty profiles must not invent agents');
          assert.equal(agent.setup, true); assert.equal(agent.sendDisabled, true);
          ready = true; break;
        }
      } catch (error) { lastError = error.message; }
      await pause(1000);
    }
    assert.ok(ready, 'Hub Code workbench and integrated Agent did not become ready. ' + lastError);
    console.log('Own Hub Code window and integrated Agent rendered; isolated empty profile, no model inference.');
  } catch (error) {
    const log = path.join(temp, 'electron.log'); if (fs.existsSync(log)) console.error(fs.readFileSync(log, 'utf8').slice(-12000));
    throw error;
  } finally {
    if (client) { await client.request('Browser.close').catch(() => {}); client.socket.close(); }
    for (let i=0; i<50 && !closed; i++) await pause(200);
    // Only the CI fixture process launched above can be terminated. Never used on the user's computer.
    if (!closed) child.kill();
    for (let i=0; i<25 && !closed; i++) await pause(200);
    const resolved = path.resolve(temp); const parent = path.resolve(os.tmpdir()) + path.sep;
    if (closed && resolved.startsWith(parent) && path.basename(resolved).startsWith('hub-desktop-ci-')) fs.rmSync(resolved, { recursive:true, force:true, maxRetries:5, retryDelay:300 });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
