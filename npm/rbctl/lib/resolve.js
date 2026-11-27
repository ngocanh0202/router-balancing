'use strict';

// Bảng nền tảng → gói binary. Thêm nền tảng = thêm gói npm/rbctl-<os>-<cpu> + một dòng ở đây
// (+ optionalDependencies, do scripts/set-version.mjs tự đồng bộ theo thư mục).
const PLATFORM_PACKAGES = {
  'win32-x64': { packageName: '@anhbui02/rbctl-win32-x64', binary: 'bin/rbctl.exe' },
  'win32-arm64': { packageName: '@anhbui02/rbctl-win32-arm64', binary: 'bin/rbctl.exe' },
  'linux-x64': { packageName: '@anhbui02/rbctl-linux-x64', binary: 'bin/rbctl' },
  'linux-arm64': { packageName: '@anhbui02/rbctl-linux-arm64', binary: 'bin/rbctl' },
  'darwin-x64': { packageName: '@anhbui02/rbctl-darwin-x64', binary: 'bin/rbctl' },
  'darwin-arm64': { packageName: '@anhbui02/rbctl-darwin-arm64', binary: 'bin/rbctl' },
};

/**
 * Linux dùng musl (Alpine…) không chạy được binary .NET build cho glibc — gói linux khai
 * `libc: ["glibc"]` nên npm bỏ qua, cần báo đúng lý do thay vì "thiếu gói".
 * @returns {boolean}
 */
function isMuslLinux() {
  if (process.platform !== 'linux') return false;
  try {
    const header = process.report.getReport().header;
    return !header.glibcVersionRuntime;
  } catch {
    return false;
  }
}

/**
 * Tìm đường dẫn binary rbctl trong gói nền tảng (pattern optionalDependencies như esbuild:
 * npm chỉ cài gói khớp os/cpu/libc, không cần postinstall tải file).
 *
 * @param {object} [options]
 * @param {string} [options.platform] process.platform (inject để test)
 * @param {string} [options.arch] process.arch (inject để test)
 * @param {boolean} [options.musl] Linux musl? (inject để test)
 * @param {(request: string) => string} [options.resolve] require.resolve (inject để test)
 * @param {Record<string, string | undefined>} [options.env] biến môi trường
 * @returns {string} đường dẫn tuyệt đối tới binary
 * @throws {Error} nền tảng không hỗ trợ hoặc gói binary chưa được cài
 */
function resolveBinary(options = {}) {
  const env = options.env || process.env;
  // Cho phép trỏ tới bản build local (dotnet publish) khi phát triển, bỏ qua gói npm
  if (env.RBCTL_BINARY_PATH) {
    return env.RBCTL_BINARY_PATH;
  }

  const platform = options.platform || process.platform;
  const arch = options.arch || process.arch;
  const musl = options.musl !== undefined ? options.musl : isMuslLinux();
  const resolve = options.resolve || require.resolve;
  const key = `${platform}-${arch}`;
  const target = PLATFORM_PACKAGES[key];

  if (!target) {
    throw new Error(
      `rbctl chưa hỗ trợ nền tảng ${key}. Hỗ trợ: ${Object.keys(PLATFORM_PACKAGES).join(', ')}.`
    );
  }
  if (platform === 'linux' && musl) {
    throw new Error('rbctl chưa hỗ trợ Linux musl (Alpine…). Dùng distro glibc (Debian, Ubuntu, Fedora…).');
  }

  try {
    return resolve(`${target.packageName}/${target.binary}`);
  } catch {
    throw new Error(
      `Không tìm thấy gói ${target.packageName}. Có thể npm đã bỏ qua optionalDependencies ` +
        '(--no-optional / --omit=optional). Cài lại: npm install -g @anhbui02/rbctl'
    );
  }
}

module.exports = { resolveBinary, PLATFORM_PACKAGES, isMuslLinux };
