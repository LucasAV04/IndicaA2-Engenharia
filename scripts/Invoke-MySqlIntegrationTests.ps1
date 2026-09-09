#requires -Version 7.4
[CmdletBinding()]
param(
    [switch]$RequireMySql
)

$connectionName = 'INDICA2_TEST_MYSQL_CONNECTION'
$connectionString = [Environment]::GetEnvironmentVariable($connectionName)

if ([string]::IsNullOrWhiteSpace($connectionString)) {
    if ($RequireMySql) {
        Write-Output "ERROR: A variavel $connectionName e obrigatoria neste ambiente."
        exit 2
    }

    Write-Output "SKIPPED: A variavel $connectionName nao esta configurada. As integracoes MySQL nao foram executadas."
    exit 0
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests/Infrastructure.Tests/Infrastructure.Tests.csproj'
$connector = Join-Path $root 'tests/Infrastructure.Tests/bin/Debug/net9.0/MySqlConnector.dll'

if (-not (Test-Path $connector)) {
    Write-Error 'O assembly MySqlConnector nao foi encontrado. Execute dotnet build antes da suite MySQL.'
    exit 3
}

try {
    Add-Type -Path $connector
    $builder = [MySqlConnector.MySqlConnectionStringBuilder]::new($connectionString)
    if ([string]::IsNullOrWhiteSpace($builder.Server) -or -not [string]::IsNullOrWhiteSpace($builder.Database)) {
        throw 'A connection string deve apontar para um servidor MySQL administrativo sem database.'
    }

    $connection = [MySqlConnector.MySqlConnection]::new($connectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = 'SELECT 1;'
            [void]$command.ExecuteScalar()
        }
        finally {
            $command.Dispose()
        }
    }
    finally {
        $connection.Dispose()
    }
}
catch {
    Write-Error 'O preflight MySQL falhou. A suite de integracao nao foi iniciada.'
    exit 1
}

$bytes = [Text.Encoding]::UTF8.GetBytes($connectionString)
$hash = [Security.Cryptography.SHA256]::HashData($bytes)
$env:INDICA2_TEST_MYSQL_PREFLIGHT_HASH = [Convert]::ToHexString($hash).ToLowerInvariant()

& dotnet test $project --no-build --no-restore --filter 'Category=MySqlIntegration' --logger 'console;verbosity=minimal'
exit $LASTEXITCODE
