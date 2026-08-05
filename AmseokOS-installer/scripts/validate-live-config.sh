#!/bin/sh
#--------------------------#
#--------在临时目录验证 live-build 配置而不污染源码树---------#
#--------Validates live-build configuration in a temporary directory--------#
#-------------------------#
set -eu

if ! command -v lb >/dev/null 2>&1; then
    printf '%s\n' "live-build is required" >&2
    exit 1
fi

installer_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
work_directory=$(mktemp -d "${TMPDIR:-/tmp}/amseokos-live-config.XXXXXX")
cleanup() {
    rm -rf -- "$work_directory"
}
trap cleanup EXIT HUP INT TERM

cp -R "$installer_root/live-build/." "$work_directory/"
(
    cd "$work_directory"
    ./auto/config
)

test -f "$work_directory/config/common"
printf '%s\n' "live-build configuration is valid"
