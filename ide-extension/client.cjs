'use strict';
const { EventEmitter } = require('node:events');

class HubClient extends EventEmitter {
  constructor(child, options = {}) {
    super(); this.child = child; this.buffer = ''; this.pending = new Map(); this.sequence = 0; this.closed = false;
    this.maxBytes = options.maxBytes || 16 * 1024 * 1024;
    child.stdout.setEncoding('utf8'); child.stderr.setEncoding('utf8');
    child.stdout.on('data', data => this.feed(data));
    child.stderr.on('data', data => this.emit('diagnostic', data.slice(-4000)));
    child.on('error', error => this.fail(error));
    child.on('exit', (code) => this.fail(new Error(`Backend đã đóng (${code ?? 'signal'}). Kết quả đã lưu; mở lại Agent để tiếp tục.`)));
    child.stdin.on('error', error => this.fail(error));
  }
  feed(data) {
    this.buffer += data;
    if (Buffer.byteLength(this.buffer, 'utf8') > this.maxBytes) { this.fail(new Error('IDE response quá lớn.')); this.child.stdin.end(); return; }
    let end;
    while ((end = this.buffer.indexOf('\n')) >= 0) {
      const line = this.buffer.slice(0, end).trim(); this.buffer = this.buffer.slice(end + 1); if (!line) continue;
      let message;
      try { message = JSON.parse(line); } catch { this.fail(new Error('Backend trả dữ liệu IDE không hợp lệ.')); this.child.stdin.end(); return; }
      if (message.event === 'state') { this.emit('state', message.data); continue; }
      const pending = this.pending.get(message.id); if (!pending) continue;
      clearTimeout(pending.timer); this.pending.delete(message.id);
      if (typeof message.error === 'string') pending.reject(new Error(message.error)); else pending.resolve(message.result);
    }
  }
  request(method, params = {}, timeoutMs = 45000) {
    if (this.closed) return Promise.reject(new Error('Backend không còn kết nối.'));
    const id = String(++this.sequence);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error(`Backend chưa xác nhận ${method}. Kiểm tra trạng thái trước khi gửi lại.`)); }, timeoutMs);
      this.pending.set(id, { resolve, reject, timer });
      this.child.stdin.write(JSON.stringify({ id, method, params }) + '\n', 'utf8', error => { if (error) this.fail(error); });
    });
  }
  fail(error) {
    if (this.closed) return; this.closed = true;
    for (const p of this.pending.values()) { clearTimeout(p.timer); p.reject(error); }
    this.pending.clear(); this.emit('closed', error.message);
  }
  dispose() { this.child.stdin.end(); this.fail(new Error('IDE đã đóng kết nối; backend sẽ dừng và lưu phiên.')); }
}
module.exports = { HubClient };
