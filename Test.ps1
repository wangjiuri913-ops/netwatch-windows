$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
$bin=Join-Path $PSScriptRoot 'test-bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$snmp=Join-Path $PSScriptRoot 'SharpSnmpLib.dll'
$sources='Core.cs','Collector.cs','Engine.cs','Tests.cs' | ForEach-Object {Join-Path $PSScriptRoot $_}
$argsList=@('/nologo','/target:exe','/codepage:65001','/reference:System.dll','/reference:System.Core.dll','/reference:System.Security.dll','/reference:System.Web.Extensions.dll',('/reference:'+$snmp),('/out:'+(Join-Path $bin 'NetWatch.Tests.exe')))
& $compiler @argsList @sources
if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
Copy-Item -LiteralPath $snmp -Destination $bin -Force
$data=Join-Path $bin ('data-'+[guid]::NewGuid().ToString('N'))
& (Join-Path $bin 'NetWatch.Tests.exe') $data
if($LASTEXITCODE -ne 0){throw 'Tests failed'}
