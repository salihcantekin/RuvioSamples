# Starts N instances of the rate limiting API, each in its own window: api-1 on 5001, api-2 on 5002, ...
#   .\scripts\api.ps1                              two instances, default behaviour
#   .\scripts\api.ps1 -Count 1                     one instance
#   .\scripts\api.ps1 -Outage FailClosed -CircuitBreaker   what happens when Ruvio is down (preview)
param(
    [int]$Count = 2,
    [ValidateSet('Throw', 'FailOpen', 'FailClosed')][string]$Outage = 'Throw',
    [switch]$CircuitBreaker,
    [string]$KeyPrefix = 'demo:rate:v1:'
)

$project = Join-Path $PSScriptRoot '..\examples\01-rate-limiting\RateLimiting.Api'
dotnet build $project -v q
if ($LASTEXITCODE -ne 0) { exit 1 }

1..$Count | ForEach-Object {
    $port = 5000 + $_
    $arguments = @(
        'run', '--no-build', '--no-launch-profile', '--project', $project, '--urls', "http://localhost:$port",
        '--', "--Demo:Instance=api-$_", "--Demo:Outage=$Outage", "--Demo:CircuitBreaker=$($CircuitBreaker.IsPresent.ToString().ToLower())",
        "--Demo:KeyPrefix=$KeyPrefix"
    )
    Start-Process dotnet -ArgumentList $arguments
    Write-Host "api-$_ -> http://localhost:$port"
}
