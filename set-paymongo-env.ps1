# Sets PayMongo credentials for the current PowerShell session.
# Run this in the same terminal before starting the API or Docker Compose:
#   . .\set-paymongo-env.ps1

$secretKey = Read-Host "PayMongo secret key (sk_test_...)"
$webhookSecret = Read-Host "PayMongo webhook secret"

if ([string]::IsNullOrWhiteSpace($secretKey)) {
    throw "PayMongo secret key cannot be empty."
}

if ([string]::IsNullOrWhiteSpace($webhookSecret)) {
    throw "PayMongo webhook secret cannot be empty."
}

# ASP.NET Core configuration names
$env:PayMongo__SecretKey = $secretKey
$env:PayMongo__WebhookSecret = $webhookSecret

# Docker Compose variable names
$env:PAYMONGO_SECRET_KEY = $secretKey
$env:PAYMONGO_WEBHOOK_SECRET = $webhookSecret

Write-Host "PayMongo environment variables set for this PowerShell session."
