# AmseokOS 项目开发约定与实施路线

## 1. 文档目的

本文件是 AmseokOS 项目的长期开发约定、架构说明、阶段计划和 AI 协作规则

任何参与本项目的 AI 或开发者在开始工作前，都必须先阅读本文件，再检查项目目录、源码、配置、测试和 Git 状态，并根据实际代码判断当前进度

进度判断以可运行代码和验证结果为准，本文件中的进度记录只作为线索，不能代替实际检查

AI 必须在现有项目基础上完成下一个尚未完成的小目标，不得因为已有实现不完整就绕开原有结构另起一套项目，也不得一次跨越多个阶段进行大范围 speculative 开发

## 2. 当前项目进展

最后检查日期：2026-08-08

当前状态：第一阶段进行中，第 1 项最小项目骨架已完成；第 3 项持久化基础与真实 PostgreSQL 迁移已验证；第 4 项管理员认证、强制改密代码闭环和测试机认证请求链路已建立；前端桌面 Shell、可复用 WindowFrame 和多窗口管理已建立，支持最小化、最大化/还原、关闭、拖动、缩放和布局持久化，并已接入只读系统设置窗口，Dashboard 和磁盘列表仍未实现；C# 与 Rust 特权边界已完成“关于本机”、物理网卡、物理块设备和现有 MD RAID 阵列只读查询代码闭环，privileged daemon 已以独立低权限账户部署到 `.7` 并完成四类真实只读 Socket 查询，C# 已新增独立网络配置安全预览边界，任何存储与网络实际写操作仍保持关闭；作为用户明确要求的插入项，低权限 Web Terminal 代码闭环已建立并已从独立构建机生成 Release 产物部署到测试机，独立系统账户、systemd 沙箱、异常自动重启、真实 Socket/PTY、权限迁移、Nginx HTTPS/WebSocket 路由和未登录拦截均已验证，仍待使用当前 Web 管理员密码完成浏览器登录后的交互终端端到端验证；终端重新认证与一次性会话流程已从 Controller 移入 Application 用例，浏览器与 broker 的双向转发也已移入独立 API Relay，拆分后的累计 15 项 xUnit 已由构建机验证，本次 Relay 关闭预算和故障分类修复新增的 3 项测试仍待在 .NET 10 环境执行；作为用户明确要求的 CI/CD 基础设施插入项，OneDev 已部署到 `console`，管理员初始化、容器持久化、AmseokOS 项目导入、独立 Agent、远程 Shell 执行器、版本化 Build Spec、完整测试构建、OneDev 制品保留和存储机上传链路均已真实验证；作为用户明确要求的 Debian 发行版插入项，Qt/QML 图形安装器、Debian source package 和 live-build 镜像配置的只读架构骨架已建立，安装器二进制包已在 Debian trixie amd64 构建、签名发布并由独立测试机通过 APT 安装与冒烟验证；当前 Web、API、privileged daemon、Terminal broker、本地控制台和单节点 etcd/NATS 配置已由 `.10` 构建为七个 Debian 包并签名发布，由 `.7` 通过局域网 curl、签名和 SHA-256 验证后安装运行，真实磁盘探测与安装执行保持关闭；Data Protection 外部密钥保护尚未完成

检查结果：

