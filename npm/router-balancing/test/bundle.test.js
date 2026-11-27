'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const pkg = require('../package.json');
const npmDir = path.join(__dirname, '..', '..');
// @anhbui02/<tên> → thư mục npm/<tên> trong repo; launcher theo trường "bin" của gói đó
const TARGET = /require\('@anhbui02\/([^/]+)\/(.+)'\)/;

test('mỗi lệnh trỏ tới launcher có thật của gói phụ thuộc', () => {
  for (const [cmd, file] of Object.entries(pkg.bin)) {
    const src = fs.readFileSync(path.join(__dirname, '..', file), 'utf8');
    const [, dep, sub] = src.match(TARGET) || [];
    assert.ok(dep, `${cmd}: thiếu require launcher`);
    assert.ok(`@anhbui02/${dep}` in pkg.dependencies, `${cmd}: @anhbui02/${dep} không có trong dependencies`);
    const depPkg = JSON.parse(fs.readFileSync(path.join(npmDir, dep, 'package.json'), 'utf8'));
    assert.equal(depPkg.bin[cmd], sub, `${cmd}: bin của @anhbui02/${dep} là ${depPkg.bin[cmd]}`);
    assert.ok(fs.existsSync(path.join(npmDir, dep, sub)), `${cmd}: không thấy npm/${dep}/${sub}`);
  }
});

test('version gói gộp và dependencies khớp nhau', () => {
  for (const v of Object.values(pkg.dependencies)) assert.equal(v, pkg.version);
});
