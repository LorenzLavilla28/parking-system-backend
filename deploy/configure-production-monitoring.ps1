param(
  [string]$Region = 'ap-southeast-1',
  [string]$InstanceId = '',
  [string]$InstanceName = 'parking-saas-api-prod',
  [string]$RoleName = 'ParkingSaaSProductionInstanceRole',
  [string]$Namespace = 'ParkingSaaS/Production',
  [string]$AlarmName = 'parking-saas-prod-root-disk-70',
  [ValidateRange(1, 99)]
  [int]$Threshold = 70,
  [string]$TopicName = 'parking-saas-production-alerts',
  [string]$AlertEmail = ''
)

$ErrorActionPreference = 'Stop'
$cloudWatchAgentPolicyArn = 'arn:aws:iam::aws:policy/CloudWatchAgentServerPolicy'

function Assert-Command {
  param([Parameter(Mandatory)][string]$Name)

  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
    throw "Required command '$Name' was not found."
  }
}

function Invoke-AwsText {
  param([Parameter(Mandatory)][string[]]$Arguments)

  $output = & aws @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "AWS CLI command failed: aws $($Arguments -join ' ')"
  }

  return ($output | Out-String).Trim()
}

function Invoke-Aws {
  param([Parameter(Mandatory)][string[]]$Arguments)

  & aws @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "AWS CLI command failed: aws $($Arguments -join ' ')"
  }
}

function Resolve-InstanceId {
  if ($script:InstanceId) {
    return $script:InstanceId
  }

  $resolved = Invoke-AwsText -Arguments @(
    'ec2', 'describe-instances',
    '--region', $Region,
    '--filters', "Name=tag:Name,Values=$InstanceName", 'Name=instance-state-name,Values=running',
    '--query', 'Reservations[].Instances[].InstanceId | [0]',
    '--output', 'text'
  )

  if (-not $resolved -or $resolved -eq 'None') {
    throw "No running EC2 instance was found with Name=$InstanceName in $Region."
  }

  return $resolved
}

function Wait-SsmCommand {
  param(
    [Parameter(Mandatory)][string]$CommandId,
    [Parameter(Mandatory)][string]$TargetInstanceId
  )

  Write-Host '==> Waiting for CloudWatch Agent configuration' -NoNewline -ForegroundColor Cyan

  for ($attempt = 1; $attempt -le 120; $attempt++) {
    Start-Sleep -Seconds 3
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
      $raw = & aws ssm get-command-invocation `
        --region $Region `
        --command-id $CommandId `
        --instance-id $TargetInstanceId `
        --output json 2>$null
      $awsExitCode = $LASTEXITCODE
    }
    finally {
      $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($awsExitCode -ne 0 -or -not $raw) {
      Write-Host '.' -NoNewline
      continue
    }

    $invocation = ($raw | Out-String | ConvertFrom-Json)
    if ($invocation.Status -in @('Pending', 'InProgress', 'Delayed')) {
      Write-Host '.' -NoNewline
      continue
    }

    Write-Host ''
    if ($invocation.StandardOutputContent) {
      Write-Host $invocation.StandardOutputContent
    }
    if ($invocation.StandardErrorContent) {
      Write-Host $invocation.StandardErrorContent -ForegroundColor Yellow
    }

    if ($invocation.Status -ne 'Success') {
      throw "CloudWatch Agent configuration finished with status $($invocation.Status)."
    }

    return
  }

  Write-Host ''
  throw 'Timed out waiting for the CloudWatch Agent configuration command.'
}

Assert-Command 'aws'
$targetInstanceId = Resolve-InstanceId

$pingStatus = Invoke-AwsText -Arguments @(
  'ssm', 'describe-instance-information',
  '--region', $Region,
  '--filters', "Key=InstanceIds,Values=$targetInstanceId",
  '--query', 'InstanceInformationList[0].PingStatus',
  '--output', 'text'
)
if ($pingStatus -ne 'Online') {
  throw "EC2 instance $targetInstanceId is not online in Systems Manager (status: $pingStatus)."
}

Write-Host "==> Granting $RoleName permission to publish CloudWatch Agent metrics..." -ForegroundColor Cyan
Invoke-Aws -Arguments @(
  'iam', 'attach-role-policy',
  '--role-name', $RoleName,
  '--policy-arn', $cloudWatchAgentPolicyArn
)

$agentConfig = @{
  agent = @{
    metrics_collection_interval = 60
    run_as_user = 'root'
  }
  metrics = @{
    namespace = $Namespace
    append_dimensions = @{
      InstanceId = '${aws:InstanceId}'
    }
    aggregation_dimensions = @(,@('InstanceId'))
    metrics_collected = @{
      disk = @{
        measurement = @('used_percent')
        metrics_collection_interval = 60
        resources = @('/')
        drop_device = $true
        drop_original_metrics = @('used_percent')
        ignore_file_system_types = @(
          'sysfs', 'devtmpfs', 'tmpfs', 'overlay', 'squashfs', 'proc',
          'devpts', 'cgroup', 'cgroup2', 'pstore', 'securityfs', 'debugfs',
          'tracefs', 'configfs'
        )
      }
    }
  }
}

