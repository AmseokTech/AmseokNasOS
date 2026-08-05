# AmseokOS

**让数据与 AI 留在本地。**

*Keep your data and AI local.*

**统一管理私有数据、本地 AI、应用服务与计算资源。**

AmseokOS 是一个以 Debian 为底座的本地智能基础设施操作系统，通过统一的 Web 桌面管理私有存储、本地 AI、应用服务、网络与计算资源。

项目使用 Angular 构建管理界面，以 ASP.NET Core 承载认证、授权和业务编排，并通过受限的 Rust 进程隔离系统查询、终端和后续特权操作。

> [!WARNING]
> AmseokOS 当前处于第一阶段开发中，仅适合开发和安全验证。图形安装器不会写入真实磁盘，存储、RAID 和网络写操作尚未开放，请勿用于承载生产数据。

## 产品定位

AmseokOS 面向希望在自有设备上掌控数据、算力和服务的个人与团队，产品能力围绕以下方向展开：

- **私有数据**：统一管理磁盘、RAID、文件系统、共享、备份与恢复
- **本地 AI**：在本地设备上部署和管理 AI 能力，让模型与数据留在自己的基础设施中
- **应用服务**：为本地应用、容器和自动化服务提供统一入口
- **计算资源**：管理主机、网络、设备、运行状态与后续集群能力

## 当前状态

项目目前仍在建立安全底座，各能力的成熟度并不相同：

| 能力 | 当前状态 |
| --- | --- |
| Web 管理桌面 | 已建立登录、强制改密、桌面 Shell、Dock 和多窗口管理 |
| 系统与网络信息 | 已建立只读查询链路；底层 Rust daemon 默认关闭，仍需完成目标机端到端验证 |
| 磁盘与 MD RAID | 已建立只读 API 和多层设备拓扑保护；前端磁盘列表、SMART 与真实设备验证尚未完成 |
| 网络配置 | 已建立安全预览、确认和回滚接口边界；实际写入执行器保持关闭 |
| Web Terminal | 已建立独立低权限 broker、重新认证和一次性会话边界；默认关闭 |
| 本地 AI | 已纳入核心产品定位；具体支持范围、部署方式和硬件兼容说明将随对应模块文档补充 |
| 图形安装器 | 已建立 Qt/QML 界面、Debian 打包和 `live-build` 骨架；真实磁盘探测与安装执行保持关闭 |
| 集群基础设施 | 已建立 PostgreSQL、SQLite、etcd 和 NATS 的数据与协议边界；高可用与应用内集群协调尚未完成 |

当前阶段不会开放任何破坏性磁盘操作。未经预检、重新认证、稳定设备身份复核、系统盘保护、资源锁、审计和失败恢复验证的写入能力，不应进入可用状态。

## 架构概览

```text
浏览器
  -> Angular Web 桌面
    -> ASP.NET Core 控制面
      -> PostgreSQL / SQLite
      -> etcd / NATS JetStream
      -> Unix Domain Socket
        -> Rust privileged daemon（受限系统查询与特权动作）
        -> Rust terminal broker（独立低权限终端）
      -> 本地 AI、应用与系统服务
```

控制面不应长期以 root 身份运行。所有系统级能力必须经过明确的权限检查和结构化接口，不接受任意 shell、程序路径或未经约束的参数。

## 仓库结构

| 目录 | 职责 |
| --- | --- |
| `AmseokOS-web/` | Angular Web 桌面、认证界面、系统设置和窗口管理 |
| `AmseokOS-server/` | ASP.NET Core API、Application 用例、Domain 模型和 Infrastructure 适配器 |
| `AmseokOS-privileged/` | Rust 特权边界和只读系统、网络、磁盘与 RAID 查询 |
| `AmseokOS-terminal/` | 与 privileged daemon 隔离的低权限 PTY broker |
| `AmseokOS-installer/` | Qt/QML 图形安装器、Debian source package 和 Live ISO 配置 |
| `AmseokOS-deploy/` | 单节点 Compose、Nginx、systemd 和本地控制台部署配置 |
| `AmseokOS-docs/` | 数据库、特权边界、安装器和部署等专题文档 |
| `agent.md` | 项目架构约定、阶段计划、质量要求和协作规则 |