- 已初始化本地 Git 仓库，本地 `devkihon` 基于并跟踪远端 `origin/devkihon`
- 已配置远端 `origin` 为 `git@github.com:AmseokTech/AmseokOS.git`
- 当前开发基线提交为 `47993b6 完善readme`
- 已新增 SSH 测试与部署目标机 Git 边界：目标机不得暂存、创建提交、变基、推送或执行会创建提交的合并；即使单次任务授权提交也必须回到本地开发工作区，目标机源码同步仅允许经过验证的 fast-forward-only 流程
- 已建立前端、后端、特权进程、部署和文档的第一阶段顶层目录，并统一使用 `AmseokOS-<用途>` 命名
- 已建立根级 `README.md`，顶部使用仓库内品牌横幅并居中展示 `AmseokOS` 标题，使用“让数据与 AI 留在本地 / Keep your data and AI local”作为品牌口号，以“统一管理私有数据、本地 AI、应用服务与计算资源 / Manage private data, local AI, application services, and computing resources—all in one place”说明用户向定位，两组文案均按中文在上、英文在下展示，并集中提供开发状态、安全边界、架构、目录、启动、验证和文档入口；2172×724 品牌横幅已使用 oxipng 10.1.1 无损压缩，从 1,167,806 字节降至 730,868 字节，解码像素、8-bit RGB 和 sRGB 配置保持一致；本地 AI 的具体支持范围、部署方式与硬件兼容说明仍待对应模块文档补充
- 已将 standalone 前端迁移至 Angular 22.0.6 和 TypeScript 6.0.3，包含路由、SCSS、严格 TypeScript、API 健康检查状态、本地反向代理配置和固定的 `6521` 开发端口
- Angular 构建器已迁移至 `@angular/build:application`，组件测试已从 Karma 浏览器执行器迁移至 Vitest 4.1.10 和 jsdom
- 已初始化 .NET 10 后端解决方案，建立 `Nas.Domain`、`Nas.Application`、`Nas.Infrastructure` 和 `Nas.Api` 项目及单向项目引用
- 已提供匿名只读的 `GET /api/health` 健康接口，前端可通过开发代理访问
- 已将 Angular 组件测试和 xUnit API 测试纳入版本控制，使本地与 CI 使用同一套测试源码
- 已接入 Angular Material 22.0.4，启用官方 Azure Blue 预构建主题、异步动画 provider，并将顶部栏与 API 连接状态改为 Material Toolbar 和 Chip
- 已从测试机源码快照恢复管理员登录入口，并接入真实 Cookie 认证、错误与提交状态以及首次登录强制改密页面
- 已将管理员登录入口从根组件迁入 `core/auth`，根组件只承载路由内容，并将用户头像与密码输入框拆为可复用的 `shared` 组件
- 已使用 Node.js 22.22.3 验证 Angular 生产构建和 7 项组件测试通过
- 已验证 Angular 生产构建、Vitest 组件测试、ASP.NET Core 解决方案构建、xUnit 测试、API 直接请求和前端代理请求通过
- Angular 22 工具链已在 Node.js 22.22.3 和 npm 10.9.8 下验证，当前系统默认的 Node.js 18 不满足运行要求
- 前端生产依赖审计无漏洞；完整开发依赖审计有 6 个来自 Angular 构建与开发工具链的上游告警，其中 3 个低危、3 个中危，当前强制自动修复会降级 Angular CLI
- 已确定数据库目标架构：对等 NAS 节点与动态控制面 Leader，节点本地使用 SQLite，集群全局数据使用 PostgreSQL HA，etcd 负责选举和租约，NATS JetStream 负责节点命令与事件
- 已确定 Web 身份边界：ASP.NET Core Identity 在 PostgreSQL 中保存账户、密码哈希、角色和权限，Data Protection 保护 Cookie 与临时令牌，节点 SQLite 不保存全量账户密码哈希
- 已确定并开始实现 C# 控制面与 Rust 特权执行面边界：ASP.NET Core 负责认证、业务编排、Operation、数据库和集群协调，独立 Rust daemon 通过 Unix Domain Socket 提供受限系统查询与特权动作；当前已建立 Rust workspace、1 MiB 有界版本化协议、peer UID 校验和首批只读动作，写动作尚未开放
- 已建立 `ClusterDbContext` 与 PostgreSQL 初始迁移，覆盖 ASP.NET Core Identity 用户/角色、权限点、节点注册、全局 Operation、审计索引和 Data Protection 密钥表
- 已增加固定初始管理员 `admin` 的 Identity 哈希种子、全部现有权限、`MustChangePassword` 状态和独立迁移；初始密码明文不写入运行时配置或数据库
- 已实现登录、会话查询、修改密码和退出 API，启用安全 Cookie、CSRF、防暴力登录锁定与限速，并由后端默认授权策略阻止未改密会话访问普通受保护接口
- 已实现 Angular 登录与强制改密闭环；修改密码后新 Identity 哈希覆盖初始哈希、清除强制改密状态、递增安全版本并退出临时会话
- 已建立深蓝色桌面 Shell，按职责拆分顶部任务栏、Dock 和桌面应用展示契约；所有成功登录统一进入懒加载的 `/desktop` 路由，强制改密会话在桌面右上角显示可复用内容投影提醒，刷新桌面后通过会话接口恢复提醒；Dock 支持键盘焦点、当前入口状态和窄屏横向滚动
- 已将桌面手绘渐变背景替换为用户提供的 16:9 山景壁纸，使用 `cover` 保持宽高比并保留深蓝色加载兜底；Angular 25 项测试与生产构建通过，构建产物包含与源文件一致的壁纸资源
- 已将前端用户可见的旧产品名统一为 `AmseokOS`，移除桌面品牌区的 CSS 手绘 Logo，将“更便捷地管理你的服务器”与产品名移至桌面右下角，并在窄屏避让底部 Dock；Angular 26 项测试与生产构建通过
- 已将系统设置注册为桌面窗口管理器中的单例应用，Dock 可打开、聚焦、最小化和恢复窗口；设置页面采用左侧“关于本机/网络”导航，关于本机展示操作系统、内核、运行时间、CPU 型号/核心/频率、内存和系统盘容量，网络页展示物理网卡型号、驱动、链路、速率、地址、网关和 DNS；当前仅在检测到 systemd DHCP 租约时标记 DHCP，其他已配置来源显示未知，避免把 NetworkManager、ifupdown 或 SLAAC 地址误报为固定地址；尚未具备安全回滚能力的 IP 写入按钮保持禁用
- 已新增独立 C# `NetworkConfiguration` Application 模块和 `POST /api/network/configuration-previews` 安全预览接口，使用 `network.manage` 独立授权策略、CSRF、按用户限速和管理员密码重新认证；DHCP 模式拒绝静态字段，固定 IPv4 模式严格校验四段十进制地址、连续且 `/1` 至 `/30` 的子网掩码、主机地址、网关同子网及重复/缺失网卡身份，返回规范化地址、掩码、前缀、当前配置、警告与结构化错误。C# 已增加 `POST /api/network/configuration-operations`、`POST /api/network/configuration-operations/{operationId}/confirm` 和 `POST /api/network/configuration-operations/{operationId}/rollback` 三个受保护接口，将参数复核、两分钟确认期限、调用身份和执行结果映射集中在 Application，并通过独立 `INetworkConfigurationExecutor` 隔离 Rust 写入边界；生产环境暂时注册拒绝型适配器，因此 Rust 原子写入、连通性确认和自动回滚尚未实现时，三个接口统一以 `503 network.write_unavailable` 关闭且不会假报成功，预览仍返回 `CanApply=false`，前端按钮也保持禁用。网络配置相关 32 项 xUnit 与 Release 构建 0 警告/0 错误通过；Operation 持久化、资源锁和审计仍未接入此流程
- 已建立 `NodeDbContext` 与 SQLite 初始迁移，覆盖节点状态、本地 Operation、资源锁、Inbox 和 Outbox，并通过临时 SQLite 数据库验证迁移、WAL、`quick_check` 和外键检查
- 已提供 PostgreSQL、单成员 etcd 和 NATS JetStream 的单节点 Compose 配置、NATS 权限配置、部署说明，以及 API 存活/就绪健康检查；etcd 健康与 NATS JetStream 发布持久化已使用对应版本的独立二进制验证
- 已验证 .NET 解决方案构建 0 警告和 0 错误、本地 2 项 API/身份种子测试、Angular 10 项组件测试与生产构建通过，并验证 PostgreSQL 认证迁移模型无待生成变更；NuGet 直接与传递依赖未发现已知漏洞
- 已建立 GitHub Actions CI，在面向 `devkihon` 或 `main` 的 Pull Request、分支推送和人工触发上并行执行仓库配置、Angular、.NET 和两套 Rust 检查；仓库门禁校验变更空白、JSON、Compose 和 systemd unit，前端执行 ESLint、生产依赖审计、带阈值覆盖率测试和生产构建，后端执行格式与分析器、NuGet 漏洞、警告即错误构建、EF 模型漂移和带覆盖率测试，Rust 执行格式、Clippy、全特性测试、固定版本 `cargo-audit` 和 Release 构建；Pull Request 另执行中危以上依赖变更审查，C# 与 JavaScript/TypeScript 使用 CodeQL `security-and-quality` 查询并按周定时扫描，前后端覆盖率报告保留 14 天
- 已在 `nastest` 安装并启用 Docker 26.1.5 和 Compose 2.26.1；由于 Docker Hub 连接超时，仓库 Compose 镜像路径尚未实际启动验证
- 已改用 Debian 官方包部署并启用 PostgreSQL 17.10、etcd 3.5.16 和 NATS 2.10.27 JetStream，服务只监听本机基础设施端口；运行时数据库与 NATS 密码保存在 `root:root`、`0600` 的仓库外环境文件中
- 已对真实 PostgreSQL 和节点 SQLite 应用迁移，验证 SQLite 为 WAL、`quick_check=ok` 且外键开启，并关闭常规启动时自动迁移
- 已在 `nastest` 定位并验证 API 未注册防伪验证过滤器以及 record DTO 验证元数据目标错误；相同最小修复已回到开发机 `devkihon` 工作流
- 已真实验证 CSRF 获取、默认管理员登录、强制改密状态会话查询、CSRF 刷新和退出链路通过；API、PostgreSQL、etcd 和 NATS 均已设置开机启动并验证 active；测试机当前由 Nginx 在 HTTPS 6521 端口承载生产构建静态资源和 API/WebSocket 反向代理，Angular 开发服务已停用
- 已修复强制改密页未绑定响应式表单导致浏览器提交不触发改密请求的问题，并用真实 DOM 提交测试覆盖请求参数与成功后返回登录页
- 已按独立低权限边界实现代码默认关闭的 Web Terminal：Angular Dock 使用 Material Dialog 先重新验证当前 Web 管理员密码，成功后再以大尺寸 Dialog 懒加载 xterm.js，不再跳转独立 `/terminal` 页面；终端复用 `shared/components/window-frame` 的 Windows 风格标题栏，最小化保持会话并可由 Dock 恢复，最大化/还原会重新计算终端尺寸，关闭才释放 WebSocket/PTY；C# 使用一次性短期授权、WebSocket Origin 与子协议校验、有界转发、空闲和最长时限，独立 Rust broker 使用 Unix Socket peer UID、固定 profile、固定环境和真实 PTY；新增 `terminal.open` 权限迁移、systemd/Nginx 草案和部署说明，终端不复用 privileged daemon
- 已验证 Rust 格式化、Clippy、4 项测试和 Release 构建通过，其中真实 PTY 测试覆盖 shell 输入输出；Cargo 元数据依赖经 OSV 查询未发现已知漏洞，CI 已纳入 `cargo audit`；验证 .NET 构建 0 警告和 0 错误、5 项 xUnit 测试、迁移模型无待生成变更、Angular 14 项组件/服务测试和生产构建通过；systemd unit 与 Nginx 配置通过本地语法检查
- 已通过局域网将源码同步到 `nastest` 的隔离构建目录并在测试机直接验证：.NET Release 构建 0 警告和 0 错误、5 项 xUnit 测试、Angular 13 项测试与生产构建、前端生产依赖审计、Rust 1.97.1 格式化、Clippy、4 项测试和 Release 构建均通过；已创建 UID 999 的 `amseoknas-terminal` 系统用户，安装 root 所有的 broker 和 systemd unit，验证 Socket mode 为 `0660`、peer UID、真实 PTY 的 UID/工作目录、无 capabilities、仅 AF_UNIX、`ProtectSystem=strict`、`ProtectHome=yes` 和 `NoNewPrivileges=yes`，PTY 内无法读取运行时秘密和 API 用户 home，也无法创建 AF_INET 连接；模拟 `SIGKILL` 后服务从 PID 35521 自动重启为 PID 35686，`NRestarts=1`。测试 release 已激活，`20260722075206_AddTerminalPermission` 已应用且自动迁移重新关闭，HTTPS 桌面与健康接口返回 200，未登录终端会话与 Socket 请求返回 401；Dialog 与 WindowFrame 前端改动已同步到测试机隔离源码并由 Angular 开发服务热构建成功。此次 Terminal 灰色 CMD 风格与认证文案调整已直接写入测试机活动源码，相关 WindowFrame 2 项远端测试和开发服务热构建通过，桌面与健康接口返回 200，Web 与 Terminal 服务保持 active；测试机仅约 1 GB 内存，不再并行执行完整 Angular 测试与生产构建，完整 14 项测试与生产构建以开发机结果为准
- 已在独立构建机 `build@192.168.1.13` 基于提交 `eb7b022` 完成可追溯 Release 构建：Angular 14 项测试与生产构建、.NET Release 0 警告/0 错误与 5 项测试、EF 模型检查、NuGet 和前端生产依赖漏洞检查、Rust 格式化、Clippy、4 项测试与 Release 构建均通过；80 个产物文件经 SHA-256 校验后由构建机直接传至 `nastest`。测试机已激活 API 与 Web release `20260722T190500Z-eb7b022`，root 所有的 Rust broker 与构建产物哈希一致；Nginx 已在 HTTPS 6521 提供静态前端及带 Upgrade 的 Terminal WebSocket 代理并设为开机启动，Angular 开发服务已停止并禁用。dev 侧验证桌面和健康接口返回 200、未登录终端会话返回 401，API、Terminal、Nginx 均 active/enabled，Terminal Socket 为 `amseoknas-terminal:amseoknas-terminal`、`0660`
- 已实现首批 C# 与 Rust 只读系统信息链路：Rust daemon 只登记 `system.getAbout` 和 `network.inspectInterfaces`，不接受任意命令或参数，要求显式 API 服务 UID、校验 Socket 目录 owner/mode 与 peer credentials，并从 `/proc`、`/sys`、`/run` 和文件系统接口读取信息；C# 增加 `system.read` 权限迁移、独立授权策略、超时 Unix Socket 客户端和只读 API，原始 daemon 诊断不直接暴露给浏览器；部署草案使用独立低权限账户、无 capabilities、`NoNewPrivileges`、严格文件系统保护和仅 AF_UNIX/AF_NETLINK。Rust 格式化、Clippy、8 项测试、Release 构建和真实本机 Socket 查询通过；.NET Release 构建 0 警告/0 错误、10 项测试和 EF 无待生成迁移通过；Angular 29 项测试与生产构建通过；NuGet 与前端生产依赖扫描未发现已知漏洞
- 已在本地验证新增质量门禁：GitHub Actions 两个工作流通过 YAML 解析、Actionlint 和空白检查；Angular ESLint 0 告警、29 项测试通过，语句/分支/函数/行覆盖率分别为 65.6%/49.83%/69.06%/64.87%，配置的最低门槛为 60%/45%/60%/60%，生产构建和生产依赖审计通过；.NET 解决方案与测试项目格式/分析器检查通过，警告即错误的 Release 构建为 0 警告/0 错误，10 项测试和 Cobertura 报告通过，EF 无待生成迁移，NuGet 直接与传递依赖无已知漏洞；两套 Rust 格式、Clippy、全特性 4/8 项测试和 Release 构建通过。开发机没有 Docker，Compose/systemd 完整环境校验、固定版本 `cargo-audit`、依赖变更审查和 CodeQL 需由 GitHub Actions 首次运行确认
- 已针对 Dock 点击“系统设置”未见窗口的问题完成端到端链路核对：测试机当前 Web Release `20260724T075839Z` 已包含 `windowAppId: settings`、设置懒加载块和 WindowManager 打开逻辑，本地测试改为真实点击 Dock 按钮并确认 WindowHost 渲染“系统设置 窗口”和设置布局，完整前端门禁通过。测试机 Nginx 当前未为 `index.html` 设置 `Cache-Control`，部署前已经打开的浏览器标签仍会继续运行旧 JavaScript；旧实现点击设置只更新顶部标题而不创建窗口，需强制刷新后加载当前哈希资源。浏览器强制刷新后的人工结果仍待用户确认
- 已按 Application 用例边界拆分 Web Terminal 会话授权：新增 `ITerminalSessionService` 与明确的创建/消费结果类型，集中处理终端开关、密码重新认证、一次性会话创建和消费；`TerminalController` 不再直接编排 `IAuthenticationService` 与 `ITerminalSessionStore`，只保留 HTTP/WebSocket 协议检查和响应映射；新增 3 项用例测试覆盖禁用、重新认证失败、成功创建及一次性消费。拆分时工作区未包含 Git 元数据且未安装 .NET 工具链，本次仅完成静态检查，构建和测试执行仍待验证
- 已将 Web Terminal 双向转发从 `TerminalController` 拆入 API 传输层 `ITerminalWebSocketRelay`：Relay 集中管理浏览器输入、broker 输出、控制消息、帧大小、空闲和最长时限、关闭握手及字节统计，Controller 只建立经过检查的 WebSocket 与 broker 会话后委托转发；新增 2 项 Relay 测试覆盖输入/resize/close 和输出/exit 两个方向。当前工作区仍无 .NET 工具链，本次新增代码和累计 15 项 xUnit 测试尚待实际构建执行
- 已修复 `ITerminalWebSocketRelay` 评审发现的两个问题：broker、转发任务和 WebSocket 关闭共用 5 秒关闭预算，请求取消时中止未完成的 WebSocket 关闭握手；非法 JSON、非法控制消息和超大消息保留对应 WebSocket 关闭码并记录为 `ProtocolViolation`，I/O 等未预期传输异常记录为 `Failed`，不再伪装成正常关闭。新增 3 项测试覆盖协议违规分类、broker I/O 故障分类和卡住的关闭握手中止，累计测试数为 18；当前工作区没有 .NET SDK，已完成差异、格式和依赖边界静态检查，新增测试仍待实际执行
- 已在目标机 `console` 停止原有 `shipit-frontend` 与 `shipit-backend` 容器，按镜像摘要部署 OneDev 15.0.6；服务使用 `onedev-data` 持久卷、`unless-stopped` 重启策略、1 CPU、768 MiB 内存和 2 GiB 含 swap 上限，Web 端口 `6610` 已从开发机验证可达，Git SSH 端口 `6611` 已发布但首次管理员初始化前 OneDev 尚未监听。容器重启后 462 个持久化文件保持不变，Web 在约 39 秒后恢复 200，未发生 OOM；已将无语言 Cookie 时的默认界面从英文改为 OneDev 内置中文，并将浏览器标签标题本地化为“OneDev - 集 Git、CI/CD、看板与软件包于一体”，使用无 Cookie 的新请求和浏览器实际页面确认首次初始化页显示“服务器设置”“创建管理员账户”等中文文本，修改前的 15.0.6 核心包已备份到持久卷内 `/opt/onedev/backup/localization-20260731/`，可直接回滚。目标机仅 1 核和约 1 GB 内存，低于 OneDev 官方建议的 2 核和 2 GB；管理员初始化和实际流水线现已完成，HTTPS、持久卷备份与 Git SSH 端到端验证仍未配置
- 已确认 OneDev 首次管理员初始化已经完成；已为 `build@192.168.188.4` 安装用户级 Git 2.47.3、Node.js 22.22.3、.NET SDK 10.0.302、Rust 1.97.1 和隔离 GCC 14，并建立到 `storage@192.168.188.8:/home/storage/storeage/AmseokOS/Test` 的受限专用 SSH 上传通道。基于提交 `7712d3d` 的真实构建已验证 Angular 生产构建、15 项 xUnit、Terminal 4 项 Rust 测试、特权守护进程 8 项 Rust 测试和 Release 构建通过；发现 .NET 10.0.9 的 `System.Security.Cryptography.Xml` 高危公告后，已将项目 ASP.NET Core/EF Core 补丁版本和 `dotnet-ef` 提升至 10.0.10、GitHub CI SDK 提升至 10.0.302，重新验证漏洞包数量为 0。安全测试包 `amseokos_0.1.0+git20260731.7712d3da.security2_amd64.deb` 已上传并通过 SHA-256 与包内容检查，SHA-256 为 `41496d3cf15c31aa616e0f22bef74d95b6b209b72eae57169f291b051fc7e739`；早期含 10.0.9 运行时的包已移动到存储目录下 `quarantine/`
- 已在 OneDev 导入 `AmseokOS` 项目和 `devkihon` 分支，将在线 Agent `amseokos-build-01` 绑定到远程 Shell 执行器 `amseokos-debian-builder`，并在仓库内保存版本化 `.onedev-buildspec.yml`。Build Spec 提交 `eebb8162ddc787feba663fb1252249d9650af29d` 的构建 #1 已在真实 Agent 上完成：Angular ESLint、29 项测试和生产构建通过，.NET 0 警告/0 错误、15 项测试及漏洞包数量 0，Terminal 4 项和 privileged 8 项 Rust 测试及 musl Release 构建通过；生成的 `amseokos_0.1.0+git20260731.eebb8162.b1_amd64.deb` 大小 39,503,624 字节，已同时保存为 OneDev 制品并上传到 `/home/storage/storeage/AmseokOS/Test`，远端 `sha256sum -c` 通过，SHA-256 为 `c8743b6c6c46710d5b86c3b88cf9093234fe1b0dadde86aad01c3b9560ea0ae7`
- 已将 OneDev Debian 流水线从单体包改为组件拆包；提交 `bc64a0a5a411b60b251ba3ac5bef01dc12d28543` 的构建 #5 已生成版本 `0.1.1+git20260731.b111024.bc64a0a5` 的 `amseokos-web`、`amseokos-api`、`amseokos-terminal`、`amseokos-privileged` 四个组件包和精确依赖它们的 `amseokos` 整套元包。Angular ESLint、29 项测试和生产构建、.NET 15 项测试与漏洞检查、Terminal 4 项及 privileged 8 项 Rust 测试和 Release 构建均通过；OneDev 制品中包含 5 个 `.deb` 与 5 个 `.sha256`，同版本 10 个文件已上传到 `/home/storage/storeage/AmseokOS/Test`，远端逐包 `sha256sum -c` 全部通过
- 已将仅人工触发的 OneDev Debian 流水线复制到 OneDev 内部 `main`：提交 `741025536ff3ee24354cdaf27cbedd3876eaaec9` 在 `AmseokOS-deploy/` 恢复经过验证的拆包、受限上传与签名发布脚本，提交 `39206a7dfd808d7b0ec66ca5c276e605154cd73f` 在仓库根目录加入 `.onedev-buildspec.yml`。OneDev GUI 已识别“构建 Debian 测试包”和 7 个步骤；参数与触发器面板均显示“未指定”，因此只会由人工点击运行，运行时先 `fetch` 并检出当时的 `main` 最新提交再构建。当前 `main` 提交显示没有关联构建，构建列表仍为此前已有的 7 个，最新 #7 是本次配置前在 `devkihon` 上取消的构建；本次未启动新构建。以上两个提交只存在于 OneDev 内部仓库，没有推送 GitHub，`devkihon` 上的历史配置也未删除
- 已在 `storage@192.168.188.8` 建立签名 APT 测试仓库 `/home/storage/storeage/AmseokOS/Repository`：使用存储机本地专用 Ed25519 OpenPGP 密钥生成 `InRelease`、`Release.gpg`、`Packages`、`SHA256SUMS` 及其签名，签名指纹为 `26A99F575933CE0A9DE2E2EA246904FC24AD8939`，密钥有效期至 2028-07-30，私钥目录和私钥文件权限分别为 `0700`、`0600`。现有 18 个 `.deb` 均先通过原始 SHA-256 和 `dpkg-deb` 结构检查再进入仓库，独立临时公钥环验证三个签名通过，使用 `signed-by` 的真实 `apt-get update` 成功并识别全部 18 个包；仓库 `Valid-Until` 为每次发布后 30 天。构建机专用 SSH 公钥现由 forced command 限制为仅可向固定测试目录使用旧版 SCP 上传或执行 `amseokos-publish`，任意远程命令已验证被拒绝；签名发布脚本最初由 OneDev 内部提交 `784b59ff` 验证，现已随上述提交复制到 OneDev 内部 `main`。仓库当前仅生成在文件系统中，尚未通过 HTTP/HTTPS 对客户端发布
- 已新增独立 `AmseokOS-installer` 模块，使用 Qt Quick/QML 提供欢迎、系统盘和安装摘要三页只读预览，C++ 按 `presentation -> domain/ports <- adapters` 单向依赖拆分，QML 只访问 `InstallerSession`，默认 `DisabledInstallationExecutor` 始终拒绝执行；`InstallationPlan` 固定 Debian trixie amd64、ext4、稳定系统盘身份、危险动作确认和非目标磁盘保护条件。已加入 CMake 严格告警构建、固定 `.clang-format`、clang-tidy analyzer/bugprone/performance/portability 静态分析、6 项 Qt Test、QML lint、自动依赖边界检查、Debian `debian/` source package、隔离临时目录镜像脚本、LightDM/Openbox Live 会话和 GitHub CI 安装器门禁；CI 已取消 push 分支过滤，任意分支每次 push 都会执行 ShellCheck、C++ 格式、边界、Release 构建、clang-tidy、QML lint、测试、安装布局和 `live-build` 配置检查。本机使用 Qt 6.11.1 与 Qt Creator 内置 LLVM 22.1.8 在全新构建目录验证上述 C++/Qt 门禁、6 项测试、Shell 语法、CI YAML 和无界面 QML 运行加载通过。当前 macOS 没有 `live-build`、`dpkg-buildpackage` 与 ShellCheck，但 Debian trixie amd64 测试构建机现已完成真实二进制包构建；`live-build`、ISO 和 QEMU 启动仍未验证，不能标记为可安装发行版
- GitHub CI 已完成 Qt/C++ Debian 安装器在 Ubuntu 24.04 Runner 上的兼容性回归：运行 `30680454537` 暴露的 7 处 ShellCheck `SC1007` 已由提交 `49b9bf4` 修复；运行 `30680592242` 暴露的 Qt 6.4 不支持 `QQmlApplicationEngine::loadFromModule` 已由提交 `59be985` 改为固定 `qrc:` URL，并加入无界面启动冒烟；运行 `30680777594` 与 `30682762095` 暴露的 QML 模块拆包依赖已由提交 `1406539`、`5fe540f` 补齐并同步 Debian 运行依赖；运行 `30682998784` 暴露的 Ubuntu 旧版 `live-build` 参数差异已由提交 `8d6a932` 按能力选择兼容语法。最终运行 `30683248128` 的 Qt/C++ job 已确认 ShellCheck、C++ 格式、依赖边界、Release 严格告警构建、clang-tidy、QML lint、无界面启动、6 项 Qt 测试、安装布局和 `live-build` 配置验证全部通过；真实 Debian trixie 包构建已由独立测试构建机补充验证，ISO 构建与 QEMU 启动仍未执行
- 已在 `test_build@192.168.188.10` 的 Debian 13 trixie amd64 环境建立单机构建与签名发布链路：源码以 `devkihon` 提交 `47993b6` 拉取到 `/var/lib/amseokos-build/src/AmseokOS`，构建暂存、root 专用签名目录和 Nginx 公开仓库分别隔离在 `/var/lib/amseokos-build`、`/var/lib/amseokos-signing/gnupg` 和 `/srv/amseokos/apt`。安装器依赖边界、C++ 格式、ShellCheck、Release 构建、clang-tidy、Qt Test、普通用户 offscreen 冒烟与仓库自带 `dpkg-buildpackage` 均通过，生成 `amseokos-installer_0.1.0_amd64.deb`，SHA-256 为 `c20641c84666b03ed4d9d3cca49120b74dc7992280f3f3954c4bab38760ba0cd`。测试仓库使用独立 Ed25519 OpenPGP 密钥签名，指纹为 `313FE99FB604BC0F7DF0EE5C2DF4F37A9B11EEDB`，私钥目录为 root `0700` 且不在 Web 根目录；`InRelease`、`Release.gpg`、`SHA256SUMS.asc`、30 天 `Valid-Until`、独立临时 APT 状态目录的 `apt-get update`、局域网客户端 HTTP 200 下载与下载后 SHA-256 均已验证，Nginx 已 active/enabled，仓库地址为 `http://192.168.188.10/apt/`
- 已在 `test_nas@192.168.188.7` 通过 `signed-by=/usr/share/keyrings/amseokos-archive-keyring.gpg` 接入上述测试仓库并安装 `amseokos-installer 0.1.0 amd64`；APT 从 `.10` 获取签名索引和包并从 Debian trixie 获取 161 个 Qt/图形运行依赖，未升级或移除现有包。安装后 `dpkg -V` 无差异、`apt-get check` 通过、`/usr/bin/amseokos-installer` 为 root 所有的 `0755`，以 `test_nas` 普通用户和 `QT_QPA_PLATFORM=offscreen` 执行 `--windowed --smoke-test` 返回成功；真实安装执行器仍为关闭状态，本次未执行磁盘探测、分区、格式化或其他安装写盘动作
- 已将 Debian 部署扩展为完整单节点包集：`amseokos-web`、`amseokos-api`、`amseokos-privileged`、`amseokos-terminal`、`amseokos-console`、`amseokos-infrastructure` 和精确版本依赖的 `amseokos` 元包。`.10` 使用经官方 SHA-256 校验的 Node.js 22.22.3、npm 10.9.8、.NET SDK 10.0.302、Rust 1.97.1 与 musl 目标，基于提交 `47993b6` 通过 Angular ESLint、29 项测试、覆盖率门槛、production build、后端测试与 linux-x64 publish、privileged 20 项 Rust 测试、Terminal 4 项 Rust 测试、两套格式/Clippy `-D warnings`/musl Release 构建和控制台测试，生成最终版本 `0.1.0+git20260807.47993b6.5`。七个包已发布到 `.10` 的 `/srv/amseokos/apt` HTTP 仓库，`InRelease`、`Release.gpg` 和 `SHA256SUMS.asc` 均由指纹 `313FE99FB604BC0F7DF0EE5C2DF4F37A9B11EEDB` 签名并通过独立公开 keyring 的 `gpgv` 验证
- `.7` 已从 `http://192.168.188.10/apt/pool/main/a/amseokos/` 使用 curl 下载上述七个最终 deb，在安装前验证 `SHA256SUMS.asc` 签名和全部 SHA-256，再由本地 deb 文件安装。PostgreSQL 17、etcd 3.5.16、NATS 2.10.27 JetStream、privileged daemon、Terminal broker、API、Nginx 和 tty1 控制台均 active/enabled，人工整组重启后 `ExecMainStatus=0`、`NRestarts=0`；PostgreSQL、etcd、NATS 监控、NATS 客户端、API 仅监听 loopback，Nginx 以目标机生成的自签证书监听 HTTPS 6521。聚合 `/health/ready`、etcd health、JetStream health、前端和 API 代理均返回健康/200；PostgreSQL 有 15 张应用表，三个秘密环境文件为 `root:root 0600`，两个 Unix Socket 为服务专用账户所有的 `0660`，`dpkg -V`、`apt-get check`、失败 unit 和近期 error journal 检查均通过。以 `amseoknas-api` UID 真实执行 `system.getAbout`、`network.inspectInterfaces`、`storage.inspectBlockDevices`、`raid.inspectArrays` 及低权限 Terminal PTY 命令全部成功，独立工作站访问地址为 `https://192.168.188.7:6521/`
- 已新增适用于 Debian 13 trixie amd64 节点的单文件安装器：任意节点可通过 curl 或 wget 从 `.10` 获取脚本，脚本先使用仓库公开 keyring 验证 `InRelease`，再配置 `signed-by` APT 源、下载精确版本依赖的七包集合、生成节点本地 TLS/数据库/NATS 配置、启用全部服务并执行健康检查；管理 IPv4 默认根据到仓库的路由自动识别，也可显式覆盖。发布脚本会把安装器纳入签名 `SHA256SUMS.asc`，并支持以仓库 pool 内已有 deb 幂等重建索引。`.7` 已真实验证显式 IP、自动 IP、重复安装不轮换 API/NATS/TLS 秘密、旧 APT source 迁移、API 与 NATS `SIGKILL` 后自动恢复，以及整机重启后七包、前端、API、PostgreSQL、etcd、NATS、两个 Unix Socket 和 tty1 控制台全部恢复；实际 PostgreSQL 集群及所有长驻服务均具有 `Restart=on-failure` 或更强策略，失败 unit 为 0
- 已新增默认关闭且与正式安装链路隔离的 Qt/QML 开发者实时预览：独立 `DeveloperPreview.qml` 只提供纯 QML 模拟会话、模拟系统盘、步骤导航与安装反馈，不创建真实执行器、不访问设备；正式构建既不打包该 QML，也不编译 `--developer-preview` 参数。`scripts/preview.sh` 会配置独立 Debug 构建并通过 Qt `qmlpreview` 启动，保存 QML 后可实时刷新；GitHub CI 增加预览构建、QML lint 和无界面冒烟。本机 Qt 6.11.1 已实际连接 `qmlpreview` 并完成人工界面检查，正式/预览严格告警构建、QML lint、clang-tidy、依赖边界、Shell 语法、正式无界面启动、预览无界面启动及正式 7 项/预览 2 个 CTest 条目均通过；本次 GitHub 托管 Runner 尚未执行
- 已将 Qt/QML 安装器整体视觉改为 macOS 安装器式深色布局：纯黑全屏背景承载居中的深灰圆角面板，欢迎页使用原创圆形 AmseokOS 数字插画、`AmseokOS 安装程序` 标题和蓝色安装按钮，系统盘、安装摘要、步骤栏与后续操作区同步采用深色配色；按钮只调用既有 `InstallerSession` 导航，未改变领域模型、执行器或真实写盘默认关闭边界。插画由内置图像生成工具制作，透明裁切后作为 768×768 PNG 编入 Qt 资源，不包含 Apple Logo、macOS 图案或第三方字体。本机已通过正式/开发者预览构建、QML lint、正式 7 项 Qt 测试、预览 2 个 CTest 条目、依赖边界和无界面启动，并实际打开开发者预览完成窗口截图视觉检查；GitHub 托管 Runner 尚未执行本次改动
- GitHub 托管 Runner 首次执行开发者预览配置时确认 Ubuntu 环境没有 `qmlpreview`，原 CMake 将仅供本地热刷新的工具错误设为预览构建硬依赖，导致配置阶段失败；现已将 `qmlpreview`/`qmlpreview6` 改为可选工具，缺少时仍可编译预览二进制并执行无界面冒烟，只有显式运行 `developer-preview` 热刷新目标才返回清晰错误。本机已模拟无 `qmlpreview` 环境验证配置、构建、QML lint 和预览冒烟通过，并确认已安装工具时实时预览目标保持可用；GitHub 托管 Runner 修复后复跑仍待提交推送验证
- 已新增并在 `.7` 部署目标系统本地控制台状态页：`tty1` 通过独立 systemd 服务循环显示 AmseokOS 品牌、主机名、Web 管理地址、版本和网络状态，`tty2` 至 `tty6` 保留 Debian 维护登录；服务只有在 `/etc/amseoknas/console-enabled` 标记存在时才替代 tty1 getty，进程使用动态用户、空 capabilities、只读系统、受限地址族，并通过 `DevicePolicy=closed` 只放行 `/dev/tty1`。控制台脚本支持无副作用 `--preview`/`--once`，环境文件可配置标题、提示语、端口和刷新间隔；本机测试、Linux `systemd-analyze verify`、`.7` 真实 tty 服务启动和整组重启恢复均通过，`ExecMainStatus=0`、`NRestarts=0`。当前真实安装器仍未写入目标 rootfs，ISO/QEMU 中的首次启动显示仍需后续验证
- 已完成 AmseokOS 命名迁移第一阶段：7 个顶层模块目录统一为 `AmseokOS-<用途>`，.NET 解决方案改为 `AmseokOS.sln`，Web 工程包标识改为 `amseok-os-web`，CI、Qt clang-tidy、构建命令和开发文档全部切换到新路径；为避免破坏已部署系统，本阶段明确保留 `Nas.*` 程序集与命名空间、`amseoknas-*` 包/服务/账户/Socket、旧 Cookie 名和管理员初始化凭据兼容行为，并在 `AmseokOS-docs/naming-migration.md` 记录后续迁移门禁。新路径下 .NET Release 构建为 0 警告/0 错误且 56 项 xUnit 全部通过，Qt Debug 构建、正式测试和开发者预览冒烟 2 项通过，两套 Rust 均通过 `aarch64-unknown-linux-gnu` 目标检查，安装器边界、控制台测试、Shell 语法、CI YAML、前端包 JSON 和旧仓库路径扫描通过；前端依赖安装、Angular lint/测试/构建及 Linux systemd/Compose 仍待 CI 验证
- 已完成 RAID 开发第一步的只读清单代码闭环：Rust privileged daemon 新增固定 `storage.inspectBlockDevices` 与 `raid.inspectArrays` 动作，从 `/sys`、`/proc` 和 udev 数据返回物理盘身份、分区、直接挂载、swap、MD 成员及现有阵列状态/成员/同步进度，不执行 `mdadm` 或任何写命令；C# 新增独立 Storage Application 端口、共享 Unix Socket 适配器、`storage.read` 授权策略和 `GET /api/storage/disks`、`GET /api/raid/arrays` 只读 API，Controller 仅做 HTTP 授权、映射和脱敏错误处理。存储拓扑现已沿 sysfs `holders` 传递追踪 MD、dm-crypt、LVM 与通用 device-mapper，根文件系统或管理目录经过多层设备后仍会向底层盘传播 `systemDevice`、`inUse` 和 swap 状态；API 同时返回依赖设备类型、`identityConflict` 与失败关闭的 `topologyComplete`，重复硬件身份不再视为稳定，旧 daemon 缺少拓扑字段时 Infrastructure 也会规范化为 `inUse=true`。伪 sysfs 集成测试覆盖 dm-crypt→LVM→根挂载，独立存储模块 11 项测试、8 项相关 xUnit、排除项目原有 macOS 终端 Socket 长路径用例后的 23 项 xUnit、Linux 目标编译检查和 Clippy `-D warnings` 通过。当前尚未完成 SMART、前端磁盘列表、真实 loop device/测试机设备验证或 RAID 写操作
- Data Protection 外部密钥保护、密码修改后旧密码失效的部署验证、会话跨重启持久化验证、PostgreSQL HA、etcd Leader/fencing 应用接入、NATS Inbox/Outbox 工作者、登录态浏览器中的系统/网络/磁盘/RAID 查询与 Terminal WebSocket 人工回归、网络配置执行与自动回滚仍未完成，第一阶段验收条件尚未满足

