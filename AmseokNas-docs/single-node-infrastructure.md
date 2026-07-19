# AmseokNas 单节点基础设施部署

本配置用于第一阶段单节点开发和首次部署，不代表 PostgreSQL、etcd 或 NATS 已具备高可用能力。

## 前置条件

- Docker Engine
- Docker Compose v2
- 不与其他服务冲突的本机 `5432`、`2379`、`4222` 和 `8222` 端口

## 配置与启动

复制 `AmseokNas-deploy/.env.example` 为不提交的 `AmseokNas-deploy/.env`，为 `POSTGRES_PASSWORD` 生成随机密码。使用 `nats server passwd` 为 NATS 客户端密码生成 bcrypt 哈希，将哈希以单引号包围后写入 `NATS_PASSWORD_HASH`，再执行：

```bash
docker compose \
  --env-file AmseokNas-deploy/.env \
  -f AmseokNas-deploy/compose.single-node.yaml \
  up -d
```

API 通过环境变量读取 PostgreSQL 密码，示例结构如下，不能把真实值写入仓库：

```text
ConnectionStrings__ClusterDatabase=Host=127.0.0.1;Port=5432;Database=amseoknas;Username=amseoknas;Password=<运行时秘密>
ConnectionStrings__NodeDatabase=Data Source=/var/lib/amseoknas/amseoknas-node.db;Foreign Keys=True
Persistence__ApplyMigrationsOnStartup=true
```

NATS 客户端使用生成哈希前的原始密码连接。原始密码必须进入独立运行时秘密，不得写入 NATS 服务端配置、Compose 文件或仓库。

## 初始管理员

首次应用认证迁移后，Web 管理入口使用以下一次性初始凭据：

```text
账户：admin
密码：AmseokNas
```

初始密码只用于首次登录。登录后只能访问会话查询、修改密码和退出接口；设置满足复杂度要求的新密码后，新 Identity 哈希会覆盖初始哈希，初始密码立即失效，并要求使用新密码重新登录。

首次迁移完成后应关闭 `Persistence__ApplyMigrationsOnStartup`，由受控升级流程执行后续迁移，避免多个控制面实例并发迁移。

## 健康检查

```bash
docker compose \
  --env-file AmseokNas-deploy/.env \
  -f AmseokNas-deploy/compose.single-node.yaml \
  ps
```

API 提供：

- `/health/live`：仅检查 API 进程存活
- `/health/ready`：检查 PostgreSQL、SQLite、etcd 和 NATS JetStream

## 安全边界

Compose 只把基础设施端口绑定到 `127.0.0.1`。etcd 单成员配置未启用 TLS 和客户端认证，只允许用于同机单节点阶段；扩展到多 NAS 前必须配置双向 TLS、三或五成员仲裁、独立证书和备份恢复流程。
