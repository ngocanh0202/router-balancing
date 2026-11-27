# Gói npm: rbtui + rb-server

Cùng cách đóng gói với [`@anhbui02/rbctl`](cli.md#phát-hành-npm): mỗi tool = **gói chính** (launcher Node)
+ **6 gói nền tảng** chứa binary self-contained (npm chỉ cài gói khớp `os`/`cpu`/`libc`, không `postinstall`).

```bash
npm install -g @anhbui02/router-balancing      # gói gộp: rbctl + rbtui + rb-server
# hoặc từng gói: npm install -g @anhbui02/rbctl @anhbui02/rbtui @anhbui02/rb-server
rb-server &        # proxy (OpenAI-compatible /v1)
rbtui              # giao diện quản lý
rbctl status       # lệnh / script
```

Cả ba dùng chung DB + khoá (macOS/Linux `~/.config/router-balancing`, Windows `%AppData%\router-balancing`).

| Thư mục | Gói |
|---|---|
| `npm/rbtui`, `npm/rbtui-<os>-<cpu>` | `@anhbui02/rbtui` + 6 gói nền tảng |
| `npm/rb-server`, `npm/rb-server-<os>-<cpu>` | `@anhbui02/rb-server` + 6 gói nền tảng |
| `npm/router-balancing` | `@anhbui02/router-balancing` — gói gộp: `dependencies` = 3 gói trên (cùng version), tự khai 3 lệnh trong `cmd/` (npm không link bin của dependency khi cài `-g`) |

- Launcher (`launcher.js`) dùng `spawn` và **chuyển tiếp SIGINT/SIGTERM/SIGHUP** cho binary — `rb-server`
  dừng gọn khi chạy dưới tmux/systemd/docker.
- Chạy với bản build local: `RBTUI_BINARY_PATH=/đường/dẫn/rbtui`, `RB_SERVER_BINARY_PATH=…`.
- Test launcher: `cd npm/rbtui && npm test` (tương tự `npm/rb-server`).
- Version: [`npm/scripts/set-version-tool.mjs`](../npm/scripts/set-version-tool.mjs) `<rbtui|rb-server> <version>`
  — trong repo để `0.0.0-dev`.

## CI/CD — [`npm-tools.yml`](../.github/workflows/npm-tools.yml)

- PR/push đụng `npm/rbtui*`, `npm/rb-server*`: `npm test` + `npm pack --dry-run`.
- Release published (hoặc chạy tay, mặc định dry-run): build 6 nền tảng mỗi tool bằng
  [`tool-build.yml`](../.github/workflows/tool-build.yml) → publish gói nền tảng rồi gói chính bằng
  **Trusted Publishing (OIDC)**. Prerelease → dist-tag `next`. Version đã có thì bỏ qua.

## Publish lần đầu (một lần cho mỗi tool)

Trusted publisher chỉ gắn được vào gói **đã tồn tại** → publish tay từ máy Windows (.NET 10 SDK + Node):

```powershell
powershell -ExecutionPolicy Bypass -File npm/scripts/publish-first-tool.ps1 -Tool rbtui -DryRun
powershell -ExecutionPolicy Bypass -File npm/scripts/publish-first-tool.ps1 -Tool rbtui
powershell -ExecutionPolicy Bypass -File npm/scripts/publish-first-tool.ps1 -Tool rb-server
# gói gộp — SAU khi rbctl, rbtui, rb-server đã có cùng version trên npm
powershell -ExecutionPolicy Bypass -File npm/scripts/publish-first-tool.ps1 -Tool router-balancing
```

Sau đó trên npmjs.com, với **từng** gói `@anhbui02/rbtui*` và `@anhbui02/rb-server*` và `@anhbui02/router-balancing` (15 gói):
Settings → Trusted Publisher → GitHub Actions — owner `ngocanh0202`, repo `router-balancing`,
workflow **`npm-tools.yml`**, environment `npm` (environment `npm` trên GitHub đã tạo cho rbctl thì dùng chung).
