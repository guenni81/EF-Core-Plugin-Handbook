# The EF Core Plugin Handbook: sources

The handbook is available in two editions:

| Edition | PDF | Sources |
|---|---|---|
| English | [`out/EF-Core-Plugin-Handbook.pdf`](out/EF-Core-Plugin-Handbook.pdf) | [`parts_en/`](parts_en) |
| Deutsch | [`out/EF-Core-Plugin-Handbuch.pdf`](out/EF-Core-Plugin-Handbuch.pdf) | [`parts/`](parts) |

The PDFs are generated from Python scripts (reportlab + pygments), so text and code
excerpts are versioned together with the code in this repository. CI builds both editions
on every push and uploads them as the `handbook` artifact.

## Building

```bash
pip install -r docs/handbook/requirements.txt
python docs/handbook/build.py --lang en    # -> docs/handbook/out/EF-Core-Plugin-Handbook.pdf
python docs/handbook/build.py --lang de    # -> docs/handbook/out/EF-Core-Plugin-Handbuch.pdf
python docs/handbook/build.py --lang en my.pdf   # custom output file
```

## Files

| File | Content |
|---|---|
| `build.py` | Entry point: runs the part scripts of one language and renders the PDF |
| `handbook_layout.py` | Layout: fonts, styles, syntax-highlighted code blocks, callouts, tables, table of contents |
| `parts_en/01_foundations.py` · `parts/01_grundlagen.py` | Title, preface, part I (chapters 1–3) |
| `parts_en/02_infrastructure.py` · `parts/02_infrastruktur.py` | Part II (chapters 4–6) |
| `parts_en/03_model.py` · `parts/03_modell.py` | Part III (chapters 7–10) |
| `parts_en/04_queries.py` · `parts/04_abfragen.py` | Part IV (chapters 11–14) |
| `parts_en/05_runtime.py` · `parts/05_laufzeit.py` | Part V (chapters 15–16) |
| `parts_en/06_migrations_design_time.py` · `parts/06_migrations_design_time.py` | Part VI (chapters 17–22) |
| `parts_en/07_quality.py` · `parts/07_qualitaet.py` | Part VII (chapters 23–26) |
| `parts_en/08_advanced.py` · `parts/08_fortgeschritten.py` | Part VIII (chapters 27–36) |
| `parts_en/09_deep_extensions.py` · `parts/09_tiefe_erweiterungen.py` | Part IX (chapters 37–42) |
| `parts_en/10_appendices.py` · `parts/10_anhaenge.py` | Appendices A–C |
| `fonts/` | DejaVu fonts (free license, see `LICENSE-DejaVu.txt`) |

## Editing rules

* **Keep both editions in sync.** Both editions share the same structure; the file names
  differ by language. Any change to the content belongs in both `parts/` and `parts_en/`.
* **Chapter numbers** are assigned automatically. Cross-references in the text
  ("chapter 37") are written by hand, however, and must be adjusted when chapters are
  inserted. The source code also refers to chapters in its comments.
* **Code lines** inside `code_block(...)` may be at most 94 characters long; otherwise the
  build stops with an error.
* **Code excerpts must match the repository.** When a class changes, update its excerpt
  as well; the file path is given in each `label`. Comments in the English excerpts use
  the same wording as the source code, the German excerpts translate them.

## License

The handbook, its sources and the generated PDFs are licensed under the MIT License, like
the rest of the repository (see [`LICENSE`](../../LICENSE)). Each PDF contains the full
license text on its last page. The fonts in `fonts/` are under their own free license
(`fonts/LICENSE-DejaVu.txt`).
