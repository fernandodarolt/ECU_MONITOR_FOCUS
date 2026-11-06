param([switch]$NoBrowser,[switch]$Demo,[string]$ReadyFile='')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
Add-Type -Path (Join-Path $root 'src\MonitorCore.cs') -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll'
$engine=New-Object Monitor9M55AD.MonitorEngine (Join-Path $root 'logs')
$listener=New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback),0
$listener.Start()
$port=([Net.IPEndPoint]$listener.LocalEndpoint).Port
$baseUrl="http://127.0.0.1:$port"
$token=[Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N')
$utf8=New-Object Text.UTF8Encoding($false)
$running=$true

function Send-Reply($stream,[int]$status,[string]$type,[byte[]]$data,[string]$download='') {
 $reason=switch($status){200{'OK'}400{'Bad Request'}403{'Forbidden'}404{'Not Found'}409{'Conflict'}500{'Internal Server Error'}default{'Error'}}
 $head="HTTP/1.1 $status $reason`r`nContent-Type: $type`r`nContent-Length: $($data.Length)`r`nConnection: close`r`nCache-Control: no-store`r`nX-Content-Type-Options: nosniff`r`nX-Frame-Options: DENY`r`nCross-Origin-Resource-Policy: same-origin`r`nContent-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`r`n"
 if($download){$safe=[IO.Path]::GetFileName($download) -replace '[^a-zA-Z0-9_.-]','_';$head+="Content-Disposition: attachment; filename=`"$safe`"`r`n"}
 $head+="`r`n";$headerBytes=[Text.Encoding]::ASCII.GetBytes($head);$stream.Write($headerBytes,0,$headerBytes.Length);$stream.Write($data,0,$data.Length);$stream.Flush()
}
function Send-Json($stream,[int]$status,$value){$j=ConvertTo-Json -InputObject $value -Compress -Depth 10;Send-Reply $stream $status 'application/json; charset=utf-8' $utf8.GetBytes($j)}
function Read-Request($stream) {
 $bytes=New-Object 'Collections.Generic.List[byte]';$watch=[Diagnostics.Stopwatch]::StartNew()
 while($true){if($watch.Elapsed.TotalSeconds -gt 4){throw 'Requisição HTTP demorou demais.'};$b=$stream.ReadByte();if($b -lt 0){throw 'Cliente desconectou.'};$bytes.Add([byte]$b);$n=$bytes.Count;if($n -gt 16384){throw 'Cabeçalho HTTP muito grande.'};if($n -ge 4 -and $bytes[$n-4] -eq 13 -and $bytes[$n-3] -eq 10 -and $bytes[$n-2] -eq 13 -and $bytes[$n-1] -eq 10){break}}
 $head=[Text.Encoding]::ASCII.GetString($bytes.ToArray());$lines=$head -split "`r`n";$first=$lines[0] -split ' '
 if($first.Count -ne 3 -or $first[0] -notin @('GET','POST') -or $first[2] -notmatch '^HTTP/1\.[01]$'){throw 'Método HTTP inválido.'}
 $headers=@{};foreach($line in $lines[1..($lines.Length-1)]){if(-not $line){continue};$sep=$line.IndexOf(':');if($sep -lt 1){throw 'Cabeçalho inválido.'};$key=$line.Substring(0,$sep).Trim();if($headers.ContainsKey($key)){throw 'Cabeçalho duplicado.'};$headers[$key]=$line.Substring($sep+1).Trim()}
 if($headers.ContainsKey('Transfer-Encoding')){throw 'Transfer-Encoding não suportado.'}
 $length=0;if($headers.ContainsKey('Content-Length')){if(-not [int]::TryParse($headers['Content-Length'],[ref]$length) -or $length -lt 0 -or $length -gt 8192){throw 'Tamanho de corpo inválido.'}}
 $body=New-Object byte[] $length;$read=0;while($read -lt $length){$n=$stream.Read($body,$read,$length-$read);if($n -le 0){throw 'Corpo HTTP truncado.'};$read+=$n}
 return @{method=$first[0];path=($first[1] -split '\?')[0];headers=$headers;body=$utf8.GetString($body)}
}
function Download-Log($stream,[string]$path,[string]$type){
 if(-not $path -or -not [IO.File]::Exists($path)){Send-Json $stream 404 @{error='Ainda não há arquivo de captura.'};return}
 $fs=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
 try{$mem=New-Object IO.MemoryStream;$fs.CopyTo($mem);$data=$mem.ToArray();$mem.Dispose()}finally{$fs.Dispose()}
 # Ao baixar durante captura, publicar apenas linhas já completas.
 $last=[Array]::LastIndexOf($data,[byte]10);if($last -ge 0 -and $last -lt $data.Length-1){$data=$data[0..$last]}
 Send-Reply $stream 200 $type $data ([IO.Path]::GetFileName($path))
}

try {
 if($Demo){$demoOptions=New-Object Monitor9M55AD.ConnectOptions;$demoOptions.Demo=$true;$demoOptions.ProfileConfirmed=$true;$engine.Start($demoOptions)}
 $ready=@{url=$baseUrl;pid=$PID;mode='server';hardware_opened=$false};$readyJson=ConvertTo-Json $ready -Compress
 if($ReadyFile){[IO.File]::WriteAllText($ReadyFile,$readyJson,$utf8)}
 Write-Output "Monitor9M55AD: $baseUrl"
 Write-Output 'Somente leitura. Adaptador USB em HS-CAN. Feche o FORScan antes de conectar.'
 Write-Output 'Mantenha esta janela aberta. Para encerrar, use Ctrl+C ou feche a janela.'
 if(-not $NoBrowser){Start-Process $baseUrl}
 while($running){
  if(-not $listener.Pending()){Start-Sleep -Milliseconds 40;continue}
  $client=$listener.AcceptTcpClient();$client.ReceiveTimeout=1800;$client.SendTimeout=1800;$stream=$client.GetStream()
  try {
   $request=Read-Request $stream;$headers=$request.headers
   if($headers['Host'] -ne "127.0.0.1:$port" -or ($headers['Origin'] -and $headers['Origin'] -ne $baseUrl) -or $headers['Sec-Fetch-Site'] -eq 'cross-site'){Send-Json $stream 403 @{error='Origem local inválida.'};continue}
   if($request.method -eq 'POST' -and $headers['X-Local-Token'] -cne $token){Send-Json $stream 403 @{error='Token local inválido.'};continue}
   $route=$request.method+' '+$request.path
   switch($route){
    'GET /' {Send-Reply $stream 200 'text/html; charset=utf-8' ([IO.File]::ReadAllBytes((Join-Path $root 'web\index.html')))}
    'GET /index.html' {Send-Reply $stream 200 'text/html; charset=utf-8' ([IO.File]::ReadAllBytes((Join-Path $root 'web\index.html')))}
    'GET /api/bootstrap' {Send-Json $stream 200 @{token=$token;version='1.3.1';profile='9M55AD';readOnly=$true}}
    'GET /api/ports' {Send-Json $stream 200 @{ports=@([IO.Ports.SerialPort]::GetPortNames() | Sort-Object)}}
    'GET /api/status' {Send-Reply $stream 200 'application/json; charset=utf-8' ($utf8.GetBytes($engine.SnapshotJson()))}
    'POST /api/connect' {
     try{
      if($engine.State -in @('connecting','running','demo')){throw 'Pare a conexão atual primeiro.'}
      $config=ConvertFrom-Json $request.body;$o=New-Object Monitor9M55AD.ConnectOptions
      $o.Port=[string]$config.port;$o.Baud=[int]$config.baud;$o.IntervalMs=[int]$config.intervalMs
      if($config.profileConfirmed -isnot [bool] -or $config.profileConfirmed -ne $true){throw 'Confirme o perfil9M55AD e a posição HS-CAN.'}
      $o.ProfileConfirmed=$true;$o.Cylinders=($config.cylinders -eq $true);$o.Context=($config.context -eq $true);$o.Validate();$engine.Start($o)
      Send-Reply $stream 200 'application/json; charset=utf-8' ($utf8.GetBytes($engine.SnapshotJson()))
     }catch{Send-Json $stream 400 @{error=$_.Exception.Message}}
    }
    'POST /api/start-demo' {
     try{if($engine.State -in @('connecting','running','demo')){throw 'Pare a conexão atual primeiro.'};$o=New-Object Monitor9M55AD.ConnectOptions;$o.Demo=$true;$o.ProfileConfirmed=$true;$o.Cylinders=$true;$o.Context=$true;$engine.Start($o);Send-Reply $stream 200 'application/json; charset=utf-8' ($utf8.GetBytes($engine.SnapshotJson()))}catch{Send-Json $stream 409 @{error=$_.Exception.Message}}
    }
    'POST /api/stop' {$engine.Stop();Send-Reply $stream 200 'application/json; charset=utf-8' ($utf8.GetBytes($engine.SnapshotJson()))}
    'POST /api/shutdown' {$engine.Stop();$running=$false;Send-Json $stream 200 @{ok=$true}}
    'GET /api/log' {Download-Log $stream $engine.LogPath 'text/csv; charset=utf-8'}
    'GET /api/raw' {Download-Log $stream $engine.RawLogPath 'application/x-ndjson; charset=utf-8'}
    default {Send-Json $stream 404 @{error='Caminho não encontrado.'}}
   }
  }catch{try{Send-Json $stream 400 @{error=$_.Exception.Message}}catch{}}
  finally{$stream.Dispose();$client.Close()}
 }
} finally {$engine.Stop();$listener.Stop()}
