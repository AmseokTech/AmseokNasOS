# AmseokNas Web Terminal 部署与安全边界

状态：已实现且默认关闭，已在 `nastest` 完成独立账户、systemd 沙箱、真实 PTY、自动重启和未登录拒绝验证；仍待使用当前 Web 管理员密码完成登录后的浏览器交互端到端验证

## 1. 边界

Web Terminal 使用独立低权限执行面：

```text
Angular xterm.js
  -> ASP.NET Core WebSocket Gateway
    -> /run/amseoknas-terminal/terminal.sock
      -> amseoknas-terminal-broker
        -> amseoknas-terminal 用户下的固定 shell
```

terminal broker 不属于 `amseoknas-privileged`，不得共享二进制、Unix Socket、Linux 用户、supplementary group 或 systemd unit。终端 shell 不具有 root、sudo、数据库秘密、Data Protection 密钥或 privileged socket 访问权限。

终端会话要求账户已经完成初始密码修改、具有管理员角色，并在每次开启前重新验证当前密码。待连接会话只在 API 进程内保存 30 秒且只能消费一次；第一版不支持 API 多实例间转移或断线重连。

## 2. 安装

构建并安装 broker：

```text
cargo build --manifest-path AmseokNas-terminal/Cargo.toml --release
install -o root -g root -m 0755 AmseokNas-terminal/target/release/amseoknas-terminal-broker /usr/libexec/amseoknas/amseoknas-terminal-broker
```

创建独立系统账户，并只允许 API 用户通过专用组连接 socket：

```text
useradd --system --home-dir /var/lib/amseoknas-terminal --shell /usr/sbin/nologin amseoknas-terminal
usermod -aG amseoknas-terminal amseoknas-api
```

安装 `AmseokNas-deploy/systemd/amseoknas-terminal.service` 后启用服务。API 用户组发生变化后必须重启 API 服务，才能获得新的 supplementary group。

若 API 服务用户不是 `amseoknas-api`，通过受保护的 systemd drop-in 设置：

```text
Environment=AMSEOKNAS_TERMINAL_ALLOWED_USER=<API 服务用户>
```

不能把该值改为运行不可信插件或普通交互进程的共享账户。

## 3. API 配置

终端默认关闭。部署完成后通过运行时环境启用，并逐项登记实际 Web Origin：

```text
Terminal__Enabled=true
Terminal__SocketPath=/run/amseoknas-terminal/terminal.sock
Terminal__AllowedOrigins__0=https://nas.example.internal
Terminal__IdleTimeoutMinutes=15
Terminal__MaximumSessionMinutes=60
```

Origin 必须包含 scheme、host 和非默认端口，不接受通配符。Nginx HTTPS server block 需要包含 `AmseokNas-deploy/nginx/web-terminal.conf` 中的 WebSocket location。

## 4. 验证

启用前必须在专用测试机确认：

- 未改密会话和非管理员无法创建终端会话
- 错误重新认证受到限速且不创建会话
- 不在允许列表中的 Origin 无法升级 WebSocket
- 待连接会话过期、跨用户使用或第二次使用时被拒绝
- terminal 用户不能读取 API 环境、数据库凭据、Data Protection 密钥和 SSH 私钥
- terminal 用户不能连接 `/run/amseoknas/privileged.sock`
- 浏览器断开、空闲超时、最长时限和 broker 重启都会终止 shell 进程
- Nginx 不缓冲终端数据，并且不会把认证信息记录到 URL

终端输入输出默认不进入审计日志。API 只记录用户、会话、来源地址、持续时间、结束原因和输入输出字节数，避免密码、Token 和私钥被终端录像泄露。
