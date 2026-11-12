#!/bin/zsh
set -e
cd "$(dirname "$0")"
if [[ -x .venv/bin/python ]]; then
  py="$PWD/.venv/bin/python"
elif [[ -x ../../redbool/motion-fighter-ai/.venv/bin/python ]]; then
  py="../../redbool/motion-fighter-ai/.venv/bin/python"
else
  py="python3"
fi
exec "$py" launch_demo.py check
