# 启动界面源码入口

这里用于保存 ISO 启动菜单、Plymouth 启动画面和以后可能需要的 Secure Boot 公开元数据。

当前骨架尚未加入最终品牌素材。后续应按启动阶段拆分：

```text
boot/
├── grub/          # UEFI 与 ISO GRUB 菜单、主题配置
├── isolinux/      # 仅在保留 Legacy BIOS/Syslinux 时使用
├── plymouth/      # Live 环境与已安装系统的启动动画
└── licenses/      # Logo、字体、图标和背景的许可证
```

源素材、配置、转换脚本与许可证进入 Git；生成的 EFI 文件、initrd、ISO 和签名私钥不进入 Git。

在决定 Secure Boot 签名链之前，不得替换 Debian 的 shim、签名 GRUB 或签名内核二进制。