下一建议工作项：先审查并提交本次新增的 Web/API Debian 打包、签名发布、Nginx、systemd 与测试节点配置脚本，补发布脚本重复执行、失败不破坏旧索引、包版本递增和全新 Debian 节点安装验证；再为测试 APT 仓库配置 HTTPS，并完成签名私钥的离线加密备份与轮换演练。若要求 `/health/ready` 通过，下一次部署应按单节点基础设施文档在 `.7` 安装并加固 etcd 与 NATS，而不是绕过就绪检查。随后在 `.10` 安装 `live-build`，执行 `validate-live-config.sh` 和只读 ISO 构建，再使用 QEMU 覆盖 BIOS 与 UEFI 启动、中文字体、软件渲染和全屏自动进入安装器；在稳定设备枚举、系统盘保护、安装计划二次确认及其测试完成前，不得替换 `DisabledInstallationExecutor` 或加入真实磁盘写入。并行由人工在 OneDev 的 `main` 页面点击“构建 Debian 测试包”完成一次流水线回归，核对 5 个拆分包、OneDev 制品、存储目录和签名 APT 索引；随后为 OneDev 配置 HTTPS，完成 OneDev 持久卷备份与 Git SSH 端到端验证，并观察低配 `console` 在持续构建和制品增长时的内存与磁盘使用。同时在具备 .NET 10 SDK 的仓库工作区运行完整 xUnit，确认 Web Terminal 会话用例、Relay 拆分、关闭预算和故障分类通过，并补一次真实 HTTP 创建会话和 WebSocket 双向交互回归；随后观察 `devkihon` 首次 GitHub Actions 运行，确认 Compose/systemd、依赖变更审查、固定版本 `cargo-audit` 和 CodeQL 在托管 Runner 上通过；再由独立构建机生成 Rust、.NET 与 Angular Release 产物，将只读 daemon 以专用账户安装到 `nastest`，按实际 API 服务 UID 写入仓库外 `privileged.env`，启用 C# `Privileged` 配置并完成浏览器端“关于本机/网络/磁盘/RAID”端到端验证，同时确认其他账户和 Terminal 用户无法连接 Socket。RAID 下一步在隔离 Linux 环境使用 loop device 建立真实的 MD、LUKS 和 LVM 组合，验证系统盘传播、swap、缺失 holder、身份冲突及重启后拓扑恢复，再实现前端只读磁盘列表和 SMART 摘要；以上门禁全部通过后，再设计 `mdadm` 预检与操作预览，仍不得直接开放创建、删除、扩容或替换动作。网络下一步在已建立的 C# 预览边界后选择并锁定首版受支持的网络管理器，再实现 Rust 结构化白名单写入、配置备份、旧/新 IP 并行生效、连通性确认、超时自动回滚、Operation、资源锁和审计；这些门禁通过前，预览必须继续返回不可应用，且不得启用当前前端按钮。仍需完成 Web Terminal 浏览器端回归、共享 Data Protection 密钥保护及会话跨重启验证

