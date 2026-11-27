#!/usr/bin/env node
'use strict';

// Launcher npm của rb-server: tìm binary trong gói nền tảng rồi chạy với stdio của terminal.
// Dùng spawn (không spawnSync) để CHUYỂN TIẾP SIGINT/SIGTERM/SIGHUP cho binary — cần cho
// rb-server chạy lâu (tmux, systemd, docker stop) và cho rbtui thoát gọn.
const fs = require('node:fs');
const { spawn } = require('node:child_process');
const { resolveBinary } = require('./lib/resolve');

let binary;
try {
  binary = resolveBinary();
} catch (error) {
  process.stderr.write(`Lỗi: ${error.message}\n`);
  process.exit(1);
}

if (process.platform !== 'win32') {
  try {
    fs.accessSync(binary, fs.constants.X_OK);
  } catch {
    try { fs.chmodSync(binary, 0o755); } catch { /* spawn sẽ báo EACCES kèm đường dẫn */ }
  }
}

const child = spawn(binary, process.argv.slice(2), { stdio: 'inherit' });
for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP']) {
  process.on(signal, () => {
    try { child.kill(signal); } catch { /* đã thoát */ }
  });
}
child.on('error', (error) => {
  process.stderr.write(`Lỗi: không chạy được ${binary}: ${error.message}\n`);
  process.exit(1);
});
child.on('exit', (code, signal) => {
  if (signal) process.kill(process.pid, signal);
  else process.exit(code === null ? 1 : code);
});
