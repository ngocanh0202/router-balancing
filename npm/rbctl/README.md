# @anhbui02/rbctl

CLI quản lý [Router Balancing](https://github.com/ngocanh0202/router-balancing) — proxy cân bằng tải LLM chạy trên Windows.

```powershell
npm install -g @anhbui02/rbctl
rbctl status
rbctl provider list
```

Hỗ trợ **Windows** (x64, ARM64), **macOS** (Intel, Apple Silicon) và **Linux glibc** (x64, ARM64). Binary tự chứa .NET runtime, không cần cài .NET.

| Nền tảng | DB | Mã hoá API key |
|---|---|---|
| Windows | `%AppData%\router-balancing\router-balancing.db` — chung với app desktop | DPAPI (cùng user với app) |
| macOS / Linux | `~/.config/router-balancing/router-balancing.db` | AES-256-GCM, khoá `secret.key` cạnh DB (quyền 0600) — sao lưu cùng DB |

rbctl đọc/ghi trực tiếp DB qua chính code của app, nên dùng được cả khi app đang tắt.

## Lệnh

```text
rbctl status
rbctl provider list|show|add|update|enable|disable|delete|test
rbctl account  list|add|update|enable|disable|delete|test
rbctl model    list|fetch|sync|add|enable|disable|enable-all|disable-all|remove|remove-all
rbctl combo    list|show|add|update|delete
rbctl key      list|create|update|enable|disable|delete
rbctl logs     [--level] [--category] [--search] [--since] [--limit] [--follow]
rbctl stats    [today|7d|30d]
rbctl settings list|get|set|reset
rbctl opencode list|generate|write
```

Tuỳ chọn chung: `--json`, `--db <file>`, `--yes`, `--help`, `--version`.

Hướng dẫn đầy đủ: [docs/cli.md](https://github.com/ngocanh0202/router-balancing/blob/master/docs/cli.md)

## Cách đóng gói

Gói này chỉ chứa launcher Node. Binary nằm trong các gói nền tảng `@anhbui02/rbctl-<os>-<cpu>` (khai báo ở `optionalDependencies`, npm chỉ cài gói khớp máy) — không có `postinstall` tải file từ mạng.

Phát triển: đặt `RBCTL_BINARY_PATH` trỏ tới binary build local để launcher dùng bản đó.