最新下一建议工作项（覆盖上方已部分完成的旧建议）：补签名发布过程的原子索引切换、旧包保留策略和一台全新 Debian 13 amd64 节点的一次性安装验证，并为测试 APT 仓库配置 HTTPS、离线加密备份签名私钥和演练轮换。应用侧继续完成当前管理员登录态下的浏览器“关于本机/网络/磁盘/RAID”与 Terminal WebSocket 回归，再推进 Data Protection 外部密钥保护、会话跨重启、etcd Leader/fencing、NATS Inbox/Outbox 和网络安全回滚；隔离 Linux 环境中的真实 MD/LUKS/LVM 组合与危险写操作门禁仍不得省略，当前存储和网络写操作继续关闭

每次完成工作后，AI 应更新本节中的最后检查日期、当前阶段、已完成项、验证结果和下一建议工作项，但不得把未经验证的内容标记为完成

## 3. AI 开始工作前的强制检查

AI 每次接手任务必须依次完成以下检查：

1. 阅读本文件以及项目中所有适用的 `AGENTS.md`、开发约定和模块文档
2. 查看项目顶层目录和相关模块结构，优先使用 `rg --files`
3. 查看 `git status --short --branch`、最近提交和当前分支，若不是 Git 仓库则明确记录
4. 查找解决当前问题所需的既有接口、模型、状态机、权限、错误码、日志、数据库访问方式和测试风格
5. 运行与当前模块相关的最小构建或测试，确认开始修改前的基线状态
6. 对照第 9 节验收标准判断项目达到哪个阶段和哪个子项
7. 选择下一项最小且可以完整交付的工作，不得仅凭文件存在就判断功能已经完成
8. 修改后重新运行相关构建、测试、静态检查或集成验证
9. 更新本文件的项目进展，但只记录已由验证结果证明的状态
10. 输出供人参考的 Commit 和 PR 文案，不执行提交、推送或创建 PR

