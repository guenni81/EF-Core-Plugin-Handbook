"""Structural checks of both editions, without building the PDFs.

    python docs/handbook/check_structure.py

1. Both editions have the same files, and per chapter the same label, the same number of
   sections, code excerpts (and "% source:" bindings), tables, callouts and figures, in the
   same order.
2. Every \\ref and \\pageref target exists in its edition.
3. The relative widths of every datatable add up to its number of columns (otherwise the table is
   wider or narrower than the text and LaTeX only reports an overfull box).
"""
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
EDITIONS = ("de", "en")

# Elements whose order and count must match per chapter
ELEMENT = re.compile(
    r"\\(chapter|section|subsection\*?)\{|\\begin\{(sourcecode|datatable|important|tip|note|"
    r"failure|flowdiagram|decisiontree|checklist)\}|^% (source):", re.M)
CHAPTER = re.compile(r"\\chapter\{[^}]*\}\\label\{([^}]+)\}")
LABEL = re.compile(r"\\label\{([^}]+)\}")
REF = re.compile(r"\\(?:page)?ref\{([^}]+)\}")
TABLE = re.compile(r"\\begin\{datatable\}\{((?:H\{[0-9.]+\})+)\}")

errors = []


def files(edition):
    return {os.path.basename(p)[:2]: p for p in sorted(glob.glob(os.path.join(HERE, edition, "*.tex")))}


def chapters(text):
    """Chapter label -> list of structural elements, in document order."""
    result, current = {}, None
    for match in ELEMENT.finditer(text):
        kind = match.group(1) or match.group(2) or match.group(3)
        if kind == "chapter":
            label = CHAPTER.match(text, match.start())
            current = label.group(1) if label else f"<unlabeled at {match.start()}>"
            result[current] = []
        elif current is not None:
            result[current].append(kind)
    return result


def line(text, pos):
    return text.count("\n", 0, pos) + 1


de, en = files("de"), files("en")
if de.keys() != en.keys():
    errors.append(f"files differ: de {sorted(de)} / en {sorted(en)}")

for number in sorted(de.keys() & en.keys()):
    texts = {e: open((de if e == "de" else en)[number], encoding="utf-8").read() for e in EDITIONS}
    structure = {e: chapters(texts[e]) for e in EDITIONS}
    if list(structure["de"]) != list(structure["en"]):
        errors.append(f"{number}: chapter labels differ: {list(structure['de'])} / {list(structure['en'])}")
        continue
    for label in structure["de"]:
        a, b = structure["de"][label], structure["en"][label]
        if a != b:
            first = next((i for i, (x, y) in enumerate(zip(a, b)) if x != y), min(len(a), len(b)))
            errors.append(f"{label}: structure differs at element {first + 1}: "
                          f"de {a[first:first + 3]} / en {b[first:first + 3]} "
                          f"(de {len(a)}, en {len(b)} elements)")

for edition in EDITIONS:
    paths = sorted(glob.glob(os.path.join(HERE, edition, "*.tex")))
    texts = {p: open(p, encoding="utf-8").read() for p in paths}
    labels = {label for text in texts.values() for label in LABEL.findall(text)}
    for path, text in texts.items():
        name = os.path.relpath(path, HERE)
        for match in REF.finditer(text):
            if match.group(1) not in labels:
                errors.append(f"{name}:{line(text, match.start())}: undefined reference {match.group(1)}")
        for match in TABLE.finditer(text):
            widths = [float(w) for w in re.findall(r"H\{([0-9.]+)\}", match.group(1))]
            if abs(sum(widths) - len(widths)) > 0.011:
                errors.append(f"{name}:{line(text, match.start())}: datatable widths add up to "
                              f"{sum(widths):.2f}, not {len(widths)}")

for error in errors:
    print(error)
print(f"{len(errors)} structural problem(s)" if errors else "structure ok")
sys.exit(1 if errors else 0)
