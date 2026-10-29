# Starts N instances of the cache stampede API, each in its own window: api-1 on 5011, api-2 on 5012, ...
#   .\scripts\api-cache.ps1            two instances
#   .\scripts\api-cache.ps1 -Count 1   one instance
param(
    [int]$Count = 2
)

$project = Join-Path $PSScriptRoot '..\examples\02-cache-stampede\CacheStampede.Api'
dotnet build $project -v q
if ($LASTEXITCODE -ne 0) { exit 1 }

1..$Count | ForEach-Object {
    $port = 5010 + $_
    $arguments = @(
        'run', '--no-build', '--no-launch-profile', '--project', $project, '--urls', "http://localhost:$port",
        '--', "--Demo:Instance=api-$_"
    )
    Start-Process dotnet -ArgumentList $arguments
    Write-Host "api-$_ -> http://localhost:$port"
}
