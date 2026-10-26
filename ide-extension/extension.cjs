'use strict';
const vscode = require('vscode');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn } = require('node:child_process');
const { HubClient } = require('./client.cjs');
const { target: linkTarget } = require('./links.cjs');

function activate(context) {
  const output = vscode.window.createOutputChannel('Hub Code • Agent');
  let client, panel, catalog, current, displayedId = '', starting = false, fresh = false, attachments = [], selectedCode = '';
  const treeChanged = new vscode.EventEmitter();
  const artifactsChanged = new vscode.EventEmitter();
  const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 90);
  status.text = '$(hubot) Hub Code'; status.command = 'multiagent.open'; status.show();
  const root = () => {
    const folders = vscode.workspace.workspaceFolders || [];
    if (folders.length !== 1 || folders[0].uri.scheme !== 'file') throw new Error('Hub Code cần đúng một thư mục local. Mở mỗi project trong một cửa sổ để giữ đúng ngữ cảnh.');
    return folders[0].uri.fsPath;
  };
  const executable = () => {
    const inspected = vscode.workspace.getConfiguration('multiagent').inspect('backendExecutable');
    const bundled = path.resolve(context.extensionPath, '..', '..', 'hub-backend', 'MultiAgentHub.exe');
    const value = process.env.MULTI_AGENT_HUB_EXE || (fs.existsSync(bundled) ? bundled : '') || inspected?.globalValue || inspected?.defaultValue;
    if (!value || !path.isAbsolute(value) || !fs.existsSync(value) || path.extname(value).toLowerCase() !== '.exe') throw new Error('Mở IDE từ MultiAgentHub.exe để nạp đúng backend, hoặc đặt đường dẫn EXE trong User Settings.');
    return value;
  };
  const post = message => { if (panel) void panel.webview.postMessage(message); };
  const error = value => { const message = value instanceof Error ? value.message : String(value); post({ type: 'error', message }); output.appendLine(message); };
  async function connect() {
    if (!vscode.workspace.isTrusted) throw new Error('Cần Trust thư mục trước khi chạy agent.');
    if (client && !client.closed) return client;
    const args = ['--ide-backend', root()];
    if (process.env.MULTI_AGENT_HUB_PROFILE) args.push('--data-dir', process.env.MULTI_AGENT_HUB_PROFILE);
    const child = spawn(executable(), args, { cwd: root(), windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], shell: false });
    client = new HubClient(child);
    client.on('diagnostic', text => output.append(text));
    client.on('closed', message => {
      status.text = '$(warning) Hub Code · đã ngắt';
      if (current) { current = { ...current, busy: false, state: 'interrupted', stopReason: message, progress: { ...current.progress, title: 'Phiên đã ngắt — chưa xong', detail: message } }; post({ type: 'state', data: current }); }
      error(message);
    });
    client.on('state', state => {
      current = state; status.text = state.busy ? '$(loading~spin) ' + (state.activity || state.progress?.title || 'Agent đang làm') : '$(hubot) ' + (state.progress?.title || 'Hub Code');
      if (catalog && state.id) { catalog.history = [{ id: state.id, title: state.title, state: state.state, timestamp: state.timestamp }, ...(catalog.history || []).filter(h => h.id !== state.id)].slice(0, 50); }
      status.tooltip = state.progress?.detail || state.project;
      treeChanged.fire(); artifactsChanged.fire(vscode.Uri.parse('hub-artifact:/plan.md')); artifactsChanged.fire(vscode.Uri.parse('hub-artifact:/walkthrough.md'));
      if (starting || displayedId === state.id) { displayedId = state.id || ''; post({ type: 'state', data: state }); }
    });
    return client;
  }
  async function initialize() {
    if (!vscode.workspace.isTrusted) { post({ type: 'restricted' }); return; }
    const c = await connect(); catalog = await c.request('initialize');
    post({ type: 'catalog', data: catalog }); treeChanged.fire();
    if (current?.id && !displayedId && !fresh) { displayedId = current.id; post({ type: 'state', data: current }); }
  }
  async function run(action) { try { await action(); } catch (e) { error(e); } }
  function newChat() { if (current?.busy) throw new Error('Dừng hoặc đợi phiên hiện tại hoàn tất trước khi tạo cuộc trò chuyện mới.'); displayedId = ''; fresh = true; attachments = []; selectedCode = ''; post({ type: 'new' }); }
  async function open() {
    if (panel) { panel.reveal(); return; }
    if (!context.workspaceState.get('ideLayout')) {
      await vscode.commands.executeCommand('vscode.setEditorLayout', { orientation: 0, groups: [{ size: 0.66 }, { size: 0.34 }] });
      await context.workspaceState.update('ideLayout', true);
    }
    panel = vscode.window.createWebviewPanel('multiagent.chat', 'Hub Code · Agent', { viewColumn: vscode.ViewColumn.Beside, preserveFocus: false }, {
      enableScripts: true, localResourceRoots: [context.extensionUri], retainContextWhenHidden: true
    });
    const nonce = crypto.randomBytes(24).toString('base64');
    const resource = name => panel.webview.asWebviewUri(vscode.Uri.joinPath(context.extensionUri, name)).toString();
    panel.webview.html = fs.readFileSync(path.join(context.extensionPath, 'view.html'), 'utf8')
      .replaceAll('{{NONCE}}', nonce).replaceAll('{{CSP}}', panel.webview.cspSource).replaceAll('{{SCRIPT}}', resource('view.js')).replaceAll('{{CSS}}', resource('view.css'));
    panel.onDidDispose(() => { panel = undefined; }, null, context.subscriptions);
    panel.webview.onDidReceiveMessage(message => run(async () => {
      if (!message || typeof message.type !== 'string') return;
      if (message.type === 'ready' || message.type === 'refresh') { await initialize(); if (displayedId && current?.id === displayedId) post({ type: 'state', data: current }); return; }
      if (message.type === 'new') { newChat(); return; }
      if (message.type === 'settings') { await settings(); return; }
      if (message.type === 'trust') { await vscode.commands.executeCommand('workbench.trust.manage'); return; }
      if (message.type === 'terminal') { if (!vscode.workspace.isTrusted) throw new Error('Project đang ở Restricted Mode. Mở Quản lý quyền project trước khi chạy terminal.'); vscode.window.createTerminal({ cwd: root(), name: 'Hub Code' }).show(); return; }
      if (message.type === 'changes') { await vscode.commands.executeCommand('workbench.view.scm'); return; }
      if (message.type === 'openLink') {
        const target = linkTarget(root(), message.url);
        if (target.external) await vscode.env.openExternal(vscode.Uri.parse(target.external));
        else { const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(target.file)); await vscode.window.showTextDocument(doc, { viewColumn: vscode.ViewColumn.One, selection: new vscode.Range(target.line, 0, target.line, 0) }); }
        return;
      }
      if (message.type === 'logs') { const c = await connect(); output.appendLine(await c.request('logs')); output.show(true); return; }
      if (message.type === 'plan' || message.type === 'walkthrough') { await artifact(message.type); return; }
      if (message.type === 'attach') {
        const chosen = await vscode.window.showOpenDialog({ canSelectMany: true, canSelectFiles: true, canSelectFolders: false, title: 'Đính kèm ảnh, PDF, Word hoặc văn bản' });
        if (chosen) for (const uri of chosen) { if (uri.scheme !== 'file') continue; if (!attachments.some(a => a.fsPath === uri.fsPath)) attachments.push(uri); }
        if (attachments.length > 10) { attachments = attachments.slice(0, 10); error('Mỗi lượt tối đa 10 tệp.'); }
        post({ type: 'attachments', data: attachments.map((a, index) => ({ index, name: path.basename(a.fsPath) })) }); return;
      }
      if (message.type === 'removeAttachment') { attachments.splice(Number(message.index), 1); post({ type: 'attachments', data: attachments.map((a, index) => ({ index, name: path.basename(a.fsPath) })) }); return; }
      const c = await connect();
      if (message.type === 'start') {
        if (current?.busy) throw new Error('Agent vẫn đang chạy; dùng Gửi bổ sung hoặc Dừng.');
        if (typeof message.prompt !== 'string' || !message.prompt.trim()) throw new Error('Nhập yêu cầu cần thực hiện.');
        starting = true;
        try {
          const result = await c.request('start', { prompt: message.prompt, mode: message.mode, agentId: message.agentId, model: message.model || '',
            previousId: displayedId, context: selectedCode, attachments: attachments.map(a => a.fsPath) });
          current = result; displayedId = result.id; fresh = false; attachments = []; selectedCode = ''; post({ type: 'accepted', data: result });
        } finally { starting = false; }
      } else if (message.type === 'stop') await c.request('stop');
      else if (message.type === 'steer') { await c.request('steer', { text: message.prompt }); post({ type: 'steered' }); }
      else if (message.type === 'coding') { const result = await c.request('coding', message.coding); if (catalog) catalog.coding = result; post({ type: 'coding', data: result }); }
      else if (message.type === 'models') post({ type: 'models', agentId: message.agentId, data: await c.request('models', { agentId: message.agentId }) });
      else if (message.type === 'history') { if (current?.busy) throw new Error('Đợi hoặc dừng phiên hiện tại trước khi đổi cuộc trò chuyện.'); const result = await c.request('open', { id: message.id }); displayedId = result.id; current = result; fresh = false; post({ type: 'state', data: result }); }
    }), null, context.subscriptions);
  }
  async function settings() {
    const args = ['--settings']; if (process.env.MULTI_AGENT_HUB_PROFILE) args.push('--data-dir', process.env.MULTI_AGENT_HUB_PROFILE);
    const child = spawn(executable(), args, { windowsHide: false, stdio: 'ignore', shell: false });
    child.on('error', error); child.on('exit', () => run(initialize)); child.unref();
  }
  function artifactText(kind) {
    if (!current?.id) return '# Chưa có phiên\n\nGửi yêu cầu để bắt đầu.';
    if (kind === 'plan') return '# Kế hoạch triển khai\n\n' + (current.tasks || []).map(t => `## ${t.id} · ${t.title}\n\n${t.description}\n\n**Tiêu chí:** ${t.acceptance}\n\n**Trạng thái:** ${t.state}\n\n${t.implementationPlan || ''}`).join('\n\n') + '\n\n' + (current.tasks?.length ? '' : 'Chế độ Làm ngay không bắt buộc tạo kế hoạch; kết quả hiển thị trong chat.');
    return '# Kết quả & kiểm chứng\n\n' + (current.progress?.title || current.state) + '\n\n' + (current.stopReason || '') + '\n\n' +
      (current.tasks || []).map(t => `## ${t.id} · ${t.title}\n\n${t.outcome || ''}\n\nKiểm tra: ${t.verification}\n\n${t.feedback || ''}`).join('\n\n') +
      '\n\n' + (current.results || []).map(r => `## ${r.name} · ${r.model}\n\n${r.text || r.error || ''}`).join('\n\n');
  }
  async function artifact(kind) { const uri = vscode.Uri.parse(`hub-artifact:/${kind}.md`); const doc = await vscode.workspace.openTextDocument(uri); await vscode.window.showTextDocument(doc, { preview: false, viewColumn: vscode.ViewColumn.One }); }
  context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider('hub-artifact', { onDidChange: artifactsChanged.event, provideTextDocumentContent: uri => artifactText(uri.path.startsWith('/plan') ? 'plan' : 'walkthrough') }));
  const treeProvider = {
    onDidChangeTreeData: treeChanged.event,
    getTreeItem: item => item,
    getChildren: item => {
      if (item) return item.children || [];
      const result = [];
      const summary = new vscode.TreeItem(current?.progress?.title || 'Sẵn sàng'); summary.iconPath = new vscode.ThemeIcon(current?.busy ? 'sync~spin' : 'hubot'); summary.command = { command: 'multiagent.open', title: 'Mở Agent' }; result.push(summary);
      for (const task of current?.tasks || []) { const node = new vscode.TreeItem(task.id + ' · ' + task.title); node.description = task.state; node.tooltip = task.acceptance; node.iconPath = new vscode.ThemeIcon(task.state === 'done' ? 'pass' : task.state === 'blocked' ? 'error' : 'circle-outline'); node.command = { command: 'multiagent.plan', title: 'Xem kế hoạch' }; result.push(node); }
      if (catalog) for (const h of catalog.history || []) { const node = new vscode.TreeItem(h.title); node.description = h.state; node.iconPath = new vscode.ThemeIcon('history'); node.command = { command: 'multiagent.openHistory', title: 'Mở phiên', arguments: [h.id] }; result.push(node); }
      return result;
    }
  };
  context.subscriptions.push(vscode.window.registerTreeDataProvider('multiagent.sessions', treeProvider));
  const commands = {
    'multiagent.open': open,
    'multiagent.new': async () => { await open(); newChat(); },
    'multiagent.settings': settings,
    'multiagent.refresh': initialize,
    'multiagent.stop': async () => { if (client && !client.closed) await client.request('stop'); },
    'multiagent.plan': () => artifact('plan'), 'multiagent.walkthrough': () => artifact('walkthrough'),
    'multiagent.openHistory': async id => { await open(); if (current?.busy) throw new Error('Phiên hiện tại còn chạy.'); const c = await connect(); const result = await c.request('open', { id }); current = result; displayedId = result.id; fresh = false; post({ type: 'state', data: result }); },
    'multiagent.selection': async () => {
      const editor = vscode.window.activeTextEditor; if (!editor || editor.selection.isEmpty) return;
      const text = editor.document.getText(editor.selection); if (text.length > 100000) throw new Error('Đoạn chọn tối đa 100.000 ký tự.');
      selectedCode = `[NGỮ CẢNH NGƯỜI DÙNG CHỌN: ${vscode.workspace.asRelativePath(editor.document.uri)}:${editor.selection.start.line + 1}]\n${text}`;
      await open(); post({ type: 'context', message: `Đã kèm ${vscode.workspace.asRelativePath(editor.document.uri)}:${editor.selection.start.line + 1}` });
    }
  };
  for (const [name, action] of Object.entries(commands)) context.subscriptions.push(vscode.commands.registerCommand(name, (...args) => run(() => action(...args))));
  context.subscriptions.push(output, status, treeChanged, artifactsChanged, { dispose: () => client?.dispose() });
  context.subscriptions.push(vscode.workspace.onDidGrantWorkspaceTrust(() => run(initialize)));
  const profile = process.env.MULTI_AGENT_HUB_PROFILE || process.env.MULTI_AGENT_HUB_DATA_DIR || (process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA, 'MultiAgentHub'));
  if (profile && fs.existsSync(profile)) {
    let reload;
    const watcher = fs.watch(profile, (_event, name) => {
      if (String(name) !== 'native-config.json' || !client || client.closed) return;
      clearTimeout(reload); reload = setTimeout(() => run(initialize), 500);
    });
    watcher.on('error', e => output.appendLine('Config watcher: ' + e.message));
    context.subscriptions.push({ dispose: () => { clearTimeout(reload); watcher.close(); } });
  }
  if (process.env.MULTI_AGENT_HUB_IDE === '1' || fs.existsSync(path.resolve(context.extensionPath, '..', '..', 'hub-backend', 'MultiAgentHub.exe'))) void run(open);
}
module.exports = { activate };
