param(
    [string]$Configuration = "Debug",
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\REPO",
    [string]$R2Profile = "$env:APPDATA\r2modmanPlus-local\REPO\profiles\REPO",
    [switch]$InstallToProfile,
    [switch]$PackageToDesktop
)

$ErrorActionPreference = "Stop"

$modName = "HellRevival"
$modVersion = "0.1.6"
$authorName = "AngelcoMilk"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$distRoot = Join-Path $root "dist"
$outDir = Join-Path $distRoot "BepInEx\plugins\$modName"
$managed = Join-Path $GameDir "REPO_Data\Managed"
$bepCore = Join-Path $R2Profile "BepInEx\core"
$pluginOut = Join-Path $outDir "$modName.dll"
$desktopZip = Join-Path ([Environment]::GetFolderPath("Desktop")) "$modName-$modVersion.zip"
$packageSource = Join-Path $root "package"

if (!(Test-Path (Join-Path $managed "Assembly-CSharp.dll"))) {
    throw "Assembly-CSharp.dll not found under $managed"
}
if (!(Test-Path (Join-Path $bepCore "BepInEx.dll"))) {
    throw "BepInEx core not found under $bepCore"
}
if (!(Test-Path $src)) {
    throw "Source directory not found at $src"
}
if (!(Test-Path $packageSource)) {
    throw "Package directory not found at $packageSource"
}

