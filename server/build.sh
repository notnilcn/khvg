#!/usr/bin/env bash
set -e
cd "$(dirname "$0")"

spacetime generate --lang csharp --out-dir ../client/Scripts/module_bindings --module-path ./spacetimedb -y
spacetime publish bullethell --delete-data -y
