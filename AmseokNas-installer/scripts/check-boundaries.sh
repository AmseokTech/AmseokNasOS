#!/bin/sh
#--------------------------#
#--------验证安装器依赖方向且拒绝跨层私有实现引用---------#
#--------Verifies installer dependency direction and rejects cross-layer private imports--------#
#-------------------------#
set -eu

installer_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

require_no_match() {
    pattern=$1
    path=$2
    message=$3

    if rg -n "$pattern" "$path"; then
        printf '%s\n' "$message" >&2
        exit 1
    fi
}

require_no_match '#include <Q|#include "(application|presentation|ports|adapters)/' \
    "$installer_root/src/domain" \
    "domain must depend only on the C++ standard library"

require_no_match '#include "(presentation|adapters)/' \
    "$installer_root/src/ports" \
    "ports must not depend on presentation or adapters"

require_no_match '#include "adapters/' \
    "$installer_root/src/presentation" \
    "presentation must depend on ports rather than concrete adapters"

require_no_match '#include "presentation/' \
    "$installer_root/src/adapters" \
    "adapters must not depend on presentation"

require_no_match 'QProcess|system\(|popen\(|/dev/' \
    "$installer_root/qml" \
    "QML must not execute commands or name device paths"

printf '%s\n' "installer dependency boundaries are valid"
