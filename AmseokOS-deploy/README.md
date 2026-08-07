# AmseokOS 单节点 Debian 部署

当前安装包支持 Debian 13（trixie）amd64。安装器会验证 APT 仓库签名，安装完整七包集合，生成仅保存在节点上的运行时秘密，并启用 PostgreSQL、etcd、NATS、API、Nginx、只读系统查询、Web Terminal 和本地控制台服务。

使用 curl：

```bash
curl -fsSL http://192.168.188.10/apt/install-amseokos.sh | sudo bash
```

使用 wget：

```bash
wget -qO- http://192.168.188.10/apt/install-amseokos.sh | sudo bash
```

默认根据访问软件仓库的路由自动识别管理 IPv4 地址。多网卡节点应显式指定：

```bash
curl -fsSL http://192.168.188.10/apt/install-amseokos.sh \
  | sudo bash -s -- --node-ip 192.168.188.7
```

其他签名仓库可以使用 `--repository-url`，固定版本可以使用 `--version`。安装完成后，Web 管理入口为 `https://<节点地址>:6521/`。

脚本会保留已存在的 TLS、PostgreSQL 和 NATS 凭据，因此可用于同一节点的包升级与配置修复。测试仓库当前使用 HTTP，适用于受信任的隔离局域网；正式环境应改为 HTTPS，并通过独立可信渠道分发仓库公钥或安装脚本摘要。
