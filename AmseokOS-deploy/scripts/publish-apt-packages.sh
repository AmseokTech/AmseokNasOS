#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 4 ]]; then
    echo "Usage: $0 REPOSITORY_ROOT SIGNING_HOME SIGNING_KEY PACKAGE..." >&2
    exit 2
fi

if [[ $EUID -ne 0 ]]; then
    echo "APT publication must run as root" >&2
    exit 1
fi

repository_root=$(realpath "$1")
signing_home=$(realpath "$2")
signing_key=$3
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
installer_script="$script_directory/install-amseokos.sh"
shift 3

for command in \
    apt-ftparchive date dpkg-deb dpkg-scanpackages find gpg gpgv gzip \
    install realpath sha256sum sort xargs; do
    if ! command -v "$command" >/dev/null 2>&1; then
        echo "Required command is missing: $command" >&2
        exit 1
    fi
done

pool_directory="$repository_root/pool/main/a/amseokos"
packages_directory="$repository_root/dists/testing/main/binary-amd64"
release_directory="$repository_root/dists/testing"
install -d -m 0755 "$pool_directory" "$packages_directory"
if [[ ! -f $installer_script ]]; then
    echo "Installer script does not exist: $installer_script" >&2
    exit 1
fi

for package in "$@"; do
    destination="$pool_directory/$(basename -- "$package")"
    if [[ ! -f $package ]]; then
        echo "Package does not exist: $package" >&2
        exit 1
    fi
    dpkg-deb --info "$package" >/dev/null
    if [[ $(realpath "$package") != $(realpath -m "$destination") ]]; then
        install -o root -g root -m 0644 "$package" "$destination"
    fi
done
install -o root -g root -m 0755 "$installer_script" \
    "$repository_root/install-amseokos.sh"

work_directory=$(mktemp -d)
trap 'rm -rf -- "$work_directory"' EXIT

cd "$repository_root"
dpkg-scanpackages --multiversion pool /dev/null >"$work_directory/Packages"
gzip -n -9 -c "$work_directory/Packages" >"$work_directory/Packages.gz"
install -o root -g root -m 0644 "$work_directory/Packages" \
    "$packages_directory/Packages"
install -o root -g root -m 0644 "$work_directory/Packages.gz" \
    "$packages_directory/Packages.gz"

valid_until=$(date -Ru -d '+30 days')
apt-ftparchive \
    -o APT::FTPArchive::Release::Origin=AmseokOS \
    -o 'APT::FTPArchive::Release::Label=AmseokOS Test Repository' \
    -o APT::FTPArchive::Release::Suite=testing \
    -o APT::FTPArchive::Release::Codename=testing \
    -o APT::FTPArchive::Release::Architectures=amd64 \
    -o APT::FTPArchive::Release::Components=main \
    -o 'APT::FTPArchive::Release::Description=AmseokOS LAN test packages' \
    -o "APT::FTPArchive::Release::Valid-Until=$valid_until" \
    release dists/testing >"$work_directory/Release"

gpg --homedir "$signing_home" --batch --yes --local-user "$signing_key" \
    --armor --detach-sign --output "$work_directory/Release.gpg" \
    "$work_directory/Release"
gpg --homedir "$signing_home" --batch --yes --local-user "$signing_key" \
    --armor --clearsign --output "$work_directory/InRelease" \
    "$work_directory/Release"

install -o root -g root -m 0644 "$work_directory/Release" \
    "$release_directory/Release"
install -o root -g root -m 0644 "$work_directory/Release.gpg" \
    "$release_directory/Release.gpg"
install -o root -g root -m 0644 "$work_directory/InRelease" \
    "$release_directory/InRelease"

{
    find pool -type f -name '*.deb' -print0 \
        | sort -z \
        | xargs -0 sha256sum
    sha256sum install-amseokos.sh
} >"$work_directory/SHA256SUMS"
gpg --homedir "$signing_home" --batch --yes --local-user "$signing_key" \
    --armor --detach-sign --output "$work_directory/SHA256SUMS.asc" \
    "$work_directory/SHA256SUMS"
install -o root -g root -m 0644 "$work_directory/SHA256SUMS" \
    "$repository_root/SHA256SUMS"
install -o root -g root -m 0644 "$work_directory/SHA256SUMS.asc" \
    "$repository_root/SHA256SUMS.asc"

gpgv --keyring "$repository_root/amseokos-archive-keyring.gpg" \
    "$release_directory/Release.gpg" "$release_directory/Release"
gpgv --keyring "$repository_root/amseokos-archive-keyring.gpg" \
    "$repository_root/SHA256SUMS.asc" "$repository_root/SHA256SUMS"
