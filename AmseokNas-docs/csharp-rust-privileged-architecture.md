# AmseokNas C# 与 Rust 特权执行架构

状态：第一阶段只读查询已开始实现；关于本机、物理网卡、物理块设备和现有 MD RAID 阵列查询代码已完成，写操作尚未开放

最后确认日期：2026-08-03

## 1. 设计结论

AmseokNas 使用 C# 控制面与独立 Rust 特权执行面：

```text
浏览器
  -> Angular Web UI
    -> REST API / SignalR
      -> ASP.NET Core / C# 控制面（普通 Linux 用户）
        -> PostgreSQL、SQLite、etcd、NATS JetStream
        -> Unix Domain Socket
          -> Rust privileged daemon（受限特权）
            -> Debian 系统工具、配置和服务
```

C# 负责判断“谁可以在什么条件下发起什么操作”，Rust 负责判断“当前节点是否仍能安全执行该操作，以及如何执行”。Debian 内核和经过选定的系统工具负责真正完成分区、RAID、文件系统、挂载和服务管理。

Rust daemon 是独立进程，不作为动态库加载到 ASP.NET Core 中。C# 与 Rust 不使用 P/Invoke、FFI 或进程内共享内存，以避免 Web API 与 root 权限、原生崩溃和发布生命周期耦合。

## 2. 设计目标

- ASP.NET Core 始终以普通 Linux 用户运行，不能直接执行任意系统命令
- root 或其他特权能力仅存在于最小化的 Rust daemon 中
- 特权接口只表达固定、强类型动作，不提供任意 shell 或通用命令执行
- C# 业务预检和 Rust 执行前复核必须同时通过
- 设备路径变化、服务重启、消息重投和进程崩溃不能导致操作落到错误设备或自动重复破坏性步骤
- 所有操作具有稳定 ID、幂等键、fencing token、超时、输出限制、结构化错误和审计关联
- 第一阶段只实现只读查询和受控测试动作，破坏性动作延后到第二阶段

本设计不能保证系统调用永不失败。可靠性目标是允许操作以明确结果失败，但失败不得绕过权限、选错设备、静默丢失状态或自动重复不可逆动作。

## 3. 职责边界

### 3.1 C# 控制面

C# 继续承担完整控制面，而不是只负责数据库：

- HTTP API、SignalR、认证入口和协议转换
- ASP.NET Core Identity、Cookie、CSRF、重新认证和权限判断
- 危险操作预览、确认短语和短期一次性确认令牌
- `Operation` 状态机、进度、取消规则、资源锁和人工恢复流程
- PostgreSQL 全局期望状态、SQLite 节点执行记录和实际状态快照
- etcd Leader 租约、fencing token 和 NATS JetStream 节点调度
- 幂等请求、并发版本、审计和结构化日志
- 受管配置的期望内容生成和版本管理
- 通过按用例拆分的 Application 客户端端口调用 Rust daemon
- 将 Rust 的结构化结果映射为领域结果、持久化状态和前端通知
- 服务重启后根据数据库记录和节点真实状态进行对账

C# Controller 不得直接使用 `Process`、写系统配置或访问调用方提供的设备路径。系统能力应先进入 Application 用例；`ISystemSettingsClient`、`IStorageInventoryClient` 等按用例细分的端口由 Application 定义，Infrastructure 提供 Unix Socket 实现。

### 3.2 Rust 特权执行面

Rust daemon 只承担当前 Debian 节点的系统观察、安全复核和受限执行：

- 监听本机 Unix Domain Socket
- 校验 socket 文件权限和连接方 peer credentials
- 校验协议版本、消息尺寸、字段、动作和参数
- 解析稳定设备 ID，并重新确认当前设备路径、序列号、WWN、容量和块设备拓扑
- 识别根文件系统、`/boot`、EFI、管理数据目录、swap、挂载、RAID、LUKS 和其他占用关系
- 拒绝旧 fencing token、目标身份变化和不满足前置条件的动作
- 使用固定绝对路径、固定动作定义和参数数组调用系统工具
- 为每个动作实施独立超时、取消策略和输出上限
- 解析结构化输出，执行后重新查询真实状态
- 对配置执行临时文件、校验、备份、原子替换、reload、健康检查和失败回滚
- 返回结构化结果、稳定错误码和经过截断、脱敏的诊断摘要

