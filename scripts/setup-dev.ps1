#Requires -Version 5.1
<#
.SYNOPSIS
    Setup completo do ambiente de desenvolvimento VisuFiscalHub.

.DESCRIPTION
    Executa em ordem:
      1. Valida pré-requisitos (Docker, dotnet, dotnet-ef)
      2. Cria infra/.env com senha gerada
      3. Sobe PostgreSQL via Docker Compose
      4. Aplica migrations EF Core
      5. Gera par RSA-2048 para JWT
      6. Gera chave AES-256 para criptografia de certificados
      7. Gera AdminKey aleatória
      8. Escreve appsettings.Development.json completo
      9. Imprime o resumo de credenciais

.NOTES
    Execute a partir da raiz do repositório:
        .\scripts\setup-dev.ps1

    Para resetar tudo (apagar banco e recriar):
        .\scripts\setup-dev.ps1 -Reset
#>

param(
    [switch]$Reset
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Cores e helpers ──────────────────────────────────────────────────────────

function Write-Header([string]$msg) {
    Write-Host ""
    Write-Host "══════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host "══════════════════════════════════════════" -ForegroundColor Cyan
}

function Write-Step([string]$msg) {
    Write-Host "  ► $msg" -ForegroundColor Yellow
}

function Write-Ok([string]$msg) {
    Write-Host "  ✔ $msg" -ForegroundColor Green
}

function Write-Fail([string]$msg) {
    Write-Host "  ✘ $msg" -ForegroundColor Red
    exit 1
}

# ── Verificar que estamos na raiz do repositório ──────────────────────────────

$repoRoot = Split-Path -Parent $PSScriptRoot
$infraDir = Join-Path $repoRoot "infra"
$apiProject = Join-Path $repoRoot "src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj"
$infraProject = Join-Path $repoRoot "src\VisuFiscalHub.Infrastructure\VisuFiscalHub.Infrastructure.csproj"
$appSettingsPath = Join-Path $repoRoot "src\VisuFiscalHub.Api\appsettings.Development.json"

if (-not (Test-Path $apiProject)) {
    Write-Fail "Execute o script a partir da raiz do repositório."
}

# ── 1. Pré-requisitos ─────────────────────────────────────────────────────────

Write-Header "1/8 — Verificando pré-requisitos"

Write-Step "Docker..."
try { docker info 2>$null | Out-Null } catch { Write-Fail "Docker não encontrado ou não está em execução." }
Write-Ok "Docker OK"

Write-Step "dotnet SDK..."
try { dotnet --version | Out-Null } catch { Write-Fail "dotnet SDK não encontrado." }
$sdkVersion = dotnet --version
Write-Ok "dotnet $sdkVersion"

Write-Step "dotnet-ef..."
$efInstalled = dotnet tool list -g 2>$null | Select-String "dotnet-ef"
if (-not $efInstalled) {
    Write-Step "Instalando dotnet-ef globalmente..."
    dotnet tool install --global dotnet-ef | Out-Null
    Write-Ok "dotnet-ef instalado"
} else {
    Write-Ok "dotnet-ef OK"
}

# ── 2. infra/.env ─────────────────────────────────────────────────────────────

Write-Header "2/8 — Configurando infra/.env"

$envFile = Join-Path $infraDir ".env"

if ($Reset -and (Test-Path $envFile)) {
    Write-Step "Reset: removendo .env existente..."
    Remove-Item $envFile -Force
}

if (-not (Test-Path $envFile)) {
    # Gera senha do banco com caracteres seguros para connection string (sem @, ;, =)
    $allowedChars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!#%^&*()'
    $pgPassword = -join ((1..32) | ForEach-Object { $allowedChars[(Get-Random -Maximum $allowedChars.Length)] })

    @"
# Gerado por setup-dev.ps1 — não commitar
POSTGRES_DB=visufiscalhub
POSTGRES_USER=visufiscal
POSTGRES_PASSWORD=$pgPassword
POSTGRES_PORT=5432
REDIS_PORT=6379
"@ | Out-File -FilePath $envFile -Encoding utf8

    Write-Ok ".env criado com senha gerada"
} else {
    Write-Ok ".env já existe — mantendo"
}

# Ler senha do banco para a connection string
$envContent = Get-Content $envFile
$pgPassword = ($envContent | Where-Object { $_ -match "^POSTGRES_PASSWORD=" }) -replace "^POSTGRES_PASSWORD=", ""
$connectionString = "Host=localhost;Port=5432;Database=visufiscalhub;Username=visufiscal;Password=$pgPassword"

# ── 3. Docker Compose (PostgreSQL) ────────────────────────────────────────────

Write-Header "3/8 — Subindo PostgreSQL via Docker Compose"

Push-Location $infraDir
try {
    if ($Reset) {
        Write-Step "Reset: derrubando containers e volumes..."
        docker compose down -v 2>$null | Out-Null
        Write-Ok "Containers e volumes removidos"
    }

    Write-Step "Iniciando containers..."
    docker compose up -d 2>&1 | Out-Null

    Write-Step "Aguardando PostgreSQL ficar saudável..."
    $attempts = 0
    do {
        Start-Sleep -Seconds 2
        $attempts++
        $health = docker inspect visu_fiscal_hub_postgres --format "{{.State.Health.Status}}" 2>$null
        if ($attempts -gt 30) { Write-Fail "PostgreSQL não ficou saudável após 60s." }
    } while ($health -ne "healthy")

    Write-Ok "PostgreSQL saudável (porta 5432)"
} finally {
    Pop-Location
}

# ── 4. Migrations EF Core ─────────────────────────────────────────────────────

Write-Header "4/8 — Aplicando migrations EF Core"

Write-Step "dotnet ef database update..."
$env:ConnectionStrings__DefaultConnection = $connectionString
dotnet ef database update `
    --project $infraProject `
    --startup-project $apiProject `
    2>&1 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

if ($LASTEXITCODE -ne 0) { Write-Fail "Falha ao aplicar migrations." }
Write-Ok "Migrations aplicadas"

# ── 5. Par RSA-2048 para JWT ──────────────────────────────────────────────────

Write-Header "5/8 — Gerando par RSA-2048 para JWT"

$keysDir = Join-Path $repoRoot "scripts\keys"
if (-not (Test-Path $keysDir)) { New-Item -ItemType Directory -Path $keysDir | Out-Null }

$privateKeyPath = Join-Path $keysDir "jwt_private.pem"
$publicKeyPath  = Join-Path $keysDir "jwt_public.pem"

if ($Reset -or -not (Test-Path $privateKeyPath)) {
    # Usa openssl se disponível, senão usa dotnet script inline
    $opensslAvailable = $null -ne (Get-Command openssl -ErrorAction SilentlyContinue)

    if ($opensslAvailable) {
        Write-Step "Gerando via openssl..."
        openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out $privateKeyPath 2>$null
        openssl rsa -pubout -in $privateKeyPath -out $publicKeyPath 2>$null
    } else {
        Write-Step "Gerando via dotnet (openssl não disponível)..."
        $genScript = @'
using System.Security.Cryptography;
using var rsa = RSA.Create(2048);
var priv = rsa.ExportRSAPrivateKeyPem();
var pub  = rsa.ExportSubjectPublicKeyInfoPem();
// Escreve em formato PEM padrão
File.WriteAllText(args[0], priv);
File.WriteAllText(args[1], pub);
Console.WriteLine("OK");
'@
        $tempScript = Join-Path $env:TEMP "gen_rsa.csx"
        $genScript | Out-File -FilePath $tempScript -Encoding utf8
        dotnet script $tempScript $privateKeyPath $publicKeyPath 2>$null

        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $privateKeyPath)) {
            # Fallback: gera diretamente via PowerShell com System.Security.Cryptography
            Write-Step "Fallback: gerando via .NET em PowerShell..."
            Add-Type -AssemblyName System.Security
            $rsa = [System.Security.Cryptography.RSA]::Create(2048)
            [System.IO.File]::WriteAllText($privateKeyPath, $rsa.ExportRSAPrivateKeyPem())
            [System.IO.File]::WriteAllText($publicKeyPath,  $rsa.ExportSubjectPublicKeyInfoPem())
            $rsa.Dispose()
        }
    }
    Write-Ok "Par RSA gerado em scripts\keys\"
} else {
    Write-Ok "Par RSA já existe — mantendo"
}

$privateKeyPem = (Get-Content $privateKeyPath -Raw).Trim()
$publicKeyPem  = (Get-Content $publicKeyPath  -Raw).Trim()

# JSON exige newlines escapados para strings multi-linha
$privateKeyPemJson = $privateKeyPem -replace "`r`n", "\n" -replace "`n", "\n"
$publicKeyPemJson  = $publicKeyPem  -replace "`r`n", "\n" -replace "`n", "\n"

# ── 6. Chave AES-256 para criptografia de certificados ────────────────────────

Write-Header "6/8 — Gerando chave AES-256 (CERT:EncryptionKey)"

$certKeyPath = Join-Path $keysDir "cert_encryption.key"

if ($Reset -or -not (Test-Path $certKeyPath)) {
    $aesKeyBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $aesKeyBase64 = [Convert]::ToBase64String($aesKeyBytes)
    $aesKeyBase64 | Out-File -FilePath $certKeyPath -Encoding ascii -NoNewline
    Write-Ok "Chave AES-256 gerada (32 bytes, Base64)"
} else {
    Write-Ok "Chave AES-256 já existe — mantendo"
}

$certEncryptionKey = (Get-Content $certKeyPath -Raw).Trim()

# ── 7. AdminKey ───────────────────────────────────────────────────────────────

Write-Header "7/8 — Gerando AdminKey"

$adminKeyPath = Join-Path $keysDir "admin.key"

if ($Reset -or -not (Test-Path $adminKeyPath)) {
    $adminKeyBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $adminKey = [Convert]::ToHexString($adminKeyBytes).ToLower()
    $adminKey | Out-File -FilePath $adminKeyPath -Encoding ascii -NoNewline
    Write-Ok "AdminKey gerada (64 hex chars)"
} else {
    Write-Ok "AdminKey já existe — mantendo"
}

$adminKey = (Get-Content $adminKeyPath -Raw).Trim()

# ── 8. appsettings.Development.json ──────────────────────────────────────────

Write-Header "8/8 — Escrevendo appsettings.Development.json"

$appSettings = @"
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Hangfire": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "$connectionString"
  },
  "Jwt": {
    "Issuer": "visu-fiscal-hub",
    "Audience": "visu-fiscal-hub-clients",
    "ExpiresInSeconds": 3600,
    "PrivateKeyPem": "$privateKeyPemJson",
    "PublicKeyPems": [
      "$publicKeyPemJson"
    ]
  },
  "AdminKey": {
    "Value": "$adminKey"
  },
  "CERT": {
    "EncryptionKey": "$certEncryptionKey"
  }
}
"@