若项目实际实现与本文架构冲突，AI 应先说明冲突及风险，再优先做兼容现有项目的最小改动；除非任务明确要求，不得擅自进行大规模重写

## 4. 人工提交与 Git 边界

所有代码必须由人最终审查并提交

AI 禁止执行以下操作：

- `git add`
- `git commit`
- `git push`
- 创建、合并或关闭 PR
- 修改远端分支
- 为了清理工作区而丢弃用户改动

AI 可以执行只读 Git 命令，用于理解当前状态、差异和历史

只有用户在当前任务中明确授权具体提交范围、远端和分支时，AI 才能为该次任务执行暂存、提交和推送；该授权仅对当次任务有效，不得扩展到后续改动，也不得绕过提交前检查

SSH 测试与部署目标机是上述授权的绝对例外。只要当前 Git 工作区位于通过 SSH 连接的测试环境或部署环境，包括兼具构建、签名、仓库发布职责的目标机，就一律禁止在该机器执行 `git add`、`git commit`、`git push`、会创建提交的 `git merge`、`git rebase`、`git cherry-pick`、创建 Git tag、修改远端分支或任何会生成本地提交的操作；即使用户在当前任务中明确要求提交，也必须返回本地开发工作区完成审查和提交，不得在目标机破例

测试与部署目标机只允许执行理解状态所需的只读 Git 命令，以及不会创建本地提交的源码同步操作；更新远端已有提交时必须使用可验证的 fast-forward-only 流程，例如先 `git fetch` 再验证目标提交并执行 `git merge --ff-only`，或使用显式 `git pull --ff-only`，不得通过普通 `git pull` 隐式产生 merge commit。未提交改动必须先回到本地开发工作区纳入版本控制，再以构建产物、软件包或明确的部署文件同步到目标机

