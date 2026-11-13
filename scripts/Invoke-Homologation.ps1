#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Init','Preflight','Build','Migrate','Start','Bootstrap','Status','Stop','Down','Backup','RestoreTest','Smoke','Cleanup')]
    [string]$Action,
    [switch]$Disposable,
    [switch]$TrustCertificate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = if ($Disposable) { 'indicaa2-homologacao-smoke' } else { 'indicaa2-homologacao' }
$private = Join-Path $root $(if ($Disposable) { '.homologacao-smoke' } else { '.homologacao' })
$emptyEnv = Join-Path $private 'compose.env'
$database = 'indicaa2_homologacao'
$ownerLabel = "com.docker.compose.project=$project"
$env:INDICAA2_PRIVATE_DIR = $private

function Invoke-Docker {
    param([string[]]$Arguments, [int]$Timeout = 120, [string]$InputFile, [string]$OutputFile)
    $info = [Diagnostics.ProcessStartInfo]::new('docker')
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.RedirectStandardInput = [bool]$InputFile
    $info.WorkingDirectory = $root
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    $inputStream = $null
    $outputStream = $null
    try {
        $errorTask = $process.StandardError.ReadToEndAsync()
        $outputTask = if ($OutputFile) {
            $outputStream = [IO.File]::Open($OutputFile, 'CreateNew')
            $process.StandardOutput.BaseStream.CopyToAsync($outputStream)
        } else { $process.StandardOutput.ReadToEndAsync() }
        $inputTask = if ($InputFile) {
            $inputStream = [IO.File]::OpenRead($InputFile)
            $inputStream.CopyToAsync($process.StandardInput.BaseStream)
        }
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        if ($inputTask) {
            if (-not $inputTask.Wait([TimeSpan]::FromSeconds($Timeout))) { throw 'Timeout de entrada Docker.' }
            $process.StandardInput.Close()
        }
        $remaining = [Math]::Max(1, $Timeout * 1000 - $deadline.ElapsedMilliseconds)
        if (-not $process.WaitForExit([int]$remaining)) { throw 'Timeout Docker.' }
        if (-not $outputTask.Wait(5000) -or -not $errorTask.Wait(5000)) { throw 'Timeout lendo saída Docker.' }
        if ($process.ExitCode -ne 0) {
            if ($Arguments -contains 'build') { Write-Host $errorTask.Result }
            if ($Disposable -and -not $OutputFile -and (Test-Path (Join-Path $private 'admin.json'))) {
                $diagnostic = $errorTask.Result + $outputTask.Result
                Test-PrivateOutput $diagnostic
                Write-Host $diagnostic
            }
            throw "Docker falhou ($($process.ExitCode)) em $($Arguments[0])."
        }
        if (-not $OutputFile) { $outputTask.GetAwaiter().GetResult().Trim() }
    } finally {
        if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
        if ($inputStream) { $inputStream.Dispose() }
        if ($outputStream) { $outputStream.Dispose() }
        $process.Dispose()
    }
}

function Invoke-Compose {
    param([string[]]$Arguments, [int]$Timeout = 120, [string]$InputFile, [string]$OutputFile)
    Invoke-Docker -Arguments (@('compose','--project-name',$project,'--env-file',$emptyEnv,'-f',(Join-Path $root 'compose.yaml')) + $Arguments) -Timeout $Timeout -InputFile $InputFile -OutputFile $OutputFile
}