Rust daemon 不负责：

- Web 用户登录、Cookie 或角色权限
- HTTP API、页面业务和 SignalR
- PostgreSQL 全局业务数据和集群调度
- 决定用户是否完成危险操作确认
- 接受任意命令名、程序路径、环境变量、systemd unit 或 shell 脚本
- 替代 mdadm、ext4、SMART 或 systemd 的成熟实现

### 3.3 Debian 系统工具

Rust 不重新实现 RAID、ext4 或 SMART 协议，而是安全封装经过版本和行为验证的 Debian 工具：

| 能力 | 首选工具或来源 | 输出策略 |
| --- | --- | --- |
| 块设备拓扑 | `/sys/class/block`，后续按需补充 `lsblk` | 只读固定字段 |
| 挂载关系 | `/proc/self/mountinfo`，后续按需补充 `findmnt` | 只读固定字段 |
| udev 属性 | `/run/udev/data`，后续按需补充 `udevadm` | 固定字段解析 |
| SMART | `smartctl` | 优先 JSON |
| 分区 | `parted`、`sgdisk` | 固定子命令和参数 |
| RAID | `mdadm` | 固定动作，优先 export/detail 输出 |
| ext4 | `mkfs.ext4`、`e2fsck` | 固定参数和退出码映射 |
| 挂载 | `mount`、`umount` | 仅受管目标和挂载点 |
| 服务 | `systemctl` | 仅登记的受管 unit |
| Samba 校验 | `testparm` | 有界文本输出 |

二进制绝对路径由部署包或受保护配置确定，并在 daemon 启动时验证；调用方不能通过协议覆盖这些路径。

## 4. 进程与权限模型

建议部署为两个基础 systemd 服务：

```text
amseoknas-api.service
  进程：ASP.NET Core
  用户：amseoknas-api
  权限：普通用户，可连接指定 Unix Socket

amseoknas-privileged.service
  进程：Rust daemon
  用户：root 或拆分后的最小特权账户
  权限：仅执行登记的节点动作
```

默认 socket 路径：

```text
/run/amseoknas/privileged.sock
```

`/run` 是临时运行目录，重启后内容消失。systemd 应通过 `RuntimeDirectory=amseoknas` 创建目录，并为 socket 设置明确 owner、group 和 mode。建议只允许 root 与专用调用组访问，例如：

```text
srw-rw---- root amseoknas /run/amseoknas/privileged.sock
```

文件权限只是第一层限制。Rust 接受连接后还必须读取 peer UID、GID 和 PID，并只接受登记的 C# 服务身份。来自合法 UID 的请求仍需通过协议、fencing token、设备身份和动作级安全检查。

daemon 启动时不得盲目删除 socket 路径。应先确认路径类型、owner 和是否存在活动监听者，只处理可证明属于自身的失效 socket，避免覆盖其他文件或服务。

## 5. 通信协议

### 5.1 传输方式

第一版使用 Unix Domain Stream Socket。C# 使用 .NET 的 `UnixDomainSocketEndPoint` 连接，Rust 使用标准库 `UnixListener` 监听；当前守护进程按连接串行处理只读小请求，后续出现长耗时动作前必须引入有界并发和动作级超时。

Stream Socket 不保留消息边界，因此协议使用：

```text
4 字节无符号大端消息长度
  + UTF-8 JSON 消息体
```

第一版已把单个请求和响应限制为 1 MiB。命令的大体积输出不得直接装入协议消息或数据库；只返回截断、脱敏摘要，必要的完整诊断以后使用受控文件存储和独立授权下载。

JSON 便于 C# 与 Rust 首次联调、测试夹具和故障排查。协议稳定且确有兼容或吞吐需求后，可以评估 Protobuf，但不能只为技术偏好提前增加代码生成链路。

### 5.2 请求信封

所有请求共享固定信封。以下示例用于说明字段语义，不代表接口已经实现：

```json
{
  "protocolVersion": 1,
  "requestId": "0190f6f4-7de8-7000-8000-000000000001",
  "operationId": "0190f6f4-7de8-7000-8000-000000000002",
  "action": "storage.inspectBlockDevices",
  "nodeId": "0190f6f4-7de8-7000-8000-000000000003",
  "clusterId": "0190f6f4-7de8-7000-8000-000000000004",
  "idempotencyKey": "opaque-value",
  "fencingToken": 42,
  "deadlineUtc": "2026-07-22T05:00:00Z",
  "parameters": {}
}
```