if (Test-Path $distRoot) {
    $resolvedDist = (Resolve-Path -LiteralPath $distRoot).Path
    $resolvedRoot = (Resolve-Path -LiteralPath $root).Path
    if (!$resolvedDist.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean dist outside workspace: $resolvedDist"
    }

    Remove-Item -LiteralPath $distRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (!(Test-Path $csc)) {
    throw "No .NET Framework csc.exe found."
}

$sources = Get-ChildItem -LiteralPath $src -Filter "*.cs" -Recurse | ForEach-Object { $_.FullName }

$refs = @(
    (Join-Path $bepCore "BepInEx.dll"),
    (Join-Path $bepCore "0Harmony.dll"),
    (Join-Path $managed "Assembly-CSharp.dll"),
    (Join-Path $managed "UnityEngine.dll"),
    (Join-Path $managed "UnityEngine.CoreModule.dll"),
    (Join-Path $managed "UnityEngine.PhysicsModule.dll"),
    (Join-Path $managed "PhotonUnityNetworking.dll"),
    (Join-Path $managed "PhotonRealtime.dll"),
    (Join-Path $managed "Photon3Unity3D.dll"),
    (Join-Path $managed "netstandard.dll")
)

$refArgs = $refs | ForEach-Object { "/reference:$_" }

function Test-GameHookTargets {
    param(
        [string]$AssemblyPath,
        [string]$CecilPath
    )

    if (!(Test-Path $CecilPath)) {
        Write-Warning "Mono.Cecil.dll not found; skipping Harmony hook target validation."
        return
    }

    Add-Type -Path $CecilPath
    $assemblyPaths = @(
        $AssemblyPath,
        (Join-Path (Split-Path -Parent $AssemblyPath) "PhotonUnityNetworking.dll"),
        (Join-Path (Split-Path -Parent $AssemblyPath) "PhotonRealtime.dll"),
        (Join-Path (Split-Path -Parent $AssemblyPath) "Photon3Unity3D.dll")
    ) | Where-Object { Test-Path $_ }

    $assemblies = @($assemblyPaths | ForEach-Object { [Mono.Cecil.AssemblyDefinition]::ReadAssembly($_) })

    function Find-TypeDefinition {
        param([string]$TypeName)
        foreach ($candidateAssembly in $assemblies) {
            $type = $candidateAssembly.MainModule.Types | Where-Object { $_.Name -eq $TypeName -or $_.FullName -eq $TypeName } | Select-Object -First 1
            if ($null -ne $type) {
                return $type
            }
        }

        return $null
    }

    $targets = @(
        @{ Type = "PlayerDeathHead"; Method = "Update"; Parameters = @() },
        @{ Type = "PlayerDeathHead"; Method = "Revive"; Parameters = @() },
        @{ Type = "PlayerAvatar"; Method = "ReviveRPC"; Parameters = @("System.Boolean", "Photon.Pun.PhotonMessageInfo") },
        @{ Type = "PlayerHealth"; Method = "HealOther"; Parameters = @("System.Int32", "System.Boolean") },
        @{ Type = "Photon.Pun.PhotonNetwork"; Method = "get_LocalPlayer"; Parameters = @() },
        @{ Type = "Photon.Pun.PhotonNetwork"; Method = "get_PlayerList"; Parameters = @() },
        @{ Type = "Photon.Realtime.Player"; Method = "SetCustomProperties"; Parameters = @("ExitGames.Client.Photon.Hashtable", "ExitGames.Client.Photon.Hashtable", "Photon.Realtime.WebFlags") },
        @{ Type = "SpectateCamera"; Method = "CheckState"; Parameters = @("SpectateCamera/State") },
        @{ Type = "SpectateCamera"; Method = "StopSpectate"; Parameters = @() },
        @{ Type = "RunManager"; Method = "ChangeLevel"; Parameters = @("System.Boolean", "System.Boolean", "RunManager/ChangeLevelType") }
    )
    $requiredFields = @(
        @{ Type = "PlayerDeathHead"; Field = "triggered" },
        @{ Type = "PlayerDeathHead"; Field = "roomVolumeCheck" },
        @{ Type = "PlayerDeathHead"; Field = "inExtractionPoint" },
        @{ Type = "PlayerDeathHead"; Field = "physGrabObject" },
        @{ Type = "PlayerAvatar"; Field = "playerDeathHead" },
        @{ Type = "PlayerAvatar"; Field = "deadSet" },
        @{ Type = "PlayerAvatar"; Field = "isDisabled" },
        @{ Type = "PlayerAvatar"; Field = "playerTransform" },
        @{ Type = "PlayerAvatar"; Field = "playerAvatarVisuals" },
        @{ Type = "PlayerAvatar"; Field = "playerDeathEffects" },
        @{ Type = "PlayerAvatar"; Field = "playerReviveEffects" },
        @{ Type = "PlayerAvatar"; Field = "playerAvatarCollision" },
        @{ Type = "PlayerAvatar"; Field = "RoomVolumeCheck" },
        @{ Type = "PlayerHealth"; Field = "health" },
        @{ Type = "PlayerHealth"; Field = "maxHealth" },
        @{ Type = "PlayerHealth"; Field = "photonView" },
        @{ Type = "PhysGrabObject"; Field = "centerPoint" },
        @{ Type = "CameraAim"; Field = "Instance" },
        @{ Type = "CameraPosition"; Field = "instance" },
        @{ Type = "RoomVolumeCheck"; Field = "inExtractionPoint" },
        @{ Type = "RoomVolumeCheck"; Field = "Mask" },
        @{ Type = "RoomVolumeCheck"; Field = "CheckPosition" },
        @{ Type = "RoomVolumeCheck"; Field = "currentSize" },
        @{ Type = "RoomVolume"; Field = "Extraction" },
        @{ Type = "SpectateCamera"; Field = "MainCamera" },
        @{ Type = "SpectateCamera"; Field = "ParentObject" },
        @{ Type = "SpectateCamera"; Field = "PreviousParent" },
        @{ Type = "SpectateCamera"; Field = "normalTransformPivot" },
        @{ Type = "AudioManager"; Field = "AudioListener" }
    )
    $requiredProperties = @(
        @{ Type = "Photon.Realtime.Player"; Property = "CustomProperties" }
    )

    $errors = New-Object System.Collections.Generic.List[string]
    foreach ($target in $targets) {
        $type = Find-TypeDefinition -TypeName $target.Type
        if ($null -eq $type) {
            $errors.Add("Missing type: $($target.Type)")
            continue
        }

        $methods = @($type.Methods | Where-Object { $_.Name -eq $target.Method })
        if ($methods.Count -eq 0) {
            $errors.Add("Missing method: $($target.Type).$($target.Method)")
            continue
        }

        $expectedParameters = @($target.Parameters)
        $hasCompatibleSignature = $false
        foreach ($method in $methods) {
            if ($method.Parameters.Count -eq $expectedParameters.Count) {
                $parametersMatch = $true
                for ($i = 0; $i -lt $expectedParameters.Count; $i++) {
                    if ($method.Parameters[$i].ParameterType.FullName -ne $expectedParameters[$i]) {
                        $parametersMatch = $false
                        break
                    }
                }

                if ($parametersMatch) {
                    $hasCompatibleSignature = $true
                    break
                }
            }
        }

        if (!$hasCompatibleSignature) {
            $errors.Add("Incompatible signature: $($target.Type).$($target.Method)($($expectedParameters -join ', '))")
        }
    }

    foreach ($requiredField in $requiredFields) {
        $type = Find-TypeDefinition -TypeName $requiredField.Type
        if ($null -eq $type) {
            $errors.Add("Missing field owner type: $($requiredField.Type)")
            continue
        }

        $field = $type.Fields | Where-Object { $_.Name -eq $requiredField.Field } | Select-Object -First 1
        if ($null -eq $field) {
            $errors.Add("Missing field: $($requiredField.Type).$($requiredField.Field)")
        }
    }

    foreach ($requiredProperty in $requiredProperties) {
        $type = Find-TypeDefinition -TypeName $requiredProperty.Type
        if ($null -eq $type) {
            $errors.Add("Missing property owner type: $($requiredProperty.Type)")
            continue
        }

        $property = $type.Properties | Where-Object { $_.Name -eq $requiredProperty.Property } | Select-Object -First 1
        if ($null -eq $property) {
            $errors.Add("Missing property: $($requiredProperty.Type).$($requiredProperty.Property)")
        }
    }

    if ($errors.Count -gt 0) {
        throw "Harmony hook target validation failed:`n$($errors -join "`n")"
    }

    Write-Host "Validated HellRevival hook targets against $AssemblyPath"
}

Test-GameHookTargets -AssemblyPath (Join-Path $managed "Assembly-CSharp.dll") -CecilPath (Join-Path $bepCore "Mono.Cecil.dll")

& $csc /nologo /codepage:65001 /target:library /optimize+ /debug:full /nowarn:1701 /out:$pluginOut $refArgs $sources
if ($LASTEXITCODE -ne 0) {
    throw "csc.exe failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $packageSource "manifest.json") -Destination (Join-Path $distRoot "manifest.json") -Force
Copy-Item -LiteralPath (Join-Path $packageSource "README.md") -Destination (Join-Path $distRoot "README.md") -Force
Copy-Item -LiteralPath (Join-Path $packageSource "icon.png") -Destination (Join-Path $distRoot "icon.png") -Force

if ($InstallToProfile -and (Test-Path (Join-Path $R2Profile "BepInEx"))) {
    $profilePluginsRoot = Join-Path $R2Profile "BepInEx\plugins"
    $r2PackageRoot = Join-Path $profilePluginsRoot "$authorName-$modName"
    $r2PackagePluginDir = Join-Path $r2PackageRoot $modName
    $profilePluginDir = Join-Path $profilePluginsRoot $modName

    if (Test-Path $r2PackagePluginDir) {
        $profilePluginDir = $r2PackagePluginDir
        Copy-Item -LiteralPath (Join-Path $packageSource "manifest.json") -Destination (Join-Path $r2PackageRoot "manifest.json") -Force
        Copy-Item -LiteralPath (Join-Path $packageSource "README.md") -Destination (Join-Path $r2PackageRoot "README.md") -Force
        Copy-Item -LiteralPath (Join-Path $packageSource "icon.png") -Destination (Join-Path $r2PackageRoot "icon.png") -Force
    }

    New-Item -ItemType Directory -Force -Path $profilePluginDir | Out-Null
    Copy-Item -LiteralPath $pluginOut -Destination (Join-Path $profilePluginDir "$modName.dll") -Force
    Write-Host "Installed to $profilePluginDir"
}

if ($PackageToDesktop) {
    if (Test-Path $desktopZip) {
        Remove-Item -LiteralPath $desktopZip -Force
    }

    $packageStage = Join-Path $root "dist_package"
    if (Test-Path $packageStage) {
        $resolvedStage = (Resolve-Path -LiteralPath $packageStage).Path
        $resolvedRoot = (Resolve-Path -LiteralPath $root).Path
        if (!$resolvedStage.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean package stage outside workspace: $resolvedStage"
        }

        Remove-Item -LiteralPath $packageStage -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $packageStage "BepInEx\plugins\$modName") | Out-Null
    Copy-Item -LiteralPath (Join-Path $distRoot "manifest.json") -Destination (Join-Path $packageStage "manifest.json") -Force
    Copy-Item -LiteralPath (Join-Path $distRoot "README.md") -Destination (Join-Path $packageStage "README.md") -Force
    Copy-Item -LiteralPath (Join-Path $distRoot "icon.png") -Destination (Join-Path $packageStage "icon.png") -Force
    Copy-Item -LiteralPath $pluginOut -Destination (Join-Path $packageStage "BepInEx\plugins\$modName\$modName.dll") -Force

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open($desktopZip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $packageStage "manifest.json"), "manifest.json") | Out-Null
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $packageStage "README.md"), "README.md") | Out-Null
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $packageStage "icon.png"), "icon.png") | Out-Null
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $pluginOut, "BepInEx/plugins/$modName/$modName.dll") | Out-Null
    }
    finally {
        $zip.Dispose()
    }

    Write-Host "Packaged $desktopZip"
}

Write-Host "Built $pluginOut"
