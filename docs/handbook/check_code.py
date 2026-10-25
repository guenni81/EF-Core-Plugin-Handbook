"""Checks the code excerpts of both editions: at most 94 characters per line.

    python docs/handbook/check_code.py

Longer lines do not fit into the code boxes; LaTeX would only report an overfull box.
"""
import glob
import os
import re
import sys

MAX_CODE_COLS = 94
HERE = os.path.dirname(os.path.abspath(__file__))
BLOCK = re.compile(r"\\begin\{sourcecode\}.*?\n(.*?)\\end\{sourcecode\}", re.S)

errors = 0
for path in sorted(glob.glob(os.path.join(HERE, "*", "*.tex"))):
    text = open(path, encoding="utf-8").read()
    for block in BLOCK.finditer(text):
        first_line = text.count("\n", 0, block.start(1)) + 1
        for offset, line in enumerate(block.group(1).split("\n")):
            if len(line) > MAX_CODE_COLS:
                errors += 1
                print(f"{os.path.relpath(path, HERE)}:{first_line + offset}: code line too "
                      f"long ({len(line)} > {MAX_CODE_COLS})")
sys.exit(1 if errors else 0)
