#!/usr/bin/env bash
#--------------------------#
#--------保留本地七包测试入口并复用正式安装器---------#
#--------Keeps the local seven-package test entry point on the production installer--------#
#-------------------------#
set -euo pipefail

if [[ $# -ne 2 ]]; then
    echo "Usage: $0 PACKAGE_DIRECTORY NODE_IP" >&2
    exit 2
fi

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
exec "$script_directory/install-amseokos.sh" \
    --package-directory "$1" \
    --node-ip "$2"
