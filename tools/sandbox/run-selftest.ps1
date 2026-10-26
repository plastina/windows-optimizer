# Runs inside Windows Sandbox (started by the .wsb logon command).
# Applies and undoes every tweak for real and writes the report to the mapped C:\out folder.
$ErrorActionPreference = 'Continue'
$out = 'C:\out'
New-Item -ItemType Directory -Path $out -Force | Out-Null

try {
    $process = Start-Process -FilePath 'C:\pkg\WindowsOptimizer.exe' `
        -ArgumentList '--selftest', "$out\report.md" `
        -Verb RunAs -Wait -PassThru
    "exit=$($process.ExitCode)" | Out-File "$out\exitcode.txt" -Encoding ascii
} catch {
    "crash: $($_.Exception.Message)" | Out-File "$out\exitcode.txt" -Encoding ascii
}

# The app's own log shows what each tweak actually did.
$log = Join-Path $env:ProgramData 'WindowsOptimizer\windows-optimizer.log'
if (Test-Path $log) { Copy-Item $log "$out\windows-optimizer.log" -Force }
'done' | Out-File "$out\done.txt" -Encoding ascii
