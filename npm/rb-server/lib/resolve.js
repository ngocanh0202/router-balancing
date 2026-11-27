'use strict';

// Bảng nền tảng → gói binary (pattern optionalDependencies như esbuild, giống @anhbui02/rbctl).
const PLATFORM_PACKAGES = {
  'win32-x64': { packageName: '@anhbui02/rb-server-win32-x64', binary: 'bin/rb-server.exe' },
  'win32-arm64': { packageName: '@anhbui02/rb-server-win32-arm64', binary: 'bin/rb-server.exe' },
  'linux-x64': { packageName: '@anhbui02/rb-server-linux-x64', binary: 'bin/rb-server' },
  'linux-arm64': { packageName: '@anhbui02/rb-server-linux-arm64', binary: 'bin/rb-server' },
  'darwin-x64': { packageName: '@anhbui02/rb-server-darwin-x64', binary: 'bin/rb-server' },
  'darwin-arm64': { packageName: '@anhbui02/rb-server-darwin-arm64', binary: 'bin/rb-server' },
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
 * Đường dẫn binary rb-server trong gói nền tảng đang cài.
 * @param {object} [options] platform/arch/musl/resolve/env — inject để test
 * @returns {string}
 */
function resolveBinary(options = {}) {
  const env = options.env || process.env;
  // Trỏ tới bản build local (dotnet publish) khi phát triển
  if (env.RB_SERVER_BINARY_PATH) return env.RB_SERVER_BINARY_PATH;

  const platform = options.platform || process.platform;
  const arch = options.arch || process.arch;
  const musl = options.musl !== undefined ? options.musl : isMuslLinux();
  const resolve = options.resolve || require.resolve;
  const key = `${platform}-${arch}`;
  const target = PLATFORM_PACKAGES[key];

  if (!target) {
    throw new Error(`rb-server chưa hỗ trợ nền tảng ${key}. Hỗ trợ: ${Object.keys(PLATFORM_PACKAGES).join(', ')}.`);
  }
  if (platform === 'linux' && musl) {
    throw new Error('rb-server chưa hỗ trợ Linux musl (Alpine…). Dùng distro glibc (Debian, Ubuntu, Fedora…).');
  }
  try {
    return resolve(`${target.packageName}/${target.binary}`);
  } catch {
    throw new Error(
      `Không tìm thấy gói ${target.packageName}. Có thể npm đã bỏ qua optionalDependencies ` +
        '(--no-optional / --omit=optional). Cài lại: npm install -g @anhbui02/rb-server'
    );
  }
}

module.exports = { resolveBinary, PLATFORM_PACKAGES, isMuslLinux };
