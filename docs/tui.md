# rbtui — giao diện terminal cho Router Balancing

`rbtui` là giao diện **toàn màn hình trong terminal** (giống opencode/htop) để quản lý Router Balancing
mà không phải nhớ cú pháp `rbctl`. Dùng **chung DB + khoá** với `rbctl` và `rb-server`
(macOS/Linux: `~/.config/router-balancing`, Windows: `%AppData%\router-balancing`), qua đúng các
service Core nên luật validate giống app/rbctl.

Project: [`src/RouterBalancing.Tui`](../src/RouterBalancing.Tui) — Terminal.Gui v1; link `CliServices.cs` +
`FileKeySecretProtector.cs` của rbctl (không chép). Không nằm trong `router-balancing.slnx`.

## Chạy

```bash
dotnet run --project src/RouterBalancing.Tui            # hoặc: rbtui [--db <file>]
```

Release: [`release-tui.yml`](../.github/workflows/release-tui.yml) đính kèm `rbtui-<tag>-linux-x64` / `-linux-arm64`.

## Phím

| Mục | Phím |
|---|---|
| Chung | `Tab` đổi khung · `↑↓` chọn · `Enter` mở/sửa · `F5` tải lại · `F2` sáng/tối (mặc định tối, nhớ lựa chọn) · `F1` trợ giúp · `Ctrl+Q` thoát |
| Providers | `a` thêm · `Space` bật/tắt · `Enter` xem model · `f` lấy model · `t` test · `k` đổi API key · `Del` xoá |
| Models | `Space` bật/tắt · `e` bật hết · `n` tắt hết · `p` chọn provider · `f` lấy lại · `+` thêm tay |
| Combos | `a` thêm · `Enter` sửa (`Space` chọn model theo thứ tự fallback) · `m` đổi Fallback/Round-robin · `Del` xoá |
| Client keys | `c` tạo (key hiện 1 lần) · `Space` bật/tắt · `Del` xoá |
| OpenCode | `Space` chọn model/combo · `w` ghi `opencode.json` · `r` ghi + khởi động lại opencode (tmux `main:opencode`) |
| Status | trạng thái proxy (`/health`), DB, số lượng |

Ghi chú: đổi provider/model/combo có hiệu lực ngay với rb-server/app; đổi client key/settings cần khởi động lại
rb-server (hoặc app). Ở tab OpenCode, client key lấy từ `~/vm/rb.key` nếu có (notebook Kaggle VM), không thì hỏi.
