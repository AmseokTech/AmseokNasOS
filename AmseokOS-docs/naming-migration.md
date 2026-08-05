# AmseokOS 命名迁移

仓库采用分阶段迁移，避免目录品牌调整同时破坏已部署系统的认证 Cookie、Unix Socket、systemd unit、Linux 账户和 Debian 包升级关系。

## 第一阶段：仓库层名称

以下名称已经统一：

- 顶层模块使用 `AmseokOS-<用途>`
- .NET 解决方案使用 `AmseokOS.sln`
- Web 工程包标识使用 `amseok-os-web`
- CI、构建脚本和开发文档使用新的仓库路径
- 用户可见标题和 systemd 描述使用 `AmseokOS`

新增仓库目录、文档标题和用户可见品牌不得继续使用旧品牌名称。

## 暂时保留的兼容标识

以下名称已被外部系统、持久化数据或部署流程使用，本阶段不直接重命名：

- C# 的 `Nas.*` 项目、程序集和命名空间
- `amseoknas-*` Debian 包、二进制、systemd unit、Linux 账户和 Unix Socket
- `AmseokNas.Auth` 与 `AmseokNas.Antiforgery` Cookie 名称
- 已存在的管理员初始化凭据兼容行为

这些标识必须在独立迁移中处理。迁移需要同时定义旧名称兼容期、数据和配置迁移、服务切换顺序、回退方案以及升级测试，不能通过全仓字符串替换完成。

## 后续阶段

1. 先为运行时路径和服务名建立新旧名称兼容矩阵
2. 设计 Cookie 双读、重新签发和会话失效策略
3. 为 systemd、Linux 账户、Socket 和 Debian 包提供升级迁移脚本
4. 再评估是否将 `Nas.*` 程序集与命名空间迁移为新的稳定技术名称
5. 在全新安装、原地升级和回滚场景验证后移除旧名称
