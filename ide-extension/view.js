'use strict';
const vscode = acquireVsCodeApi();
const $ = id => document.getElementById(id);
const saved = vscode.getState() || {};
let catalog = { agents: [], history: [] }, state = {}, submitting = false, nearBottom = true, lastPrompt = '', rolesDirty = false, restricted = false;
const nodes = new Map();
const persist = () => vscode.setState({ prompt: $('prompt').value, mode: $('mode').value, agentId: $('agent').value, model: $('model').value });
const send = (type, data = {}) => vscode.postMessage({ type, ...data });
const text = (element, value) => { const next = String(value || ''); if (element.textContent !== next) element.textContent = next; };
function markdown(element, value) {
  const fragment = document.createDocumentFragment(); let code = null;
  for (const line of String(value || '').split('\n')) {
    if (line.startsWith('```')) { if (code) code = null; else { code = document.createElement('pre'); const content = document.createElement('code'); code.append(content); fragment.append(code); } continue; }
    if (code) { code.firstChild.append(document.createTextNode(line + '\n')); continue; }
    const heading = line.match(/^(#{1,4})\s+(.*)$/); const paragraph = document.createElement(heading ? `h${heading[1].length + 1}` : 'div');
    const input = heading ? heading[2] : line;
    const tokens = /(`[^`]+`|\*\*[^*]+\*\*|\[[^\]]+\]\([^\s]+\))/g; let last = 0, match;
    while ((match = tokens.exec(input))) {
      paragraph.append(document.createTextNode(input.slice(last, match.index))); const token = match[0];
      if (token.startsWith('`')) { const code = document.createElement('code'); code.textContent = token.slice(1, -1); paragraph.append(code); }
      else if (token.startsWith('**')) { const strong = document.createElement('strong'); strong.textContent = token.slice(2, -2); paragraph.append(strong); }
      else { const parts = token.match(/^\[([^\]]+)\]\((.+)\)$/); const link = document.createElement('a'); link.textContent = parts[1]; link.href = '#'; link.onclick = e => { e.preventDefault(); send('openLink', { url: parts[2] }); }; paragraph.append(link); }
      last = match.index + token.length;
    }
    paragraph.append(document.createTextNode(input.slice(last) || (input.length ? '' : '\n'))); fragment.append(paragraph);
  }
  element.replaceChildren(fragment);
}
function options(select, agents, selected) {
  select.replaceChildren(); const empty = document.createElement('option'); empty.value = ''; empty.textContent = 'Chọn agent…'; select.append(empty);
  for (const agent of agents) { const option = document.createElement('option'); option.value = agent.id; option.textContent = `${agent.name} · ${agent.model || 'theo kết nối'}`; select.append(option); }
  select.value = agents.some(a => a.id === selected) ? selected : '';
}
function modelOptions(id, agentId) { const list = $(id); list.replaceChildren(); for (const model of catalog.agents.find(a => a.id === agentId)?.models || []) { const option = document.createElement('option'); option.value = model; list.append(option); } }
function controls() {
  const busy = !!state.busy;
  document.body.classList.toggle('running', busy); document.body.classList.toggle('success', !busy && state.state === 'completed');
  $('stop').hidden = !busy; $('progress').hidden = !busy;
  $('mode').disabled = $('agent').disabled = $('model').disabled = busy || submitting;
  $('send').disabled = restricted || submitting || !catalog.agents.length || busy && $('mode').value !== 'plan';
  $('send').textContent = submitting ? 'Đang gửi…' : busy ? 'Bổ sung ↑' : 'Gửi ↑';
  $('new').disabled = busy || submitting;
  $('modeHelp').textContent = $('mode').value === 'fast' ? 'Một agent xử lý yêu cầu rồi dừng. Không mở vòng bàn luận.' : $('mode').value === 'review' ? 'Chỉ đọc và nhận xét code; không sửa project.' : 'Cả nhóm review toàn bộ giải pháp → Đầu não → Thợ code → kiểm tra/review.';
  if ($('mode').value === 'plan' && !catalog.coding?.enabled) $('roles').open = true;
}
function message(key, name, model, body, phase, error, user = false) {
  let node = nodes.get(key);
  if (!node) { node = document.createElement('article'); node.className = 'message'; node.innerHTML = '<div class="meta"><span class="name"></span><span class="model"></span></div><div class="body"></div><div class="failure"></div>'; nodes.set(key, node); $('feed').append(node); }
  node.classList.toggle('user', user); node.classList.toggle('agent', !user); node.classList.toggle('running', phase === 'running');
  text(node.querySelector('.name'), name); text(node.querySelector('.model'), model);
  const content = node.querySelector('.body'); const signature = String(body || '') + '\x00' + phase;
  if (node._signature !== signature) { node._signature = signature; if (user || phase === 'running') text(content, body); else markdown(content, body); }
  text(node.querySelector('.failure'), error);
  node.querySelector('.failure').hidden = !error;
}
function render(next) {
  if (state.id && next.id !== state.id) { nodes.clear(); $('feed').querySelectorAll('.message').forEach(n => n.remove()); }
  state = next; $('empty').hidden = !!next.id;
  text($('workspace'), next.project || catalog.project); $('workspace').title = next.project || catalog.project || '';
  text($('status'), next.progress?.title || 'Sẵn sàng'); text($('activity'), next.activity || next.progress?.detail || next.stopReason);
  text($('counts'), [next.progress?.progress, next.progress?.timing].filter(Boolean).join(' · '));
  for (const old of next.history || []) { message(`history-${old.id}-user`, 'Bạn', '', old.prompt, '', '', true); for (const result of old.results || []) message(`history-${old.id}-${result.agentId}`, result.name, result.model, result.text, result.state, ''); }
  if (next.prompt) message('user', 'Bạn', '', next.prompt, '', '', true);
  if (next.turns?.length) for (const turn of next.turns) message(`turn-${turn.number}-${turn.kind}`, turn.name, [turn.model, turn.role, turn.taskId].filter(Boolean).join(' · '), turn.text, turn.state, turn.error, turn.kind === 'user');
  else for (const result of next.results || []) message(`result-${result.agentId}`, result.name, result.model, result.text || (result.state === 'queued' ? 'Chờ lượt' : ''), result.state, result.error);
  controls(); if (nearBottom) $('feed').scrollTop = $('feed').scrollHeight; else $('bottom').hidden = false;
}
$('feed').addEventListener('scroll', () => { nearBottom = $('feed').scrollHeight - $('feed').scrollTop - $('feed').clientHeight < 70; if (nearBottom) $('bottom').hidden = true; });
$('bottom').onclick = () => { nearBottom = true; $('feed').scrollTop = $('feed').scrollHeight; $('bottom').hidden = true; };
for (const id of ['new', 'refresh', 'settings', 'plan', 'walkthrough', 'changes', 'terminal', 'logs', 'attach', 'stop', 'trust']) $(id).onclick = () => send(id);
function submit() {
  const prompt = $('prompt').value.trim(); if (!prompt || submitting || $('send').disabled) return;
  $('error').hidden = true; lastPrompt = prompt;
  if (state.busy) send('steer', { prompt });
  else { submitting = true; controls(); send('start', { prompt, mode: $('mode').value, agentId: $('agent').value, model: $('model').value.trim() }); }
}
$('send').onclick = submit;
$('prompt').value = saved.prompt || ''; $('mode').value = saved.mode || 'fast';
$('prompt').addEventListener('input', persist); $('prompt').addEventListener('keydown', e => { if (e.ctrlKey && e.key === 'Enter') { e.preventDefault(); submit(); } });
$('mode').onchange = () => { persist(); controls(); };
$('agent').onchange = () => { $('model').value = ''; modelOptions('models', $('agent').value); persist(); };
$('model').oninput = persist;
$('modelsButton').onclick = () => send('models', { agentId: $('agent').value });
$('brain').onchange = () => { rolesDirty = true; $('brainModel').value = ''; modelOptions('brainModels', $('brain').value); };
$('worker').onchange = () => { rolesDirty = true; $('workerModel').value = ''; modelOptions('workerModels', $('worker').value); };
$('brainModel').oninput = $('workerModel').oninput = () => { rolesDirty = true; };
$('roleModels').onclick = () => { for (const agentId of new Set([$('brain').value, $('worker').value].filter(Boolean))) send('models', { agentId }); };
$('saveRoles').onclick = () => send('coding', { coding: { enabled: true, brain: { role: 'brain', agentId: $('brain').value, model: $('brainModel').value.trim() }, implementer: { role: 'implementer', agentId: $('worker').value, model: $('workerModel').value.trim() } } });
window.addEventListener('message', event => {
  const message = event.data;
  if (message.type === 'restricted') { restricted = true; $('trustNotice').hidden = false; text($('status'), 'Chờ quyền project'); text($('activity'), 'Khung Agent sẵn sàng; chưa chạy công cụ.'); controls(); }
  else if (message.type === 'catalog') {
    restricted = false; $('trustNotice').hidden = true;
    const chosen = $('agent').value || saved.agentId; catalog = message.data;
    options($('agent'), catalog.agents, chosen); modelOptions('models', $('agent').value); if (!$('model').value && saved.agentId === $('agent').value) $('model').value = saved.model || '';
    options($('brain'), catalog.agents, rolesDirty ? $('brain').value : catalog.coding?.brain?.agentId); options($('worker'), catalog.agents, rolesDirty ? $('worker').value : catalog.coding?.implementer?.agentId);
    if (!rolesDirty) { $('brainModel').value = catalog.coding?.brain?.model || ''; $('workerModel').value = catalog.coding?.implementer?.model || ''; }
    modelOptions('brainModels', $('brain').value); modelOptions('workerModels', $('worker').value); text($('workspace'), catalog.project);
    $('setupNotice').hidden = catalog.agents.length > 0;
    controls();
  } else if (message.type === 'state' || message.type === 'accepted') {
    if (message.type === 'accepted') { submitting = false; if ($('prompt').value.trim() === lastPrompt) $('prompt').value = ''; $('attachments').replaceChildren(); $('context').hidden = true; persist(); }
    render(message.data);
  } else if (message.type === 'error') { submitting = false; text($('error'), message.message); $('error').hidden = false; controls(); }
  else if (message.type === 'steered') { if ($('prompt').value.trim() === lastPrompt) $('prompt').value = ''; persist(); }
  else if (message.type === 'new') { nodes.clear(); state = {}; $('feed').querySelectorAll('.message').forEach(n => n.remove()); $('empty').hidden = false; $('attachments').replaceChildren(); $('context').hidden = true; text($('status'), 'Sẵn sàng'); text($('activity'), 'Cuộc trò chuyện mới'); text($('counts'), ''); $('error').hidden = true; controls(); }
  else if (message.type === 'coding') { rolesDirty = false; catalog.coding = message.data; text($('modeHelp'), 'Đã lưu phân công. ' + (state.busy ? 'Áp dụng ở ranh giới bước tiếp theo.' : 'Chọn Kế hoạch → triển khai để sử dụng.')); $('roles').open = false; }
  else if (message.type === 'models') {
    const agent = catalog.agents.find(a => a.id === message.agentId); if (agent) agent.models = message.data;
    for (const [select, list] of [['agent', 'models'], ['brain', 'brainModels'], ['worker', 'workerModels']]) if ($(select).value === message.agentId) modelOptions(list, message.agentId);
  } else if (message.type === 'attachments') {
    $('attachments').replaceChildren(); for (const file of message.data) { const chip = document.createElement('span'); chip.className = 'file'; text(chip, file.name); const remove = document.createElement('button'); remove.textContent = '×'; remove.title = 'Bỏ ' + file.name; remove.onclick = () => send('removeAttachment', { index: file.index }); chip.append(remove); $('attachments').append(chip); }
  } else if (message.type === 'context') { text($('context'), message.message); $('context').hidden = false; }
});
controls(); send('ready');
