'use strict';
// CI-only: a clean profile, no account, no model/CLI invocation, no editor UI.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { once } = require('node:events');
const { HubClient } = require('../ide-extension/client.cjs');
(async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'hub-ide-ci-')); const project = path.join(root, 'project'); fs.mkdirSync(project);
  const child = spawn(process.argv[2], ['--ide-backend', project, '--data-dir', path.join(root, 'profile')], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  const closed = once(child, 'close');
  const client = new HubClient(child); let diagnostics = ''; client.on('diagnostic', value => diagnostics += value);
  try {
    const info = await client.request('initialize'); assert.equal(fs.realpathSync.native(info.project).toLowerCase(), fs.realpathSync.native(project).toLowerCase()); assert.deepEqual(info.agents, []);
    await assert.rejects(client.request('start', { prompt: 'Never call a real model.', agentId: 'missing', mode: 'fast' }), /Chọn một agent/);
    const state = await client.request('snapshot'); assert.equal(state.busy, false); assert.equal(fs.realpathSync.native(state.project).toLowerCase(), fs.realpathSync.native(project).toLowerCase());
    await assert.rejects(client.request('arbitrary-shell', { command: 'not-executed' }), /chưa hỗ trợ/);
    client.dispose(); const [code] = await closed; assert.equal(code, 0, diagnostics);
    console.log('IDE backend framing, empty onboarding and clean shutdown passed.');
  } finally { client.dispose(); await closed; fs.rmSync(root, { recursive: true, force: true, maxRetries: 30, retryDelay: 100 }); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
