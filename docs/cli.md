# rbctl — CLI quản lý Router Balancing

`rbctl` là công cụ dòng lệnh đọc/ghi **trực tiếp** DB SQLite của Router Balancing qua chính các service trong `RouterBalancing.Core`, nên luật validate và hành vi giống hệt UI. Chạy được cả khi app đang tắt.

Chạy trên Windows (x64, ARM64), macOS (Intel, Apple Silicon) và Linux glibc (x64, ARM64):

| Nền tảng | DB mặc định | Mã hoá API key provider |
|---|---|---|
| Windows | `%AppData%\router-balancing\router-balancing.db` — **chung với app desktop** | DPAPI CurrentUser, giống app |
| macOS / Linux | `~/.config/router-balancing/router-balancing.db` (theo `XDG_CONFIG_HOME`) — DB riêng của CLI, app desktop chỉ có trên Windows | AES-256-GCM, khoá ngẫu nhiên trong `secret.key` **cạnh file DB**, quyền `0600` |

Trên macOS/Linux, `secret.key` là thứ duy nhất giải mã được API key: sao lưu cùng DB; mất file khoá thì nhập lại key bằng `rbctl account update <account> --api-key …`. rbctl từ chối dùng file khoá mà user khác đọc được (như ssh) — sửa bằng `chmod 600`. DB chép từ Windows sang dùng được nhưng key DPAPI trong đó không giải mã được, phải nhập lại.

Project: [`src/RouterBalancing.Cli`](../src/RouterBalancing.Cli) · Test: [`tests/RouterBalancing.Cli.Tests`](../tests/RouterBalancing.Cli.Tests)

## Build & chạy

```powershell
# chạy trực tiếp từ source
dotnet run --project src/RouterBalancing.Cli -- status

# publish single-file cho một nền tảng (win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64)
dotnet publish src/RouterBalancing.Cli -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/cli
publish/cli/rbctl.exe provider list
```

```powershell
# test
dotnet test tests/RouterBalancing.Cli.Tests/RouterBalancing.Cli.Tests.csproj --no-banner
```

CLI là phần bổ sung độc lập — không nằm trong `router-balancing.slnx`, không sửa workflow có sẵn:

| Workflow | Việc |
|---|---|
| [`ci-cli.yml`](../.github/workflows/ci-cli.yml) | Build + test CLI trên Windows, Ubuntu, macOS khi đổi CLI/Core |
| [`cli-build.yml`](../.github/workflows/cli-build.yml) | Workflow dùng chung: build single-file 6 RID trên runner đúng OS (macOS build trên macOS để được ký ad-hoc), smoke test DB + mã hoá key trên bản chạy được |
| [`release-cli.yml`](../.github/workflows/release-cli.yml) | Khi publish release: test → `cli-build` → upload `rbctl-<tag>-<rid>[.exe]` + `SHA256SUMS-cli.txt` vào cùng release |
| [`npm-cli.yml`](../.github/workflows/npm-cli.yml) | Test launcher npm; khi release: `cli-build` → publish 7 gói npm |

CLI phát hành dạng file binary trần chứ không phải `.zip` vì auto-update của app lấy asset `.zip` đầu tiên trong release.

CLI không thêm package NuGet nào ngoài những gì Core đã dùng (DI/HttpClientFactory lấy từ shared framework `Microsoft.AspNetCore.App`).

## Lưu ý vận hành

- **Windows: chạy cùng user với app** — key DPAPI CurrentUser chỉ user đó giải mã được.
- **Linux**: cần `libicu` (có sẵn trên hầu hết distro); chưa hỗ trợ musl/Alpine.
- **App đang chạy**: thay đổi provider/account/model/combo có hiệu lực ngay (proxy đọc DB mỗi request). Riêng **settings** và **client key** nằm trong cache RAM của app → rbctl in cảnh báo, cần khởi động lại app.
- **Migration**: rbctl migrate DB giống app khi khởi động; nếu DB cần migrate mà app đang chạy thì rbctl từ chối (tránh đổi schema dưới chân app khác phiên bản).
- `startWithWindows` không đổi được qua CLI (cần ghi registry qua app).
- `opencode generate/write` dùng cổng trong settings; nếu app chọn cổng khác vì cổng bận, truyền `--base-url`.

