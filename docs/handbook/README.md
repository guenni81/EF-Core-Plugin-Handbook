# The EF Core Plugin Handbook: sources

The handbook is available in two editions:

| Edition | PDF | Sources |
|---|---|---|
| English | [`out/EF-Core-Plugin-Handbook.pdf`](out/EF-Core-Plugin-Handbook.pdf) | [`EF-Core-Plugin-Handbook.tex`](EF-Core-Plugin-Handbook.tex), [`en/`](en) |
| Deutsch | [`out/EF-Core-Plugin-Handbuch.pdf`](out/EF-Core-Plugin-Handbuch.pdf) | [`EF-Core-Plugin-Handbuch.tex`](EF-Core-Plugin-Handbuch.tex), [`de/`](de) |

The PDFs are typeset with LaTeX (LuaLaTeX, KOMA-Script, minted), so text and code excerpts
are versioned together with the code in this repository. CI builds both editions on every
push and uploads them as the `handbook` artifact.

## Building

With a TeX Live installation (2025 or newer, including `latexmk` and `minted`):

```bash
cd docs/handbook
python3 check_code.py                  # code lines at most 94 characters
latexmk EF-Core-Plugin-Handbook.tex    # -> out/EF-Core-Plugin-Handbook.pdf
latexmk EF-Core-Plugin-Handbuch.tex    # -> out/EF-Core-Plugin-Handbuch.pdf
latexmk -c                             # remove auxiliary files (out/aux/)
```

Without a local TeX Live, use the same Docker image as CI:

```bash
cd docs/handbook
docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD":/w -w /w \
  texlive/texlive:latest latexmk EF-Core-Plugin-Handbuch.tex
```

`latexmkrc` selects LuaLaTeX and the output directory. minted calls `latexminted`, which
TeX Live allows in restricted shell-escape mode, so `-shell-escape` is not needed.

## Files

| File | Content |
|---|---|
| `EF-Core-Plugin-Handbook.tex` · `EF-Core-Plugin-Handbuch.tex` | Main document of each edition: language, PDF metadata, chapter files |
| `handbook.sty` | Layout: fonts, colors, page style, code blocks, callouts, tables, flow diagrams, decision trees, checklists, title block |
| `en/01_foundations.tex` · `de/01_grundlagen.tex` | Title, preface, table of contents, part I |
| `en/02_infrastructure.tex` · `de/02_infrastruktur.tex` | Part II |
| `en/03_model.tex` · `de/03_modell.tex` | Part III |
| `en/04_queries.tex` · `de/04_abfragen.tex` | Part IV |
| `en/05_runtime.tex` · `de/05_laufzeit.tex` | Part V |
| `en/06_migrations_design_time.tex` · `de/06_migrations_design_time.tex` | Part VI |
| `en/07_quality.tex` · `de/07_qualitaet.tex` | Part VII |
| `en/08_advanced.tex` · `de/08_fortgeschritten.tex` | Part VIII |
| `en/09_deep_extensions.tex` · `de/09_tiefe_erweiterungen.tex` | Part IX |
| `en/10_appendices.tex` · `de/10_anhaenge.tex` | Appendices A–D, license |
| `check_code.py` | Checks the maximum line length of all code excerpts |
| `fonts/` | DejaVu fonts (free license, see `LICENSE-DejaVu.txt`) |

## Building blocks

The chapter files only use the commands from `handbook.sty`:

```latex
\chapter{Value Generation}\label{ch:value-generation}
\section{...}                                  % sections are not numbered
\code{IMethodCallTranslator}                   % inline code
\begin{sourcecode}[Query/CurrentTenantTranslator.cs]{csharp}
...                                            % csharp, sql, xml or text
\end{sourcecode}
\begin{tip}[Optional title] ... \end{tip}      % also: important, note
\begin{datatable}{H{0.8}H{1.2}}                % relative widths, sum = number of columns
  \textbf{Head} & \textbf{Head} \\ \tablehead
  Cell & Cell \\ \tablerule
  Cell & Cell \\
\end{datatable}
\begin{flowdiagram} \flowstep{Stage}{Description} ... \end{flowdiagram}
\begin{decisiontree}                           % "yes" leads right, "no" down
  \decision{Question?}{Outcome} ... \decisionend{Otherwise}
\end{decisiontree}
\cellmeta{public · multiple · Scoped}          % grey second line in a table cell
\begin{checklist} \item ... \end{checklist}
```

## Editing rules

* **Edition.** The book has its own version, `\handbookversion` in `handbook.sty` (currently
  1.0). It is independent of the package versions (10.x = EF Core 10) and appears on the
  title page and in the page header. Raise it when a changed edition is published: the
  minor version for corrections and additions, the major version for a revised book.

* **Keep both editions in sync.** Both editions share the same structure and the same
  labels; the file names differ by language. Any change to the content belongs in both
  `de/` and `en/`.
* **Cross-references** use `\ref`: `Kapitel~\ref{ch:value-generation}`,
  `chapter~\ref{ch:value-generation}`, `appendix~\ref{app:A}`, `part~\ref{part:VIII}`.
  Chapter labels are derived from the English title and are the same in both editions, so
  inserting a chapter renumbers all references automatically.
* **Code comments name chapters by title, never by number**, because `\ref` does not work
  inside verbatim code: `(chapter "Value generation")` in the repository and the English
  excerpts, `(Kapitel „Value Generation“)` in the German excerpts. When a chapter is
  renamed, search for its old title.
* **Code lines** inside `sourcecode` may be at most 94 characters long; `check_code.py`
  (and CI) fails otherwise. Code is copied verbatim; no LaTeX escaping is needed there.
* **Code excerpts must match the repository.** When a class changes, update its excerpt
  as well; the file path is given in the optional argument of `sourcecode`. Comments in the
  English excerpts use the same wording as the source code, the German excerpts translate
  them.
* **Special characters** in running text must be escaped as usual in LaTeX (`\_`, `\&`,
  `\%`, `\#`, `\{`, `\}`). In the German edition write `\textquotedbl{}` instead of a
  straight `"`, which babel uses as a shorthand.

## License

The handbook, its sources and the generated PDFs are licensed under the MIT License, like
the rest of the repository (see [`LICENSE`](../../LICENSE)). Each PDF contains the full
license text on its last page. The fonts in `fonts/` are under their own free license
(`fonts/LICENSE-DejaVu.txt`).
