'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { resolveBinary } = require('../lib/resolve');

test('win32-x64 resolve tới rbctl.exe của gói nền tảng', () => {
  const requested = [];
  const path = resolveBinary({
    platform: 'win32',
    arch: 'x64',
    env: {},
    resolve: (request) => {
      requested.push(request);
      return 'C:\\node_modules\\@anhbui02\\rbctl-win32-x64\\bin\\rbctl.exe';
    },
  });
  assert.deepEqual(requested, ['@anhbui02/rbctl-win32-x64/bin/rbctl.exe']);
  assert.match(path, /rbctl\.exe$/);
});

test('mọi nền tảng hỗ trợ đều có thư mục gói khớp tên + binary', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const { PLATFORM_PACKAGES } = require('../lib/resolve');
  const main = require('../package.json');
  for (const [key, target] of Object.entries(PLATFORM_PACKAGES)) {
    const dir = path.join(__dirname, '..', '..', `rbctl-${key}`);
    const pkg = JSON.parse(fs.readFileSync(path.join(dir, 'package.json'), 'utf8'));
    assert.equal(pkg.name, target.packageName, key);
    assert.deepEqual(pkg.files.filter((f) => f.startsWith('bin/')), [target.binary], key);
    assert.ok(target.packageName in main.optionalDependencies, `${key} thiếu trong optionalDependencies`);
  }
});

for (const [platform, arch, binary] of [
  ['win32', 'arm64', 'bin/rbctl.exe'],
  ['linux', 'x64', 'bin/rbctl'],
  ['linux', 'arm64', 'bin/rbctl'],
  ['darwin', 'x64', 'bin/rbctl'],
  ['darwin', 'arm64', 'bin/rbctl'],
]) {
  test(`${platform}-${arch} resolve tới ${binary}`, () => {
    const path = resolveBinary({ platform, arch, musl: false, env: {}, resolve: (r) => `/x/${r}` });
    assert.equal(path, `/x/@anhbui02/rbctl-${platform}-${arch}/${binary}`);
  });
}

test('nền tảng không hỗ trợ báo lỗi rõ ràng', () => {
  assert.throws(
    () => resolveBinary({ platform: 'freebsd', arch: 'x64', env: {}, resolve: () => 'x' }),
    /chưa hỗ trợ nền tảng freebsd-x64/
  );
});

test('Linux musl báo không hỗ trợ thay vì thiếu gói', () => {
  assert.throws(
    () => resolveBinary({ platform: 'linux', arch: 'x64', musl: true, env: {}, resolve: () => 'x' }),
    /musl/
  );
});

test('thiếu gói optional gợi ý cài lại', () => {
  assert.throws(
    () =>
      resolveBinary({
        platform: 'win32',
        arch: 'x64',
        env: {},
        resolve: () => {
          throw new Error('MODULE_NOT_FOUND');
        },
      }),
    /npm install -g @anhbui02\/rbctl/
  );
});

test('RBCTL_BINARY_PATH ghi đè mọi nền tảng', () => {
  const path = resolveBinary({
    platform: 'linux',
    arch: 'arm64',
    env: { RBCTL_BINARY_PATH: '/tmp/rbctl' },
    resolve: () => {
      throw new Error('không được gọi');
    },
  });
  assert.equal(path, '/tmp/rbctl');
});