测试源码应与对应实现一同提交、审查并由 CI 执行

测试临时文件、测试结果和覆盖率产物必须由 `.gitignore` 排除，并在提交前确认未进入暂存区

### 4.1 提交与交付语言

Commit 标题与正文、PR 标题与正文、变更说明、发布说明和 AI 结束工作时的交付文案默认以中文为第一语言

只有用户在当前任务中明确要求使用英文时，才对用户指定的 Commit、PR 或交付内容改用英文；该要求仅对当前任务和指定内容有效，不改变后续任务的中文默认规则

Conventional Commit 的 `type`、`scope` 以及代码标识符、文件路径、命令、协议名和无法准确翻译的技术术语可以保留英文，不视为违反中文优先规则

### 4.2 提交前强制检查

任何提交或推送前必须逐项检查：

1. 使用 `git status --short --branch` 确认分支、修改范围和未跟踪文件
2. 使用 `git diff --cached` 逐文件审查实际待提交内容，不允许使用未经审查的全量暂存
3. 确认测试结果、覆盖率文件和临时测试数据未进入暂存区，并确认测试源码变更与对应实现相关
4. 确认 `bin`、`obj`、`dist`、`build`、`node_modules`、IDE 缓存和其他构建产物未进入暂存区
5. 确认日志、缓存、临时文件、系统垃圾文件和编辑器备份未进入暂存区
6. 扫描密码、私钥、证书私钥、Token、API Key、Access Key、Secret、连接字符串和真实环境变量
7. 检查 `.env`、本地配置、云服务凭据、SSH 密钥、签名密钥和生产配置是否被误加入
8. 对疑似敏感信息逐项人工判断，不能仅依赖 `.gitignore` 或自动扫描结果
9. 确认 Git 作者姓名和邮箱符合当前仓库要求
10. 运行适用的构建、静态检查和本地测试，记录实际结果，但不得提交测试产物和构建产物
11. 再次检查暂存区文件列表和差异，确认没有无关改动后才允许提交
12. 推送前确认远端地址、目标分支和提交历史，禁止覆盖远端或进行未经授权的强制推送

发现任何无法确认的密钥、Token、API 凭据或生产配置时必须停止提交和推送，先通知用户处理

AI 完成一个可交付工作项后，必须提供：

- 建议的 Commit 标题
- 建议的 Commit 正文
- 建议的 PR 标题
- 建议的 PR 内容
- 已执行的验证命令及结果
- 未验证事项和剩余风险

建议 Commit 格式：

```text
<type>(<scope>): <简洁说明>

<为什么修改>
<主要实现>
<必要的兼容性或风险说明>
```

`type` 优先使用 `feat`、`fix`、`refactor`、`test`、`docs`、`build` 或 `chore`

PR 标题应描述本次交付解决的问题，不能使用“更新代码”“若干修改”等模糊表述

PR 内容必须至少包含以下部分：

```markdown
## 原有项目内容

说明修改前已有的架构、功能、行为和限制，只写与本次改动相关的事实

## 改动内容

说明本次修改的文件、接口、流程、状态和测试，不夸大未完成能力

## 解决的问题

说明原先无法完成、容易出错、不安全或体验不好的具体点，以及现在如何改善

## 验证

列出实际执行的命令、测试场景和结果

## 风险与后续工作

说明兼容性、未覆盖场景、迁移要求和下一建议工作项
```

## 5. 总体架构

推荐技术组合：

- 前端：Angular + TypeScript
- 后端控制面：ASP.NET Core / C#
- 特权执行面：独立 Rust privileged daemon
- 系统底层：Debian Linux + apt/deb 包
- 浏览器实时通信：SignalR / WebSocket
- 节点命令与事件：NATS JetStream
- 节点本地持久化：SQLite
- 集群全局持久化：PostgreSQL HA
- Leader 选举与租约：etcd
- 部署方式：systemd 服务 + Nginx 反向代理
- 应用中心：后续阶段可选 Docker，不属于第一阶段

总体调用关系：

```text
浏览器
  -> Angular Web UI
    -> REST API / SignalR
      -> 任意对等 NAS 的 ASP.NET Core Web API（普通系统用户）
        -> 当前控制面 Leader
          -> PostgreSQL HA（全局期望状态、身份和调度）
          -> etcd（选举、租约和 fencing token）
          -> NATS JetStream（节点命令与事件）
            -> 目标 NAS 的 SQLite（本地实际状态和执行记录）
              -> Unix socket
                -> privileged daemon（受限特权、结构化白名单动作）
                  -> Debian 系统工具和服务
```

前端只负责展示和交互，不直接操作 Linux 系统

ASP.NET Core Web API 负责认证、授权、业务编排、期望状态、任务状态、审计和对外 API，不得长期以 root 身份运行

privileged daemon 负责磁盘、RAID、挂载、服务和系统配置等特权动作，不接受任意 shell、任意程序路径或未经约束的参数

Debian 负责真正执行 SMB、NFS、mdadm、SMART、systemd、Docker、网络、日志和电源等底层能力

## 6. 前端架构

推荐目录：

```text
AmseokOS-web/
  src/app/
    core/
      auth/
      guards/
      interceptors/
      layout/
      services/
    shared/
      components/
      directives/
      pipes/
      models/
    shell/
      desktop/
      dock/
      window-manager/
      app-launcher/
    features/
      dashboard/
      storage/
      shares/
      users/
      network/
      services/
      docker/
      backup/
      logs/
      settings/
    app.routes.ts
    app.config.ts
```

`core` 保存认证、HTTP 拦截器、权限守卫、全局布局和 SignalR 等全局单例能力

`shared` 保存真正跨业务复用的组件、指令、管道和公共模型，不应成为无边界的杂物目录

`shell` 保存桌面、Dock、启动台、窗口管理器、顶部菜单栏和通知中心

`features` 按 Dashboard、Storage、Shares、Users、Network、Services、Docker、Backup、Logs 和 Settings 等业务能力拆分

推荐调用方向：

```text
Page Component
  -> Feature Service
    -> API Client
      -> ASP.NET Core REST API
```

前端必须覆盖加载、空数据、错误、权限不足、断线和危险操作确认状态，并满足基本键盘操作与可访问性要求

SignalR 用于状态变化通知，REST API 是最终状态来源，断线重连后必须通过 REST 重新同步

## 7. 后端架构

推荐目录：

```text
AmseokOS-server/
  src/
    Nas.Api/
      Controllers/
      Hubs/
      Middleware/
    Nas.Application/
      SystemStatus/
      Storage/
      Shares/
      Users/
      Operations/
      Backup/
    Nas.Infrastructure/
      Persistence/
      Linux/
      Samba/
      Systemd/
      Smart/
      Mdadm/
      FileSystem/
    Nas.Domain/
      Models/
      Permissions/
      Operations/
      Events/
  tests/
```

推荐增加独立 Rust 特权进程，详细边界见 `AmseokOS-docs/csharp-rust-privileged-architecture.md`：

```text
AmseokOS-privileged/
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
  tests/
```

`Nas.Api` 只处理 HTTP、SignalR、认证入口、中间件和协议转换，不在 Controller 中直接调用 `Process` 或写系统配置

`Nas.Application` 负责编排业务用例、权限决策、操作状态和资源锁

`Nas.Infrastructure` 负责 PostgreSQL 与 SQLite 持久化、etcd 与 NATS 集成、受管配置生成以及通过强类型客户端与 Rust privileged daemon 通信；Linux 系统查询和特权动作不得绕过该边界

`Nas.Domain` 保存核心模型、权限、统一状态机、错误类型和领域事件，不依赖外层项目

优先复用明确接口，例如：

```text
IPrivilegedClient
ISystemdService
ISambaConfigWriter
IMdadmService
ISmartService
IBlockDeviceService
IOperationRepository
IResourceLockService
```

## 8. 核心设计约束

### 8.1 单一存储路线

第一版固定使用 `mdadm + ext4`

Btrfs 可在后续独立路线中评估，必须同时设计巡检、平衡、容量和故障恢复策略

