# AmseokOS 数据库架构设计

状态：已选定，单节点持久化基础已实现

最后确认日期：2026-07-17

## 1. 设计目标

AmseokOS 采用对等 NAS 节点和动态控制面 Leader。所有 NAS 安装相同程序，可以作为存储节点，也可以在满足资格和仲裁条件后成为控制面节点。

数据库架构固定为：

```text
节点本地状态：SQLite
集群全局状态：PostgreSQL HA
Leader 选举与租约：etcd
节点命令与事件：NATS JetStream
Web 身份组件：ASP.NET Core Identity
Cookie 与临时令牌保护：ASP.NET Core Data Protection
```

数据库只保存管理面元数据，不保存 NAS 用户文件。SQLite、PostgreSQL、etcd 和 NATS 的数据目录都不得放在普通共享目录或可由非管理用户写入的路径。

## 2. 数据所有权

全局期望状态和节点实际状态必须分开保存，不能让 PostgreSQL 与 SQLite 同时成为同一记录的可写事实源。

### 2.1 PostgreSQL 全局事实源

PostgreSQL 保存：

- 集群、节点注册信息和控制面资格
- Web 用户、角色、权限和账户安全状态
- 集群会话、会话撤销和登录失败状态
- 全局期望配置、配置版本和策略
- Operation 的创建、分配、操作者、目标节点和全局状态视图
- 全局幂等记录、Leader 任期、fencing token 引用和任务归属
- 审计索引、告警和节点上报事件的汇总视图
- ASP.NET Core Data Protection 密钥环的受保护持久化数据

PostgreSQL 不直接声明磁盘当前路径、挂载状态或命令已经成功；这些实际状态必须由目标节点观察并上报。

### 2.2 SQLite 节点事实源

每台 NAS 使用独立 SQLite 数据库，保存：

- 本机稳定设备标识及最近一次观测状态
- 本机文件系统、阵列、挂载和服务的实际状态快照
- 本机 Operation 执行状态、阶段、资源锁和恢复检查点
- 已消费命令、幂等键和 fencing token
- 待发送的 Outbox 事件及已接收命令的 Inbox 去重记录
- 本地审计事件和受长度限制的诊断摘要
- 短期授权快照及其全局权限版本号

SQLite 不保存整个集群的密码哈希、长期会话或全局权限事实源。节点无法访问 PostgreSQL 时，不允许使用过期授权快照开始危险操作。

## 3. 账户、密码与权限

账户和权限使用 ASP.NET Core Identity 建立在 PostgreSQL 上。Identity 负责用户生命周期、密码哈希验证、安全戳、锁定和令牌基础能力；业务权限仍由 AmseokOS 的明确权限点控制。

建议关系：

```text
Users
  -> UserRoles
    -> Roles
      -> RolePermissions
        -> Permissions

Users
  -> Sessions
  -> LoginAttempts
  -> MfaCredentials
  -> PasswordHistory（启用历史策略时）
```

### 3.1 密码边界

数据库只能保存成熟密码哈希组件生成的哈希和算法参数，不保存明文密码或可逆加密密码。

必须遵守：

- 使用 ASP.NET Core Identity `IPasswordHasher` 生成和验证密码哈希
- 每个密码使用组件生成的独立随机盐
- 不使用 MD5、SHA-1 或直接 SHA-256 保存密码
- 若增加 pepper，必须保存在数据库之外的集群秘密存储中
- API Token、恢复码和一次性凭据只保存哈希
- TOTP 密钥等必须恢复的秘密使用应用层加密，主密钥不与密文保存在同一数据库
- 日志、审计和异常中不得记录密码、哈希、令牌或 MFA 密钥

### 3.2 角色与权限

初始角色为 `admin`、`storage-manager`、`user-manager`、`app-manager` 和 `viewer`。角色是权限集合，后端必须按权限点授权，不能只判断角色名称。

权限修改必须：

- 增加全局权限版本号
- 使受影响用户的安全戳或授权版本失效
- 撤销不再满足权限要求的活跃会话
- 写入不可由普通用户修改的审计事件
- 使节点本地旧授权快照不能执行写操作或危险操作

## 4. Web 会话和多控制面实例

Web 使用安全 Cookie 会话。Cookie 设置 `HttpOnly`、`Secure` 和明确的 `SameSite`，所有状态修改请求实施 CSRF 防护。

所有控制面实例必须共享同一 ASP.NET Core Data Protection 应用名称和密钥环，才能在 Leader 切换或请求落到其他 NAS 后继续验证 Cookie。Data Protection 密钥环可以持久化到 PostgreSQL，但必须再使用数据库之外的证书或主密钥进行静态加密保护。

会话表至少保存会话 ID 的哈希、用户 ID、创建与过期时间、最近活动时间、安全戳版本、撤销时间和必要的客户端摘要。不得保存原始会话令牌。

高危操作必须重新认证，重新认证结果使用短期、一次性、绑定用户、Operation、目标资源和参数摘要的确认记录，不能仅依赖已有 Cookie。

## 5. 集群一致性与防脑裂

etcd 负责控制面 Leader 选举、成员租约和单调递增的 fencing token。PostgreSQL 保存业务引用和历史，不替代 etcd 的仲裁职责。

危险命令必须同时携带：

- `ClusterId`
- `NodeId`
- `OperationId`
- 幂等键
- Leader 任期或租约标识
- fencing token
- 目标资源稳定 ID
- 参数摘要和期望版本

节点执行前必须校验 fencing token 新于本机已接受值。旧 Leader 即使恢复网络，也不能凭旧租约继续写入或执行危险操作。