## 开发环境

当前 CI 使用或验证的主要工具版本如下：

- Node.js `22.22.3` 与 npm
- .NET SDK `10.0.302`
- Rust `1.97.1`
- Docker Engine 与 Docker Compose v2
- 安装器开发需要 CMake、Ninja、Qt `6.4+`、clang-format 和 clang-tidy

### 启动单节点基础设施

先按照[单节点基础设施部署说明](AmseokOS-docs/single-node-infrastructure.md)生成本地运行时密码并配置未纳入版本控制的 `.env`，然后执行：

```bash
docker compose \
  --env-file AmseokOS-deploy/.env \
  -f AmseokOS-deploy/compose.single-node.yaml \
  up -d
```

不得把 PostgreSQL、NATS、证书私钥或其他运行时秘密写入仓库。

### 启动 API 与 Web

配置数据库连接和首次迁移策略后，在仓库根目录启动 API：

```bash
dotnet run --project AmseokOS-server/src/Nas.Api
```

在另一个终端安装前端依赖并启动局域网 HTTPS 开发服务：

```bash
cd AmseokOS-web
npm ci
npm start
```

证书生成、局域网访问和客户端信任方式见[局域网 HTTPS 开发说明](AmseokOS-docs/development-https.md)。

## 验证

各模块的主要质量门禁如下：

```bash
# Angular
cd AmseokOS-web
npm run lint
npm run test:ci
npm run build

# ASP.NET Core
cd ../AmseokOS-server
dotnet build AmseokOS.sln --configuration Release --warnaserror
dotnet test tests/Nas.Api.Tests/Nas.Api.Tests.csproj --configuration Release

# Rust privileged daemon
cd ../AmseokOS-privileged
cargo +1.97.1 fmt --check
cargo +1.97.1 clippy --all-targets --all-features -- -D warnings
cargo +1.97.1 test --locked --all-features

# Rust terminal broker
cd ../AmseokOS-terminal
cargo +1.97.1 fmt --check
cargo +1.97.1 clippy --all-targets --all-features -- -D warnings
cargo +1.97.1 test --locked --all-features
```

安装器的构建、实时预览、QML lint、C++ 静态分析和测试命令见[安装器开发说明](AmseokOS-installer/README.md)。完整门禁以 [GitHub Actions CI](.github/workflows/ci.yml) 为准。

## 文档

- [C# 与 Rust 特权执行架构](AmseokOS-docs/csharp-rust-privileged-architecture.md)
- [数据库架构](AmseokOS-docs/database-architecture.md)
- [Debian 镜像与图形安装器架构](AmseokOS-docs/debian-image-installer-architecture.md)
- [单节点基础设施部署](AmseokOS-docs/single-node-infrastructure.md)
- [局域网 HTTPS 开发](AmseokOS-docs/development-https.md)
- [Web Terminal 部署与安全边界](AmseokOS-docs/web-terminal.md)
- [本地控制台状态页](AmseokOS-deploy/console/README.md)

## 路线方向

1. 完成认证、数据保护、只读设备查询、Operation、资源锁和单节点协调等安全底座
2. 打通 `mdadm + ext4 + SMB` 的存储纵向闭环
3. 增加故障恢复、告警、备份与恢复演练
4. 在统一权限、审计和资源边界上扩展本地 AI、应用服务、容器与集群能力

详细阶段约束与当前验证记录见 [`agent.md`](agent.md)。项目实际状态始终以可运行代码和最新验证结果为准。

## 贡献

提交修改前请先阅读 [`AGENTS.md`](AGENTS.md) 和 [`agent.md`](agent.md)，遵守模块依赖方向、安全边界、测试和人工审查要求。不要在缺少完整预检、回滚和验证的情况下开放系统写入或危险操作。