ZFS 应作为独立版本或独立产品路线，不得作为可随意混用的可选包加入第一版

第一版不在正常存储流程中引入 LVM，除非后续需求明确且完成独立设计

推荐资源关系：

```text
PhysicalDisk
  -> Partition
    -> RaidArray（可选）
      -> FileSystem
        -> Mount
          -> Share
```

每种资源必须定义稳定标识、可执行动作、前置条件、冲突资源、销毁条件、异常发现方式和恢复策略

### 8.2 稳定设备标识与系统盘保护

禁止把 `/dev/sda`、`/dev/sdb` 等易变化名称作为数据库主标识或危险操作的唯一目标

优先使用：

- `/dev/disk/by-id/...`
- 文件系统 UUID
- PARTUUID
- mdadm UUID
- LVM UUID，仅在未来启用 LVM 时使用

API 只接收后端签发的设备 ID，后端和 privileged daemon 在执行前都必须重新解析并复核序列号、容量、当前路径、挂载、RAID 和占用状态

第一版 Web UI 必须完全禁止对以下设备执行破坏性操作：

- 根文件系统所在磁盘
- `/boot` 和 EFI 所在磁盘
- 管理数据库和配置目录所在卷
- swap 所在设备
- 已挂载设备及其父设备
- RAID、LUKS 或未来 LVM 的任何关联设备

### 8.3 统一 Operation 模型

项目统一使用 `Operation` 表示格式化、RAID、配置应用、备份和恢复等异步操作，不再并行维护语义重复的 Task 模型

建议状态：

```text
Queued
WaitingForLock
Running
Cancelling
Succeeded
Failed
Cancelled
Interrupted
```

周期性计划使用 `JobDefinition` 表示，每次运行产生一个 Operation

每个 Operation 至少保存：

- 操作类型和操作者
- 目标资源稳定 ID
- 请求参数快照和幂等键
- 当前阶段、进度和状态
- 创建、开始和结束时间
- 退出码、脱敏后的输出和错误
- 资源锁和确认记录

资源锁使用持久化租约和续约机制，进程异常退出后不得自动重复破坏性命令，必须先对账实际系统状态

取消只允许用于底层明确支持安全中断的阶段

### 8.4 危险操作两阶段确认

格式化、删除阵列、移除磁盘、删除卷、恢复备份、修改网络、关机和重启等动作使用预检与执行两阶段流程

预检至少返回规范目标、预计步骤、受影响资源、风险、阻断原因、确认短语和短期一次性令牌

令牌必须绑定用户、操作类型、目标资源、参数摘要和过期时间，并在使用或目标状态变化后失效

执行前重新检查权限、管理员重新认证、设备身份、占用状态、资源锁和系统盘保护

危险操作不能仅依赖前端弹窗，预览、确认、执行、取消、失败和回滚必须进入审计日志

### 8.5 配置事务与状态对账

Samba、NFS、Nginx、systemd 和挂载配置应使用 NAS 自己管理的 include 文件或 drop-in，避免直接覆盖用户主配置

推荐流程：

```text
读取当前版本
  -> 根据期望状态生成临时配置
  -> 语法和业务校验
  -> 备份当前配置
  -> 同文件系统原子替换
  -> reload 或 restart
  -> 状态与功能健康检查
  -> 成功提交版本，失败回滚并再次校验
```

不同服务使用对应校验器，例如 Samba 使用 `testparm`，Nginx 使用 `nginx -t`，systemd 使用 `systemd-analyze verify`

PostgreSQL 保存集群全局期望状态，节点 SQLite 保存本地执行记录和实际状态快照，Debian 查询结果代表当前实际状态

系统启动时进行状态对账，发现人工修改时先告警，由管理员选择导入当前配置或重新应用期望状态，不得默认覆盖

### 8.6 privileged daemon 安全边界

daemon 仅提供固定结构化动作，不接受任意 shell、命令名、程序路径、环境变量或任意 systemd unit

必须实施：

- Unix socket 文件权限和 peer credentials 校验
- 参数数组调用，不经过 shell 拼接
- 固定二进制绝对路径和最小环境变量
- 每个动作独立的参数验证、超时、取消和输出上限
- daemon 端再次进行设备身份、系统盘和占用状态复核
- 敏感输出脱敏和结构化审计
- 最小权限原则，能拆分的权限不统一授予 root 能力

### 8.7 认证、授权与审计

认证和授权是两项独立检查

推荐角色：

- `admin`
- `storage-manager`
- `user-manager`
- `app-manager`
- `viewer`

推荐权限点：

```text
storage.read
storage.write
storage.format
raid.manage
share.read
share.manage
user.read
user.manage
network.read
network.manage
service.read
service.manage
docker.read
docker.manage
backup.read
backup.manage
system.reboot
system.shutdown
logs.read
```

Web 账户使用 ASP.NET Core Identity 管理，账户、密码哈希、角色和权限以 PostgreSQL 为全局事实源；密码只保存不可逆哈希，不保存明文或可逆密文，节点 SQLite 不保存全量账户密码哈希

所有控制面实例共享受静态加密保护的 ASP.NET Core Data Protection 密钥环。Cookie 设置 `HttpOnly`、`Secure` 和合理的 `SameSite`，所有状态修改接口实施 CSRF 防护

必须包含登录限速、失败处理、会话撤销、首次启动设置管理员密码、高危操作重新认证和不可由普通用户修改的审计记录

### 8.8 PostgreSQL 与 SQLite 持久化

数据库详细设计见 `AmseokOS-docs/database-architecture.md`

PostgreSQL 是集群身份、权限、全局期望状态和调度的事实源；每个节点的 SQLite 是本机实际状态、Operation 执行、资源锁、Inbox 和 Outbox 的事实源。两者不使用分布式事务，通过 NATS JetStream、幂等键、版本号和状态对账同步

SQLite 启用独立迁移、WAL、定期备份、完整性检查和严格文件权限；PostgreSQL 启用独立迁移、备份、恢复验证和高可用切换。所有数据库、消息和仲裁数据不得放在普通共享目录

单节点部署仍运行 PostgreSQL、SQLite、单成员 etcd 和单节点 NATS JetStream，保持与集群模式相同的数据所有权和协议边界；高可用模式使用 3 个或 5 个投票成员，两个节点不得启用无人值守自动选举

大体积命令输出不得无限写入 PostgreSQL 或 SQLite，应实施长度限制、轮转或受控文件存储

### 8.9 API 约定

建议基础 API：

```text
GET  /api/system/status
GET  /api/system/metrics
GET  /api/storage/disks
GET  /api/storage/filesystems
GET  /api/storage/smart/{deviceId}
GET  /api/raid/arrays
GET  /api/shares
POST /api/shares
PUT  /api/shares/{id}
DELETE /api/shares/{id}
GET  /api/users
GET  /api/groups
GET  /api/services
GET  /api/logs/audit
POST /api/operation-previews
POST /api/operations
GET  /api/operations/{id}
POST /api/operations/{id}/cancel
```

危险写入使用异步 Operation，返回 `202 Accepted` 和资源位置

写接口统一考虑幂等键、乐观并发版本、结构化错误码、correlation ID 和审计事件 ID

服务接口只接受后端登记的受管服务 ID，不允许调用方传入任意 systemd unit 名称

### 8.10 可靠性与备份原则

RAID 不是备份，不能防止误删、勒索软件、应用错误、整机损坏和灾害

SMART 正常也不能保证磁盘不会突然故障

系统必须分别考虑：

- RAID 降级、换盘和重建
- 配置数据库、系统配置、密钥和用户映射备份
- 恢复流程演练
- 重建期间告警和高 IO 任务限制
- 服务崩溃、系统重启和断电后的状态对账

## 9. 第一至第四阶段实施计划

阶段必须按顺序推进，只有当前阶段的验收条件全部满足后，才能把进度标记为进入下一阶段

用户明确要求的紧急修复可以插入，但 AI 必须说明它与当前阶段的关系

### 第一阶段：安全底座与最小可观察系统

目标：建立能够安全继续扩展的项目骨架，不进行破坏性磁盘操作

实施顺序：

1. 初始化 Angular、ASP.NET Core、测试项目和本地开发配置
2. 建立 Domain、Application、Infrastructure、Api 分层和依赖方向测试
3. 实现 PostgreSQL 与 SQLite 独立迁移、SQLite WAL、基础备份、启动完整性检查和严格文件权限
4. 使用 ASP.NET Core Identity 实现首次启动管理员、密码哈希、登录、会话、权限和审计骨架，并配置共享且受静态加密保护的 Data Protection 密钥环
5. 建立单成员 etcd Leader 租约和 fencing token，以及单节点 NATS JetStream、Inbox、Outbox 和幂等消费骨架
6. 实现系统状态和物理磁盘只读查询，统一稳定设备 ID
7. 建立 Operation 状态机、PostgreSQL 全局仓储、SQLite 节点仓储、幂等和资源锁基础结构
8. 建立 privileged daemon 与 Unix socket 强类型协议，但第一阶段只开放无破坏性的查询或受控测试动作
9. 实现系统盘识别和保护规则，并用测试覆盖多层块设备关系
10. Angular 实现登录、Dashboard、磁盘只读列表、错误状态和断线恢复
11. 提供 PostgreSQL、etcd、NATS、systemd 与 Nginx 的单节点部署草案和健康检查

第一阶段验收条件：

