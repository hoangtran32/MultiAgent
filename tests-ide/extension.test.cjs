'use strict';
const test = require('node:test'); const assert = require('node:assert/strict'); const vm = require('node:vm'); const fs = require('node:fs'); const path = require('node:path'); const { EventEmitter } = require('node:events'); const { PassThrough } = require('node:stream');
test('extension keeps a new conversation empty across metadata refresh and refuses duplicate sends while busy', async () => {
  const commands = new Map(), messages = [], requests = []; let receive, grantTrust;
  const disposable = { dispose() {} }; const child = new EventEmitter(); child.stdout = new PassThrough(); child.stderr = new PassThrough(); child.stdin = new PassThrough();
  const project = path.resolve('ide-fixture'); let state = { id: 'job1', busy: false, title: 'First request', state: 'completed', results: [] };
  child.stdin.on('data', bytes => { for (const line of bytes.toString().trim().split('\n')) { const r = JSON.parse(line); requests.push(r); const result = r.method === 'initialize' ? { project, agents: [{ id: 'A', name: 'Fixture' }], history: [] } : state; child.stdout.write(JSON.stringify({ id: r.id, result }) + '\n'); } });
  class Emitter { constructor() { this.event = () => disposable; } fire() {} dispose() {} }
  const vscode = {
    workspace: { isTrusted: false, onDidGrantWorkspaceTrust: callback => { grantTrust = callback; return disposable; }, workspaceFolders: [{ uri: { scheme: 'file', fsPath: project } }], getConfiguration: () => ({ inspect: () => ({}) }), registerTextDocumentContentProvider: () => disposable },
    window: { createOutputChannel: () => ({ append() {}, appendLine() {}, dispose() {} }), createStatusBarItem: () => ({ show() {}, dispose() {} }), registerTreeDataProvider: () => disposable,
      createWebviewPanel: () => ({ webview: { cspSource: 'fixture:', asWebviewUri: value => value, postMessage: value => messages.push(value), onDidReceiveMessage: callback => { receive = callback; return disposable; } }, onDidDispose: () => disposable, reveal() {} }) },
    commands: { registerCommand: (name, callback) => { commands.set(name, callback); return disposable; }, executeCommand: async () => {} },
    EventEmitter: Emitter, StatusBarAlignment: { Left: 1 }, ViewColumn: { Beside: 2, One: 1 },
    Uri: { joinPath: (_root, name) => name, parse: value => value }
  };
  const file = path.resolve(__dirname, '../ide-extension/extension.cjs'); const exported = {};
  const fakeFs = { ...fs, existsSync: () => true, watch: () => ({ on() {}, close() {} }) };
  const customRequire = name => name === 'vscode' ? vscode : name === 'node:child_process' ? { spawn: () => child } : name === 'node:fs' ? fakeFs : name.startsWith('./') ? require(path.resolve(path.dirname(file), name)) : require(name);
  const factory = vm.runInNewContext('(function(require,module){' + fs.readFileSync(file, 'utf8') + '\n})', { process: { env: { MULTI_AGENT_HUB_EXE: path.join(project, 'MultiAgentHub.exe') } }, setTimeout, clearTimeout });
  factory(customRequire, exported);
  exported.exports.activate({ extensionPath: path.dirname(file), extensionUri: {}, subscriptions: [], workspaceState: { get: () => true, update: async () => {} } });
  await commands.get('multiagent.open')(); await receive({ type: 'ready' });
  assert.equal(messages.at(-1).type, 'restricted'); assert.equal(requests.length, 0);
  await receive({ type: 'start', prompt: 'Must not execute in Restricted Mode', agentId: 'A', mode: 'fast' }); assert.equal(requests.length, 0);
  vscode.workspace.isTrusted = true; await grantTrust(); assert.equal(messages.at(-1).type, 'catalog');
  await receive({ type: 'start', prompt: 'First request', agentId: 'A', mode: 'fast' }); assert.equal(messages.at(-1).type, 'accepted');
  await receive({ type: 'new' }); const before = messages.length; await receive({ type: 'refresh' });
  assert.equal(messages.slice(before).some(m => m.type === 'state'), false);
  state = { ...state, busy: true, state: 'running' }; child.stdout.write(JSON.stringify({ event: 'state', data: state }) + '\n');
  const starts = requests.filter(r => r.method === 'start').length; await receive({ type: 'start', prompt: 'Duplicate', agentId: 'A', mode: 'fast' });
  assert.equal(requests.filter(r => r.method === 'start').length, starts); assert.equal(messages.at(-1).type, 'error');
  await receive({ type: 'stop' }); assert.equal(requests.at(-1).method, 'stop');
  child.emit('exit', 0);
});
