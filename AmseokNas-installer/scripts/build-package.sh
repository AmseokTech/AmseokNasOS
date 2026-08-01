#!/bin/sh
#--------------------------#
#--------在 Debian 构建机生成可追溯安装器二进制包---------#
#--------Builds the traceable installer binary package on a Debian builder--------#
#-------------------------#
set -eu

installer_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)

if ! command -v dpkg-buildpackage >/dev/null 2>&1; then
    printf '%s\n' "dpkg-buildpackage is required; run this script on Debian trixie" >&2
    exit 1
fi

cd "$installer_root"
dpkg-buildpackage --build=binary --unsigned-buildinfo --unsigned-changes