## Tham chiếu đối tượng

| Loại | Cách gõ |
|---|---|
| `<provider>` | id, identifier (`openrouter`) hoặc tên |
| `<model>` | id, hoặc `<provider>/<modelId>` — tách ở `/` đầu tiên (`openrouter/meta-llama/llama-3.1-8b:free`) |
| `<account>` | id, hoặc `<provider>/<tên account>` |
| `<combo>` | id hoặc tên; trong item combo dùng `combo:<tên>` |
| `<key>` | id hoặc tên client key |

## Lệnh

```text
rbctl status                                   # app, /health proxy, DB, số lượng
rbctl provider list|show|add|update|enable|disable|delete|test
rbctl account  list|add|update|enable|disable|delete|test
rbctl model    list|fetch|sync|add|enable|disable|enable-all|disable-all|remove|remove-all
rbctl combo    list|show|add|update|delete
rbctl key      list|create|update|enable|disable|delete
rbctl logs     [--level warn] [--category request] [--search x] [--since 2h] [--limit 50] [--follow]
rbctl stats    [today|7d|30d] [--top 10]
rbctl settings list|get|set|reset
rbctl opencode list|generate|write
```

`rbctl <nhóm> help` in chi tiết tham số từng nhóm.

Tuỳ chọn chung: `--json` (xuất JSON cho script), `--db <file>` (DB khác), `--yes` (bỏ xác nhận xoá), `--help`, `--version`.

Exit code: `0` OK · `1` lỗi (không tìm thấy, validate, test kết nối fail, proxy không phản hồi ở `status`) · `2` sai cú pháp · `3` huỷ xác nhận.

## Ví dụ

```powershell
# Thêm provider OpenAI-compatible, nạp model, test
rbctl provider add --name "Groq" --base-url https://api.groq.com/openai/v1 --api-key $env:GROQ_KEY
rbctl model fetch groq
rbctl provider test groq

# Thêm account thứ hai chỉ phục vụ model llama*, giới hạn 200 request/ngày
rbctl account add groq --name backup --api-key $env:GROQ_KEY2 --patterns "llama*" --daily-requests 200

# Combo fallback: thử Groq trước rồi OpenRouter
rbctl combo add coder --mode fallback groq/llama-3.3-70b-versatile openrouter/qwen/qwen3-coder:free

# Tạo client key cho laptop, giới hạn 60 request/phút
rbctl key create laptop --rpm 60

# Ghi config opencode với combo vừa tạo, lưu lựa chọn cho trang Tools
rbctl opencode write --models default --combos coder --api-key rb-xxxx --save-selection

# Theo dõi log lỗi request
rbctl logs --level error --category request --follow

# Dùng trong script
rbctl stats 7d --json | ConvertFrom-Json | Select-Object requests, errors
```

## Phát hành npm

