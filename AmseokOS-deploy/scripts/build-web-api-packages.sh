#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 VERSION OUTPUT_DIRECTORY [SOURCE_ROOT]" >&2
}

if [[ $# -lt 2 || $# -gt 3 ]]; then
    usage
    exit 2
fi

version=$1
output_directory=$(realpath -m "$2")
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source_root=${3:-$(realpath "$script_directory/../..")}
deploy_root=$(realpath "$script_directory/..")
architecture=$(dpkg --print-architecture)

if ! dpkg --validate-version "$version"; then
    echo "Invalid Debian version: $version" >&2
    exit 2
fi

if [[ $architecture != amd64 ]]; then
    echo "Only amd64 API packages are currently supported; detected $architecture" >&2
    exit 1
fi

for command in dotnet npm dpkg-deb install realpath; do
    if ! command -v "$command" >/dev/null 2>&1; then
        echo "Required command is missing: $command" >&2
        exit 1
    fi
done

web_root="$source_root/AmseokOS-web"
server_root="$source_root/AmseokOS-server"
for required_path in \
    "$web_root/package-lock.json" \
    "$server_root/src/Nas.Api/Nas.Api.csproj" \
    "$deploy_root/nginx/amseoknas.conf" \
    "$deploy_root/systemd/amseoknas-api.service"; do
    if [[ ! -e $required_path ]]; then
        echo "Required source path is missing: $required_path" >&2
        exit 1
    fi
done

mkdir -p "$output_directory"
work_directory=$(mktemp -d)
trap 'rm -rf -- "$work_directory"' EXIT

(
    cd "$web_root"
    npm ci
    npm run lint
    npm run test:ci
    npm run build -- --configuration production
)

(
    cd "$server_root"
    dotnet test tests/Nas.Api.Tests/Nas.Api.Tests.csproj --configuration Release
    dotnet publish src/Nas.Api/Nas.Api.csproj \
        --configuration Release \
        --runtime linux-x64 \
        --self-contained true \
        --output "$work_directory/api-publish" \
        -p:ContinuousIntegrationBuild=true \
        -p:DebugType=None \
        -p:DebugSymbols=false
)

web_build="$web_root/dist/amseok-nas-web/browser"
if [[ ! -f $web_build/index.html ]]; then
    echo "Angular browser output was not found at $web_build" >&2
    exit 1
fi

web_package="$work_directory/amseokos-web"
install -d "$web_package/DEBIAN" "$web_package/usr/share/amseoknas/web" \
    "$web_package/etc/nginx/conf.d"
cp -a "$web_build/." "$web_package/usr/share/amseoknas/web/"
install -m 0644 "$deploy_root/nginx/amseoknas.conf" \
    "$web_package/etc/nginx/conf.d/amseoknas.conf"
cat >"$web_package/DEBIAN/control" <<EOF
Package: amseokos-web
Version: $version
Section: web
Priority: optional
Architecture: all
Depends: nginx, openssl
Maintainer: AmseokOS Team
Description: AmseokNAS browser frontend and nginx site
EOF
cat >"$web_package/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v systemctl >/dev/null 2>&1; then
    systemctl try-reload-or-restart nginx.service >/dev/null 2>&1 || true
fi
EOF
chmod 0755 "$web_package/DEBIAN/postinst"

api_package="$work_directory/amseokos-api"
install -d "$api_package/DEBIAN" "$api_package/usr/lib/amseoknas/api" \
    "$api_package/usr/lib/systemd/system"
cp -a "$work_directory/api-publish/." "$api_package/usr/lib/amseoknas/api/"
install -m 0644 "$deploy_root/systemd/amseoknas-api.service" \
    "$api_package/usr/lib/systemd/system/amseoknas-api.service"
cat >"$api_package/DEBIAN/control" <<EOF
Package: amseokos-api
Version: $version
Section: admin
Priority: optional
Architecture: $architecture
Depends: libc6, libgcc-s1, libstdc++6, libicu76, libssl3t64, zlib1g, postgresql
Maintainer: AmseokOS Team
Description: AmseokNAS self-contained control-plane API
EOF
cat >"$api_package/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if ! getent group amseoknas-api >/dev/null; then
    addgroup --system amseoknas-api
fi
if ! getent passwd amseoknas-api >/dev/null; then
    adduser --system --ingroup amseoknas-api --home /var/lib/amseoknas \
        --no-create-home --shell /usr/sbin/nologin amseoknas-api
fi
install -d -o amseoknas-api -g amseoknas-api -m 0700 /var/lib/amseoknas
install -d -o root -g amseoknas-api -m 0750 /etc/amseoknas
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload
    systemctl enable amseoknas-api.service >/dev/null 2>&1 || true
fi
EOF
cat >"$api_package/DEBIAN/prerm" <<'EOF'
#!/bin/sh
set -e
if command -v systemctl >/dev/null 2>&1; then
    systemctl stop amseoknas-api.service >/dev/null 2>&1 || true
fi
EOF
chmod 0755 "$api_package/DEBIAN/postinst" "$api_package/DEBIAN/prerm"

dpkg-deb --root-owner-group --build "$web_package" \
    "$output_directory/amseokos-web_${version}_all.deb"
dpkg-deb --root-owner-group --build "$api_package" \
    "$output_directory/amseokos-api_${version}_${architecture}.deb"

sha256sum \
    "$output_directory/amseokos-web_${version}_all.deb" \
    "$output_directory/amseokos-api_${version}_${architecture}.deb"
