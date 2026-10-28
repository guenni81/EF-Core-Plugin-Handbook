# CLAUDE.md

This repository is primarily a handbook ("The EF Core Plugin Handbook", German and English
edition) with a reference implementation: a multi-tenancy plugin for EF Core 10
(`src/Acme.EntityFrameworkCore.Tenancy` core for any EF provider, `.Relational` layer,
`.PostgreSQL` and `.SqlServer` providers), plus the small sample plugin
`src/Acme.EntityFrameworkCore.Functions.PostgreSQL` (custom SQL node and aggregate, no tenancy).
Code, comments and messages are written in English. Detailed editing rules for the book are
in `docs/handbook/README.md`; contributor rules in `CONTRIBUTING.md`.

## Building the handbook

Sources: `docs/handbook/de/*.tex` (main document `EF-Core-Plugin-Handbuch.tex`) and
`docs/handbook/en/*.tex` (`EF-Core-Plugin-Handbook.tex`), layout in `handbook.sty`. LuaLaTeX
via `latexmk` (`latexmkrc`: `$pdf_mode = 4`, output to `out/`, aux files to `out/aux/`,
ignored by git). The PDFs in `docs/handbook/out/*.pdf` are committed; regenerate them after
content changes (usually as a separate commit "Regenerate the handbook PDFs").

The local TeX Live (2023, no `latexmk`) is too old; build with the Docker image instead
(verified, both editions build in one run):

```bash
cd docs/handbook
python3 check_code.py         # line length <= 94, bound excerpts match the source files
python3 check_structure.py    # editions in sync, \ref targets exist, datatable widths
docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD":/w -w /w texlive/texlive:latest \
  sh -c 'latexmk EF-Core-Plugin-Handbuch.tex && latexmk EF-Core-Plugin-Handbook.tex'
! grep -n 'undefined' out/aux/*.log   # CI fails on undefined references
```

## Rules for the book

- **German and English stay in sync.** Every content change is made in both `de/` and `en/`
  (same structure, same labels; `check_structure.py` compares sections, listings, tables,
  callouts per chapter). In German running text use `\textquotedbl{}` instead of `"`.
- **Code excerpts are copied, not included.** A `% source: <repo path>` line directly before
  `\begin{sourcecode}` binds an excerpt to its file; `check_code.py` (and CI) fails when the
  excerpt no longer matches (comments and `...` lines are ignored, `(strings)` also ignores
  string literals). After changing code: run `check_code.py` and update every reported excerpt
  in both editions. Unbound excerpts that name a repo file must be marked "vereinfacht" /
  "simplified".
- **Cross-references** in the book use `\label`/`\ref` (`ch:...`, `part:...`, `app:...`), so
  chapter numbers in the text update themselves. Code comments name chapters by title, never by
  number; when renaming a chapter, search for the old title.
- **Fixed chapter numbers outside LaTeX must be updated by hand** when chapters move:
  the table "What the plugin demonstrates" in `README.md` (column "Chapter") and the counts in
  the handbook section of `README.md`. Inside the book never write a chapter or part number
  as plain text; check new text with
  `grep -nE "(Kapitel|chapter|Teil|part)~?\s?[0-9IVX]+" de/*.tex en/*.tex | grep -v '\\ref'`.
- Every chapter starts with `\versionrisk{stable|medium|volatile}` after its `\chapter` line;
  the table in the upgrade playbook (chapter 27, `sec:version-dependency`) lists the medium and
  volatile chapters and must be updated when a rating changes or a chapter is added.
- Book edition `\handbookversion` (`handbook.sty`) equals `VersionPrefix` in
  `Directory.Build.props`; raise both together at release time (current work: edition 1.2).

## Building and testing the code

```bash
dotnet build
dotnet test tests/Acme.EntityFrameworkCore.Tenancy.Tests                # unit tests, no database
dotnet test tests/Acme.EntityFrameworkCore.Tenancy.IntegrationTests     # PostgreSQL via Testcontainers (Docker)
dotnet test tests/Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests      # SQL Server via Testcontainers (Docker)
eng/consumer-test/run.sh --nativeaot   # packs, fresh app, dotnet-ef, compiled model, NativeAOT (Docker + clang)
```

Public API changes show up as RS0016/RS0017: update `PublicAPI.Unshipped.txt`. Uses of
internal EF Core / Npgsql APIs (`*.Internal`, EF1001) need a comment explaining why.

## Changelog

Every change goes into `CHANGELOG.md` under `## [Unreleased]` (Keep a Changelog: Added /
Changed / Fixed / Removed); at release the section is folded into the new version.
