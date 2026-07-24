# AmseokNas 项目开发约定与实施路线

## 1. 文档目的

本文件是 AmseokNas 项目的长期开发约定、架构说明、阶段计划和 AI 协作规则

任何参与本项目的 AI 或开发者在开始工作前，都必须先阅读本文件，再检查项目目录、源码、配置、测试和 Git 状态，并根据实际代码判断当前进度

进度判断以可运行代码和验证结果为准，本文件中的进度记录只作为线索，不能代替实际检查

AI 必须在现有项目基础上完成下一个尚未完成的小目标，不得因为已有实现不完整就绕开原有结构另起一套项目，也不得一次跨越多个阶段进行大范围 speculative 开发

## 2. 当前项目进展

最后检查日期：2026-07-24

当前状态：第一阶段进行中，第 1 项最小项目骨架已完成；第 3 项持久化基础与真实 PostgreSQL 迁移已验证；第 4 项管理员认证、强制改密代码闭环和测试机认证请求链路已建立；前端桌面 Shell、可复用 WindowFrame 和多窗口管理已建立，支持最小化、最大化/还原、关闭、拖动、缩放和布局持久化，并已接入只读系统设置窗口，Dashboard 和磁盘列表仍未实现；C# 与 Rust 特权边界已完成首批“关于本机”和物理网卡只读查询代码闭环，但尚未安装到测试机，DHCP/固定 IP 写入仍保持关闭；作为用户明确要求的插入项，低权限 Web Terminal 代码闭环已建立并已从独立构建机生成 Release 产物部署到测试机，独立系统账户、systemd 沙箱、异常自动重启、真实 Socket/PTY、权限迁移、Nginx HTTPS/WebSocket 路由和未登录拦截均已验证，仍待使用当前 Web 管理员密码完成浏览器登录后的交互终端端到端验证；Data Protection 外部密钥保护尚未完成

检查结果：

- 已初始化本地 Git 仓库，本地 `devkihon` 基于并跟踪远端 `origin/devkihon`
- 已配置远端 `origin` 为 `git@github.com:AmseokTech/AmseokOS.git`
- 当前开发基线提交为 `8624e97 build(ci): 完善分层质量检测与安全扫描`
- 已建立前端、后端、特权进程、部署和文档的第一阶段顶层目录，并统一使用 `AmseokNas-<用途>` 命名
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
- 已将前端用户可见产品名从 `AmseokNas` 统一为 `AmseokOS`，移除桌面品牌区的 CSS 手绘 Logo，将“更便捷地管理你的服务器”与产品名移至桌面右下角，并在窄屏避让底部 Dock；Angular 26 项测试与生产构建通过
- 已将系统设置注册为桌面窗口管理器中的单例应用，Dock 可打开、聚焦、最小化和恢复窗口；设置页面采用左侧“关于本机/网络”导航，关于本机展示操作系统、内核、运行时间、CPU 型号/核心/频率、内存和系统盘容量，网络页展示物理网卡型号、驱动、链路、速率、地址、网关和 DNS；当前仅在检测到 systemd DHCP 租约时标记 DHCP，其他已配置来源显示未知，避免把 NetworkManager、ifupdown 或 SLAAC 地址误报为固定地址；尚未具备安全回滚能力的 IP 写入按钮保持禁用
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
- Data Protection 外部密钥保护、密码修改后旧密码失效的部署验证、会话跨重启持久化验证、PostgreSQL HA、etcd Leader/fencing 应用接入、NATS Inbox/Outbox 工作者、privileged daemon 测试机安装、物理磁盘查询和网络安全写入仍未实现，第一阶段验收条件尚未满足

下一建议工作项：观察 `devkihon` 首次 GitHub Actions 运行，确认 Compose/systemd、依赖变更审查、固定版本 `cargo-audit` 和 CodeQL 在托管 Runner 上通过；随后在独立构建机生成 Rust、.NET 与 Angular Release 产物，将只读 daemon 以专用账户安装到 `nastest`，按实际 API 服务 UID 写入仓库外 `privileged.env`，启用 C# `Privileged` 配置并完成浏览器端“关于本机/网络”端到端验证，同时确认其他账户和 Terminal 用户无法连接 Socket。之后再设计网络写入阶段：必须先完成 `network.manage` 授权、重新认证、配置预览、稳定网卡 ID、旧/新 IP 并行生效、连通性确认、超时自动回滚、Operation 与审计，不得直接把当前禁用按钮接到写配置动作。仍需完成 Web Terminal 浏览器端回归、共享 Data Protection 密钥保护及会话跨重启验证

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
AmseokNas-web/
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
AmseokNas-server/
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

推荐增加独立 Rust 特权进程，详细边界见 `AmseokNas-docs/csharp-rust-privileged-architecture.md`：

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

数据库详细设计见 `AmseokNas-docs/database-architecture.md`

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
