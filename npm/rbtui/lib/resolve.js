'use strict';

// Bảng nền tảng → gói binary (pattern optionalDependencies như esbuild, giống @anhbui02/rbctl).
const PLATFORM_PACKAGES = {
  'win32-x64': { packageName: '@anhbui02/rbtui-win32-x64', binary: 'bin/rbtui.exe' },
  'win32-arm64': { packageName: '@anhbui02/rbtui-win32-arm64', binary: 'bin/rbtui.exe' },
  'linux-x64': { packageName: '@anhbui02/rbtui-linux-x64', binary: 'bin/rbtui' },
  'linux-arm64': { packageName: '@anhbui02/rbtui-linux-arm64', binary: 'bin/rbtui' },
  'darwin-x64': { packageName: '@anhbui02/rbtui-darwin-x64', binary: 'bin/rbtui' },
  'darwin-arm64': { packageName: '@anhbui02/rbtui-darwin-arm64', binary: 'bin/rbtui' },
};

function isMuslLinux() {
  if (process.platform !== 'linux') return false;
  try {
    return !process.report.getReport().header.glibcVersionRuntime;
  } catch {
    return false;
  }
}

/**
 * Đường dẫn binary rbtui trong gói nền tảng đang cài.
 * @param {object} [options] platform/arch/musl/resolve/env — inject để test
 * @returns {string}
 */
function resolveBinary(options = {}) {
  const env = options.env || process.env;
  // Trỏ tới bản build local (dotnet publish) khi phát triển
  if (env.RBTUI_BINARY_PATH) return env.RBTUI_BINARY_PATH;

  const platform = options.platform || process.platform;
  const arch = options.arch || process.arch;
  const musl = options.musl !== undefined ? options.musl : isMuslLinux();
  const resolve = options.resolve || require.resolve;
  const key = `${platform}-${arch}`;
  const target = PLATFORM_PACKAGES[key];

  if (!target) {
    throw new Error(`rbtui chưa hỗ trợ nền tảng ${key}. Hỗ trợ: ${Object.keys(PLATFORM_PACKAGES).join(', ')}.`);
  }
  if (platform === 'linux' && musl) {
    throw new Error('rbtui chưa hỗ trợ Linux musl (Alpine…). Dùng distro glibc (Debian, Ubuntu, Fedora…).');
  }
  try {
    return resolve(`${target.packageName}/${target.binary}`);
  } catch {
    throw new Error(
      `Không tìm thấy gói ${target.packageName}. Có thể npm đã bỏ qua optionalDependencies ` +
        '(--no-optional / --omit=optional). Cài lại: npm install -g @anhbui02/rbtui'
    );
  }
}

module.exports = { resolveBinary, PLATFORM_PACKAGES, isMuslLinux };
