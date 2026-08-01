#!/bin/sh
#--------------------------#
#--------在隔离临时目录组合安装器包与 Debian Live 镜像---------#
#--------Combines the installer package and Debian Live image in an isolated directory--------#
#-------------------------#
set -eu

if [ "$#" -ne 2 ]; then
    printf '%s\n' "usage: $0 <amseokos-installer.deb> <output.iso>" >&2
    exit 2
fi

package_path=$1
output_path=$2
installer_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)

if [ ! -f "$package_path" ]; then
    printf 'installer package not found: %s\n' "$package_path" >&2
    exit 1
fi

if ! command -v lb >/dev/null 2>&1; then
    printf '%s\n' "live-build is required; run this script on Debian trixie" >&2
    exit 1
fi

work_directory=$(mktemp -d "${TMPDIR:-/tmp}/amseokos-live.XXXXXX")
cleanup() {
    rm -rf -- "$work_directory"
}
trap cleanup EXIT HUP INT TERM

cp -R "$installer_root/live-build/." "$work_directory/"
cp "$package_path" "$work_directory/config/packages.chroot/"

(
    cd "$work_directory"
    ./auto/config
    lb build
)

image_path=$(find "$work_directory" -maxdepth 1 -type f -name '*.iso' -print -quit)
if [ -z "$image_path" ]; then
    printf '%s\n' "live-build completed without producing an ISO" >&2
    exit 1
fi

output_directory=$(dirname -- "$output_path")
mkdir -p "$output_directory"
cp "$image_path" "$output_path"
sha256sum "$output_path" > "$output_path.sha256"
