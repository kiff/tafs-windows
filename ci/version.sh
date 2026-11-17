#!/usr/bin/env bash
# This build's version, into the job's environment: 0.1.<run>.0 for the team's builds, or for a release run
# (docs/install.md, Releasing) <release>.<run>, with the setup named by the release alone (tafs-setup-1.0.0-x64.exe).
# A release is three numbers, from main only. Reads RELEASE (empty for none) and RUN.
set -euo pipefail
if [ -n "${RELEASE:-}" ]; then
  [[ "$RELEASE" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "::error::a release is three numbers, such as 1.0.0"; exit 1; }
  [ "$GITHUB_REF" = refs/heads/main ] || { echo "::error::a release is made from main only"; exit 1; }
  echo "VERSION=$RELEASE.$RUN" >> "$GITHUB_ENV"
  echo "SETUP_NAME=$RELEASE" >> "$GITHUB_ENV"
else
  echo "VERSION=0.1.$RUN.0" >> "$GITHUB_ENV"
  echo "SETUP_NAME=0.1.$RUN.0" >> "$GITHUB_ENV"
fi
