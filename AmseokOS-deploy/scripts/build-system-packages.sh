#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
    echo "Usage: $0 VERSION OUTPUT_DIRECTORY [SOURCE_ROOT]" >&2
    exit 2
fi

version=$1
output_directory=$(realpath -m "$2")
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source_root=${3:-$(realpath "$script_directory/../..")}
deploy_root=$(realpath "$script_directory/..")
architecture=$(dpkg --print-architecture)
rust_target=${AMSEOKNAS_RUST_TARGET:-x86_64-unknown-linux-musl}

if ! dpkg --validate-version "$version"; then
    echo "Invalid Debian version: $version" >&2
    exit 2
fi

if [[ $architecture != amd64 ]]; then
    echo "Only amd64 system packages are currently supported; detected $architecture" >&2
    exit 1
fi

for command in cargo cp dpkg-deb install mktemp realpath sha256sum; do
    if ! command -v "$command" >/dev/null 2>&1; then
        echo "Required command is missing: $command" >&2
        exit 1
    fi
done

privileged_root="$source_root/AmseokOS-privileged"
terminal_root="$source_root/AmseokOS-terminal"
for required_path in \
    "$privileged_root/Cargo.lock" \
    "$terminal_root/Cargo.lock" \
    "$deploy_root/systemd/amseoknas-privileged.service" \
    "$deploy_root/systemd/amseoknas-terminal.service" \
    "$deploy_root/systemd/amseoknas-console.service" \
    "$deploy_root/systemd/etcd.service.d/amseoknas.conf" \
    "$deploy_root/systemd/nats-server.service.d/amseoknas.conf" \
    "$deploy_root/nats/nats-server.conf"; do
    if [[ ! -e $required_path ]]; then
        echo "Required source path is missing: $required_path" >&2
        exit 1
    fi
done

mkdir -p "$output_directory"
work_directory=$(mktemp -d)
trap 'rm -rf -- "$work_directory"' EXIT

build_rust_component() {
    local manifest=$1
    cargo fmt --manifest-path "$manifest" -- --check
    cargo clippy --locked --manifest-path "$manifest" --all-targets -- -D warnings
    cargo test --locked --manifest-path "$manifest" --all-targets
    cargo build --locked --manifest-path "$manifest" --release --target "$rust_target"
}

write_control() {
    local package_root=$1
    local package_name=$2
    local package_architecture=$3
    local dependencies=$4
    local description=$5

    cat >"$package_root/DEBIAN/control" <<EOF
Package: $package_name
Version: $version
Section: admin
Priority: optional
Architecture: $package_architecture
Depends: $dependencies
Maintainer: AmseokOS Team
Description: $description
EOF
}

build_rust_component "$privileged_root/Cargo.toml"
build_rust_component "$terminal_root/Cargo.toml"
"$deploy_root/console/tests/console-dashboard-test.sh"

privileged_package="$work_directory/amseokos-privileged"
install -d "$privileged_package/DEBIAN" \
    "$privileged_package/usr/libexec/amseoknas" \
    "$privileged_package/usr/lib/systemd/system"
install -m 0755 \
    "$privileged_root/target/$rust_target/release/amseoknas-privileged" \
    "$privileged_package/usr/libexec/amseoknas/amseoknas-privileged"
install -m 0644 "$deploy_root/systemd/amseoknas-privileged.service" \
    "$privileged_package/usr/lib/systemd/system/amseoknas-privileged.service"
write_control "$privileged_package" amseokos-privileged "$architecture" \
    "adduser, e2fsprogs, mdadm, nfs-kernel-server, samba, smartmontools, systemd, util-linux" \
    "AmseokNAS constrained inventory, RAID, data-volume, and share daemon"
cat >"$privileged_package/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if ! getent group amseoknas >/dev/null; then
    addgroup --system amseoknas
fi
if ! getent group amseoknas-data >/dev/null; then
    addgroup --system amseoknas-data
fi
if getent passwd amseoknas-api >/dev/null; then
    usermod -aG amseoknas amseoknas-api
fi
install -d -o root -g amseoknas-data -m 0750 /srv/amseoknas/volumes
install -d -o root -g root -m 0755 /etc/samba/smb.conf.d /etc/exports.d
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload
    systemctl enable amseoknas-privileged.service >/dev/null 2>&1 || true
fi
EOF
chmod 0755 "$privileged_package/DEBIAN/postinst"

terminal_package="$work_directory/amseokos-terminal"
install -d "$terminal_package/DEBIAN" \
    "$terminal_package/usr/libexec/amseoknas" \
    "$terminal_package/usr/lib/systemd/system"
install -m 0755 \
    "$terminal_root/target/$rust_target/release/amseoknas-terminal-broker" \
    "$terminal_package/usr/libexec/amseoknas/amseoknas-terminal-broker"
