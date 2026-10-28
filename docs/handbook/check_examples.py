"""Runs the handbook's executable examples.

    python3 docs/handbook/check_examples.py            # all examples
    python3 docs/handbook/check_examples.py --list     # only show what would run

A listing becomes an executable example through a comment line directly before it:

    % run: dotnet-test
    \\begin{sourcecode}[Shell]{text}

Both editions must contain the same examples with the same commands (comments after "#" may be
translated). Lines ending with a backslash are joined. Kinds:

dotnet-test  Every line is a "dotnet test" command. The script adds a TRX logger and reads the
             counters from the result file: at least one test must have run and none may have
             failed. The exit code alone is not enough, a filter that matches nothing exits with 0.
shopapi      The HTTP session of samples/ShopApi. The listing's "docker compose up -d" and
             "dotnet run" are replaced by a temporary PostgreSQL container and the sample started
             against it; then the listing's curl requests are sent. Every POST must return 201,
             a GET with a tenant header only orders of that tenant, a GET without it none.

Everything runs in a temporary directory and a container with a unique name; both are removed
at the end, also on failure. Requires the .NET SDK and, for shopapi, Docker. Exit code 1 if an
example fails.
"""
import glob
import json
import os
import re
import shlex
import signal
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLOCK = re.compile(r"\\begin\{sourcecode\}\[(.*?)\]\{\w+\}.*?\n(.*?)\\end\{sourcecode\}", re.S)
MARKER = re.compile(r"^% run: ([\w-]+)$")
TRX = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
POSTGRES_IMAGE = "postgres:17-alpine"


class ExampleFailed(Exception):
    pass


def commands(code):
    """The listing's commands: continuations joined, comments and blank lines removed."""
    joined = re.sub(r"\\\n\s*", " ", code)
    result = []
    for line in joined.split("\n"):
        line = shlex.join(shlex.split(line, comments=True))
        if line:
            result.append(line)
    return result


def examples(edition):
    """(kind, file:line, commands) of every marked listing of one edition, in book order."""
    found = []
    for path in sorted(glob.glob(os.path.join(HERE, edition, "*.tex"))):
        text = open(path, encoding="utf-8").read()
        for block in BLOCK.finditer(text):
            previous = text[:block.start()].rstrip("\n").split("\n")[-1]
            if marker := MARKER.match(previous):
                line = text.count("\n", 0, block.start()) + 1
                found.append((marker.group(1), f"{edition}/{os.path.basename(path)}:{line}",
                              commands(block.group(2))))
    return found


def run_dotnet_test(command, work):
    argv = shlex.split(command)
    if argv[:2] != ["dotnet", "test"]:
        raise ExampleFailed(f"not a dotnet test command: {command}")

    name = f"example-{uuid.uuid4().hex[:8]}.trx"
    process = subprocess.run(
        argv + ["--logger", f"trx;LogFileName={name}", "--results-directory", work],
        cwd=ROOT, capture_output=True, text=True)
    trx = os.path.join(work, name)
    if not os.path.isfile(trx):
        # No result file: no test run at all (e.g. the filter matched nothing)
        raise ExampleFailed(f"no test results (exit code {process.returncode}):\n"
                            + process.stdout[-2000:] + process.stderr[-2000:])

    counters = ET.parse(trx).getroot().find(f"{TRX}ResultSummary/{TRX}Counters")
    executed, failed = int(counters.get("executed")), int(counters.get("failed"))
    if executed < 1:
        raise ExampleFailed(f"no test was executed: {command}")
    if failed or process.returncode:
        raise ExampleFailed(f"{failed} of {executed} tests failed (exit code {process.returncode})")
    return f"{executed} test(s) executed, all passed"


def check_dotnet_test(lines, work):
    return "; ".join(run_dotnet_test(line, work) for line in lines)


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def wait_until(condition, seconds, what):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if condition():
            return
        time.sleep(1)
    raise ExampleFailed(f"timed out after {seconds} s waiting for {what}")


def request(method, url, headers):
    req = urllib.request.Request(url, method=method, headers=headers,
                                 data=b"" if method == "POST" else None)
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def parse_curl(line, base):
    argv = shlex.split(line)
    method, headers, url = "GET", {}, None
    i = 1
    while i < len(argv):
        if argv[i] == "-X":
            method, i = argv[i + 1], i + 2
        elif argv[i] == "-H":
            key, value = argv[i + 1].split(":", 1)
            headers[key.strip()] = value.strip()
            i += 2
        else:
            url, i = argv[i], i + 1
    if url is None or not url.startswith("http://localhost:5000/"):
        raise ExampleFailed(f"unexpected curl command: {line}")
    return method, headers, base + url[len("http://localhost:5000"):]


