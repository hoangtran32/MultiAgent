'use strict';
const test = require('node:test'); const assert = require('node:assert/strict'); const path = require('node:path'); const { target } = require('../ide-extension/links.cjs');
test('agent links open only a workspace file or an explicit HTTP link on user click', () => {
  const root = path.resolve('fixture-project'); assert.deepEqual(target(root, 'src/main.cs:42'), { file: path.join(root, 'src', 'main.cs'), line: 41 });
  assert.deepEqual(target(root, 'https://example.com/help?q=agent'), { external: 'https://example.com/help?q=agent' });
  for (const link of ['../outside.txt', 'command:workbench.action.terminal.new', 'javascript:alert(1)', 'file:///outside.txt', '.git/config', 'https://user:password@example.com/', 'a\x00b']) assert.throws(() => target(root, link));
});
