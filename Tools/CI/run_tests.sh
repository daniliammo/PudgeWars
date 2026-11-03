#!/usr/bin/env bash
# Call the pinned GameCI CLI directly: the v4 action serializes false as an unsupported --no-* flag.
set -euo pipefail

ci_executable="${1:?Usage: run_tests.sh <game-ci executable> [project directory]}"
ci_project="${2:-.}"
ci_unity_version="$(sed -n 's/^m_EditorVersion: //p' "$ci_project/ProjectSettings/ProjectVersion.txt")"
if [[ ! "$ci_unity_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+$ ]]; then
  echo "Cannot read the exact Unity version from ProjectVersion.txt" >&2
  exit 1
fi

# Credentials stay in UNITY_* environment variables; never place them in argv.
# The project is positional and must precede flags in this CLI's argument parser.
exec "$ci_executable" test "$ci_project" \
  --docker \
  --dockerShmSize=1025m \
  --engine=unity \
  --engineVersion="$ci_unity_version" \
  --testPlatforms=playmode \
  --coverageEnabled=false \
  --customParameters=-force-glcore \
  --artifactsPath=artifacts/tests \
  --containerRegistryRepository=unityci/editor \
  --containerRegistryImageVersion=3 \
  --verbose
