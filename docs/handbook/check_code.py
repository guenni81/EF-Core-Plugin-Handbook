"""Checks the code excerpts of both editions.

    python docs/handbook/check_code.py

1. At most 94 characters per line: longer lines do not fit into the code boxes; LaTeX would only
   report an overfull box.
2. Excerpts bound to a source file with "% source: <path>" (see excerpts.py) still match it.
3. An excerpt whose title starts with a C# file of the repository is either bound to it or
   marked as simplified ("vereinfacht" / "simplified"), so that readers know it is not a literal
   copy. Application files that only exist as examples (generated migrations) are exempt.
"""
import glob
import os
import re
import sys

from excerpts import find_mismatch

MAX_CODE_COLS = 94
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLOCK = re.compile(r"\\begin\{sourcecode\}\[(.*?)\]\{\w+\}.*?\n(.*?)\\end\{sourcecode\}", re.S)
MARKER = re.compile(r"^% source: (\S+)( \(strings\))?$")
REPO_FILES = {os.path.basename(p) for p in glob.glob(os.path.join(ROOT, "**", "*.cs"), recursive=True)
              if os.sep + "obj" + os.sep not in p and os.sep + "bin" + os.sep not in p}


def named_file(title):
    """The C# file a listing claims to show: its title starts with a path of a repository file."""
    plain = re.sub(r"\\allowbreak\{\}", "", title).replace("\\_", "_")
    name = os.path.basename(re.split(r" ›| \(|,", plain)[0].strip())
    return name if name.endswith(".cs") and name in REPO_FILES else None


errors = 0


def report(path, line, message):
    global errors
    errors += 1
    print(f"{os.path.relpath(path, HERE)}:{line}: {message}")


for path in sorted(glob.glob(os.path.join(HERE, "*", "*.tex"))):
    text = open(path, encoding="utf-8").read()
    for block in BLOCK.finditer(text):
        title, code = block.group(1), block.group(2)
        first_line = text.count("\n", 0, block.start(2)) + 1
        for offset, line in enumerate(code.split("\n")):
            if len(line) > MAX_CODE_COLS:
                report(path, first_line + offset,
                       f"code line too long ({len(line)} > {MAX_CODE_COLS})")

        previous = text[:block.start()].rstrip("\n").split("\n")[-1]
        marker = MARKER.match(previous)
        if marker:
            source = os.path.join(ROOT, marker.group(1))
            if not os.path.isfile(source):
                report(path, first_line - 1, f"source file {marker.group(1)} does not exist")
                continue
            missing = find_mismatch(code, open(source, encoding="utf-8").read(),
                                    ignore_strings=bool(marker.group(2)))
            if missing is not None:
                report(path, first_line - 1,
                       f"excerpt no longer matches {marker.group(1)}: '{missing}'")
        elif named_file(title) and not re.search(r"vereinfacht|simplified", title):
            report(path, first_line - 1, "excerpt of a C# file needs '% source:' or 'simplified'")

print(f"{errors} problem(s) in code excerpts" if errors else "code excerpts ok")
sys.exit(1 if errors else 0)
