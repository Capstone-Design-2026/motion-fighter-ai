#!/bin/bash
set -euo pipefail
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project"
export PYTHONPATH="$project/src"
export PYTHONDONTWRITEBYTECODE=1
export MPLCONFIGDIR="${TMPDIR:-/tmp}/redbool-demo-mpl"
if [[ ! -x .venv/bin/python ]]; then
  echo '프로젝트 .venv를 찾지 못했습니다. 기존 Python 환경을 확인하세요.' >&2
  exit 1
fi
exec .venv/bin/python -m redbool_demo.live --backend avfoundation "$@"