- 前后端可构建并通过基础测试
- 管理员可以安全登录和退出
- 未授权和无权限请求被正确拒绝
- 可以只读查看系统状态、磁盘稳定 ID 和 SMART 摘要
- 系统盘和不可靠设备会被明确标记且无法进入危险流程
- Operation 可创建测试任务、持久化、恢复和审计
- 单节点模式使用 PostgreSQL、SQLite、etcd 和 NATS 的正式数据与协议边界，Leader 租约过期后旧 fencing token 被拒绝
- Web API 以普通用户运行且不能直接执行任意系统命令
- 重启服务后 PostgreSQL 全局状态、SQLite 节点状态和只读系统状态能够恢复

### 第二阶段：mdadm + ext4 + SMB 纵向闭环

目标：打通唯一一条可靠的数据卷和共享流程

实施顺序：

1. 完成危险操作预检、一次性令牌、重新认证和执行接口
2. privileged daemon 增加受限的分区、mdadm、ext4、挂载和 Samba 动作
3. 实现空闲磁盘判定、资源锁和执行前二次设备复核
4. 实现 mdadm 创建、发现、停止和系统重启后重新组装
5. 实现 ext4 创建、UUID 挂载和 NAS 受管挂载配置
6. 实现 NAS 用户、用户组和 SMB 访问策略
7. 使用受管 include 生成 Samba 配置，执行 `testparm`、原子替换、reload、健康检查和回滚
8. 实现 Angular Storage、Operations 和 Shares 页面及实时进度
9. 覆盖失败、重复提交、浏览器重试、服务重启和并发冲突场景

第二阶段验收条件：

- 能从合格空闲磁盘创建 mdadm 阵列或单盘 ext4 数据卷
- 所有破坏性动作经过预检、确认、重新认证、资源锁和审计
- 能按 UUID 挂载并在系统重启后恢复
- 能创建 SMB 共享并由实际客户端访问
- 配置错误不会破坏原有可用配置
- Operation 失败或服务重启不会自动重复破坏性命令
- 同一资源上的冲突操作会被拒绝或等待

### 第三阶段：故障恢复、告警与备份

目标：让系统不仅能创建存储，还能在异常和磁盘故障时安全维护数据

实施顺序：

1. 实现 SMART 定时检查、容量、服务和 RAID 状态告警
2. 实现阵列降级识别、磁盘更换、重建进度和重建期间限制
3. 实现配置漂移检测、导入当前配置和重新应用期望状态
4. 完善 Interrupted Operation 的人工复核、重试和恢复流程
5. 选定一种主备份引擎，优先在 BorgBackup 与 Restic 中二选一
6. 实现备份计划、保留策略、凭据安全存储和恢复预览
7. 实现管理数据库、配置、密钥和用户映射的系统备份
8. 建立可重复的恢复演练和故障注入测试
9. 完善日志轮转、告警确认和运维诊断包

第三阶段验收条件：

- 能正确发现 RAID 降级并指导完成安全换盘和重建
- 告警具有去重、状态、严重级别和审计记录
- 至少一种备份方案能完成备份、校验和真实恢复演练
- 管理面数据库和配置可从独立备份恢复
- 人工修改配置不会被静默覆盖
- 进程崩溃、服务重启和模拟断电后不会造成重复破坏性操作

### 第四阶段：可选服务与应用生态

目标：在核心存储可靠后扩展 NFS、Docker、远程能力和插件系统

实施顺序：

1. 增加 NFS 管理并复用配置事务、权限和审计框架
2. 增加 Docker 应用中心，固定 Debian 源或 Docker 官方源的唯一支持策略
3. 实现容器资源限制、卷路径权限、网络暴露和升级回滚
4. 评估安全远程访问，不默认把管理端口暴露到公网
5. 保留 `.ans` 包格式草案，先支持官方内置应用
6. 第三方插件开放前完成签名、发布者信任、解压安全、独立 origin、iframe sandbox、CSP 和能力令牌
7. 插件支持升级、禁用、卸载、兼容性检查和回滚
8. 根据明确需求再评估 Btrfs 或 ZFS 独立存储路线，不与第一版路线混用

第四阶段验收条件：

- 新服务沿用统一认证、权限、Operation、配置事务和审计机制
- Docker 应用不能绕过 NAS 的目录权限和资源限制
- 远程访问默认安全且具有清晰威胁模型
- 第三方插件不能继承主站完整登录态或直接调用未授权 API
- 插件安装能防御 ZIP Slip、符号链接、重复路径和解压炸弹
- 所有可选能力可以关闭，且不会影响核心 SMB 存储功能

## 10. Debian 底层包策略

包按能力安装，不在第一版一次性安装所有工具

核心候选包：

```text
smartmontools
parted
gdisk
e2fsprogs
mdadm
rsync
iproute2
ethtool
logrotate
```

SMB 能力：

```text
samba
avahi-daemon
wsdd
```

NFS 能力：

```text
nfs-kernel-server
nfs-common
```

可选文件系统和加密能力：

```text
xfsprogs
btrfs-progs
cryptsetup
```

监控与诊断候选：

```text
lm-sensors
sysstat
iotop
htop
lsof
procps
```

备份候选：

```text
borgbackup
restic
rclone
```

电源候选：

```text
nut
acpid
powertop
```

Docker 应在第四阶段启用，并在 Debian 软件源与 Docker 官方软件源之间选择唯一受支持路线，不允许混装

防火墙统一以 nftables 为底层策略，不同时维护 ufw 与 nftables 两套互相竞争的规则来源

FTP、WebDAV、下载和媒体服务不是核心 NAS 第一版依赖，仅在明确需求和安全评估后加入

## 11. 实时通信

SignalR 可推送：

- CPU、内存、网络和磁盘状态变化
- Operation 状态和进度
- RAID 降级与重建进度
- SMART 与容量告警
- 备份进度
- 受控的容器日志片段
- 服务状态变化

推送必须有频率限制、背压、输出长度限制和断线恢复

前端收到通知后按需通过 REST 获取权威状态，不依赖 SignalR 消息保存最终业务状态

## 12. 插件包草案

`.ans` 可以定义为 ZIP 容器，包含 `manifest.toml` 和静态前端资源

示例结构：

```text
photo-manager.ans
  manifest.toml
  icon.png
  dist/
    browser/
      index.html
      main.js
      styles.css
      assets/
```

示例 manifest：

```toml
format = "ans-v1"
id = "photo-manager"
name = "Photo Manager"
version = "1.0.0"
description = "Manage NAS photos"
icon = "icon.png"
entry = "dist/browser/index.html"

permissions = [
  "storage.read",
  "storage.write",
  "notification.send"
]

[window]
width = 1000
height = 700
resizable = true
```

manifest 是声明文件，不执行代码，因此优先 TOML 而不是 Lua

声明 permissions 不等于完成授权，后端必须依据能力令牌逐项执行权限检查

第一至第三阶段不开放第三方 `.ans` 安装，第四阶段安全模型完成前只允许内置应用

## 13. 代码质量要求

### 13.1 目录与代码注释

每个源码文件夹最好在该文件夹的主要入口文件开头使用中文和英文说明文件夹职责及边界，让维护者不需要阅读全部实现即可判断代码归属

开头注释使用以下风格：

```text
//--------------------------//
//--------中文注释---------//
//--------英文注释--------//
//-------------------------//
```

实际使用时将“中文注释”和“英文注释”替换为该文件夹或模块的职责与边界，保留四行结构

注释要求：

- 中文注释结尾不加“。”
- 中英文内容保持简单、专业和含义一致，不使用过多修饰词
- 注释应说明职责、边界、约束或设计原因，不重复代码可以直接表达的内容
- 已准确说明职责与边界且格式合规的文件头注释应保持稳定，不得仅因修改同一文件而重写、润色或调整；仅在模块职责、边界或约束实际变化，或原注释存在错误时进行最小必要修改
- 权限检查、危险操作、状态转换、资源锁、并发控制、回滚、设备识别和非直观算法等关键代码必须使用 `//` 添加必要备注
- 关键代码注释应解释为什么这样处理以及不能越过的边界，不逐行翻译代码
- 注释必须随实现同步更新，失效注释视为代码缺陷
- 对不支持 `//` 注释的 JSON 等格式，不得为了添加注释破坏文件语法，应在同目录可注释的主要入口文件中说明职责与边界
- 自动生成文件、第三方代码和构建产物不得手工添加此类注释，应在生成源或相邻的人工维护入口中说明

所有代码修改遵守以下规则：

- 修改前阅读相邻实现和调用方，优先复用现有契约
- 选择解决当前任务的最小完整改动
- 不在 Controller 中实现业务逻辑或系统命令调用
- 不拼接 shell，不信任用户提供的路径、设备名、服务名或参数
- 认证、授权、资源归属和危险操作确认分别检查
- 非简单流程使用集中定义的状态机，不散落字符串状态
- 错误码、日志、ID、分页、并发和幂等规则保持一致
- 不记录密码、令牌、密钥和未经必要处理的个人信息
- 不留下死代码、未使用依赖、无调用接口和无法运行的半成品重写
- 为安全边界、状态转换、失败回滚和设备识别编写测试
- 修改后运行最小有意义的测试、静态检查或构建
- 不因测试缺失而宣称功能完成

## 14. AI 结束工作时的输出格式

AI 完成工作后使用以下结构向用户汇报：

```markdown
完成结果

- 本次完成的最小工作项
- 当前项目达到的阶段和子项
- 关键文件

验证

- `<实际执行的命令>`：通过或失败及摘要

Commit 参考

标题：`feat(scope): ...`

正文：
...

PR 参考

标题：...

## 原有项目内容
...

## 改动内容
...

## 解决的问题
...

## 验证
...

## 风险与后续工作
...
```

AI 的 Commit 和 PR 文案仅供人工参考，人工应检查差异、验证结果和敏感信息后自行提交
