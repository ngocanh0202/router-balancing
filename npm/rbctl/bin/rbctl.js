#!/usr/bin/env node
'use strict';

const fs = require('node:fs');
const { spawnSync } = require('node:child_process');
const { resolveBinary } = require('../lib/resolve');

let binary;
try {
  binary = resolveBinary();
} catch (error) {
  process.stderr.write(`Lỗi: ${error.message}\n`);
  process.exit(1);
}

// macOS/Linux: một số cách cài (pnpm, cache bị giải nén lại, copy thủ công) làm mất bit thực thi
// của file trong gói nền tảng — tự thêm lại nếu có quyền, không thì để spawn báo lỗi rõ.
if (process.platform !== 'win32') {
  try {
    fs.accessSync(binary, fs.constants.X_OK);
  } catch {
    try {
      fs.chmodSync(binary, 0o755);
    } catch {
      // Không có quyền chmod (cài global bằng root) — spawn bên dưới sẽ báo EACCES kèm đường dẫn
    }
  }
}

// stdio inherit: UTF-8, câu hỏi xác nhận [y/N] và `logs --follow` chạy như gọi binary trực tiếp.
// Ctrl+C gửi tới cả process group → rbctl tự xử lý huỷ mềm; wrapper chỉ chờ và trả exit code.
const result = spawnSync(binary, process.argv.slice(2), { stdio: 'inherit' });

if (result.error) {
  process.stderr.write(`Lỗi: không chạy được ${binary}: ${result.error.message}\n`);
  process.exit(1);
}
if (result.signal) {
  process.kill(process.pid, result.signal);
}
process.exit(result.status === null ? 1 : result.status);
