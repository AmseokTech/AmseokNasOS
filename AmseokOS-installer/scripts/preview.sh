#!/bin/sh
#--------------------------#
#--------配置并启动与生产执行器隔离的 QML 实时预览---------#
#--------Configures and starts live QML preview isolated from production execution--------#
#-------------------------#
set -eu

installer_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
build_directory=${AMSEOKOS_PREVIEW_BUILD_DIR:-"$installer_root/build-preview"}
qt_cmake=${QT_CMAKE:-}

if [ -z "$qt_cmake" ] && command -v qt-cmake >/dev/null 2>&1; then
    qt_cmake=$(command -v qt-cmake)
fi

if [ -z "$qt_cmake" ]; then
    for cache_file in "$build_directory/CMakeCache.txt" "$installer_root/build/CMakeCache.txt"; do
        if [ ! -f "$cache_file" ]; then
            continue
        fi

        qt6_cmake_directory=$(sed -n 's/^Qt6_DIR:PATH=//p' "$cache_file" | sed -n '1p')
        if [ -n "$qt6_cmake_directory" ]; then
            qt_prefix=$(CDPATH='' cd -- "$qt6_cmake_directory/../../.." && pwd)
            if [ -x "$qt_prefix/bin/qt-cmake" ]; then
                qt_cmake="$qt_prefix/bin/qt-cmake"
                break
            fi
        fi
    done
fi

if [ -z "$qt_cmake" ] || [ ! -x "$qt_cmake" ]; then
    printf '%s\n' \
        "qt-cmake was not found; set QT_CMAKE to the Qt 6 qt-cmake executable" >&2
    exit 1
fi

"$qt_cmake" \
    -S "$installer_root" \
    -B "$build_directory" \
    -G Ninja \
    -DCMAKE_BUILD_TYPE=Debug \
    -DAMSEOKOS_ENABLE_DEVELOPER_PREVIEW=ON \
    -DBUILD_TESTING=ON

cmake --build "$build_directory" --target developer-preview
