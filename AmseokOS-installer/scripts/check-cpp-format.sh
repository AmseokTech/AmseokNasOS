#!/bin/sh
#--------------------------#
#--------以仓库固定规则检查全部安装器 C++ 源码格式---------#
#--------Checks all installer C++ sources against the repository format---------#
#-------------------------#
set -eu

installer_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
clang_format=${CLANG_FORMAT:-clang-format}

if ! command -v "$clang_format" >/dev/null 2>&1; then
    printf 'clang-format command not found: %s\n' "$clang_format" >&2
    exit 1
fi

find "$installer_root/src" "$installer_root/tests" \
    -type f \( -name '*.cpp' -o -name '*.h' \) -print0 \
    | xargs -0 "$clang_format" --dry-run --Werror

printf '%s\n' "C++ formatting is valid"
