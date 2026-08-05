# AmseokOS 本地控制台状态页

该组件在目标系统的 `tty1` 显示只读状态页，内容包括 AmseokOS 品牌、主机名、Web 管理地址、版本和网络状态。它不提供 shell、不接受管理命令，也不连接 privileged daemon 或 Web Terminal broker。

默认使用 ASCII 文案，因为标准 Linux 内核控制台不能可靠显示完整中文字体。品牌副标题、提示语、端口和刷新间隔集中在 `/etc/default/amseoknas-console`，可以按产品需要修改。

## 文件布局

```text
/usr/libexec/amseoknas/amseoknas-console-dashboard
/etc/default/amseoknas-console
/etc/amseoknas/console-enabled
/etc/systemd/system/amseoknas-console.service
/etc/systemd/system/getty@tty1.service.d/50-amseoknas-console.conf
```

`console-enabled` 是显式启用标记。只有标记存在时，tty1 的默认 getty 才会停止，控制台状态页服务才会启动。tty2 至 tty6 继续提供 Debian 维护登录。

## 开发预览

在仓库根目录运行：

```bash
NO_COLOR=1 AmseokOS-deploy/console/amseoknas-console-dashboard --preview
```

## 目标系统安装草案

以下命令未来应由 Debian 包或真实安装器在目标 rootfs 中完成，当前安装器尚未开放真实写盘：

```bash
install -D -m 0755 AmseokOS-deploy/console/amseoknas-console-dashboard \
  /usr/libexec/amseoknas/amseoknas-console-dashboard
install -D -m 0644 AmseokOS-deploy/console/amseoknas-console.env.example \
  /etc/default/amseoknas-console
install -D -m 0644 AmseokOS-deploy/console/README.md \
  /usr/share/doc/amseoknas-console/README.md
install -D -m 0644 AmseokOS-deploy/systemd/amseoknas-console.service \
  /etc/systemd/system/amseoknas-console.service
install -D -m 0644 AmseokOS-deploy/systemd/getty@tty1.service.d/50-amseoknas-console.conf \
  /etc/systemd/system/getty@tty1.service.d/50-amseoknas-console.conf
install -D -m 0644 /dev/null /etc/amseoknas/console-enabled
systemctl daemon-reload
systemctl enable --now amseoknas-console.service
```

回退时先删除 `/etc/amseoknas/console-enabled`，再禁用 `amseoknas-console.service` 并重启 `getty@tty1.service`，即可恢复 tty1 登录提示。