def check_shopapi(lines, work):
    if "docker compose up -d" not in lines or "dotnet run --project samples/ShopApi" not in lines:
        raise ExampleFailed("the listing no longer starts the database and the sample as expected")
    requests = [line for line in lines if line.startswith("curl ")]
    if not requests:
        raise ExampleFailed("the listing contains no curl request")

    container = f"acme-examples-{uuid.uuid4().hex[:8]}"
    app = None
    log_path = os.path.join(work, "shopapi.log")
    try:
        subprocess.run(["docker", "run", "-d", "--rm", "--name", container,
                        "-e", "POSTGRES_USER=shop_owner", "-e", "POSTGRES_PASSWORD=shop_owner",
                        "-e", "POSTGRES_DB=shop", "-p", "127.0.0.1::5432", POSTGRES_IMAGE],
                       check=True, capture_output=True, text=True)
        # Over TCP: the image's init phase runs a server that only listens on the socket
        wait_until(lambda: subprocess.run(
            ["docker", "exec", container, "pg_isready", "-h", "127.0.0.1", "-U", "shop_owner"],
            capture_output=True).returncode == 0, 60, "PostgreSQL")
        pg_port = subprocess.run(["docker", "port", container, "5432/tcp"], check=True,
                                 capture_output=True, text=True).stdout.split(":")[-1].strip()

        app_port = free_port()
        base = f"http://127.0.0.1:{app_port}"
        database = f"Host=127.0.0.1;Port={pg_port};Database=shop_api"
        env = dict(os.environ,
                   ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS=base,
                   ConnectionStrings__Shop=f"{database};Username=shop_app;Password=shop_app",
                   ConnectionStrings__ShopOwner=f"{database};Username=shop_owner;Password=shop_owner")
        with open(log_path, "w") as log:
            app = subprocess.Popen(
                ["dotnet", "run", "--project", "samples/ShopApi", "--no-launch-profile"],
                cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)

        def started():
            if app.poll() is not None:
                raise ExampleFailed("the sample exited:\n" + open(log_path).read()[-3000:])
            try:
                return request("GET", base + "/orders", {})[0] == 200
            except OSError:
                return False

        wait_until(started, 300, "the sample to answer")

        for line in requests:
            method, headers, url = parse_curl(line, base)
            status, body = request(method, url, headers)
            if method == "POST":
                if status != 201:
                    raise ExampleFailed(f"{line}: status {status}")
                continue
            if status != 200:
                raise ExampleFailed(f"{line}: status {status}")
            orders = json.loads(body)
            tenant = headers.get("X-Demo-Tenant")
            if tenant is None and orders:
                raise ExampleFailed(f"{line}: {len(orders)} orders without a tenant")
            if tenant is not None and (not orders or any(
                    o["tenantId"].lower() != tenant.lower() for o in orders)):
                raise ExampleFailed(f"{line}: expected only orders of {tenant}, got {orders}")
        return f"{len(requests)} requests as in the listing, isolation holds"
    finally:
        if app is not None and app.poll() is None:
            os.killpg(app.pid, signal.SIGTERM)
            try:
                app.wait(timeout=30)
            except subprocess.TimeoutExpired:
                os.killpg(app.pid, signal.SIGKILL)
        subprocess.run(["docker", "rm", "-f", container], capture_output=True)


CHECKS = {"dotnet-test": check_dotnet_test, "shopapi": check_shopapi}


def main():
    english, german = examples("en"), examples("de")
    errors = 0
    if [(k, c) for k, _, c in english] != [(k, c) for k, _, c in german]:
        print("the editions differ in their executable examples:")
        for kind, where, lines in english + german:
            print(f"  {where}: {kind}: {lines}")
        return 1

    for kind, where, lines in english:
        if kind not in CHECKS:
            print(f"{where}: unknown kind '{kind}'")
            errors += 1
            continue
        if "--list" in sys.argv:
            print(f"{where}: {kind}: " + " | ".join(lines))
            continue
        with tempfile.TemporaryDirectory(prefix="handbook-example-") as work:
            try:
                print(f"{where}: {kind}: {CHECKS[kind](lines, work)}")
            except (ExampleFailed, subprocess.CalledProcessError) as failure:
                print(f"{where}: {kind}: FAILED: {failure}")
                errors += 1

    if not english:
        print("no executable examples found")
        return 1
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
