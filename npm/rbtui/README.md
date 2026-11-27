# @anhbui02/rbtui

Giao diện terminal toàn màn hình cho Router Balancing — provider, model, combo, client key, OpenCode (Windows, macOS, Linux).

```bash
npm install -g @anhbui02/rbtui
rbtui                     # mở giao diện (dùng chung DB với rbctl/rb-server)
rbtui --db <file>         # DB khác
```

Cùng bộ với [`@anhbui02/rbctl`](https://www.npmjs.com/package/@anhbui02/rbctl) — cả ba dùng chung DB + khoá:
macOS/Linux `~/.config/router-balancing/`, Windows `%AppData%\router-balancing\`.

```bash
npm install -g @anhbui02/rbctl @anhbui02/rbtui @anhbui02/rb-server
```

Binary tự chứa .NET runtime (không cần cài .NET). Hỗ trợ Windows (x64, ARM64), macOS (Intel, Apple Silicon),
Linux glibc (x64, ARM64). Tài liệu: [docs/tui.md](https://github.com/ngocanh0202/router-balancing/blob/master/docs/tui.md).
