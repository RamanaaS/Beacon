# Builds the log-parser-shim native library and its standalone C test/benchmark executable.
# Requires gcc (this project was built against MinGW-w64/WinLibs, POSIX threads, UCRT runtime) on
# PATH. Windows-only for now, same explicit, documented limitation as Phase 6's WindowsFontResolver
# - cross-compiling for a Linux container target is real work deferred to Phase 15/17, not silently
# assumed away.

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $here "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "Building log_parser_shim.dll..."
gcc -shared -O2 -Wall -Wextra `
    -o (Join-Path $dist "log_parser_shim.dll") `
    (Join-Path $here "log_parser.c")
if ($LASTEXITCODE -ne 0) { throw "DLL build failed" }

Write-Host "Building test_log_parser.exe..."
gcc -O2 -Wall -Wextra `
    -o (Join-Path $dist "test_log_parser.exe") `
    (Join-Path $here "test_log_parser.c") (Join-Path $here "log_parser.c")
if ($LASTEXITCODE -ne 0) { throw "Test executable build failed" }

Write-Host "Build succeeded: $dist"
