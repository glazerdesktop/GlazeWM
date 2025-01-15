$ErrorActionPreference = 'Stop'; # stop on all errors

$toolsDir          = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$installerLocation = Join-Path $toolsDir 'installer-universal.exe'

$packageArgs = @{
  packageName    = $packageName
  fileType       = 'exe'
  file           = $installerLocation
  silentArgs     = "/qn /norestart"
  validExitCodes = @(0, 3010, 1641)
  softwareName   = 'GlazeWM'
}

Install-ChocolateyInstallPackage @packageArgs
