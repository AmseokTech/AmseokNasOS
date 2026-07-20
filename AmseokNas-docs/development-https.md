# 本地 HTTPS 开发

前端开发服务器默认使用 HTTPS，访问地址为 `https://localhost:6521`。API 继续监听
`http://localhost:5080`，Angular 开发代理负责转发 `/api` 请求，并向 ASP.NET Core
传递 `X-Forwarded-Proto: https`，使认证和 CSRF Cookie 使用 HTTPS 安全属性。

开发环境需要 .NET SDK，以及 Angular 22 支持的 Node.js 版本（推荐 Node.js 24）。

## 首次启动

先启动 API：

```bash
dotnet run --project AmseokNas-server/src/Nas.Api
```

再启动前端：

```bash
cd AmseokNas-web
npm start
```

`npm start` 会调用 .NET SDK 的开发证书工具，自动完成以下操作：

1. 检查系统是否已信任 ASP.NET Core localhost 开发证书。
2. 首次运行时请求系统授权以信任证书。
3. 将证书和私钥导出到 `AmseokNas-web/.certs/`，供 Angular 使用。
4. 启动 `https://localhost:6521`。

`.certs/` 已排除在 Git 之外，不能提交证书私钥。如果系统无法自动建立信任，可先手动执行：

```bash
dotnet dev-certs https --trust
```

仅在排查 HTTPS 本身的问题时，可用 `npm run start:http` 临时退回 HTTP；日常开发应使用
`npm start`，以便尽早发现 Secure Cookie 和 HTTPS 代理相关问题。
