param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$snmp = Join-Path $PSScriptRoot 'SharpSnmpLib.dll'
$sources = 'Core.cs','Collector.cs','Engine.cs','Server.cs','Program.cs','AssemblyInfo.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }
$argsList = @('/nologo','/target:winexe','/optimize+','/codepage:65001','/platform:anycpu','/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Security.dll','/reference:System.Web.dll','/reference:System.Web.Extensions.dll',('/reference:' + $snmp),('/out:' + (Join-Path $OutputDirectory 'NetWatch.exe')),('/resource:' + (Join-Path $PSScriptRoot 'index.html') + ',web.index.html'),('/resource:' + (Join-Path $PSScriptRoot 'app.js') + ',web.app.js'),('/resource:' + (Join-Path $PSScriptRoot 'style.css') + ',web.style.css'))
$argsList += '/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')
& $compiler @argsList @sources
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
Copy-Item -LiteralPath $snmp -Destination (Join-Path $OutputDirectory 'SharpSnmpLib.dll') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NetWatch.exe.config') -Destination $OutputDirectory -Force
Write-Output (Join-Path $OutputDirectory 'NetWatch.exe')