function New-Secret { [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }

function Initialize-Private {
    if ($TrustCertificate -and (-not $IsWindows -or $Disposable)) { throw 'Confiança explícita disponível no perfil persistente Windows.' }
    if (Test-Path -LiteralPath $private) { throw 'Diretório privado já existe.' }
    if ($Disposable) {
        foreach ($kind in 'container','network','volume') {
            $flags = if ($kind -eq 'container') { @('-a') } else { @() }
            if (Invoke-Docker -Arguments (@($kind,'ls','-q','--filter',"label=$ownerLabel") + $flags)) {
                throw 'Projeto descartável já existe.'
            }
        }
        $networks = Invoke-Docker -Arguments @('network','ls','--format','{{.Name}}')
        $volumes = Invoke-Docker -Arguments @('volume','ls','--format','{{.Name}}')
        if ("$($project)_loopback" -in ($networks -split '\r?\n') -or "$($project)_private" -in ($networks -split '\r?\n') -or "$($project)_data" -in ($volumes -split '\r?\n')) {
            throw 'Nome Docker reservado já está em uso.'
        }
    }

    [void][IO.Directory]::CreateDirectory($private)
    if (-not $IsWindows) { [IO.File]::SetUnixFileMode($private, 'UserRead,UserWrite,UserExecute') }
    $settings = @{
        MySqlPassword = New-Secret
        JwtKey = New-Secret
        PixKey = New-Secret
        ChargeKey = New-Secret
        CertificatePassword = New-Secret
    }
    $rootPassword = New-Secret
    [IO.File]::WriteAllText($emptyEnv, '')
    [IO.File]::WriteAllText((Join-Path $private 'application.json'), ($settings | ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $private 'mysql-password'), $settings.MySqlPassword)
    [IO.File]::WriteAllText((Join-Path $private 'mysql-root-password'), $rootPassword)
    [IO.File]::WriteAllLines((Join-Path $private 'mysql-client.cnf'),
        @('[client]','user=indicaa2',"password=$($settings.MySqlPassword)",'host=127.0.0.1','protocol=tcp',"database=$database"))
    [IO.File]::WriteAllLines((Join-Path $private 'mysql-root.cnf'), @('[client]','user=root',"password=$rootPassword",'protocol=socket'))

    $key = [Security.Cryptography.RSA]::Create(2048)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=localhost', $key, 'SHA256', [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $san.AddDnsName('localhost')
        $request.CertificateExtensions.Add($san.Build())
        $oids = [Security.Cryptography.OidCollection]::new()
        [void]$oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $false))
        $now = [DateTimeOffset]::UtcNow
        $certificate = $request.CreateSelfSigned($now.AddMinutes(-5), $now.AddDays(30))
        try {
            [IO.File]::WriteAllBytes((Join-Path $private 'localhost.pfx'), $certificate.Export('Pfx', $settings.CertificatePassword))
            [IO.File]::WriteAllText((Join-Path $private 'localhost.crt'), $certificate.ExportCertificatePem())
            if ($TrustCertificate) {
                $store = [Security.Cryptography.X509Certificates.X509Store]::new('Root','CurrentUser')
                try { $store.Open('ReadWrite'); $store.Add($certificate) } finally { $store.Dispose() }
            }
        } finally { $certificate.Dispose() }
    } finally { $key.Dispose() }

    if ($Disposable) {
        $admin = @{Name='Administrador fictício'; Email="$([guid]::NewGuid().ToString('N'))@example.invalid"; Password=(New-Secret)}
        [IO.File]::WriteAllText((Join-Path $private 'admin.json'), ($admin | ConvertTo-Json))
        [IO.File]::WriteAllText((Join-Path $private 'disposable-owner'), $project)
    }
}

function Assert-Owned {
    foreach ($resource in @(@('network',"$($project)_private"), @('network',"$($project)_loopback"), @('volume',"$($project)_data"))) {
        $names = Invoke-Docker -Arguments @($resource[0],'ls','--format','{{.Name}}')
        if ($resource[1] -in ($names -split '\r?\n')) {
            $label = Invoke-Docker -Arguments @($resource[0],'inspect','--format','{{index .Labels "com.docker.compose.project"}}',$resource[1])
            if ($label -cne $project) { throw 'Recurso Docker pertence a outro projeto.' }
        }
    }
    $containers = Invoke-Docker -Arguments @('container','ls','-a','--filter',"label=$ownerLabel",'--format','{{.Names}}')
    foreach ($name in ($containers -split '\r?\n' | Where-Object { $_ })) {
        if ($name -cnotmatch "^$project-(mysql|migrate|api)-1$") { throw 'Container não previsto no projeto.' }
    }
}

