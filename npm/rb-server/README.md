# @anhbui02/rb-server

Proxy Router Balancing chạy không giao diện (OpenAI-compatible /v1) cho Linux/macOS/server — dùng chung DB với rbctl.

```bash
npm install -g @anhbui02/rb-server
rb-server                 # chạy proxy, in "LISTENING <port>" khi sẵn sàng
rb-server --db <file>     # DB khác (mặc định giống rbctl)
```

Cùng bộ với [`@anhbui02/rbctl`](https://www.npmjs.com/package/@anhbui02/rbctl) — cả ba dùng chung DB + khoá:
macOS/Linux `~/.config/router-balancing/`, Windows `%AppData%\router-balancing\`.

```bash
npm install -g @anhbui02/rbctl @anhbui02/rbtui @anhbui02/rb-server
```

Binary tự chứa .NET runtime (không cần cài .NET). Hỗ trợ Windows (x64, ARM64), macOS (Intel, Apple Silicon),
Linux glibc (x64, ARM64). Tài liệu: [docs/server.md](https://github.com/ngocanh0202/router-balancing/blob/master/docs/server.md).
