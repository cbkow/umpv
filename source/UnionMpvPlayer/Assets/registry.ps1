param(
    [string]$action,
    [string]$appPath,
    [string]$iconPath
)

$fileTypes = @(".mp4", ".mov", ".mxf", ".gif", ".mkv", ".avi")
$progId = "UnionMpvPlayer.FileHandler"
$baseClassesPath = "HKCU:\Software\Classes"

function Ensure-KeyExists {
    param([string]$keyPath)
    if (!(Test-Path $keyPath)) {
        New-Item -Path $keyPath -Force | Out-Null
    }
}

function Ensure-PropertyExists {
    param([string]$keyPath, [string]$propertyName, [string]$value)
    if (Test-Path $keyPath) {
        if (Get-ItemProperty -Path $keyPath -Name $propertyName -ErrorAction SilentlyContinue) {
            Remove-ItemProperty -Path $keyPath -Name $propertyName -ErrorAction SilentlyContinue
        }
    }
    New-ItemProperty -Path $keyPath -Name $propertyName -Value $value -PropertyType String | Out-Null
}

function Install-RegistryKeys {
    foreach ($extension in $fileTypes) {
        try {
            Write-Host "Processing file type: $extension"

            # File extension key
            $extensionKey = "$baseClassesPath\$extension"
            Ensure-KeyExists -keyPath $extensionKey
            Set-ItemProperty -Path $extensionKey -Name "(Default)" -Value $progId

            # OpenWithProgids key
            $openWithProgidsKey = "$extensionKey\OpenWithProgids"
            Ensure-KeyExists -keyPath $openWithProgidsKey
            Ensure-PropertyExists -keyPath $openWithProgidsKey -propertyName $progId -value ""

            # ProgID key
            $progIdKey = "$baseClassesPath\$progId"
            Ensure-KeyExists -keyPath $progIdKey
            Set-ItemProperty -Path $progIdKey -Name "(Default)" -Value "Union MPV Player File"
            Set-ItemProperty -Path $progIdKey -Name "FriendlyTypeName" -Value "Union MPV Player"

            # DefaultIcon key
            $defaultIconKey = "$progIdKey\DefaultIcon"
            Ensure-KeyExists -keyPath $defaultIconKey
            Set-ItemProperty -Path $defaultIconKey -Name "(Default)" -Value "`"$iconPath`""

            # Shell Command
            $commandKey = "$progIdKey\shell\open\command"
            Ensure-KeyExists -keyPath $commandKey
            Set-ItemProperty -Path $commandKey -Name "(Default)" -Value "`"$appPath`" `"%1`""

            Write-Host "Successfully installed registry keys for $extension"
        } catch {
            Write-Host "Error occurred while processing $extension"
        }
    }
}

function Uninstall-RegistryKeys {
    foreach ($extension in $fileTypes) {
        try {
            Write-Host "Processing file type: $extension"

            # File extension key
            $extensionKey = "$baseClassesPath\$extension"
            if (Test-Path $extensionKey) {
                Remove-Item -Path $extensionKey -Recurse -Force
                Write-Host "Removed key for $extension"
            }

            # ProgID key
            $progIdKey = "$baseClassesPath\$progId"
            if (Test-Path $progIdKey) {
                Remove-Item -Path $progIdKey -Recurse -Force
                Write-Host "Removed ProgID key for $progId"
            }
        } catch {
            Write-Host "Error occurred while processing $extension"
        }
    }
}

if ($action -eq "install") {
    Install-RegistryKeys
} elseif ($action -eq "uninstall") {
    Uninstall-RegistryKeys
} else {
    Write-Host "Unknown action: $action"
}