两节点集群没有多数派容错能力，不启用无人值守的自动故障切换。自动选举使用 3 个或 5 个投票成员，普通存储节点不必全部成为投票成员。

## 6. PostgreSQL 与 SQLite 之间的同步

两个数据库之间不使用分布式事务。

推荐流程：

```text
PostgreSQL 创建并提交 Operation
  -> Outbox 发布 NATS 命令
    -> 目标节点 Inbox 去重并持久化
      -> SQLite 记录执行阶段和本地资源锁
        -> Outbox 发布执行事件
          -> PostgreSQL 更新全局 Operation 视图
```

NATS JetStream 提供持久消息、确认和重投递，但业务仍按至少一次投递设计。每个消费者必须幂等，不能把消息成功发送等同于底层操作成功。

## 7. 数据库上下文与迁移边界

后端至少拆分两个持久化上下文：

```text
ClusterDbContext
  -> PostgreSQL
  -> Identity、全局配置、全局 Operation、审计索引

NodeDbContext
  -> SQLite
  -> 节点实际状态、本地 Operation、锁、Inbox、Outbox
```

两个上下文使用独立迁移历史、备份、恢复和健康检查。Domain 与 Application 只依赖仓储和事务边界接口，不直接依赖 EF Core、Npgsql 或 SQLite 类型。

所有跨节点主键使用全局唯一 ID。所有带更新竞争的数据使用显式版本号；不得依赖数据库自增 ID、设备易变路径或时间戳作为唯一并发判断。

## 8. 部署模式

### 8.1 单节点开发与首次部署

一台 NAS 同时运行节点服务和控制面 Leader：

- 一个本地 SQLite 文件
- 一个 PostgreSQL 实例
- 一个 etcd 成员
- 一个启用 JetStream 的 NATS 实例

单成员模式没有高可用能力，但必须使用与集群模式相同的协议、ID、租约和数据边界，避免扩容时重写业务模型。

### 8.2 高可用集群

高可用模式至少使用 3 台符合控制面资格的 NAS：

- PostgreSQL 一个可写主库和至少两个可提升副本
- etcd 3 个投票成员
- NATS JetStream 3 个副本节点
- 控制面 API 可运行在多个 NAS，写入编排受当前 Leader 和 fencing token 约束

任何 NAS 都可以在完成资格检查、数据同步和成员变更后成为控制面节点，但不能未经仲裁自行宣称为 Leader。

## 9. 备份与恢复

必须分别备份：

- PostgreSQL 全局数据库及迁移版本
- 每台节点 SQLite 数据库及 WAL 一致性快照
- etcd 成员与集群快照
- NATS JetStream 必须保留的流和消费者状态
- Data Protection 密钥环及其外部解密材料
- TOTP 等应用层加密数据对应的外部主密钥

备份和密钥不得只保存在同一 NAS 或同一阵列。恢复演练必须验证账户登录、权限撤销、Leader 切换、Operation 对账和旧 fencing token 被拒绝。

## 10. 当前实施边界

当前已经实现：

- `ClusterDbContext` 的 PostgreSQL 模型和初始迁移，包括 ASP.NET Core Identity 表、权限点、节点注册、全局 Operation、审计索引和 Data Protection 密钥表
- 固定初始管理员的 Identity 密码哈希、`MustChangePassword` 状态、管理员角色与现有全部权限种子；数据库和运行时配置不保存初始密码明文
- Cookie 登录、CSRF、失败锁定、登录限速、会话查询、退出和强制修改密码 API；修改成功后新哈希覆盖初始哈希并撤销临时会话
- Angular 登录和强制改密页面，包含提交、失败、复杂度校验和修改成功状态
- `NodeDbContext` 的 SQLite 模型和初始迁移，包括节点状态、本地 Operation、资源锁、Inbox 和 Outbox
- 两个数据库的启动迁移开关、独立健康检查，以及 SQLite 文件权限、WAL 和完整性检查
- PostgreSQL、单成员 etcd、NATS JetStream 的单节点 Compose 配置和部署说明
- etcd HTTP 健康验证，以及 NATS JetStream 创建流、发布消息和文件持久化验证

当前尚未实现或验证：

- 当前环境没有 Docker，Compose 未实际启动，PostgreSQL 初始迁移尚未对真实服务执行
- 认证迁移尚未在真实 PostgreSQL 执行，浏览器 Cookie、CSRF、锁定和改密闭环尚未进行真实服务集成验证
- Data Protection 密钥环尚未接入数据库之外的证书或主密钥保护，多控制面共享 Cookie 尚未验证
- etcd 尚未接入应用内 Leader 租约、成员资格和 fencing token
- NATS JetStream 尚未接入应用内命令发布、Inbox/Outbox 工作者和幂等消费
- PostgreSQL HA、三成员 etcd、三副本 JetStream、备份恢复、故障切换和脑裂测试尚未完成

后续仍按小步交付：

1. 补齐 `ClusterId`、`NodeId`、Operation、身份权限和持久化事务边界契约
2. 在具备 Docker 的目标机启动单节点基础设施并执行 PostgreSQL 迁移
3. 在真实 PostgreSQL 和浏览器环境验证 Identity、Cookie 与强制改密，并接入 Data Protection 外部密钥保护
4. 接入 etcd 单成员租约、Leader 身份和 fencing token
5. 接入 NATS JetStream、Inbox、Outbox 和幂等消费
6. 在破坏性存储操作开放前完成三节点仲裁、故障切换和脑裂测试
