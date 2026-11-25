#!/bin/bash
set -euo pipefail
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project"
export PYTHONPATH="$project/src"
export PYTHONDONTWRITEBYTECODE=1
exec .venv/bin/python -m redbool_demo.probe "$@"
