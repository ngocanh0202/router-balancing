'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { resolveBinary, PLATFORM_PACKAGES } = require('../lib/resolve');

test('mọi nền tảng có thư mục gói khớp tên + binary + optionalDependencies', () => {
  const main = require('../package.json');
  for (const [key, target] of Object.entries(PLATFORM_PACKAGES)) {
    const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', `rbtui-${key}`, 'package.json'), 'utf8'));
    assert.equal(pkg.name, target.packageName, key);
    assert.deepEqual(pkg.files.filter((f) => f.startsWith('bin/')), [target.binary], key);
    assert.ok(target.packageName in main.optionalDependencies, key);
  }
});

test('linux-x64 resolve tới bin/rbtui của gói nền tảng', () => {
  const requested = [];
  resolveBinary({ platform: 'linux', arch: 'x64', musl: false, env: {},
    resolve: (r) => { requested.push(r); return '/x/' + r; } });
  assert.deepEqual(requested, ['@anhbui02/rbtui-linux-x64/bin/rbtui']);
});

test('win32 dùng .exe', () => {
  const p = resolveBinary({ platform: 'win32', arch: 'x64', env: {}, resolve: (r) => r });
  assert.match(p, /\.exe$/);
});

test('biến môi trường RBTUI_BINARY_PATH được ưu tiên', () => {
  assert.equal(resolveBinary({ env: { RBTUI_BINARY_PATH: '/local/rbtui' } }), '/local/rbtui');
});

test('nền tảng lạ và musl báo lỗi rõ', () => {
  assert.throws(() => resolveBinary({ platform: 'aix', arch: 'ppc64', env: {} }), /chưa hỗ trợ nền tảng/);
  assert.throws(() => resolveBinary({ platform: 'linux', arch: 'x64', musl: true, env: {} }), /musl/);
});

test('thiếu gói nền tảng → gợi ý cài lại', () => {
  assert.throws(() => resolveBinary({ platform: 'linux', arch: 'x64', musl: false, env: {},
    resolve: () => { throw new Error('x'); } }), /npm install -g @anhbui02\/rbtui/);
});
