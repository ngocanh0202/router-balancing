#!/usr/bin/env node
// Đặt cùng một version cho gói chính + mọi gói nền tảng và ghim optionalDependencies vào
// đúng version đó — lệch version thì npm sẽ cài binary của bản khác.
//
// Dùng: node npm/scripts/set-version.mjs <version|tag>   (vd 1.12.3 hoặc v1.12.3)
import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const npmDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const raw = process.argv[2];
if (!raw) {
  console.error('Thiếu version. Dùng: node npm/scripts/set-version.mjs 1.12.3');
  process.exit(2);
}

const version = raw.replace(/^v/, '');
// SemVer 2.0 (rút gọn): MAJOR.MINOR.PATCH[-prerelease][+build]
if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$/.test(version)) {
  console.error(`Version không hợp lệ: '${raw}'`);
  process.exit(2);
}

const mainPath = join(npmDir, 'rbctl', 'package.json');
const main = JSON.parse(readFileSync(mainPath, 'utf8'));
main.version = version;

for (const dir of readdirSync(npmDir)) {
  const pkgPath = join(npmDir, dir, 'package.json');
  if (!dir.startsWith('rbctl-') || !existsSync(pkgPath)) continue;
  const pkg = JSON.parse(readFileSync(pkgPath, 'utf8'));
  pkg.version = version;
  writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + '\n');
  main.optionalDependencies[pkg.name] = version;
  console.log(`${pkg.name}@${version}`);
}

writeFileSync(mainPath, JSON.stringify(main, null, 2) + '\n');
console.log(`${main.name}@${version}`);
