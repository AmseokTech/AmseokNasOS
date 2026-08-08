#!/usr/bin/env bash
#--------------------------#
#--------从签名仓库安装并配置单节点 AmseokOS---------#
#--------Installs and configures a single AmseokOS node from a signed repository--------#
#-------------------------#
set -euo pipefail
export PATH=/usr/sbin:/usr/bin:/sbin:/bin
export DEBIAN_FRONTEND=noninteractive

readonly default_repository_url=http://192.168.188.10/apt
readonly repository_key_path=/usr/share/keyrings/amseokos-archive-keyring.gpg
readonly repository_source_path=/etc/apt/sources.list.d/amseokos-testing.list
readonly obsolete_repository_source_path=/etc/apt/sources.list.d/amseokos.list

repository_url=$default_repository_url
node_ip=
package_version=
package_directory=
enable_console=true
work_directory=

usage() {
    cat <<'EOF'
Usage: install-amseokos.sh [OPTIONS]

Install the complete AmseokOS single-node Debian package set, configure runtime
secrets, and enable all services for automatic restart and boot startup.

Options:
  --repository-url URL   Signed APT repository (default: http://192.168.188.10/apt)
  --node-ip IPV4         Management IPv4 address (default: route-based detection)
  --version VERSION      Install an exact AmseokOS package version
  --package-directory DIR
                         Install exactly one local copy of each of the seven packages
  --disable-console      Do not replace tty1 with the local status screen
  -h, --help             Show this help
EOF
}

log() {
    printf '[AmseokOS] %s\n' "$*"
}

die() {
    printf '[AmseokOS] ERROR: %s\n' "$*" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || die "Required command is missing: $1"
}

validate_ipv4() {
    local address=$1
    local octet
    local -a octets

    [[ $address =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || return 1
    IFS=. read -r -a octets <<<"$address"
    for octet in "${octets[@]}"; do
        ((10#$octet <= 255)) || return 1
    done
}

fetch_file() {
    local url=$1
    local destination=$2

    if command -v curl >/dev/null 2>&1; then
        curl --fail --silent --show-error --location \
            --connect-timeout 10 --max-time 120 \
            --output "$destination" "$url"
    elif command -v wget >/dev/null 2>&1; then
        wget --quiet --timeout=120 --output-document="$destination" "$url"
    else
        die "curl or wget is required"
    fi
}

detect_node_ip() {
    local url=$1
    local repository_host
    local repository_address
    local detected_ip

    repository_host=${url#*://}
    repository_host=${repository_host%%/*}
    repository_host=${repository_host%%:*}
    repository_address=$(getent ahostsv4 "$repository_host" 2>/dev/null \
        | awk 'NR == 1 { print $1 }')
    [[ -n $repository_address ]] || die "Cannot resolve repository host: $repository_host"
    detected_ip=$(ip -4 route get "$repository_address" 2>/dev/null \
        | awk '{ for (field = 1; field <= NF; field++) if ($field == "src") { print $(field + 1); exit } }')
    validate_ipv4 "$detected_ip" || die "Cannot detect the management IPv4 address; use --node-ip"
    printf '%s\n' "$detected_ip"
}

find_single_package() {
    local directory=$1
    local pattern=$2
    local component=$3
    local -a matches

    mapfile -t matches < <(find "$directory" -maxdepth 1 -type f \
        -name "$pattern" -print | sort)
    [[ ${#matches[@]} -eq 1 ]] \
        || die "Exactly one $component package is required; found ${#matches[@]}"
    printf '%s\n' "${matches[0]}"
}

install_prerequisites() {
    log "Installing Debian runtime prerequisites"
    apt-get update
    apt-get install -y \
        apache2-utils ca-certificates curl etcd-server gpgv nginx openssl \
        postgresql nats-server
}

configure_repository() {
    local work_directory=$1
    local downloaded_key="$work_directory/amseokos-archive-keyring.gpg"
    local inrelease="$work_directory/InRelease"
    local source_file="$work_directory/amseokos.list"

    [[ $repository_url == http://* || $repository_url == https://* ]] \
        || die "Repository URL must use HTTP or HTTPS"
    log "Verifying signed repository metadata from $repository_url"
    fetch_file "$repository_url/amseokos-archive-keyring.gpg" "$downloaded_key"
    fetch_file "$repository_url/dists/testing/InRelease" "$inrelease"
    gpgv --keyring "$downloaded_key" "$inrelease"

    install -o root -g root -m 0644 "$downloaded_key" "$repository_key_path"
    printf 'deb [arch=amd64 signed-by=%s] %s testing main\n' \
        "$repository_key_path" "$repository_url" >"$source_file"
    install -o root -g root -m 0644 "$source_file" "$repository_source_path"
    if [[ -f $obsolete_repository_source_path ]] \
        && grep -Fq "$repository_url testing main" "$obsolete_repository_source_path"; then
        rm -f -- "$obsolete_repository_source_path"
    fi
}

remove_obsolete_repository_source() {
    if [[ -f $repository_source_path && -f $obsolete_repository_source_path ]] \
        && grep -Fq "$repository_url testing main" "$repository_source_path" \
        && grep -Fq "$repository_url testing main" "$obsolete_repository_source_path"; then
        rm -f -- "$obsolete_repository_source_path"
    fi
}

install_repository_packages() {
    local package_name=amseokos

    if [[ -n $package_version ]]; then
        dpkg --validate-version "$package_version" \
            || die "Invalid Debian version: $package_version"
        package_name="amseokos=$package_version"
    fi

    log "Downloading and installing $package_name"
    apt-get update
    apt-get install -y --no-install-recommends "$package_name"
}

install_local_packages() {
    local directory
    local -a packages

    directory=$(realpath "$package_directory")
    packages=(
        "$(find_single_package "$directory" 'amseokos-web_*_all.deb' amseokos-web)"
        "$(find_single_package "$directory" 'amseokos-api_*_amd64.deb' amseokos-api)"
        "$(find_single_package "$directory" 'amseokos-privileged_*_amd64.deb' amseokos-privileged)"
        "$(find_single_package "$directory" 'amseokos-terminal_*_amd64.deb' amseokos-terminal)"
        "$(find_single_package "$directory" 'amseokos-console_*_all.deb' amseokos-console)"
        "$(find_single_package "$directory" 'amseokos-infrastructure_*_all.deb' amseokos-infrastructure)"
        "$(find_single_package "$directory" 'amseokos_*_amd64.deb' amseokos)"
    )

    log "Installing the local seven-package set"
    apt-get install -y "${packages[@]}"
}

ensure_tls_certificate() {
    install -d -o root -g root -m 0700 /etc/amseoknas/tls
    if [[ ! -s /etc/amseoknas/tls/amseoknas.key \
        || ! -s /etc/amseoknas/tls/amseoknas.crt ]]; then
        log "Generating a self-signed management certificate for $node_ip"
        openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 365 \
            -subj "/CN=$node_ip" \
            -addext "subjectAltName=IP:$node_ip" \
            -keyout /etc/amseoknas/tls/amseoknas.key \
            -out /etc/amseoknas/tls/amseoknas.crt
        chmod 0600 /etc/amseoknas/tls/amseoknas.key
        chmod 0644 /etc/amseoknas/tls/amseoknas.crt
    fi
}

existing_database_password() {
    [[ -r /etc/amseoknas/api.env ]] || return 0
    sed -n 's/^ConnectionStrings__ClusterDatabase=".*;Password=\([^";]*\)"$/\1/p' \
        /etc/amseoknas/api.env | head -n 1
}

configure_postgresql() {
    local database_password=$1

    systemctl enable --now postgresql.service
    if ! runuser -u postgres -- psql -tAc \
        "SELECT 1 FROM pg_roles WHERE rolname = 'amseoknas'" | grep -qx 1; then
        runuser -u postgres -- createuser --login amseoknas
    fi
    runuser -u postgres -- psql --set=ON_ERROR_STOP=1 --command \
        "ALTER ROLE amseoknas WITH LOGIN PASSWORD '$database_password'" >/dev/null
    if ! runuser -u postgres -- psql -tAc \
        "SELECT 1 FROM pg_database WHERE datname = 'amseoknas'" | grep -qx 1; then
        runuser -u postgres -- createdb --owner=amseoknas amseoknas
    fi
}

write_runtime_configuration() {
    local work_directory=$1
    local database_password=$2
    local api_environment="$work_directory/api.env"
    local privileged_environment="$work_directory/privileged.env"
    local api_uid

    api_uid=$(id -u amseoknas-api)
    {
        printf 'ConnectionStrings__ClusterDatabase="Host=127.0.0.1;Port=5432;Database=amseoknas;Username=amseoknas;Password=%s"\n' \
            "$database_password"
        printf 'ConnectionStrings__NodeDatabase="Data Source=/var/lib/amseoknas/amseoknas-node.db;Foreign Keys=True"\n'
        printf 'Persistence__ApplyMigrationsOnStartup=true\n'
        printf 'Terminal__Enabled=true\n'
        printf 'Terminal__SocketPath=/run/amseoknas-terminal/terminal.sock\n'
        printf 'Terminal__AllowedOrigins__0=https://%s:6521\n' "$node_ip"
        printf 'Terminal__PendingSessionLifetimeSeconds=30\n'
        printf 'Terminal__IdleTimeoutMinutes=15\n'
        printf 'Terminal__MaximumSessionMinutes=60\n'
        printf 'Privileged__Enabled=true\n'
        printf 'Privileged__SocketPath=/run/amseoknas/privileged.sock\n'
        printf 'Privileged__TimeoutSeconds=5\n'
        printf 'Privileged__RaidTimeoutSeconds=60\n'
    } >"$api_environment"
    install -o root -g root -m 0600 "$api_environment" /etc/amseoknas/api.env

    {
        printf 'AMSEOKNAS_PRIVILEGED_ALLOWED_UID=%s\n' "$api_uid"
        printf 'AMSEOKNAS_PRIVILEGED_SOCKET_PATH=/run/amseoknas/privileged.sock\n'
    } >"$privileged_environment"
    install -o root -g root -m 0600 "$privileged_environment" \
        /etc/amseoknas/privileged.env

    usermod -aG amseoknas,amseoknas-terminal amseoknas-api
}

configure_nats() {
    local work_directory=$1
    local nats_environment="$work_directory/nats.env"
    local nats_user
    local nats_password_hash
    local nats_store_directory
    local generated_password

    if [[ -r /etc/amseoknas/nats.env ]]; then
        nats_user=$(sed -n 's/^NATS_USER=//p' /etc/amseoknas/nats.env | head -n 1)
        nats_password_hash=$(sed -n "s/^NATS_PASSWORD_HASH='\(.*\)'$/\1/p" \
            /etc/amseoknas/nats.env | head -n 1)
        nats_store_directory=$(sed -n 's/^NATS_STORE_DIR=//p' \
            /etc/amseoknas/nats.env | head -n 1)
    fi

    if [[ -z ${nats_user:-} || -z ${nats_password_hash:-} \
        || -z ${nats_store_directory:-} ]]; then
        nats_user=amseoknas
        generated_password=$(openssl rand -hex 24)
        nats_password_hash=$(htpasswd -bnBC 12 '' "$generated_password" | tr -d ':\n')
        nats_store_directory=/var/lib/nats/jetstream
    fi

    {
        printf 'NATS_USER=%s\n' "$nats_user"
        printf "NATS_PASSWORD_HASH='%s'\n" "$nats_password_hash"
        printf 'NATS_STORE_DIR=%s\n' "$nats_store_directory"
    } >"$nats_environment"
    install -o root -g root -m 0600 "$nats_environment" /etc/amseoknas/nats.env
    install -d -o etcd -g etcd -m 0700 /var/lib/etcd/amseoknas
    install -d -o nats -g nats -m 0700 "$nats_store_directory"

    NATS_USER=$nats_user \
        NATS_PASSWORD_HASH=$nats_password_hash \
        NATS_STORE_DIR=$nats_store_directory \
        /usr/sbin/nats-server -t \
        -c /usr/share/amseoknas/infrastructure/nats-server.conf
}

configure_restart_policies() {
    local work_directory=$1
    local restart_policy="$work_directory/restart-policy.conf"
    local unit
    local -a units=(
        etcd.service
        nats-server.service
        nginx.service
        postgresql@.service
    )

    {
        printf '[Service]\n'
        printf 'Restart=on-failure\n'
        printf 'RestartSec=3s\n'
    } >"$restart_policy"
    for unit in "${units[@]}"; do
        install -d -o root -g root -m 0755 "/etc/systemd/system/$unit.d"
        install -o root -g root -m 0644 "$restart_policy" \
            "/etc/systemd/system/$unit.d/90-amseokos-restart.conf"
    done
}

enable_services() {
    local -a services=(
        postgresql.service
        etcd.service
        nats-server.service
        amseoknas-privileged.service
        amseoknas-terminal.service
        nginx.service
        amseoknas-api.service
    )

    if [[ $enable_console == true ]]; then
        install -o root -g root -m 0644 /dev/null /etc/amseoknas/console-enabled
        services+=(amseoknas-console.service)
    else
        rm -f /etc/amseoknas/console-enabled
        systemctl disable --now amseoknas-console.service >/dev/null 2>&1 || true
    fi

    nginx -t
    configure_restart_policies "$work_directory"
    systemctl daemon-reload
    systemctl enable "${services[@]}"
    systemctl restart postgresql.service etcd.service nats-server.service
    systemctl restart amseoknas-privileged.service amseoknas-terminal.service
    systemctl restart amseoknas-api.service nginx.service
    if [[ $enable_console == true ]]; then
        systemctl restart amseoknas-console.service
    fi
}

wait_for_url() {
    local url=$1
    local allow_insecure_tls=${2:-false}
    local _

    for _ in {1..30}; do
        if [[ $allow_insecure_tls == true ]]; then
            curl -kfsS --max-time 3 "$url" >/dev/null && return 0
        elif curl -fsS --max-time 3 "$url" >/dev/null; then
            return 0
        fi
        sleep 1
    done
    die "Timed out waiting for $url"
}

verify_services() {
    local configuration_file=$1
    local service
    local restart_policy
    local -a postgresql_units
    local -a services=(
        postgresql.service etcd.service nats-server.service
        amseoknas-privileged.service amseoknas-terminal.service
        nginx.service amseoknas-api.service
    )

    [[ $enable_console == false ]] || services+=(amseoknas-console.service)
    wait_for_url http://127.0.0.1:2379/health
    wait_for_url 'http://127.0.0.1:8222/healthz?js-enabled-only=true'
    wait_for_url http://127.0.0.1:5000/health/live
    wait_for_url http://127.0.0.1:5000/health/ready
    wait_for_url https://127.0.0.1:6521/ true

    sed 's/^Persistence__ApplyMigrationsOnStartup=true$/Persistence__ApplyMigrationsOnStartup=false/' \
        /etc/amseoknas/api.env >"$configuration_file"
    install -o root -g root -m 0600 "$configuration_file" /etc/amseoknas/api.env
    systemctl restart amseoknas-api.service
    wait_for_url http://127.0.0.1:5000/health/ready

    for service in "${services[@]}"; do
        systemctl is-enabled --quiet "$service" \
            || die "$service is not enabled for boot startup"
        systemctl is-active --quiet "$service" \
            || die "$service is not active"
    done

    for service in \
        etcd.service nats-server.service nginx.service \
        amseoknas-privileged.service amseoknas-terminal.service \
        amseoknas-console.service amseoknas-api.service; do
        [[ $service != amseoknas-console.service || $enable_console == true ]] || continue
        restart_policy=$(systemctl show -p Restart --value "$service")
        [[ $restart_policy != no ]] || die "$service does not have a restart policy"
    done

    mapfile -t postgresql_units < <(
        systemctl list-units --all --type=service 'postgresql@*.service' \
            --no-legend --plain | awk '{ print $1 }'
    )
    [[ ${#postgresql_units[@]} -gt 0 ]] \
        || die "No PostgreSQL cluster service was found"
    for service in "${postgresql_units[@]}"; do
        restart_policy=$(systemctl show -p Restart --value "$service")
        [[ $restart_policy != no ]] || die "$service does not have a restart policy"
    done
}

parse_arguments() {
    while [[ $# -gt 0 ]]; do
        case $1 in
            --repository-url)
                [[ $# -ge 2 ]] || die "--repository-url requires a value"
                repository_url=$2
                shift 2
                ;;
            --node-ip)
                [[ $# -ge 2 ]] || die "--node-ip requires a value"
                node_ip=$2
                shift 2
                ;;
            --version)
                [[ $# -ge 2 ]] || die "--version requires a value"
                package_version=$2
                shift 2
                ;;
            --package-directory)
                [[ $# -ge 2 ]] || die "--package-directory requires a value"
                package_directory=$2
                shift 2
                ;;
            --disable-console)
                enable_console=false
                shift
                ;;
            -h | --help)
                usage
                exit 0
                ;;
            *)
                die "Unknown option: $1"
                ;;
        esac
    done
}

main() {
    local database_password

    parse_arguments "$@"
    repository_url=${repository_url%/}
    [[ $EUID -eq 0 ]] || die "Run this installer as root (for example: curl ... | sudo bash)"
    [[ $(dpkg --print-architecture) == amd64 ]] \
        || die "Only Debian amd64 nodes are currently supported"
    [[ -r /etc/os-release ]] || die "/etc/os-release is missing"
    # shellcheck disable=SC1091
    . /etc/os-release
    [[ ${ID:-} == debian && ${VERSION_CODENAME:-} == trixie ]] \
        || die "This package set currently supports Debian 13 (trixie) only"

    require_command apt-get
    require_command dpkg
    require_command systemctl
    work_directory=$(mktemp -d)
    trap 'rm -rf -- "${work_directory:-}"' EXIT

    remove_obsolete_repository_source
    install_prerequisites
    if [[ -n $package_directory ]]; then
        install_local_packages
    else
        configure_repository "$work_directory"
        install_repository_packages
    fi

    require_command getent
    require_command htpasswd
    require_command ip
    require_command nginx
    require_command openssl
    require_command runuser
    require_command usermod
    if [[ -z $node_ip ]]; then
        node_ip=$(detect_node_ip "$repository_url")
    fi
    validate_ipv4 "$node_ip" || die "Invalid management IPv4 address: $node_ip"

    ensure_tls_certificate
    database_password=$(existing_database_password)
    if [[ -z $database_password ]]; then
        database_password=$(openssl rand -hex 24)
    fi
    configure_postgresql "$database_password"
    write_runtime_configuration "$work_directory" "$database_password"
    configure_nats "$work_directory"
    enable_services
    verify_services "$work_directory/api.env.final"

    log "Installation completed"
    log "Web interface: https://$node_ip:6521/"
    log "APT source: $repository_source_path"
}

main "$@"