约束：

- `protocolVersion` 用于显式拒绝不兼容调用方
- `requestId` 用于单次通信关联，不能代替业务幂等键
- 危险动作必须携带 `operationId`、`idempotencyKey` 和 `fencingToken`
- 只读查询可以没有业务 Operation，但仍必须有 `requestId`、deadline 和大小限制
- deadline 由 C# 提供，Rust 同时使用动作自身的最大超时，取两者中更严格者
- `parameters` 必须根据 `action` 反序列化为明确类型，不得作为任意字典直接传给命令行
- Web 用户 ID 可以作为审计关联字段传递，但 Rust 不把它当作调用方身份凭据

### 5.3 响应信封

```json
{
  "protocolVersion": 1,
  "requestId": "0190f6f4-7de8-7000-8000-000000000001",
  "success": true,
  "result": {},
  "error": null,
  "diagnostics": {
    "durationMs": 18,
    "truncated": false
  }
}
```

失败响应必须区分业务可预期错误与 daemon 内部异常：

```json
{
  "protocolVersion": 1,
  "requestId": "0190f6f4-7de8-7000-8000-000000000001",
  "success": false,
  "result": null,
  "error": {
    "code": "resource.identity_changed",
    "message": "Target device identity changed after preview",
    "retryable": false
  },
  "diagnostics": {
    "durationMs": 11,
    "truncated": false
  }
}
```

错误码应集中定义并在 C#、Rust 契约测试中保持一致，至少覆盖：

- `protocol.unsupported_version`
- `auth.peer_not_allowed`
- `request.invalid`
- `request.too_large`
- `operation.stale_fencing_token`
- `operation.duplicate_requires_reconciliation`
- `resource.not_found`
- `resource.identity_changed`
- `resource.system_disk`
- `resource.busy`
- `tool.not_available`
- `tool.timeout`
- `tool.failed`
- `result.verification_failed`
- `internal.unexpected`

Rust 不向 C# 返回内部堆栈、秘密、完整环境变量或无界命令输出。C# 不把 daemon 的原始错误直接暴露给浏览器。

## 6. 动作模型

动作必须是封闭集合，每个动作拥有独立参数类型、验证器、执行器和结果类型。禁止提供 `RunShell`、`RunCommand`、`ExecuteScript` 或调用方可指定程序路径的等价接口。

### 6.1 第一阶段只读动作

```text
system.getStatus
storage.inspectBlockDevices
storage.inspectMounts
storage.readSmart
raid.inspectArrays
service.inspectManagedService
```

当前已实现的只读动作：

```text
system.getAbout
network.inspectInterfaces
storage.inspectBlockDevices
raid.inspectArrays
```

这些动作只从受信任的 `/proc`、`/sys`、`/run` 和文件系统接口读取数据，不接受参数，也不执行外部命令。块设备查询返回 WWN/序列号优先的身份、分区、直接挂载、swap、占用状态和传递式依赖设备；它沿 sysfs `holders` 关系追踪 MD、dm-crypt、LVM 与通用 device-mapper，使根文件系统经过多层块设备后仍能保护底层物理盘。阵列查询返回级别、状态、UUID、成员、降级数和内核同步进度。`ID_PATH` 和内核主次设备号明确标记为非稳定身份，重复 WWN/序列号也会标记 `identityConflict` 并撤销稳定身份；若 holder 无法完整解析，`topologyComplete` 为 `false`，后续危险流程必须失败关闭。Rust 守护进程要求显式配置唯一允许的 API 进程 UID，并在 Unix Socket 上校验 peer credentials；C# 端通过独立的 `system.read`、`network.read` 与 `storage.read` 权限策略开放 HTTP 查询。

当前只读实现已用伪 sysfs 集成测试覆盖分区经 dm-crypt 与 LVM 承载根文件系统，以及 MD 经加密层承载数据挂载的组合关系；尚未在真实 Linux loop device、测试机物理磁盘或实际 LUKS/LVM/MD 设备上验证，也未执行 SMART 查询。第二阶段的任何写动作仍必须等待真实设备集成测试、Operation、资源锁和执行前二次复核完成，不能只依据展示字段判断“可写”。

