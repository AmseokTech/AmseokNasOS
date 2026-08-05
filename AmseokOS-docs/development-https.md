# 局域网 HTTPS 开发

前端开发服务器监听所有本机网络接口，并使用脚本自动签发的局域网证书。启动时会打印实际访问地址，
例如 `https://192.168.1.8:6521`。API 继续监听 `http://localhost:5080`，Angular 开发代理
负责转发 `/api` 请求，并向 ASP.NET Core 传递 `X-Forwarded-Proto: https`，使认证和 CSRF
Cookie 使用 HTTPS 安全属性。

开发环境需要 OpenSSL，以及 Angular 22 支持的 Node.js 版本（推荐 Node.js 24）。

## 启动

先启动 API：

```bash
dotnet run --project AmseokOS-server/src/Nas.Api
```

再启动前端：

```bash
cd AmseokOS-web
npm start
```

`npm start` 会自动完成以下操作：

1. 探测开发机所有 RFC1918 局域网 IPv4 地址和主机名。
2. 首次启动时在 `AmseokOS-web/.certs/` 创建开发 CA。
3. 签发包含所有探测地址的服务器证书，地址变化或证书临近过期时自动续签。
4. 以 HTTPS 启动 Angular，并打印全部局域网访问地址。

如需加入自动探测不到的固定域名或 IP，可使用逗号分隔：

```bash
AMSEOK_DEV_HOSTS=nas.dev.lan,192.168.1.20 npm start
```

## 让其他设备信任证书

局域网中的手机或其他电脑必须安装并信任以下根证书，浏览器才不会显示证书警告：

```text
AmseokOS-web/.certs/amseok-dev-ca.crt
```

只分发 `.crt` 文件。不得复制或分发 `.key` 文件；CA 私钥可以签发受信任证书，必须只保留在开发机。
各客户端只需安装一次 CA，后续服务器证书续签不需要重复安装。

`.certs/` 已排除在 Git 之外，证书和私钥不会提交到仓库。仅在排查 HTTPS 本身的问题时，
可用 `npm run start:http` 临时退回局域网 HTTP。
