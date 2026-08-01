# AmseokOS Debian 镜像与图形安装器架构

状态：已建立只读代码骨架，真实安装执行保持关闭

最后确认日期：2026-08-01

## 1. 目标

AmseokOS 使用 Debian trixie `amd64` 作为首个发行基线，通过 Debian `live-build` 生成 ISO Hybrid。用户看到的是独立的 Qt Quick/QML 安装界面，不暴露 Debian Installer 原有页面。

当前交付只建立可构建、可测试、可预览的架构，不宣称已经能够安装系统。

## 2. 运行边界

```text
Qt/QML 页面（无特权）
  -> InstallerSession（唯一 UI 公共入口）
    -> InstallationPlan（纯规则与计划校验）
    -> IInstallationExecutor（结构化执行端口）
      -> DisabledInstallationExecutor（当前唯一实现）

Debian live-build
  -> 安装 amseokos-installer.deb
  -> LightDM 自动登录 Live 用户
  -> Openbox 启动全屏 amseokos-installer
```

界面不得使用 `QProcess`、shell、设备路径或直接文件系统写入。真实执行器不得接受任意命令、程序路径、环境变量或调用方提供的 `/dev/*` 路径。

## 3. 系统盘安全

首版只允许一个明确选择的系统盘，且安装计划必须满足：

- Debian 基线固定为 `trixie amd64`
- 系统文件系统固定为 `ext4`
- 系统盘具有稳定 ID、型号和非零容量
- 用户完成不可逆动作确认
- 所有非目标磁盘保持不变

任何条件缺失时，安装计划不能进入执行器。真实执行器完成前，默认适配器始终返回 `execution.disabled`。

后续设备探测必须至少复核 WWN、序列号、型号、容量、当前路径、挂载、swap、RAID、LUKS、根文件系统和 EFI 关系。不能把 `/dev/sda` 或 `/dev/nvme0n1` 作为数据库主标识或唯一确认信息。

## 4. 镜像构建与制品

Git 保存：

- C++、QML、CMake、测试和边界检查
- `debian/` source package 配置
- `live-build` 配置、包列表和 Live 会话配置
- 镜像构建脚本、文档和公开素材

Git 不保存：

- `.deb`、`.udeb`、ISO、磁盘镜像和 chroot
- APT、ISO、Secure Boot 或 TLS 私钥
- 构建缓存、安装日志中的秘密和机器专用配置

包与 ISO 必须由 Debian trixie 构建机从 Git 检出内容生成。镜像构建脚本只在 `mktemp` 目录运行，再把最终 ISO 和 SHA-256 文件复制到指定制品目录。

## 5. 下一实施顺序

1. 用只读适配器枚举块设备并生成稳定系统盘候选
2. 增加系统盘保护规则和多层块设备关系测试
3. 定义不可变安装计划、预览摘要与二次确认
4. 分阶段实现 GPT、EFI、ext4、debootstrap、APT 和 GRUB 适配器
5. 每个阶段加入执行后复核、失败状态和可恢复日志
6. 在 QEMU 覆盖 BIOS、UEFI、SATA、NVMe、多盘和仓库失败
7. 通过真实空白磁盘验收后才开放生产执行器

不得为追求界面进度直接从 QML 或 `InstallerSession` 调用分区与格式化命令。