function Remove-Disposable {
    $marker = Join-Path $private 'disposable-owner'
    if (-not $Disposable -or -not (Test-Path -LiteralPath $marker) -or
        [IO.File]::ReadAllText($marker) -cne $project) { throw 'Limpeza exige perfil descartável inicializado por este script.' }
    Assert-Owned
    Invoke-Compose -Arguments @('down','--volumes','--timeout','30') | Out-Null
    foreach ($kind in 'container','network','volume') {
        $flags = if ($kind -eq 'container') { @('-a') } else { @() }
        if (Invoke-Docker -Arguments (@($kind,'ls','-q','--filter',"label=$ownerLabel") + $flags)) {
            throw 'Restaram recursos descartáveis do projeto.'
        }
    }
    foreach ($file in 'compose.env','application.json','localhost.pfx','localhost.crt','admin.json','mysql-password','mysql-root-password','mysql-client.cnf','mysql-root.cnf','disposable-owner') {
        [IO.File]::Delete((Join-Path $private $file))
    }
    [IO.Directory]::Delete($private)
    'Containers, redes e volumes descartáveis restantes: 0.'
}

$mysqlWithCredentials = 'umask 077; f=$(mktemp); trap ''rm -f "$f"'' EXIT; cat /run/secrets/mysql-root.cnf > "$f" && tool=$1 && shift && "$tool" --defaults-extra-file="$f" "$@"'

function Invoke-MySql {
    param([string]$Sql, [string]$Schema = '', [string]$InputFile)
    $args = @('exec','-T')
    if (-not $InputFile) { $args += '--interactive=false' }
    $args += @('mysql','sh','-c',$mysqlWithCredentials,'mysql-admin','mysql','--batch','--skip-column-names')
    if ($Schema) { $args += "--database=$Schema" }
    if ($Sql) { $args += "--execute=$Sql" }
    Invoke-Compose -Arguments $args -InputFile $InputFile
}

function Save-Backup {
    $path = Join-Path $private "backup-$([Guid]::NewGuid().ToString('N')).sql"
    try {
        Invoke-Compose -Arguments @('exec','-T','--interactive=false','mysql','sh','-c',$mysqlWithCredentials,'mysql-admin','mysqldump',
            '--single-transaction','--no-tablespaces','--set-gtid-purged=OFF',$database) -OutputFile $path
    } catch { [IO.File]::Delete($path); throw }
    $path
}

function Test-Restore([string]$Path) {
    $temporary = 'indicaa2_restore_' + [Guid]::NewGuid().ToString('N')
    if ($temporary -cnotmatch '^indicaa2_restore_[0-9a-f]{32}$') { throw 'Nome temporário inválido.' }
    Invoke-MySql -Sql "CREATE DATABASE $temporary CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci" | Out-Null
    try {
        Invoke-MySql -Schema $temporary -InputFile $Path | Out-Null
        $query = "SET SESSION group_concat_max_len=8192; SELECT CONCAT(COUNT(*), ':', SHA2(GROUP_CONCAT(CONCAT_WS(':',ordinal,name,sha256,applied_at) ORDER BY ordinal SEPARATOR '|'),256)) FROM migration_journal WHERE applied_at IS NOT NULL"
        $restored = Invoke-MySql -Schema $temporary -Sql $query
        if ($restored -notmatch '^16:' -or $restored -cne (Invoke-MySql -Schema $database -Sql $query)) {
            throw 'Journal restaurado difere do principal.'
        }
        $query = 'SELECT COUNT(*) FROM usuarios'
        if ((Invoke-MySql -Schema $temporary -Sql $query) -cne (Invoke-MySql -Schema $database -Sql $query)) {
            throw 'Contagem de usuários restaurados divergente.'
        }
    } finally { Invoke-MySql -Sql "DROP DATABASE $temporary" | Out-Null }
    'Backup restaurado e banco temporário removido.'
}

function Invoke-Migrations {
    Invoke-Compose -Arguments @('up','-d','--wait','--wait-timeout','90','mysql') | Out-Null
    Invoke-Compose -Arguments @('up','--no-deps','--exit-code-from','migrate','migrate') -Timeout 150
}