第一阶段还可以继续提供不接触真实存储的受控测试动作，用于验证 socket、peer credentials、超时、取消、错误映射和协议兼容性。

### 6.2 第二阶段写入动作

只有第一阶段安全边界和系统盘识别通过测试后，才增加：

```text
storage.createPartitionTable
storage.createPartition
raid.createArray
raid.stopArray
raid.addDevice
raid.removeDevice
filesystem.createExt4
filesystem.checkExt4
mount.mountManagedFileSystem
mount.unmountManagedFileSystem
config.applyManagedSambaConfig
service.reloadManagedService
```

写动作不能直接接收 `/dev/sda` 等易变路径。请求携带后端签发的稳定设备 ID 和预检时的身份快照，Rust 在执行前重新解析并比较当前状态。

## 7. 危险操作流程

以创建 mdadm RAID 为例：

```text
用户提交磁盘选择
  -> C# 验证登录、raid.manage 和资源归属
  -> C# 调用 Rust 获取实时设备拓扑
  -> C# 生成操作预览、风险、确认短语和一次性令牌
  -> 用户重新认证并确认
  -> C# 重验权限、令牌、参数摘要和当前 Leader
  -> C# 创建 Operation，获取持久化资源锁与 fencing token
  -> C# 通过 Unix Socket 请求 Rust 执行
  -> Rust 验证 peer、协议、token、参数和稳定设备身份
  -> Rust 重新检查系统盘、挂载、swap、RAID 和占用关系
  -> Rust 使用固定 mdadm 动作执行
  -> Rust 重新查询阵列并验证 mdadm UUID、成员和状态
  -> Rust 返回结构化结果
  -> C# 持久化阶段和结果，写审计并通知前端
```

C# 预检用于业务授权、用户确认和 Operation 建模；Rust 复核是不可绕过的最终机器安全门。任何一层发现目标状态变化都必须停止，重新预览，不能沿用旧确认继续执行。

## 8. 幂等、故障与恢复

危险系统操作通常无法获得跨数据库、消息队列、进程和内核的“恰好一次”保证，因此不能把网络超时直接当作操作失败，也不能自动原样重试。

必须遵循：

- C# 在调用前持久化 Operation、阶段、目标快照、幂等键和资源锁
- Rust 在执行前检查动作是否已经通过真实系统状态达成
- 命令开始、命令返回和结果复核是不同阶段
- C# 断线或 daemon 重启后，将不确定操作标记为 `Interrupted` 或进入对账阶段
- 对账先查询磁盘、阵列、文件系统和挂载实际状态，再决定成功、失败、人工介入或安全续接
- 不可逆动作在结果不确定时禁止自动重放
- fencing token 必须单调校验，旧 Leader 请求一律拒绝
- 取消只用于底层明确支持安全中断的阶段；不能通过杀进程假装所有动作都可取消

## 9. 系统命令执行规则

每个 Rust 动作独立声明：

- 固定二进制绝对路径
- 允许的参数集合和取值范围
- 最小环境变量
- 工作目录
- 最大运行时间
- 标准输入策略，默认关闭
- 标准输出和错误输出上限
- 允许的退出码
- 取消是否安全
- 执行前检查
- 执行后验证
- 日志脱敏规则

进程必须通过参数数组启动，不经过 `/bin/sh -c`。默认清空继承环境，只补充工具确实需要的固定环境。不得把用户输入直接用于程序路径、参数片段、环境变量名、挂载点或配置文件路径。

工具输出首先按受支持版本的结构化格式解析。解析失败是显式错误，不能靠宽松字符串匹配推断危险操作已经成功。

## 10. 配置文件边界

C# 保存集群期望状态并生成与业务相关的配置模型；Rust 负责将经验证的受管内容安全落盘。配置应用遵循：

```text
校验期望版本和受管目标
  -> 在目标目录所在文件系统创建临时文件
  -> 设置 owner、group 和 mode
  -> 运行对应语法与业务校验器
  -> 备份当前受管版本
  -> 原子替换
  -> reload 或 restart 登记的服务
  -> 功能健康检查
  -> 成功确认，失败回滚并再次校验
```

