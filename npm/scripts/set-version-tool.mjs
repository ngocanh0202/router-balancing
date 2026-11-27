#!/usr/bin/env node
// Đặt cùng version cho gói chính + 6 gói nền tảng của MỘT tool (rbtui | rb-server) và ghim
// optionalDependencies — giống set-version.mjs của rbctl nhưng theo tên tool.
// Dùng: node npm/scripts/set-version-tool.mjs <rbtui|rb-server|router-balancing> <version|tag>
// router-balancing = gói gộp: ghim dependencies (rbctl, rbtui, rb-server) vào cùng version.
import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const npmDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const [tool, raw] = process.argv.slice(2);
if (!['rbtui', 'rb-server', 'router-balancing'].includes(tool) || !raw) {
  console.error('Dùng: node npm/scripts/set-version-tool.mjs <rbtui|rb-server|router-balancing> 1.12.3');
  process.exit(2);
}
const version = raw.replace(/^v/, '');
if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$/.test(version)) {
  console.error(`Version không hợp lệ: '${raw}'`);
  process.exit(2);
}
const mainPath = join(npmDir, tool, 'package.json');
const main = JSON.parse(readFileSync(mainPath, 'utf8'));
main.version = version;
for (const dep of Object.keys(main.dependencies || {})) main.dependencies[dep] = version;
for (const dir of readdirSync(npmDir)) {
  const pkgPath = join(npmDir, dir, 'package.json');
  if (!dir.startsWith(`${tool}-`) || !existsSync(pkgPath)) continue;
  const pkg = JSON.parse(readFileSync(pkgPath, 'utf8'));
  pkg.version = version;
  writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + '\n');
  main.optionalDependencies[pkg.name] = version;
  console.log(`${pkg.name}@${version}`);
}
writeFileSync(mainPath, JSON.stringify(main, null, 2) + '\n');
console.log(`${main.name}@${version}`);
