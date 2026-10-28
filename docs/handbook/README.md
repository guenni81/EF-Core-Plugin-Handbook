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
python3 check_code.py                  # line length, excerpts against the source files
python3 check_structure.py             # editions in sync, references, table widths
python3 check_examples.py              # run the listings marked with "% run:" (.NET SDK, Docker)
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
| `en/08_advanced.tex` · `de/08_fortgeschritten.tex` | Part VIII, advanced extension points |
| `en/09_operations.tex` · `de/09_betrieb.tex` | Part IX, operation and integration |
| `en/10_deep_extensions.tex` · `de/10_tiefe_erweiterungen.tex` | Part X, deep extensions |
| `en/11_providers.tex` · `de/11_provider.tex` | Part XI, multiple providers |
| `en/12_appendices.tex` · `de/12_anhaenge.tex` | Part XII: appendices A–F (exercises, sources), license |
| `check_code.py`, `excerpts.py` | Line length of all code excerpts; bound excerpts against their source files |
| `check_structure.py` | Both editions in sync per chapter, reference targets, datatable widths |
| `check_examples.py` | Runs the listings marked with `% run:`: the exercise's test command (TRX counters, at least one test executed) and the ShopApi session against PostgreSQL in Docker |
| `fonts/` | DejaVu fonts (free license, see `LICENSE-DejaVu.txt`) |

## Building blocks

The chapter files only use the commands from `handbook.sty`:

```latex
\chapter{Value Generation}\label{ch:value-generation}
\versionrisk{stable}                           % stable, medium or volatile (EF1001); listed in chapter 27
\section{...}                                  % sections are not numbered
\code{IMethodCallTranslator}                   % inline code
\begin{sourcecode}[Query/CurrentTenantTranslator.cs]{csharp}
...                                            % csharp, sql, xml or text
\end{sourcecode}
\begin{tip}[Optional title] ... \end{tip}      % also: important, note, failure
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
  1.2). The package versions equal it (`VersionPrefix` in `Directory.Build.props`, 1.2.0),
  so raise both together; it appears on the title page and in the page header. The minor
  version for corrections and additions, the major version for a revised book or a new EF
  Core major version.

* **Keep both editions in sync.** Both editions share the same structure and the same
  labels; the file names differ by language. Any change to the content belongs in both
  `de/` and `en/`.
* **Cross-references** use `\ref`: `Kapitel~\ref{ch:value-generation}`,
  `chapter~\ref{ch:value-generation}`, `appendix~\ref{app:A}`, `part~\ref{part:advanced}`.
  Chapter labels are derived from the English title and are the same in both editions, so
  inserting a chapter renumbers all references automatically.
* **Code comments name chapters by title, never by number**, because `\ref` does not work
  inside verbatim code: `(chapter "Value generation")` in the repository and the English
  excerpts, `(Kapitel „Value Generation“)` in the German excerpts. When a chapter is
  renamed, search for its old title.
* **Code lines** inside `sourcecode` may be at most 94 characters long; `check_code.py`
  (and CI) fails otherwise. Code is copied verbatim; no LaTeX escaping is needed there.
* **Code excerpts must match the repository.** Bind an excerpt to its file with a comment
  line `% source: <path from the repository root>` directly before `\begin{sourcecode}`, in
  both editions; `check_code.py` then fails when the excerpt no longer matches. Comments and
  `...` lines are ignored, so the German excerpts translate comments and excerpts may be
  shortened; line breaks may differ. Add ` (strings)` when messages are translated. An
  excerpt that rewrites code says so in its title ("vereinfacht" / "simplified").
* **Executable examples.** A comment line `% run: <kind>` directly before a shell listing makes
  `check_examples.py` (and the CI job `handbook-examples`) run it; the commands must be identical
  in both editions, only comments after `#` may be translated. Kinds: `dotnet-test` (every line
  a `dotnet test` command; at least one test must run, none fail) and `shopapi` (the ShopApi
  session: database and sample are started by the script, the curl requests come from the
  listing). Only these listings are executed; C# excerpts are bound with `% source:` or marked
  as simplified, other listings are shortened and never compiled.
* **Both editions have the same structure.** `check_structure.py` compares sections,
  listings, `% source:` bindings, tables and callouts per chapter; the relative widths of a
  `datatable` must add up to its number of columns.
* **Special characters** in running text must be escaped as usual in LaTeX (`\_`, `\&`,
  `\%`, `\#`, `\{`, `\}`). In the German edition write `\textquotedbl{}` instead of a
  straight `"`, which babel uses as a shorthand.

## License

The handbook, its sources and the generated PDFs are licensed under the MIT License, like
the rest of the repository (see [`LICENSE`](../../LICENSE)). Each PDF contains the full
license text on its last page. The fonts in `fonts/` are under their own free license
(`fonts/LICENSE-DejaVu.txt`).
