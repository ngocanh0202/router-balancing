'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const launcher = path.join(__dirname, '..', 'bin', 'rbctl.js');

// Binary giả là shell script — chỉ chạy được trên POSIX; trên Windows test resolve.test.js đã đủ.
const posixOnly = { skip: process.platform === 'win32' };

function fakeBinary(script) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'rbctl-npm-'));
  const file = path.join(dir, 'rbctl');
  fs.writeFileSync(file, `#!/bin/sh\n${script}\n`, { mode: 0o755 });
  return file;
}

test('chuyển nguyên tham số và stdout của binary', posixOnly, () => {
  const binary = fakeBinary('printf "%s|" "$@"');
  const result = spawnSync(process.execPath, [launcher, 'provider', 'add', '--name', 'Nhà A'], {
    env: { ...process.env, RBCTL_BINARY_PATH: binary },
    encoding: 'utf8',
  });
  assert.equal(result.status, 0);
  assert.equal(result.stdout, 'provider|add|--name|Nhà A|');
});

test('giữ nguyên exit code của binary', posixOnly, () => {
  const binary = fakeBinary('exit 3');
  const result = spawnSync(process.execPath, [launcher, 'key', 'delete', 'x'], {
    env: { ...process.env, RBCTL_BINARY_PATH: binary },
  });
  assert.equal(result.status, 3);
});

test('tự thêm bit thực thi khi binary bị mất quyền chạy', posixOnly, () => {
  const binary = fakeBinary('echo ok');
  fs.chmodSync(binary, 0o644);
  const result = spawnSync(process.execPath, [launcher], {
    env: { ...process.env, RBCTL_BINARY_PATH: binary },
    encoding: 'utf8',
  });
  assert.equal(result.status, 0);
  assert.equal(result.stdout.trim(), 'ok');
});

test('binary không tồn tại → exit 1 kèm thông báo', () => {
  const result = spawnSync(process.execPath, [launcher, 'status'], {
    env: { ...process.env, RBCTL_BINARY_PATH: path.join(os.tmpdir(), 'khong-ton-tai-rbctl') },
    encoding: 'utf8',
  });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /không chạy được/);
});
