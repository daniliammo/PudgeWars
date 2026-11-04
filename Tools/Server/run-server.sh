#!/usr/bin/env bash
set -euo pipefail

server_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
server_binary="$server_directory/PudgeWarsServer.x86_64"
if [[ ! -x "$server_binary" ]]; then
    echo "Server executable not found or not executable: $server_binary" >&2
    exit 1
fi
cd -- "$server_directory"
exec "$server_binary" -batchmode -nographics -server -logFile - "$@"
