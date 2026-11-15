#!/bin/zsh
set -e
cd "$(dirname "$0")"
python3.11 -m venv .venv
.venv/bin/python -m pip install -r requirements-camera.txt
.venv/bin/python launch_demo.py check
