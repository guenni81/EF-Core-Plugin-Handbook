#!/usr/bin/env bash
# Consumer test: packs the four libraries, builds a fresh application against the packages (not
# the projects) and runs the real dotnet-ef tooling on it. Everything happens in a temporary
# directory with its own NuGet cache, which is removed at the end.
#
#   eng/consumer-test/run.sh [--nativeaot] [EF Core version for dotnet-ef and the Design package]
#
# Without a version the newest stable 10.0.x patch is used for the tooling. --nativeaot also
# publishes and starts a NativeAOT application against PostgreSQL in Docker (needs clang).
#
# Chapter "Compiled models, trimming and NativeAOT" describes what is checked and what is not.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NATIVEAOT=0
if [ "${1:-}" = "--nativeaot" ]; then NATIVEAOT=1; shift; fi
EF_VERSION="${1:-$(curl -fsS https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.design/index.json \
    | python3 -c 'import json,sys; print([v for v in json.load(sys.stdin)["versions"] if v.startswith("10.0.") and "-" not in v][-1])')}"
WORK="$(mktemp -d)"
[ "${KEEP:-}" = 1 ] && echo "Keeping $WORK" || trap 'rm -rf "$WORK"' EXIT

FEED="$WORK/feed"
APP="$WORK/consumer"
export NUGET_PACKAGES="$WORK/packages"           # never the user's global cache
export DOTNET_CLI_HOME="$WORK/home"              # nor its tool resolver cache
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

step() { printf '\n== %s\n' "$*"; }
fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }
contains() { grep -qF -- "$2" "$1" || fail "'$2' not found in $(basename "$1")"; }

step "Pack the libraries"
for project in Acme.EntityFrameworkCore.Tenancy Acme.EntityFrameworkCore.Tenancy.Relational \
               Acme.EntityFrameworkCore.Tenancy.PostgreSQL Acme.EntityFrameworkCore.Tenancy.SqlServer; do
    dotnet pack "$ROOT/src/$project" -c Release -o "$FEED" --nologo -v quiet \
        -p:ContinuousIntegrationBuild=true
done
ACME_VERSION="$(basename "$FEED"/Acme.EntityFrameworkCore.Tenancy.PostgreSQL.*.nupkg .nupkg \
    | sed 's/^Acme\.EntityFrameworkCore\.Tenancy\.PostgreSQL\.//')"
echo "Acme packages $ACME_VERSION, EF Core tooling $EF_VERSION"

step "Create the consumer"
cp -r "$ROOT/eng/consumer-test/template" "$APP"
# Acme.* only from the local feed, everything else only from nuget.org
cat > "$APP/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="Acme.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
EOF
cd "$APP"
export AcmeVersion="$ACME_VERSION" EFCoreVersion="$EF_VERSION"
dotnet new tool-manifest --force >/dev/null
dotnet tool install dotnet-ef --version "$EF_VERSION" >/dev/null
dotnet tool run dotnet-ef --version | tail -1

