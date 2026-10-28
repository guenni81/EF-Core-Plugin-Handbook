# Contributing

Thanks for your interest! Issues and pull requests are welcome, both for the plugin code
and for the handbook.

## Code

1. Build and run the tests before opening a pull request:
   ```bash
   dotnet build
   dotnet test tests/Acme.EntityFrameworkCore.Tenancy.Tests
   dotnet test tests/Acme.EntityFrameworkCore.Tenancy.IntegrationTests   # requires Docker
   dotnet test tests/Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests     # requires Docker
   dotnet test tests/Acme.EntityFrameworkCore.Tenancy.Cosmos.Tests        # requires Docker (emulator)
   ```
2. Code, comments and messages are written in English.
3. The packages use `Microsoft.CodeAnalysis.PublicApiAnalyzers`: changes to the public API
   show up as RS0016/RS0017 warnings. Update `PublicAPI.Unshipped.txt` accordingly, for
   example with the IDE code fix "Add all items in document/project/solution to the public
   API". At release time, move the entries to `PublicAPI.Shipped.txt`.
4. Places that use internal EF Core or Npgsql APIs (`*.Internal`, warning EF1001) need a
   comment explaining why.

## Handbook

The handbook is written in LaTeX and exists in English (`docs/handbook/en`) and German
(`docs/handbook/de`). Please keep both editions in sync: a change to the content
belongs in both languages. If you are not comfortable writing one of them, say so in the
pull request and someone else will translate it.

Code excerpts in the handbook should match the code in the repository. When you change a
class, also update its excerpt (the file path is in the optional argument of each
`sourcecode` block). Comments that point to the handbook name the chapter by its title,
not its number, e.g. `(chapter "Value generation")`. See
[`docs/handbook/README.md`](docs/handbook/README.md) for build instructions and editing
rules.