Rust 只允许写入登记的 NAS 受管路径，不能接受调用方提供的任意目标文件。Samba、NFS、Nginx、systemd 和挂载配置优先使用 include 或 drop-in，不覆盖用户主配置。

## 11. Web SSH 与终端边界

Web SSH 不属于磁盘 privileged daemon。终端允许用户输入任意命令，而 privileged daemon 的安全前提是只开放固定动作，两者合并会使动作白名单失去意义。

如第四阶段确认需要 Web 终端，推荐：

```text
浏览器 xterm.js
  -> C# WebSocket Terminal Gateway
    -> 独立 Unix Socket
      -> nas-terminal-broker（独立进程和权限）
        -> 低权限 PTY / shell
```

C# 负责 Web 认证、重新认证、会话、并发限制、空闲超时和审计。终端 broker 只负责 PTY 生命周期、窗口尺寸和低权限 shell，不与 `amseoknas-privileged` 共用二进制、socket、Linux 用户或 systemd unit。默认不提供 root shell，提权由独立、明确的 sudo 策略控制。

当前 C# API 中，`TerminalController` 只处理授权、Origin、子协议、WebSocket 升级和 HTTP 结果映射；`ITerminalSessionService` 编排重新认证与一次性授权会话，`ITerminalWebSocketRelay` 负责浏览器和 broker 之间的有界双向转发及超时关闭，`ITerminalBrokerClient` 封装独立 Unix Socket 协议。

当前已按该边界建立 `AmseokNas-terminal` 独立 Rust broker、C# WebSocket Gateway，以及由 Material Dialog 承载的 Angular xterm.js 终端。测试机已完成独立 Linux 账户、systemd 沙箱、异常自动重启、Unix Socket/PTY 和跨权限访问验证，并通过 HTTPS Angular 开发代理启用；仍需使用当前 Web 管理员账户完成浏览器重新认证与 WebSocket 交互端到端验证，生产 Nginx 长连接也尚未验证。部署约束见 `AmseokNas-docs/web-terminal.md`。

## 12. 推荐实现结构

### 12.1 C#

```text
AmseokNas-server/src/
  Nas.Domain/
    Operations/
    Errors/
  Nas.Application/
    SystemSettings/
    Storage/
    Operations/
    Privileged/
      PrivilegedContracts.cs
  Nas.Infrastructure/
    Privileged/
      UnixSocketPrivilegedClient.cs
      Protocol/
    Persistence/
    ClusterServices/
  Nas.Api/
```

按用例拆分的客户端接口是 C# 与系统能力之间的 Application 端口。Infrastructure 可以由一个 Unix Socket 适配器实现多个窄接口并封装 Socket、JSON 和 Rust 协议类型，Application 与 Domain 不依赖这些传输细节。

### 12.2 Rust

```text
AmseokNas-privileged/
  Cargo.toml
  Cargo.lock
  src/
    main.rs
    protocol/
    actions/
    inventory/
    safety/
    storage/
    config/
    execution/
    audit/
  tests/
```

Rust 第一版建议使用最小依赖集合：

- Tokio：异步 Unix Socket、进程、信号、时间和取消协作
- `tokio-util`：长度前缀分帧和最大帧限制
- Serde 与 `serde_json`：强类型 JSON 请求响应
- `tracing`：结构化日志
- `thiserror`：稳定错误分类

依赖版本应由 `Cargo.lock` 固定，并在构建和发布前执行格式化、Clippy、测试、依赖许可证与已知漏洞检查。peer credentials 优先使用 Tokio 当前稳定 API；只有确实缺少系统能力时才增加 `nix` 等更底层依赖。

## 13. 测试要求

### 13.1 协议测试

- C# 与 Rust 使用相同请求和响应夹具
- 覆盖协议版本不匹配、未知动作、字段缺失和未知枚举
- 覆盖拆包、粘包、半包、超大帧、提前断开和无效 UTF-8
- 覆盖请求超时、取消和 daemon 重启
- 覆盖稳定错误码在两端映射一致

### 13.2 安全边界测试

- 未授权 UID、GID 或进程身份不能调用 daemon
- 普通用户不能替换 socket 或受管配置
- 所有动作拒绝任意程序路径、shell 字符串和额外参数
- 输出、日志和错误不泄露秘密或无界内容
- 旧 fencing token 和重复危险请求被拒绝或转入对账

