"""Builds the EF Core Plugin Handbook as PDF (German or English).

    pip install -r docs/handbook/requirements.txt
    python docs/handbook/build.py                 # German  -> out/EF-Core-Plugin-Handbuch.pdf
    python docs/handbook/build.py --lang en       # English -> out/EF-Core-Plugin-Handbook.pdf
    python docs/handbook/build.py --lang en x.pdf # custom output file

The parts live in parts/ (de) and parts_en/ (en). They are executed in file name order in
a shared namespace and append their flowables to the list S. Code lines are limited to
94 characters.
"""
import argparse
import glob
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import handbook_layout  # noqa: E402
from handbook_layout import *  # noqa: E402,F401,F403

CONFIG = {
    "de": ("parts", "EF-Core-Plugin-Handbuch.pdf", "Das EF Core Plugin-Handbuch",
           "Professionelle Plugins für Entity Framework Core von A bis Z"),
    "en": ("parts_en", "EF-Core-Plugin-Handbook.pdf", "The EF Core Plugin Handbook",
           "Professional plugins for Entity Framework Core from A to Z"),
}

args = argparse.ArgumentParser()
args.add_argument("--lang", choices=sorted(CONFIG), default="de")
args.add_argument("output", nargs="?")
options = args.parse_args()

folder, default_name, title, subject = CONFIG[options.lang]
handbook_layout.set_language(options.lang)

# Every part script appends to S (flowables of the document)
S = []
for part_file in sorted(glob.glob(os.path.join(HERE, folder, "*.py"))):
    with open(part_file, encoding="utf-8") as f:
        exec(compile(f.read(), part_file, "exec"))

output = options.output or os.path.join(HERE, "out", default_name)
os.makedirs(os.path.dirname(os.path.abspath(output)), exist_ok=True)

build(output, S, title, "Acme.EntityFrameworkCore.Tenancy · EF Core 10", title, subject)
print(f"PDF: {output}")
