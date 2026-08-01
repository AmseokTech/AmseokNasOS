# AmseokOS Debian 镜像与安装器

本目录承载 AmseokOS 的 Debian Live 镜像、Qt/QML 安装界面、安装计划契约和 Debian 打包源码。

当前状态是**只读架构骨架**：页面可以导航，安装计划可以校验，但唯一执行适配器为 `DisabledInstallationExecutor`。它不会探测、分区、格式化、挂载或写入任何真实磁盘。

## 依赖方向

```text
QML
  -> presentation/InstallerSession
    -> domain/InstallationPlan
    -> ports/IInstallationExecutor
      <- adapters/DisabledInstallationExecutor

main.cpp 是唯一组合入口
live-build 只消费已构建的 Debian 包
```

边界规则：

- QML 只能调用 `InstallerSession`，不能启动进程或访问设备
- `domain` 只依赖 C++ 标准库
- `ports` 可以依赖 `domain`，不能依赖 UI 或具体适配器
- `presentation` 可以依赖 `domain` 和 `ports`，不能依赖具体适配器
- `adapters` 实现 `ports`，不能反向依赖 UI
- 真实执行器以后必须使用结构化计划、稳定设备 ID 和固定动作，不接受任意 shell

## 本机构建与预览

```bash
cmake \
  -S AmseokNas-installer \
  -B AmseokNas-installer/build \
  -G Ninja \
  -DCMAKE_PREFIX_PATH=/Users/goodgirlkihon/Qt/6.11.1/macos \
  -DCMAKE_EXPORT_COMPILE_COMMANDS=ON \
  -DCMAKE_BUILD_TYPE=Debug

cmake --build AmseokNas-installer/build
ctest --test-dir AmseokNas-installer/build --output-on-failure --no-tests=error
```

C++ 质量检查：

```bash
AmseokNas-installer/scripts/check-cpp-format.sh
AmseokNas-installer/scripts/run-clang-tidy.sh AmseokNas-installer/build
```

静态分析依赖 CMake 使用 `-DCMAKE_EXPORT_COMPILE_COMMANDS=ON` 生成编译数据库。GitHub Actions 会在任意分支每次 push 时执行 ShellCheck、C++ 格式、依赖边界、clang-tidy、Release 构建、QML lint、Qt Test、安装布局和 `live-build` 配置检查。

macOS 窗口预览：

```bash
open AmseokNas-installer/build/amseokos-installer.app --args --windowed
```

Linux 构建后运行：

```bash
AmseokNas-installer/build/amseokos-installer --windowed
```

无界面启动冒烟检查：

```bash
QT_QPA_PLATFORM=offscreen \
  AmseokNas-installer/build/amseokos-installer --windowed --smoke-test
```

## 开发者实时预览

开发者预览使用独立的 `DeveloperPreview.qml` 模拟会话，提供模拟系统盘、步骤导航和开始安装反馈。它不会创建真实安装执行器，也不会访问本机磁盘；默认 Release 构建不包含该入口。

在仓库根目录运行：

```bash
AmseokNas-installer/scripts/preview.sh
```

脚本会创建独立的 `build-preview` Debug 构建并启动 Qt `qmlpreview`。保持窗口和终端运行，保存 `qml/` 下的文件后，界面会自动刷新。首次运行如果尚未配置过 Qt，可显式指定 `qt-cmake`：

```bash
QT_CMAKE=/Users/goodgirlkihon/Qt/6.11.1/macos/bin/qt-cmake \
  AmseokNas-installer/scripts/preview.sh
```

`qmlpreview` 只用于本地实时刷新。缺少该工具时仍可配置、构建并执行下面的无界面预览验证；只有 `developer-preview` 实时刷新目标会提示安装 Qt QML tooling。

预览构建也可以单独执行无界面验证：

```bash
QT_QPA_PLATFORM=offscreen \
  AmseokNas-installer/build-preview/amseokos-installer.app/Contents/MacOS/amseokos-installer \
  --developer-preview --smoke-test
```

## Debian 包与镜像

以下命令必须在 Debian trixie amd64 构建机执行：

```bash
cd AmseokNas-installer
./scripts/build-package.sh
./scripts/build-image.sh ../amseokos-installer_0.1.0_amd64.deb ../out/amseokos-installer-amd64.iso
```

镜像使用 `live-build` 生成 Debian trixie `amd64` ISO Hybrid，启动后由 LightDM 自动进入 Openbox 会话并运行全屏安装器。构建脚本在 `mktemp` 临时目录工作，不把 chroot、下载缓存、`.deb` 或 ISO 写入源码目录。

## 当前明确未实现

- 真实块设备枚举与系统盘候选筛选
- 系统盘稳定身份复核与系统盘保护
- 分区、格式化、debootstrap、APT 和 GRUB 执行
- 危险操作二次确认、恢复与安装日志
- BIOS、UEFI、Secure Boot 和真实硬件安装验证
- APT HTTPS 发布和镜像签名

在这些安全边界及测试完成前，不得把 `DisabledInstallationExecutor` 替换成直接调用系统命令的实现。
