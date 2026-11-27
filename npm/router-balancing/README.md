# @anhbui02/router-balancing

Cài **cả bộ** [Router Balancing](https://github.com/ngocanh0202/router-balancing) bằng một lệnh:

```bash
npm install -g @anhbui02/router-balancing
```

| Lệnh | Việc |
|---|---|
| `rb-server` | Proxy OpenAI-compatible (`/v1`) chạy không giao diện — Linux/macOS/server |
| `rbtui` | Giao diện terminal toàn màn hình: provider, model, combo, client key, OpenCode |
| `rbctl` | Lệnh / script (provider, combo, key, logs, stats, settings, `opencode write`…) |

Cả ba dùng chung DB + khoá: macOS/Linux `~/.config/router-balancing/`, Windows `%AppData%\router-balancing\`.
Binary tự chứa .NET runtime — không cần cài .NET.

Gói này chỉ gom [`@anhbui02/rbctl`](https://www.npmjs.com/package/@anhbui02/rbctl),
[`@anhbui02/rbtui`](https://www.npmjs.com/package/@anhbui02/rbtui) và
[`@anhbui02/rb-server`](https://www.npmjs.com/package/@anhbui02/rb-server) (cùng version). Đã cài riêng từng gói
thì gỡ trước (`npm rm -g @anhbui02/rbctl @anhbui02/rbtui @anhbui02/rb-server`) để tránh trùng lệnh.