function Start-Environment {
    Invoke-Migrations | Out-Null
    Invoke-Compose -Arguments @('up','-d','--wait','--wait-timeout','90','api') | Out-Null
}

function Invoke-Bootstrap {
    if (-not $Disposable -and -not (Test-Path -LiteralPath (Join-Path $private 'admin.json'))) {
        $name = Read-Host 'Nome fictício'
        $email = Read-Host 'E-mail fictício'
        $password = Read-Host 'Senha (20 a 72 bytes UTF-8)' -MaskInput
        [IO.File]::WriteAllText((Join-Path $private 'admin.json'), (@{Name=$name;Email=$email;Password=$password} | ConvertTo-Json))
    }
    try {
        Invoke-Compose -Arguments @('run','--rm','--no-deps','-T','bootstrap') | Out-Null
    } finally {
        if (-not $Disposable) { [IO.File]::Delete((Join-Path $private 'admin.json')) }
    }
}

function Request([string]$Path, [hashtable]$Options = @{}) {
    Invoke-WebRequest -Uri "https://localhost:8443$Path" -SkipCertificateCheck -SkipHttpErrorCheck -TimeoutSec 6 @Options
}

function Assert-Response([string]$Path, [int]$Code) {
    $response = Request $Path
    if ($response.StatusCode -ne $Code) { throw "HTTP inesperado para $Path ($($response.StatusCode))." }
    $response
}

function Wait-Ready([int]$Code) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        try { if ((Request '/health/ready').StatusCode -eq $Code) { return } } catch { }
        Start-Sleep -Milliseconds 500
    } while ($timer.Elapsed.TotalSeconds -lt 60)
    throw "Timeout aguardando readiness $Code."
}

function Test-PrivateOutput([string]$Output, [string[]]$Additional = @()) {
    $settings = Get-Content -Raw (Join-Path $private 'application.json') | ConvertFrom-Json -AsHashtable
    $admin = Get-Content -Raw (Join-Path $private 'admin.json') | ConvertFrom-Json
    $sensitive = @($settings.Values) + @($admin.Password, [IO.File]::ReadAllText((Join-Path $private 'mysql-root-password'))) + $Additional
    foreach ($value in $sensitive) {
        if ($value -and $Output.Contains($value, [StringComparison]::Ordinal)) { throw 'Material privado detectado; saída omitida.' }
    }
    if ($Output -match '(?i)Server=mysql;|User ID=indicaa2;|BEGIN (?:RSA |EC )?PRIVATE KEY') { throw 'Configuração privada detectada.' }
}

