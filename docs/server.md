# rb-server — proxy Router Balancing không giao diện

`rb-server` chạy **proxy** Router Balancing (API OpenAI-compatible `/v1/chat/completions`, `/v1/models`)
mà không cần app desktop — dùng cho Linux/macOS/server (vd Kaggle, VPS), nơi app MAUI không chạy.
Cấu hình (provider, account, model, combo, client key, settings) quản lý bằng [`rbctl`](cli.md);
hai chương trình dùng **chung DB + file khoá**:

| Nền tảng | DB mặc định | Mã hoá API key |
|---|---|---|
| macOS / Linux | `~/.config/router-balancing/router-balancing.db` | AES-256-GCM, `secret.key` cạnh DB (giống rbctl) |
| Windows | `%AppData%\router-balancing\router-balancing.db` | DPAPI CurrentUser (giống app) — **đừng chạy cùng lúc với app desktop** (trùng cổng/DB) |

Project: [`src/RouterBalancing.Server`](../src/RouterBalancing.Server) — chỉ tham chiếu Core, dùng lại
`FileKeySecretProtector` của rbctl (link file). Không nằm trong `router-balancing.slnx`.

## Build & chạy

```bash
dotnet run --project src/RouterBalancing.Server
# single-file cho Linux
dotnet publish src/RouterBalancing.Server -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/server
./publish/server/rb-server            # in "LISTENING <port>" khi sẵn sàng
```

Release: workflow [`release-server.yml`](../.github/workflows/release-server.yml) đính kèm
`rb-server-<tag>-linux-x64` / `-linux-arm64` vào release (kèm `SHA256SUMS-server.txt`).

## Dùng với rbctl

```bash
rbctl provider add --name NVIDIA --identifier nvidia --base-url https://integrate.api.nvidia.com/v1 --api-key nvapi-...
rbctl model fetch nvidia && rbctl model enable-all nvidia
rbctl combo add ...                         # xem: rbctl combo help
rbctl key create opencode                   # client key cho opencode / tool khác
rbctl opencode write ...                    # ghi provider router-balancing vào opencode.json
rbctl status                                # thấy /health của rb-server
```

- Đổi provider/account/model/combo: **có hiệu lực ngay** (proxy đọc DB mỗi request).
- Đổi **settings** (port, LAN…) hoặc **client key**: khởi động lại `rb-server` (cache RAM, giống app).
- Cổng mặc định `8317` (bản build **Debug**, vd `dotnet run`, mặc định `8417` như app Debug); bận thì tự chọn cổng khác (xem dòng `LISTENING`). Bind `127.0.0.1` trừ khi bật `lanAccess`.
- Dừng: Ctrl+C hoặc SIGTERM.

## Không hỗ trợ

- Sidecar `opencode-proxy` (lane free của opencode.ai) — binary chỉ đóng gói cho Windows, rb-server không dùng.
- Auto-update, tray, khởi động cùng hệ điều hành — chỉ có ở app desktop.
