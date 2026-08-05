#!/bin/sh
#--------------------------#
#--------验证控制台状态页的稳定输出与安全参数回退---------#
#--------Verifies stable console output and safe argument fallbacks--------#
#-------------------------#
set -eu

console_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
deploy_root=$(CDPATH='' cd -- "$console_root/.." && pwd)
dashboard="$console_root/amseoknas-console-dashboard"
service_file="$deploy_root/systemd/amseoknas-console.service"
getty_drop_in="$deploy_root/systemd/getty@tty1.service.d/50-amseoknas-console.conf"

assert_contains() {
    output=$1
    expected=$2

    if ! printf '%s\n' "$output" | /usr/bin/grep -F -- "$expected" >/dev/null; then
        printf '%s\n' "expected output to contain: $expected" >&2
        exit 1
    fi
}

assert_file_contains() {
    file=$1
    expected=$2

    if ! /usr/bin/grep -F -- "$expected" "$file" >/dev/null; then
        printf '%s\n' "expected $file to contain: $expected" >&2
        exit 1
    fi
}

preview_output=$(NO_COLOR=1 \
    AMSEOKOS_CONSOLE_TITLE='AmseokOS Test' \
    AMSEOKOS_CONSOLE_SUBTITLE='Read-only local status' \
    AMSEOKOS_CONSOLE_FOOTER='Open the dashboard' \
    "$dashboard" --preview)

assert_contains "$preview_output" "AmseokOS Test"
assert_contains "$preview_output" "Read-only local status"
assert_contains "$preview_output" "https://192.168.1.100:6521"
assert_contains "$preview_output" "0.1.0-preview"
assert_contains "$preview_output" "Maintenance login: press Ctrl+Alt+F2"

fallback_output=$(NO_COLOR=1 \
    AMSEOKOS_CONSOLE_WEB_PORT='not-a-port' \
    "$dashboard" --preview)
assert_contains "$fallback_output" "https://192.168.1.100:6521"

if "$dashboard" --unsupported >/dev/null 2>&1; then
    printf '%s\n' "unsupported arguments must be rejected" >&2
    exit 1
fi

assert_file_contains "$service_file" "ConditionPathExists=/etc/amseoknas/console-enabled"
assert_file_contains "$service_file" "DynamicUser=yes"
assert_file_contains "$service_file" "StandardOutput=tty"
assert_file_contains "$service_file" "TTYPath=/dev/tty1"
assert_file_contains "$service_file" "RestrictAddressFamilies=AF_UNIX AF_NETLINK"
assert_file_contains "$getty_drop_in" "ConditionPathExists=!/etc/amseoknas/console-enabled"

printf '%s\n' "console dashboard tests passed"
