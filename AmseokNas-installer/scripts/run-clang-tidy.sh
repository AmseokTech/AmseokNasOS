#!/bin/sh
#--------------------------#
#--------基于 CMake 编译数据库执行安装器 C++ 静态分析---------#
#--------Runs installer C++ static analysis from the CMake compile database---------#
#-------------------------#
set -eu

if [ "$#" -ne 1 ]; then
    printf '%s\n' "usage: $0 <cmake-build-directory>" >&2
    exit 2
fi

installer_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
build_directory=$1
clang_tidy=${CLANG_TIDY:-clang-tidy}

if ! command -v "$clang_tidy" >/dev/null 2>&1; then
    printf 'clang-tidy command not found: %s\n' "$clang_tidy" >&2
    exit 1
fi

if [ ! -f "$build_directory/compile_commands.json" ]; then
    printf 'CMake compile database not found: %s\n' \
        "$build_directory/compile_commands.json" >&2
    exit 1
fi

build_directory=$(CDPATH='' cd -- "$build_directory" && pwd)

find "$installer_root/src" "$installer_root/tests" \
    -type f -name '*.cpp' -print0 \
    | xargs -0 "$clang_tidy" \
        --quiet \
        --warnings-as-errors='*' \
        -p "$build_directory"

printf '%s\n' "clang-tidy analysis is valid"