Gói [`@anhbui02/rbctl`](https://www.npmjs.com/package/@anhbui02/rbctl) (thư mục [`npm/`](../npm)) — cài bằng `npm install -g @anhbui02/rbctl`.

| Gói | Nội dung |
|---|---|
| `@anhbui02/rbctl` | Launcher Node (`bin/rbctl.js`) — tìm và chạy binary, chuyển nguyên tham số + exit code |
| `@anhbui02/rbctl-win32-x64`, `-win32-arm64` | `rbctl.exe` single-file self-contained cho Windows |
| `@anhbui02/rbctl-linux-x64`, `-linux-arm64` | `rbctl` cho Linux, khai `libc: glibc` |
| `@anhbui02/rbctl-darwin-x64`, `-darwin-arm64` | `rbctl` cho macOS, build + ký ad-hoc trên runner macOS |

Mỗi gói nền tảng khai `os`/`cpu` nên npm chỉ cài đúng một gói khớp máy. RID .NET của gói nằm ở `config.dotnetRid` trong `package.json` — workflow và script đọc từ đó.

Gói chính khai báo gói nền tảng ở `optionalDependencies` (pattern của esbuild) — không có `postinstall` tải file từ mạng. Version của mọi gói luôn bằng nhau, do [`npm/scripts/set-version.mjs`](../npm/scripts/set-version.mjs) đặt từ tag release (`v1.12.3` → `1.12.3`); trong repo để `0.0.0-dev`.

Workflow [`npm-cli.yml`](../.github/workflows/npm-cli.yml):

- PR/push đụng `npm/**` hoặc CLI: `npm test` (launcher) + `npm pack --dry-run`.
- Release published: test CLI → build 6 nền tảng (`cli-build.yml`) → publish 6 gói nền tảng rồi gói chính bằng **Trusted Publishing (OIDC)**, provenance tự động. Version prerelease (`1.13.0-beta.1`) lên dist-tag `next`. Version đã có trên npm thì bỏ qua → chạy lại an toàn.
- Chạy tay (workflow_dispatch): nhập version, mặc định `dry-run`.

### Thiết lập một lần

Trusted publisher chỉ gắn được vào gói **đã tồn tại**, nên mỗi gói mới phải publish tay một lần từ máy Windows. Script tự tìm gói nào chưa có version đó trên npm (gói đã có thì bỏ qua), build binary cho các gói đó (cross-compile từ Windows), đặt version, publish rồi khôi phục package.json:

```powershell
powershell -ExecutionPolicy Bypass -File npm/scripts/publish-first.ps1 -DryRun   # chạy thử
powershell -ExecutionPolicy Bypass -File npm/scripts/publish-first.ps1           # publish thật
```

Khi thêm nền tảng mới (vd 5 gói ngoài win32-x64 sau bản 1.12.3), chạy lại script với version **đã publish** của gói chính để chỉ khởi tạo các gói còn thiếu — gói chính bản đó không trỏ tới chúng nên vô hại; bản release kế tiếp CI publish đủ 7 gói:

```powershell
powershell -ExecutionPolicy Bypass -File npm/scripts/publish-first.ps1 -Version 1.12.3
```

Binary macOS build từ Windows chưa ký ad-hoc nên chỉ dùng để khởi tạo gói; bản phát hành thật luôn do CI build trên runner macOS.

Hoặc làm tay từng bước (một nền tảng):

```powershell
dotnet publish src/RouterBalancing.Cli -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
  -o npm/rbctl-win32-x64/bin
node npm/scripts/set-version.mjs 1.12.3
npm login
cd npm/rbctl-win32-x64; npm publish --access public
cd ../rbctl;            npm publish --access public
cd ../..; git checkout -- npm/   # bỏ version vừa đặt, giữ 0.0.0-dev trong repo
```

Sau đó trên npmjs.com, với **từng** gói (7 gói `@anhbui02/rbctl*`), chỉ tích "Allow npm publish": Settings → Trusted Publisher → GitHub Actions — owner `ngocanh0202`, repo `router-balancing`, workflow `npm-cli.yml`, environment `npm`. Trên GitHub tạo environment `npm` (Settings → Environments; thêm required reviewers nếu muốn duyệt tay). Có thể bật "Require two-factor authentication and disallow tokens" cho gói sau khi trusted publishing chạy ổn.

Phát triển launcher: `cd npm/rbctl && npm test`; đặt `RBCTL_BINARY_PATH` trỏ tới binary build local để chạy `node bin/rbctl.js` với bản đó.

Thêm nền tảng: tạo `npm/rbctl-<os>-<cpu>/` (copy một gói có sẵn, sửa `name`/`os`/`cpu`/`config.dotnetRid`), thêm dòng vào `PLATFORM_PACKAGES` trong `npm/rbctl/lib/resolve.js`, thêm RID vào matrix của `cli-build.yml`. Test launcher kiểm tra bảng và thư mục gói khớp nhau.