$agentConfigJson = $agentConfig | ConvertTo-Json -Depth 10 -Compress
$agentConfigBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($agentConfigJson))
$requestPath = Join-Path ([IO.Path]::GetTempPath()) "parking-saas-monitoring-$([Guid]::NewGuid().ToString('N')).json"
$remoteConfigPath = '/opt/aws/amazon-cloudwatch-agent/etc/parking-saas.json'

try {
  $request = @{
    DocumentName = 'AWS-RunShellScript'
    InstanceIds = @($targetInstanceId)
    Comment = 'Configure Parking SaaS root disk monitoring'
    TimeoutSeconds = 600
    Parameters = @{
      commands = @(
        'set -euo pipefail',
        'sudo dnf install -y amazon-cloudwatch-agent',
        "printf '%s' '$agentConfigBase64' | base64 -d | sudo tee '$remoteConfigPath' >/dev/null",
        "sudo /opt/aws/amazon-cloudwatch-agent/bin/amazon-cloudwatch-agent-ctl -a fetch-config -m ec2 -s -c 'file:$remoteConfigPath'",
        'sudo systemctl enable amazon-cloudwatch-agent',
        'sudo systemctl is-active amazon-cloudwatch-agent'
      )
      executionTimeout = @('600')
    }
  } | ConvertTo-Json -Depth 10

  [IO.File]::WriteAllText($requestPath, $request, [Text.UTF8Encoding]::new($false))
  $requestUri = 'file://' + $requestPath.Replace('\', '/')

  Write-Host "==> Installing and starting CloudWatch Agent on $targetInstanceId..." -ForegroundColor Cyan
  $commandId = Invoke-AwsText -Arguments @(
    'ssm', 'send-command',
    '--region', $Region,
    '--cli-input-json', $requestUri,
    '--query', 'Command.CommandId',
    '--output', 'text'
  )
  Wait-SsmCommand -CommandId $commandId -TargetInstanceId $targetInstanceId
}
finally {
  Remove-Item -LiteralPath $requestPath -Force -ErrorAction SilentlyContinue
}

Write-Host "==> Creating SNS topic $TopicName..." -ForegroundColor Cyan
$topicArn = Invoke-AwsText -Arguments @(
  'sns', 'create-topic',
  '--region', $Region,
  '--name', $TopicName,
  '--tags', 'Key=Application,Value=PBP-Parking', 'Key=Environment,Value=Production',
  '--query', 'TopicArn',
  '--output', 'text'
)

if ($AlertEmail) {
  $existingSubscription = Invoke-AwsText -Arguments @(
    'sns', 'list-subscriptions-by-topic',
    '--region', $Region,
    '--topic-arn', $topicArn,
    '--query', "Subscriptions[?Endpoint=='$AlertEmail'].SubscriptionArn | [0]",
    '--output', 'text'
  )

  if (-not $existingSubscription -or $existingSubscription -eq 'None') {
    Write-Host "==> Sending SNS subscription confirmation to $AlertEmail..." -ForegroundColor Cyan
    Invoke-Aws -Arguments @(
      'sns', 'subscribe',
      '--region', $Region,
      '--topic-arn', $topicArn,
      '--protocol', 'email',
      '--notification-endpoint', $AlertEmail
    )
  }
  else {
    Write-Host "SNS subscription already exists for $AlertEmail."
  }
}

Write-Host "==> Creating the $Threshold% root disk alarm..." -ForegroundColor Cyan
Invoke-Aws -Arguments @(
  'cloudwatch', 'put-metric-alarm',
  '--region', $Region,
  '--alarm-name', $AlarmName,
  '--alarm-description', "Production EC2 root disk usage has been at or above $Threshold% for 10 minutes.",
  '--namespace', $Namespace,
  '--metric-name', 'disk_used_percent',
  '--dimensions', "Name=InstanceId,Value=$targetInstanceId",
  '--statistic', 'Average',
  '--period', '300',
  '--evaluation-periods', '2',
  '--datapoints-to-alarm', '2',
  '--threshold', $Threshold.ToString(),
  '--comparison-operator', 'GreaterThanOrEqualToThreshold',
  '--treat-missing-data', 'missing',
  '--alarm-actions', $topicArn,
  '--ok-actions', $topicArn,
  '--tags', 'Key=Application,Value=PBP-Parking', 'Key=Environment,Value=Production'
)

Write-Host "Monitoring configured for $targetInstanceId." -ForegroundColor Green
Write-Host "Alarm: $AlarmName"
Write-Host "SNS topic: $topicArn"
if (-not $AlertEmail) {
  Write-Host 'No notification email was supplied. Re-run with -AlertEmail you@example.com to add one.' -ForegroundColor Yellow
}