### 13.3 设备测试

- 使用 loop device、临时镜像和隔离测试机覆盖块设备关系
- 覆盖系统盘位于普通分区、mdadm、LUKS 和未来可选设备映射之上的场景
- 覆盖设备路径变化但稳定 ID 不变
- 覆盖序列号、容量或父子关系在预检后变化
- 覆盖已挂载、swap、RAID 成员和繁忙设备
- 破坏性测试不得在开发机真实数据盘运行

### 13.4 故障测试

- 命令启动前、执行中、返回后和结果复核时分别终止 C# 或 Rust
- 模拟超时、非零退出、截断输出和不可解析输出
- 模拟 C# 收不到响应但底层动作已经成功
- 模拟服务重启、消息重投、资源锁过期和 Leader 变化
- 验证系统不会自动重复不可逆步骤

## 14. 分阶段落地

### 第一阶段

1. 初始化 Rust workspace、格式化、Clippy、测试和 CI
2. 建立 Unix Socket、peer credentials、长度分帧和版本化协议
3. 在 C# 中建立按用例拆分的特权查询端口和超时、取消、错误映射
4. 实现无副作用的受控测试动作
5. 实现系统状态、磁盘拓扑、挂载、SMART 和 RAID 只读查询
6. 实现稳定设备 ID、系统盘保护和多层块设备测试
7. 接入只读 API、节点 SQLite 快照和前端磁盘列表

第一阶段不得包含创建分区、格式化、创建 RAID 或擦除签名。

### 第二阶段

1. 完成 Operation、资源锁、确认令牌、重新认证和 fencing token 闭环
2. 逐个增加分区、mdadm、ext4 和挂载动作
3. 每个动作分别完成预检、执行后验证、故障注入和恢复测试
4. 增加受管 Samba 配置事务
5. 在专用测试机和可丢弃磁盘上完成真实重启与失败恢复验证

## 15. 当前实施状态

截至 2026-08-03：

- C# 控制面骨架、身份认证和 PostgreSQL/SQLite 基础已经存在
- Domain 已定义统一 `OperationStatus`，权限中已包含 `storage.read`、`storage.format` 和 `raid.manage`
- `AmseokNas-privileged` 已建立 1 MiB 有界版本化 Unix Socket 协议、peer UID 校验和固定只读动作白名单，不提供任意命令执行入口
- C# Application 已按系统设置与存储清单用例拆分客户端端口；Infrastructure 复用单一 Unix Socket 适配器；HTTP Controller 只负责 `storage.read` 授权、协议映射和脱敏错误响应
- Rust 已实现 `system.getAbout`、`network.inspectInterfaces`、`storage.inspectBlockDevices` 和 `raid.inspectArrays`；存储拓扑已补充 MD、dm-crypt、LVM、通用 device-mapper 的传递式 holders 遍历、系统/管理目录挂载保护、swap 传播、稳定身份冲突和拓扑完整性标记。存储模块 11 项独立测试已实际执行通过，Linux 目标 `cargo check --tests` 与 Clippy `-D warnings` 通过；完整 daemon 仍需在 Linux 环境执行测试
- C# 存储查询相关 8 项 Controller/协议测试全部通过，并覆盖旧 daemon 缺少拓扑字段时规范化为占用状态；除项目原有 macOS Unix Socket 长路径测试外的 23 项 xUnit 全部通过，解决方案格式验证通过
- 独立 `AmseokNas-terminal` Rust workspace、C# WebSocket Gateway 和 Angular Material/xterm.js 弹窗已经实现并通过本地及测试机构建；测试机已验证独立账户、systemd 沙箱、服务保活、Unix Socket/PTY、秘密与网络隔离、权限迁移和未登录拦截，浏览器登录后的 WebSocket 交互以及生产 Nginx 长连接仍待验证；该实现不属于 privileged daemon
- 当前开发环境已安装 Rust 1.97.1 toolchain，并已补充 Linux 交叉检查目标和 Clippy 组件
- 当前完成的是只读清单代码闭环，不代表 RAID 创建、删除、扩容、替换、文件系统或挂载功能已经完成
