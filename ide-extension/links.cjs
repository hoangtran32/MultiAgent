'use strict';
const path = require('node:path');
function target(root, value) {
  if (typeof value !== 'string' || value.length > 4096 || /[\x00-\x1f]/.test(value)) throw new Error('Liên kết không hợp lệ.');
  if (/^https?:\/\//i.test(value)) { const url = new URL(value); if (url.username || url.password) throw new Error('Không mở URL có thông tin đăng nhập.'); return { external: url.href }; }
  if (/^[a-z][a-z0-9+.-]*:/i.test(value) && !/^[a-z]:[\\/]/i.test(value)) throw new Error('Chỉ mở file trong project hoặc liên kết HTTP/HTTPS.');
  const match = value.match(/^(.*?)(?::(\d+))?$/); const file = path.resolve(root, match[1]); const relative = path.relative(root, file);
  if (relative === '..' || relative.startsWith('..' + path.sep) || path.isAbsolute(relative)) throw new Error('Liên kết file nằm ngoài project.');
  if (relative.split(path.sep).some(part => part.toLowerCase() === '.git')) throw new Error('Không mở file nội bộ Git từ chat.');
  return { file, line: Math.max(0, Math.min(1000000, Number(match[2] || 1) - 1)) };
}
module.exports = { target };
