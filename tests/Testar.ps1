$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not (Test-Path (Join-Path $root 'src\MonitorCore.cs'))){ Write-Output 'FAIL: núcleo de leitura ainda não implementado'; exit 1 }
try {
  Add-Type -Path @((Join-Path $root 'src\MonitorCore.cs'),(Join-Path $PSScriptRoot 'CoreTests.cs'),(Join-Path $PSScriptRoot 'InitializationTests.cs')) -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll'
  $count=[CoreTests]::Run()
  $result=@{result='PASS';checks=$count;vehicle='NOT_VERIFIED';serial_ports_opened=0;utc=[DateTime]::UtcNow.ToString('o')}
  [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'core_result.json'),(ConvertTo-Json $result -Compress),(New-Object Text.UTF8Encoding($false)))
} catch { Write-Output $_.Exception.ToString(); exit 1 }