install -m 0644 "$deploy_root/systemd/amseoknas-terminal.service" \
    "$terminal_package/usr/lib/systemd/system/amseoknas-terminal.service"
write_control "$terminal_package" amseokos-terminal "$architecture" \
    "adduser, bash, systemd" \
    "AmseokNAS isolated low-privilege Web Terminal broker"
cat >"$terminal_package/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if ! getent group amseoknas-terminal >/dev/null; then
    addgroup --system amseoknas-terminal
fi
if ! getent passwd amseoknas-terminal >/dev/null; then
    adduser --system --ingroup amseoknas-terminal \
        --home /var/lib/amseoknas-terminal --no-create-home \
        --shell /usr/sbin/nologin amseoknas-terminal
fi
if getent passwd amseoknas-api >/dev/null; then
    usermod -aG amseoknas-terminal amseoknas-api
fi
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload
    systemctl enable amseoknas-terminal.service >/dev/null 2>&1 || true
fi
EOF
chmod 0755 "$terminal_package/DEBIAN/postinst"

console_package="$work_directory/amseokos-console"
install -d "$console_package/DEBIAN" \
    "$console_package/usr/libexec/amseoknas" \
    "$console_package/usr/lib/systemd/system/getty@tty1.service.d" \
    "$console_package/etc/default" \
    "$console_package/usr/share/doc/amseoknas-console"
install -m 0755 "$deploy_root/console/amseoknas-console-dashboard" \
    "$console_package/usr/libexec/amseoknas/amseoknas-console-dashboard"
install -m 0644 "$deploy_root/systemd/amseoknas-console.service" \
    "$console_package/usr/lib/systemd/system/amseoknas-console.service"
install -m 0644 \
    "$deploy_root/systemd/getty@tty1.service.d/50-amseoknas-console.conf" \
    "$console_package/usr/lib/systemd/system/getty@tty1.service.d/50-amseoknas-console.conf"
install -m 0644 "$deploy_root/console/amseoknas-console.env.example" \
    "$console_package/etc/default/amseoknas-console"
install -m 0644 "$deploy_root/console/README.md" \
    "$console_package/usr/share/doc/amseoknas-console/README.md"
write_control "$console_package" amseokos-console all \
    "iproute2, systemd" \
    "AmseokNAS read-only local console status screen"
cat >"$console_package/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload
    systemctl enable amseoknas-console.service >/dev/null 2>&1 || true
fi
EOF
chmod 0755 "$console_package/DEBIAN/postinst"

infrastructure_package="$work_directory/amseokos-infrastructure"
install -d "$infrastructure_package/DEBIAN" \
    "$infrastructure_package/usr/lib/systemd/system/etcd.service.d" \
    "$infrastructure_package/usr/lib/systemd/system/nats-server.service.d" \
    "$infrastructure_package/usr/share/amseoknas/infrastructure"
install -m 0644 "$deploy_root/systemd/etcd.service.d/amseoknas.conf" \
    "$infrastructure_package/usr/lib/systemd/system/etcd.service.d/amseoknas.conf"
install -m 0644 "$deploy_root/systemd/nats-server.service.d/amseoknas.conf" \
    "$infrastructure_package/usr/lib/systemd/system/nats-server.service.d/amseoknas.conf"
install -m 0644 "$deploy_root/nats/nats-server.conf" \
    "$infrastructure_package/usr/share/amseoknas/infrastructure/nats-server.conf"
write_control "$infrastructure_package" amseokos-infrastructure all \
    "etcd-server, nats-server, systemd" \
    "AmseokNAS single-node etcd and NATS configuration"
cat >"$infrastructure_package/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload
    systemctl enable etcd.service nats-server.service >/dev/null 2>&1 || true
fi
EOF
chmod 0755 "$infrastructure_package/DEBIAN/postinst"

meta_package="$work_directory/amseokos"
install -d "$meta_package/DEBIAN"
write_control "$meta_package" amseokos "$architecture" \
    "amseokos-web (= $version), amseokos-api (= $version), amseokos-privileged (= $version), amseokos-terminal (= $version), amseokos-console (= $version), amseokos-infrastructure (= $version)" \
    "AmseokNAS complete single-node package set"

for package_name in \
    amseokos-privileged amseokos-terminal amseokos-console \
    amseokos-infrastructure amseokos; do
    package_root="$work_directory/$package_name"
    package_architecture=$(sed -n 's/^Architecture: //p' "$package_root/DEBIAN/control")
    dpkg-deb --root-owner-group --build "$package_root" \
        "$output_directory/${package_name}_${version}_${package_architecture}.deb"
done

sha256sum "$output_directory"/*.deb
