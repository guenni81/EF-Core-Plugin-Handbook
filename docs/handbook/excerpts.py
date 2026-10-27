"""Compares code excerpts of the handbook with the source files they come from.

An excerpt is bound to its source by a comment line directly before the listing:

    % source: src/Acme.EntityFrameworkCore.Tenancy/Interceptors/TenantSaveChangesInterceptor.cs
    \\begin{sourcecode}[...]{csharp}

Comments are ignored, so the German edition may translate them. Lines that only elide code
("...", "// ...") are ignored too. With "% source: <path> (strings)" the contents of string
literals are not compared either, for excerpts whose messages are translated. Every remaining line must occur in the source file, in the
same order; code in between may be left out, and line breaks may differ. Excerpts that rewrite code ("simplified") get
no marker.
"""
import re

ELISION = re.compile(r"^(\.\.\.|…)")


def _strip_comment(line):
    """Removes a // comment outside of string and character literals."""
    quote = None
    i = 0
    while i < len(line):
        c = line[i]
        if quote:
            if c == "\\":
                i += 1
            elif c == quote:
                quote = None
        elif c in "\"'":
            quote = c
        elif line.startswith("//", i):
            return line[:i]
        i += 1
    return line


STRING = re.compile(r'\$?@?"(?:[^"\\]|\\.)*"')


def normalize_code(code, ignore_strings=False):
    """Code lines without comments, elisions and blank lines, with collapsed whitespace."""
    result = []
    in_block_comment = False
    for raw in code.split("\n"):
        line = raw.strip()
        if in_block_comment:
            if "*/" in line:
                in_block_comment = False
            continue
        if line.startswith("/*"):
            in_block_comment = "*/" not in line
            continue
        if line.startswith("///") or line.startswith("#pragma"):
            continue
        line = _strip_comment(line).strip()
        if not line or ELISION.match(line):
            continue
        if ignore_strings:
            line = STRING.sub('""', line)
        result.append(re.sub(r"\s+", " ", line))
    return result


def _squeeze(line):
    return re.sub(r"\s+", "", line)


def find_mismatch(excerpt, source, ignore_strings=False):
    """None if the excerpt is an ordered subset of the source, else the first missing line.

    Whitespace is removed completely: the book wraps long lines differently from the source.
    """
    available = "".join(_squeeze(line) for line in normalize_code(source, ignore_strings))
    position = 0
    for line in normalize_code(excerpt, ignore_strings):
        found = available.find(_squeeze(line), position)
        if found < 0:
            return line
        position = found + len(_squeeze(line))
    return None
