$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path 'dist' | Out-Null
$references = @('/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Runtime.Serialization.dll')
& $compiler /nologo /utf8output /codepage:65001 /target:winexe /platform:anycpu /optimize+ /out:dist\FastAccount.exe @references src\Vault.cs src\AccountCsv.cs src\Dialogs.cs src\MainForm.cs src\Program.cs
if ($LASTEXITCODE -ne 0) { throw '程序编译失败' }
& $compiler /nologo /utf8output /codepage:65001 /target:exe /out:tests\Tests.exe /r:System.Runtime.Serialization.dll src\Vault.cs tests\Tests.cs
if ($LASTEXITCODE -ne 0) { throw '测试编译失败' }
& .\tests\Tests.exe
if ($LASTEXITCODE -ne 0) { throw '测试失败' }
& $compiler /nologo /utf8output /codepage:65001 /target:exe /out:tests\CsvTests.exe /r:System.Runtime.Serialization.dll src\Vault.cs src\AccountCsv.cs tests\CsvTests.cs
if ($LASTEXITCODE -ne 0) { throw 'CSV tests build failed' }
& .\tests\CsvTests.exe
if ($LASTEXITCODE -ne 0) { throw 'CSV tests failed' }
& $compiler /nologo /utf8output /codepage:65001 /target:exe /out:tests\UiTests.exe @references src\Vault.cs src\AccountCsv.cs src\Dialogs.cs src\MainForm.cs tests\UiTests.cs
if ($LASTEXITCODE -ne 0) { throw '界面测试编译失败' }
& .\tests\UiTests.exe
if ($LASTEXITCODE -ne 0) { throw '界面测试失败' }
Write-Host 'Built: dist\FastAccount.exe'
