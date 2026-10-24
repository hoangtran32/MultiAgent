'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const { PassThrough } = require('node:stream');
const { HubClient } = require('../ide-extension/client.cjs');
function fixture() { const child = new EventEmitter(); child.stdout = new PassThrough(); child.stderr = new PassThrough(); child.stdin = new PassThrough(); const requests = []; let input = ''; child.stdin.on('data', bytes => { input += bytes; let n; while ((n = input.indexOf('\n')) >= 0) { requests.push(JSON.parse(input.slice(0, n))); input = input.slice(n + 1); } }); return { child, requests, client: new HubClient(child) }; }
test('partial UTF-8 frames and multiline answers are delivered once without losing streamed activity', async () => {
  const { child, requests, client } = fixture(); const states = []; client.on('state', s => states.push(s));
  const pending = client.request('start', { prompt: 'Chào 🧠\nDòng 2' }); assert.equal(requests[0].params.prompt, 'Chào 🧠\nDòng 2');
  const bytes = Buffer.from(JSON.stringify({ event: 'state', data: { text: 'Đang chạy 🧠' } }) + '\n' + JSON.stringify({ id: requests[0].id, result: { text: 'Đã xong\nKết quả' } }) + '\n');
  for (let i = 0; i < bytes.length; i += 3) child.stdout.write(bytes.subarray(i, i + 3));
  assert.deepEqual(await pending, { text: 'Đã xong\nKết quả' }); assert.deepEqual(states, [{ text: 'Đang chạy 🧠' }]); client.dispose();
});
test('interleaved responses are correlated, errors retain the draft and a timeout never resends the request', async () => {
  const { child, requests, client } = fixture(); const first = client.request('initialize'); const second = client.request('start');
  child.stdout.write(JSON.stringify({ id: requests[1].id, error: 'Exact agent unavailable' }) + '\n'); await assert.rejects(second, /Exact agent unavailable/);
  child.stdout.write(JSON.stringify({ id: requests[0].id, result: { agents: [] } }) + '\n'); assert.deepEqual(await first, { agents: [] });
  await assert.rejects(client.request('start', {}, 10), /chưa xác nhận/); assert.equal(requests.length, 3); client.dispose();
});
test('backend exit rejects pending work and emits a disconnect rather than pretending that the model completed', async () => {
  const { child, client } = fixture(); let closed; client.on('closed', value => closed = value); const request = client.request('start'); child.emit('exit', 1);
  await assert.rejects(request, /Backend đã đóng/); assert.match(closed, /để tiếp tục/); assert.equal(client.closed, true); await assert.rejects(client.request('start'), /không còn kết nối/);
});
test('malformed or oversized protocol cannot be treated as a chatbot answer', async () => {
  for (const value of ['not-json\n', 'x'.repeat(65)]) { const { child } = fixture(); const client = new HubClient(child, { maxBytes: 64 }); const request = client.request('start'); child.stdout.write(value); await assert.rejects(request, /không hợp lệ|quá lớn/); }
});