$appSettings | Out-File -FilePath $appSettingsPath -Encoding utf8
Write-Ok "appsettings.Development.json escrito"

# ── Resumo ────────────────────────────────────────────────────────────────────

Write-Header "Setup concluído"

Write-Host ""
Write-Host "  CREDENCIAIS DE DESENVOLVIMENTO" -ForegroundColor White
Write-Host "  ──────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  PostgreSQL   : localhost:5432 / visufiscalhub" -ForegroundColor White
Write-Host "  AdminKey     : $adminKey" -ForegroundColor Yellow
Write-Host "  Chaves JWT   : scripts\keys\jwt_private.pem / jwt_public.pem" -ForegroundColor White
Write-Host "  Chave CERT   : scripts\keys\cert_encryption.key" -ForegroundColor White
Write-Host ""
Write-Host "  PRÓXIMOS PASSOS" -ForegroundColor White
Write-Host "  ──────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  1. Subir a API:" -ForegroundColor White
Write-Host "     dotnet run --project src\VisuFiscalHub.Api" -ForegroundColor Cyan
Write-Host ""
Write-Host "  2. Abrir Hangfire dashboard (após subir a API):" -ForegroundColor White
Write-Host "     http://localhost:5000/hangfire" -ForegroundColor Cyan
Write-Host ""
Write-Host "  3. Criar o primeiro ClienteApp (substituir ADMIN_KEY abaixo):" -ForegroundColor White
Write-Host @"
     curl -X POST http://localhost:5000/api/v1/clientes ``
       -H "Content-Type: application/json" ``
       -H "X-Admin-Key: $adminKey" ``
       -d '{"name":"Meu App","clientId":"meu-app","clientSecret":"MinhaS3nha!","webhookUrl":null}'
"@ -ForegroundColor Cyan
Write-Host ""
Write-Host "  ATENÇÃO" -ForegroundColor Red
Write-Host "  ──────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  • Os arquivos em scripts\keys\ contêm segredos — não commitar." -ForegroundColor Red
Write-Host "  • appsettings.Development.json está no .gitignore? Verifique." -ForegroundColor Red
Write-Host ""