step "Restore resolves exactly the packages just built"
dotnet restore --nologo -v quiet
for package in "$FEED"/*.nupkg; do
    file="$(basename "$package" .nupkg)"
    id="$(echo "${file%."$ACME_VERSION"}" | tr '[:upper:]' '[:lower:]')"
    cached="$NUGET_PACKAGES/$id/$ACME_VERSION/$id.$ACME_VERSION.nupkg.sha512"
    [ -f "$cached" ] || fail "$id was not restored"
    [ "$(openssl dgst -sha512 -binary "$package" | base64 -w0)" = "$(cat "$cached")" ] \
        || fail "$id in the cache is not the package just built"
    echo "ok   $id $ACME_VERSION"
done

step "Build"
dotnet build --nologo -v quiet --no-restore

step "Migrations through dotnet-ef (design-time services found automatically)"
ef() { dotnet tool run dotnet-ef "$@" --no-build; }
ef migrations add Initial --context PgContext -o Migrations/Pg >/dev/null
ef migrations add Initial --context SqlContext -o Migrations/Sql >/dev/null
dotnet build --nologo -v quiet --no-restore
ef migrations script --context PgContext -o "$WORK/pg.sql" >/dev/null
ef migrations script --context SqlContext -o "$WORK/sql.sql" >/dev/null
contains "$WORK/pg.sql" 'ALTER TABLE "Orders" ENABLE ROW LEVEL SECURITY;'
contains "$WORK/pg.sql" 'ALTER TABLE "Orders" FORCE ROW LEVEL SECURITY;'
contains "$WORK/pg.sql" 'CREATE POLICY tenant_isolation ON "Orders"'
contains "$WORK/pg.sql" 'CREATE INDEX "IX_Orders_TenantId"'
contains "$WORK/sql.sql" 'CREATE SECURITY POLICY [acme].[tenant_dbo_Orders]'
contains "$WORK/sql.sql" "SESSION_CONTEXT(N''TenantId'')"
contains Migrations/Pg/PgContextModelSnapshot.cs '"Acme:Rls:Enabled", true'
contains Migrations/Sql/SqlContextModelSnapshot.cs '"Acme:SqlServerRls:SessionKey", "TenantId"'
echo "ok   RLS objects in both scripts and snapshots"

step "No phantom changes after the first migration"
ef migrations has-pending-model-changes --context PgContext
ef migrations has-pending-model-changes --context SqlContext

step "Runtime model"
dotnet run --no-build --no-restore

step "Compiled model with the query filter is rejected by EF Core"
if ef dbcontext optimize --context PgContext -o "$WORK/rejected" > "$WORK/optimize.log" 2>&1; then
    fail "EF Core generated a compiled model with a query filter; update chapter and test"
fi
contains "$WORK/optimize.log" "query filters are not supported"
echo "ok   documented limit: no compiled model with query filters"

step "Compiled model without the query filter: generate, compile, load with UseModel"
ef dbcontext optimize --context CompiledPgContext -o CompiledModels -n Consumer.CompiledModels >/dev/null
dotnet build --nologo -v quiet --no-restore -p:DefineConstants=COMPILED_MODEL
dotnet run --no-build --no-restore

if [ "$NATIVEAOT" = 1 ]; then
    step "NativeAOT: publish with precompiled queries, run against PostgreSQL with RLS"
    cp -r "$ROOT/eng/consumer-test/aot-template" "$WORK/aot"
    cp nuget.config dotnet-tools.json "$WORK/aot/"
    container="consumer-aot-$$"
    docker run -d --rm --name "$container" -e POSTGRES_PASSWORD=owner -p 127.0.0.1::5432 \
        postgres:17-alpine >/dev/null
    trap 'docker rm -f "$container" >/dev/null 2>&1; [ "${KEEP:-}" = 1 ] || rm -rf "$WORK"' EXIT
    (cd "$WORK/aot" && dotnet publish -c Release -r linux-x64 -p:PublishAot=true \
        -p:EFOptimizeContext=true -p:EFScaffoldModelStage=publish \
        -p:EFPrecompileQueriesStage=publish -o out --nologo -v quiet) | tee "$WORK/aot.log"
    # EF and Npgsql themselves report trim/AOT warnings (IL2104/IL3053); the plugin must not
    if grep -E "IL(2104|3053)" "$WORK/aot.log" | grep -q "Acme\."; then
        fail "an Acme assembly produced trim or AOT analysis warnings"
    fi
    echo "ok   no trim or AOT analysis warnings from the Acme assemblies"
    until docker exec "$container" pg_isready -U postgres >/dev/null 2>&1; do sleep 1; done
    psql() { docker exec -i "$container" psql -v ON_ERROR_STOP=1 -q "$@"; }
    port="$(docker port "$container" 5432/tcp | head -1 | sed 's/.*://')"

    step "Deployment as in docs/deployment: roles, bundle as shop_owner, app as shop_app"
    psql -U postgres -v owner_password=owner -v app_password=app \
        < "$ROOT/docs/deployment/setup-roles.sql"
    (cd "$APP" && dotnet tool run dotnet-ef migrations bundle --context PgContext \
        --self-contained -r linux-x64 -o "$WORK/efbundle" --force >/dev/null)
    owner="Host=127.0.0.1;Port=$port;Username=shop_owner;Password=owner;Database=shop"
    "$WORK/efbundle" --connection "$owner" >/dev/null
    "$WORK/efbundle" --connection "$owner" | tee "$WORK/bundle.log" >/dev/null
    contains "$WORK/bundle.log" "No migrations were applied"
    echo "ok   bundle applied as the owner; a second run applies nothing"
    if psql -U shop_app -d shop -c 'ALTER TABLE "Orders" DISABLE ROW LEVEL SECURITY' \
        2> "$WORK/ddl.log"; then
        fail "the application role can change the table"
    fi
    contains "$WORK/ddl.log" 'must be owner of table Orders'
    echo "ok   the application role cannot run DDL"
    "$WORK/aot/out/Aot" "Host=127.0.0.1;Port=$port;Username=shop_app;Password=app;Database=shop"
fi

step "All checks passed"