function Invoke-Smoke {
    if (-not $Disposable) { throw 'Smoke exige -Disposable.' }
    Initialize-Private
    try {
        Invoke-Compose -Arguments @('config','--quiet') | Out-Null
        Invoke-Compose -Arguments @('build','--pull','--no-cache','api') -Timeout 1200 | Out-Null
        Invoke-Compose -Arguments @('run','--rm','--no-deps','-T','api','--preflight-homologation') | Out-Null
        Invoke-Compose -Arguments @('up','-d','--wait','--wait-timeout','90','mysql') | Out-Null
        Invoke-Compose -Arguments @('up','-d','--no-deps','api') | Out-Null
        Wait-Ready 503
        Assert-Response '/health/live' 200 | Out-Null
        Assert-Owned
        Invoke-Compose -Arguments @('rm','--stop','--force','api') | Out-Null
        Start-Environment
        Wait-Ready 200
        $apiId = Invoke-Compose -Arguments @('ps','-q','api')
        $migrationId = Invoke-Compose -Arguments @('ps','-a','-q','migrate')
        $apiStarted = Invoke-Docker -Arguments @('inspect','--format','{{.State.StartedAt}}',$apiId)
        $migrationFinished = Invoke-Docker -Arguments @('inspect','--format','{{.State.FinishedAt}}',$migrationId)
        if ([DateTimeOffset]::Parse($apiStarted) -lt [DateTimeOffset]::Parse($migrationFinished)) {
            throw 'API iniciou antes da conclusão administrativa.'
        }
        Invoke-Compose -Arguments @('exec','-T','--interactive=false','api','sh','-ec',
            'test "$(id -u)" -ne 0 && ! command -v node && test -z "$(dotnet --list-sdks)" && test ! -e /var/run/docker.sock') | Out-Null
        $live = Assert-Response '/health/live' 200
        $ready = Assert-Response '/health/ready' 200
        if ($live.Content -cne '{"status":"ok"}' -or $ready.Content -cne $live.Content -or
            ($ready.Headers['Cache-Control'] -join ',') -ne 'no-store') { throw 'Contrato de saúde divergente.' }
        $frontendPage = Assert-Response '/' 200
        if ($frontendPage.Content -notmatch '<div id="root">' -or
            ($frontendPage.Headers['X-Content-Type-Options'] -join ',') -ne 'nosniff' -or
            ($frontendPage.Headers['X-Frame-Options'] -join ',') -ne 'DENY' -or
            -not $frontendPage.Headers['Content-Security-Policy']) { throw 'Frontend ou headers inesperados.' }
        foreach ($attempt in 1..2) {
            if ((Assert-Response '/minha-conta' 200).Content -cne $frontendPage.Content) { throw 'Fallback SPA inesperado.' }
        }
        if ((Assert-Response '/api/rota-inexistente' 404).Content -match '<html|id="root"') { throw 'Fallback vazou para API.' }
        $js = [regex]::Match($frontendPage.Content,'src="(/assets/[^"]+\.js)"').Groups[1].Value
        if (-not $js) { throw 'Bundle frontend ausente.' }
        Test-PrivateOutput ((Assert-Response $js 200).Content)

        $admin = Get-Content -Raw (Join-Path $private 'admin.json') | ConvertFrom-Json
        $login = @{Method='Post';ContentType='application/json';Body=(@{email=$admin.Email;senha=(New-Secret)} | ConvertTo-Json)}
        $invalid = Request '/api/auth/login' $login
        if ($invalid.StatusCode -ne 401 -or $invalid.Content -match 'StackTrace|Exception|Password|senha_hash') { throw 'Login inválido inseguro.' }
        Invoke-Bootstrap
        Invoke-Bootstrap
        $login.Body = @{email=$admin.Email;senha=$admin.Password} | ConvertTo-Json
        $session = Request '/api/auth/login' $login
        if ($session.StatusCode -ne 200) { throw 'Login administrativo falhou.' }
        $token = ($session.Content | ConvertFrom-Json).accessToken
        if (-not $token) { throw 'Token administrativo ausente.' }
        if ((Request '/api/admin/dashboard' @{Headers=@{Authorization="Bearer $token"}}).StatusCode -ne 200) { throw 'Dashboard falhou.' }
        Assert-Response '/api/public/indicacoes/codigos/INVALID0' 404 | Out-Null

        $journalQuery = 'SELECT CONCAT(COUNT(*),":",MAX(applied_at)) FROM migration_journal WHERE applied_at IS NOT NULL'
        $journal = Invoke-MySql -Schema $database -Sql $journalQuery
        if ($journal -notmatch '^16:') { throw 'Journal incompleto.' }
        $hash = Invoke-MySql -Schema $database -Sql 'SELECT sha256 FROM migration_journal WHERE ordinal=16'
        try {
            Invoke-MySql -Schema $database -Sql "UPDATE migration_journal SET sha256=REPEAT('0',64) WHERE ordinal=16" | Out-Null
            Wait-Ready 503
            Assert-Response '/health/live' 200 | Out-Null
        } finally { Invoke-MySql -Schema $database -Sql "UPDATE migration_journal SET sha256='$hash' WHERE ordinal=16" | Out-Null }
        Wait-Ready 200

        Invoke-Compose -Arguments @('stop','mysql') | Out-Null
        Wait-Ready 503
        Assert-Response '/health/live' 200 | Out-Null
        Invoke-Compose -Arguments @('up','-d','--wait','--wait-timeout','90','mysql') | Out-Null
        Wait-Ready 200
        Invoke-Compose -Arguments @('restart','api') | Out-Null
        Wait-Ready 200
        Invoke-Compose -Arguments @('stop') | Out-Null
        Start-Environment
        Wait-Ready 200
        if ((Invoke-MySql -Schema $database -Sql $journalQuery) -cne $journal) { throw 'Journal mudou após restart.' }
        if ((Request '/api/auth/login' $login).StatusCode -ne 200) { throw 'Administrador não persistiu.' }

        $backup = Save-Backup
        try {
            Test-PrivateOutput ([IO.File]::ReadAllText($backup))
            Test-Restore $backup
        } finally { [IO.File]::Delete($backup) }
        $config = Invoke-Compose -Arguments @('config','--format','json') | ConvertFrom-Json
        if (-not $config.networks.private.internal -or $config.services.mysql.ports -or
            $config.services.api.ports.Count -ne 1 -or $config.services.api.ports[0].host_ip -ne '127.0.0.1' -or
            -not $config.services.api.read_only -or $config.services.api.privileged) { throw 'Isolamento Compose inválido.' }
        foreach ($setting in 'PagamentoPix__ProcessamentoWorker__Habilitado','RecebimentoPix__ProcessamentoWorker__Habilitado',
            'CashbackPagamentoPreparacaoWorker__Habilitado','RecebimentoPix__Habilitado') {
            if ($config.services.api.environment.$setting -cne 'false') { throw 'Worker ou recebimento habilitado.' }
        }
        foreach ($image in 'indicaa2-homologacao-api:local','mysql:8.0.46') {
            Test-PrivateOutput (Invoke-Docker -Arguments @('history','--no-trunc',$image))
        }
        foreach ($service in 'api','mysql') {
            $id = Invoke-Compose -Arguments @('ps','-q',$service)
            Test-PrivateOutput (Invoke-Docker -Arguments @('inspect',$id))
        }
        Test-PrivateOutput (Invoke-Compose -Arguments @('logs','--no-color','--tail','200')) @($token)
        'Smoke aprovado: HTTPS, SPA, API, journal, autenticação, recuperação, persistência, backup e privacidade.'
    } catch {
        Invoke-Compose -Arguments @('ps','-a') | Write-Output
        try {
            $log = Invoke-Compose -Arguments @('logs','--no-color','--tail','100')
            Test-PrivateOutput $log
            $log -split '\r?\n' | Where-Object { $_ -match 'Migration aplicada:|Falha administrativa|Configuração de homologação inválida|ready for connections' }
        } catch { 'Logs indisponíveis ou omitidos por conterem material privado.' }
        throw
    } finally { Remove-Disposable }
}

if ($Action -eq 'Init') { Initialize-Private; return }
if ($Action -eq 'Smoke') { Invoke-Smoke; return }
if (-not (Test-Path -LiteralPath $emptyEnv)) { throw 'Execute Init para preparar a configuração privada.' }

switch ($Action) {
    'Preflight' {
        Invoke-Compose -Arguments @('config','--quiet') | Out-Null
        Invoke-Compose -Arguments @('run','--rm','--no-deps','-T','api','--preflight-homologation')
    }
    'Build' { Invoke-Compose -Arguments @('build','--pull','--no-cache','api') -Timeout 1200 }
    'Migrate' { Invoke-Migrations }
    'Start' { Start-Environment }
    'Bootstrap' { Invoke-Bootstrap }
    'Status' { Invoke-Compose -Arguments @('ps','-a') }
    'Stop' { Assert-Owned; Invoke-Compose -Arguments @('stop','--timeout','30') }
    'Down' { Assert-Owned; Invoke-Compose -Arguments @('down','--timeout','30') }
    'Backup' { Save-Backup }
    'RestoreTest' {
        $backup = Save-Backup
        try { Test-Restore $backup } finally { [IO.File]::Delete($backup) }
    }
    'Cleanup' { Remove-Disposable }
}
