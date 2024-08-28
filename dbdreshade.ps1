# Add necessary assembly for creating message boxes
Add-Type -AssemblyName PresentationFramework

# Function to display a message box with a given message
function Show-MessageBox($message) {
    [System.Windows.MessageBox]::Show($message)
}

# Function to append log entries to the log box
function Add-LogEntry($message) {
    $logBox.AppendText("$message`r`n")
}

# Function to download and update presets from GitHub repository
function Update-PresetsFromGitHub {
    $repoUrl = 'https://api.github.com/repos/Joolace/dbd-reshade/contents/Presets'
    $presetJsonUrl = 'https://raw.githubusercontent.com/Joolace/dbd-reshade/main/media/presets.json'
    $presetDir = Join-Path -Path $PSScriptRoot -ChildPath "Presets"
    $localJsonPath = Join-Path -Path $PSScriptRoot -ChildPath "media\presets.json"
    
    # Make sure the Presets directory exists
    if (-not (Test-Path -Path $presetDir)) {
        New-Item -Path $presetDir -ItemType Directory -Force
    }

    # Make sure the media directory exists
    $mediaDir = Split-Path -Path $localJsonPath -Parent
    if (-not (Test-Path -Path $mediaDir)) {
        New-Item -Path $mediaDir -ItemType Directory -Force
    }
    
    $headers = @{
        "User-Agent" = "PowerShell Script" # GitHub API requires a User-Agent header
    }

    # Fetch and parse the presets JSON file from GitHub
    $remotePresetJsonResponse = Invoke-WebRequest -Uri $presetJsonUrl -Headers $headers -UseBasicP
    $remotePresetJson = $remotePresetJsonResponse.Content | ConvertFrom-Json

    if (-not $remotePresetJson) {
        Show-MessageBox "Error retrieving presets JSON from GitHub."
        return $false
    }

    # Fetch the list of presets from GitHub repository
    $response = Invoke-WebRequest -Uri $repoUrl -Headers $headers -UseBasicP
    $responseJson = $response.Content | ConvertFrom-Json

    if (-not $responseJson) {
        Show-MessageBox "Error retrieving presets list from GitHub."
        return $false
    }

    $presetFiles = $responseJson | Where-Object { $_.name -match '\.ini$' }
    $existingPresets = Get-ChildItem -Path $presetDir -Filter "*.ini" | Select-Object -ExpandProperty Name

    $newPresets = $presetFiles | Where-Object { $_.name -notin $existingPresets }

    if ($newPresets) {
        Show-MessageBox "New presets are available for download!"
    }

    # Download new presets
    foreach ($file in $presetFiles) {
        $fileName = $file.name
        $fileUrl = $file.download_url
        $localPath = Join-Path -Path $presetDir -ChildPath $fileName

        if ($file.name -in $existingPresets) {
            Add-LogEntry "Preset $fileName already exists. Skipping download."
            continue
        }

        Add-LogEntry "Downloading $fileName from $fileUrl"
        try {
            Invoke-WebRequest -Uri $fileUrl -OutFile $localPath -ErrorAction Stop
            Add-LogEntry "$fileName downloaded successfully."
        } catch {
            $errorMessage = $_.Exception.Message
            Add-LogEntry ("Error downloading " + $fileName + ": " + $errorMessage)
        }
    }

    # Check if the local JSON file exists
    if (Test-Path -Path $localJsonPath) {
        $localPresetJson = Get-Content -Path $localJsonPath | ConvertFrom-Json
    } else {
        $localPresetJson = @()
    }

    # Compare the remote JSON with the local JSON
    $remotePresetJsonHash = ($remotePresetJson | ConvertTo-Json -Depth 10).GetHashCode()
    $localPresetJsonHash = ($localPresetJson | ConvertTo-Json -Depth 10).GetHashCode()

    if ($remotePresetJsonHash -ne $localPresetJsonHash) {
        # Update local JSON file with the remote one if different
        Add-LogEntry "Updating local presets.json as it differs from the remote version."
        $remotePresetJson | ConvertTo-Json -Depth 10 | Set-Content -Path $localJsonPath
    } else {
        Add-LogEntry "Local presets.json is already up-to-date."
    }

    Add-LogEntry "Preset update process completed."
    return $true
}

# Function to retrieve the game installation directory
function Get-GameDirectory {
    # Try to get the Steam installation path
    $steamGamePath = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 381210' -ErrorAction SilentlyContinue).InstallLocation
    
    # If Steam path is found, return it
    if ($steamGamePath) {
        return $steamGamePath
    }

    # Try to get the Epic Games installation path
    $epicGamesLauncherPath = "C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests"
    
    if (Test-Path $epicGamesLauncherPath) {
        # Loop through all the manifest files
        $manifests = Get-ChildItem -Path $epicGamesLauncherPath -Filter *.item -ErrorAction SilentlyContinue
        foreach ($manifest in $manifests) {
            try {
                $manifestContent = Get-Content -Path $manifest.FullName -Raw | ConvertFrom-Json
                if ($manifestContent.AppName -eq "DeadByDaylight") {
                    return $manifestContent.InstallLocation
                }
            } catch {
                Add-LogEntry "Error reading manifest file $($manifest.FullName): $_"
            }
        }
    }

    # If neither path is found, show a message and return $null
    Show-MessageBox "Unable to find the Dead by Daylight installation."
    return $null
}

# Function to check if ReShade is installed in the given game directory
function Is-ReShadeInstalled($gameDir) {
    $reshadeIniPathSteam = Join-Path -Path $gameDir -ChildPath "DeadByDaylight\Binaries\Win64\ReShade.ini"
    $reshadeIniPathEpic = Join-Path -Path $gameDir -ChildPath "DeadByDaylight\Binaries\EGS\ReShade.ini"

    Add-LogEntry "Checking ReShade installation:"
    Add-LogEntry "  ReShade.ini path (Steam): $reshadeIniPathSteam"
    Add-LogEntry "  ReShade.ini path (Epic): $reshadeIniPathEpic"

    # Check if ReShade.ini exists
    $reshadeInstalledSteam = Test-Path $reshadeIniPathSteam
    $reshadeInstalledEpic = Test-Path $reshadeIniPathEpic

    Add-LogEntry "  ReShade.ini found (Steam): $reshadeInstalledSteam"
    Add-LogEntry "  ReShade.ini found (Epic): $reshadeInstalledEpic"

    return ($reshadeInstalledSteam -or $reshadeInstalledEpic)
}

# Function to download and install ReShade
function Install-ReShade($gameDir) {
    $tempDir = "$env:TEMP\ReShadeInstaller"
    New-Item -Path $tempDir -ItemType Directory -Force | Out-Null

    Add-LogEntry "Fetching the latest version of ReShade..."

    # Get the latest ReShade setup executable URL
    try {
        $reshadeUrl = Invoke-WebRequest -Uri 'https://reshade.me/' -ErrorAction Stop | 
                      Select-Object -ExpandProperty Links | 
                      Where-Object { $_.href -match 'ReShade_Setup_.*\.exe' -and $_.href -notmatch 'Addon' } | 
                      Select-Object -ExpandProperty href | 
                      Sort-Object -Descending | 
                      Select-Object -First 1
    } catch {
        Show-MessageBox "Error retrieving ReShade URL: $_"
        Add-LogEntry "Error retrieving ReShade URL: $_"
        return $false
    }

    if (-not $reshadeUrl) {
        Show-MessageBox "Error retrieving ReShade URL."
        Add-LogEntry "Error retrieving ReShade URL."
        return $false
    }

    $reshadeInstaller = "$tempDir\ReShade_Setup.exe"
    $reshadeUrl = "https://reshade.me$reshadeUrl"

    Add-LogEntry "Downloading ReShade from URL: $reshadeUrl"
    try {
        Invoke-WebRequest -Uri $reshadeUrl -OutFile $reshadeInstaller -ErrorAction Stop
    } catch {
        Show-MessageBox "Error downloading ReShade installer: $_"
        Add-LogEntry "Error downloading ReShade installer: $_"
        return $false
    }

    Add-LogEntry "Installing ReShade..."
    try {
        Start-Process -FilePath $reshadeInstaller -ArgumentList "/install", "/game=$gameDir", "/path=$gameDir", "/preprocessor=0", "/silent" -NoNewWindow -Wait
    } catch {
        Add-LogEntry "Error running ReShade installer: $_"
        Show-MessageBox "Error running ReShade installer: $_"
        return $false
    }

    # Remove the temporary files after installation is complete
    try {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue -Confirm:$false
    } catch {
        Add-LogEntry "Error removing temporary files: $_"
        Show-MessageBox "Error removing temporary files: $_"
        return $false
    }

    Add-LogEntry "ReShade installation completed."
    return $true
}

# Function to set the preset path in ReShade.ini
function Set-PresetPathInReShadeIni($gameDir, $presetPath) {
    # Define possible paths for ReShade.ini for both Steam and Epic Games installations
    $reshadeIniPathSteam = Join-Path -Path $gameDir -ChildPath "DeadByDaylight\Binaries\Win64\ReShade.ini"
    $reshadeIniPathEpic = Join-Path -Path $gameDir -ChildPath "DeadByDaylight\Binaries\EGS\ReShade.ini"
    
    # Check which ReShade.ini file exists
    if (Test-Path $reshadeIniPathSteam) {
        $reshadeIniPath = $reshadeIniPathSteam
    } elseif (Test-Path $reshadeIniPathEpic) {
        $reshadeIniPath = $reshadeIniPathEpic
    } else {
        Show-MessageBox "ReShade.ini not found in the game directory."
        return $false
    }

    # Read the contents of ReShade.ini
    try {
        $iniContent = Get-Content -Path $reshadeIniPath -Raw
        $iniLines = $iniContent -split "`r`n"
    } catch {
        Add-LogEntry "Error reading ReShade.ini file: $_"
        Show-MessageBox "Error reading ReShade.ini file. Please check the file permissions."
        return $false
    }

    Add-LogEntry "Updating PresetPath in ReShade.ini"

    # Find the [GENERAL] section
    $generalSectionIndex = $iniLines.IndexOf('[GENERAL]')
    if ($generalSectionIndex -eq -1) {
        Add-LogEntry "[GENERAL] section not found in ReShade.ini"
        Show-MessageBox "[GENERAL] section not found in ReShade.ini"
        return $false
    }

    # Initialize a flag to check if PresetPath exists
    $presetPathFound = $false

    # Loop through lines starting from the [GENERAL] section to find or set PresetPath
    for ($i = $generalSectionIndex + 1; $i -lt $iniLines.Length; $i++) {
        if ($iniLines[$i] -match '^\[.+\]') {
            # Break if a new section starts
            break
        }
        if ($iniLines[$i] -match '^PresetPath=') {
            # Update the existing PresetPath line
            $iniLines[$i] = "PresetPath=$presetPath"
            $presetPathFound = $true
            break
        }
    }

    # If PresetPath was not found, add it just after the [GENERAL] section
    if (-not $presetPathFound) {
        $iniLines = $iniLines[0..$generalSectionIndex] + "PresetPath=$presetPath" + $iniLines[($generalSectionIndex+1)..($iniLines.Length-1)]
    }

    # Write the updated content back to ReShade.ini
    try {
        Set-Content -Path $reshadeIniPath -Value ($iniLines -join "`r`n") -Force
    } catch {
        Add-LogEntry "Error writing to ReShade.ini file: $_"
        Show-MessageBox "Error writing to ReShade.ini file. Please check file permissions."
        return $false
    }

    Add-LogEntry "Preset path set in ReShade.ini to $presetPath"
    return $true
}

# Function to start capturing log output
function Start-LogCapture {
    $global:logFile = "$env:TEMP\ReShadeInstallerLog.txt"
    Start-Transcript -Path $global:logFile -Append
}

# Function to stop capturing log output and display it
function Stop-LogCapture {
    Stop-Transcript
    if (Test-Path $global:logFile) {
        $logContent = Get-Content -Path $global:logFile -Raw
        Add-LogEntry $logContent
        Remove-Item -Path $global:logFile -Force
    }
}

# Function to load preset descriptions from a JSON file
function Load-PresetDescriptions {
    # Path to the JSON file containing preset descriptions
    $jsonPath = Join-Path -Path $PSScriptRoot -ChildPath "media\presets.json"
    
    if (Test-Path -Path $jsonPath) {
        try {
            # Read the JSON file as raw content and convert it to a PowerShell object
            $jsonContent = Get-Content -Path $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
            
            # Return the JSON content as a PowerShell object
            return $jsonContent
        } catch {
            # Display an error message if loading or parsing the JSON fails
            Show-MessageBox "Error loading JSON file: $_"
            return @{}
        }
    } else {
        # Display an error message if the JSON file is not found
        Show-MessageBox "JSON file not found at path: $jsonPath"
        return @{}
    }
}


# Function to update preset description in the GUI
function Update-PresetDescription($presetName) {
    $descriptions = Load-PresetDescriptions
    if ($descriptions -and $descriptions.PSObject.Properties.Match($presetName)) {
        $preset = $descriptions.$presetName
        if ($preset) {
            $description = $preset.description
            $videoLink = $preset.videoLink
            if ($description) {
                $descriptionLabel.Text = $description
            } else {
                $descriptionLabel.Text = "No description available."
            }
            
            # Clear any previous links
            $descriptionLink.Links.Clear()
            
            if ($videoLink) {
                $descriptionLink.Text = "More Info"
                
                # Add the link
                $linkStart = $descriptionLink.Text.IndexOf("More Info")
                $linkLength = $descriptionLink.Text.Length
                $descriptionLink.Links.Add($linkStart, $linkLength - $linkStart, $videoLink)
                
                # Associate the URL with the LinkClicked event
                $descriptionLink.Tag = $videoLink
            } else {
                $descriptionLink.Text = ""
            }
        } else {
            $descriptionLabel.Text = "No description available for this preset."
            $descriptionLink.Text = ""
        }
    } else {
        $descriptionLabel.Text = "No description available for this preset."
        $descriptionLink.Text = ""
    }
}

# Function to reload preset list
function Reload-PresetList {
    # Clear the current items in the list box
    $listBox.Items.Clear()

    # Load preset files into the list box
    $presetFiles = Get-ChildItem -Path $presetDir -Filter "*.ini"
    foreach ($preset in $presetFiles) {
        $listBox.Items.Add($preset.Name)
    }
}

# Create the GUI interface
[void][System.Reflection.Assembly]::LoadWithPartialName("System.Drawing")
[void][System.Reflection.Assembly]::LoadWithPartialName("System.Windows.Forms")

$form = New-Object System.Windows.Forms.Form
$form.Text = "ReShade Installer for Dead by Daylight"
$form.Size = New-Object System.Drawing.Size(500, 750)
$form.StartPosition = "CenterScreen"
$form.BackColor = [System.Drawing.Color]::Black
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.MinimizeBox = $false

# Paths to media and presets directories
$mediaDir = Join-Path -Path $PSScriptRoot -ChildPath "media"
$presetDir = Join-Path -Path $PSScriptRoot -ChildPath "Presets"

# Check if presets directory exists
if (-Not (Test-Path -Path $presetDir)) {
    Show-MessageBox "The presets folder was not found: $presetDir"
    exit
}

# Check if media directory exists
if (-Not (Test-Path -Path $mediaDir)) {
    Show-MessageBox "The media folder was not found: $mediaDir"
    exit
}

# Set up custom font
$fontPath = Join-Path -Path $mediaDir -ChildPath "Montserrat-Regular.ttf"
if (Test-Path -Path $fontPath) {
    $fontCollection = New-Object System.Drawing.Text.PrivateFontCollection
    $fontCollection.AddFontFile($fontPath)
    $font = New-Object System.Drawing.Font($fontCollection.Families[0], 10, [System.Drawing.FontStyle]::Regular)
} else {
    Show-MessageBox "Montserrat font not found in the media folder: $fontPath"
    exit
}

# Add a logo image to the form
$logoPath = Join-Path -Path $mediaDir -ChildPath "dbdreshade.png"
if (Test-Path -Path $logoPath) {
    $logo = New-Object System.Windows.Forms.PictureBox
    $logo.Image = [System.Drawing.Image]::FromFile($logoPath)
    $logo.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $logo.Width = 400
    $logo.Height = 100
    $logo.BackColor = [System.Drawing.Color]::Transparent
    $logo.Location = New-Object System.Drawing.Point(50, 10)
    $form.Controls.Add($logo)
} else {
    Show-MessageBox "Logo image not found in the media folder: $logoPath"
}

# Add and configure the label for instructions
$label = New-Object System.Windows.Forms.Label
$label.Location = New-Object System.Drawing.Point(10,120)
$label.Size = New-Object System.Drawing.Size(460,30)
$label.Text = "Please select a preset and install it to the game."
$label.ForeColor = [System.Drawing.Color]::White
$label.Font = New-Object System.Drawing.Font($font.FontFamily, 10, [System.Drawing.FontStyle]::Bold)
$label.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$form.Controls.Add($label)

# Add and configure the list box for presets
$listBox = New-Object System.Windows.Forms.ListBox
$listBox.Location = New-Object System.Drawing.Point(10,150)
$listBox.Size = New-Object System.Drawing.Size(460,120)
$listBox.Font = $font
$form.Controls.Add($listBox)

# Add and configure the description label
$descriptionLabel = New-Object System.Windows.Forms.Label
$descriptionLabel.Location = New-Object System.Drawing.Point(10, 280)  # Updated location
$descriptionLabel.Size = New-Object System.Drawing.Size(460,60)  # Adjusted size
$descriptionLabel.ForeColor = [System.Drawing.Color]::White
$descriptionLabel.Font = New-Object System.Drawing.Font($font.FontFamily, 8, [System.Drawing.FontStyle]::Regular)
$descriptionLabel.TextAlign = [System.Drawing.ContentAlignment]::TopLeft
$descriptionLabel.AutoSize = $false
$form.Controls.Add($descriptionLabel)

# Add and configure the description link
$descriptionLink = New-Object System.Windows.Forms.LinkLabel
$descriptionLink.Location = New-Object System.Drawing.Point(10, 340)  # Updated location
$descriptionLink.Size = New-Object System.Drawing.Size(460,20)  # Adjusted size
$descriptionLink.LinkColor = [System.Drawing.Color]::LightBlue
$descriptionLink.VisitedLinkColor = [System.Drawing.Color]::LightPink
$descriptionLink.Font = New-Object System.Drawing.Font($font.FontFamily, 8, [System.Drawing.FontStyle]::Regular)
$descriptionLink.Add_LinkClicked({
    $url = $descriptionLink.Tag
    if ($url) {
        Start-Process $url
    }
})
$form.Controls.Add($descriptionLink)

# Add and configure the "Select Folder" button
$buttonSelectFolder = New-Object System.Windows.Forms.Button
$buttonSelectFolder.Location = New-Object System.Drawing.Point(10, 370)  # Adjusted location
$buttonSelectFolder.Size = New-Object System.Drawing.Size(460,30)
$buttonSelectFolder.Text = "Select Folder"
$buttonSelectFolder.BackColor = [System.Drawing.Color]::White
$buttonSelectFolder.ForeColor = [System.Drawing.Color]::Black
$buttonSelectFolder.Font = New-Object System.Drawing.Font($font.FontFamily, 10, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($buttonSelectFolder)

# Add and configure the "Install or Change Preset" button
$buttonInstall = New-Object System.Windows.Forms.Button
$buttonInstall.Location = New-Object System.Drawing.Point(10, 410)  # Adjusted location
$buttonInstall.Size = New-Object System.Drawing.Size(460,30)
$buttonInstall.Text = "Install or Change Preset"
$buttonInstall.BackColor = [System.Drawing.Color]::White
$buttonInstall.ForeColor = [System.Drawing.Color]::Black
$buttonInstall.Font = New-Object System.Drawing.Font($font.FontFamily, 10, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($buttonInstall)

# Add and configure the log box for displaying logs
$logBox = New-Object System.Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
$logBox.Location = New-Object System.Drawing.Point(10, 450)  # Updated location
$logBox.Size = New-Object System.Drawing.Size(460, 200)
$logBox.Font = $font
$logBox.BackColor = [System.Drawing.Color]::Black
$logBox.ForeColor = [System.Drawing.Color]::White
$form.Controls.Add($logBox)

# Load preset files into the list box
$presetFiles = Get-ChildItem -Path $presetDir -Filter "*.ini"
foreach ($preset in $presetFiles) {
    $listBox.Items.Add($preset.Name)
}

# Global variable to store selected folder path
$global:selectedFolder = ""

# Function to open a folder browser dialog and select a folder
function Browse-Folder {
    $folderBrowser = New-Object System.Windows.Forms.FolderBrowserDialog
    $folderBrowser.Description = "Select the folder where you want to install the preset."
    if ($folderBrowser.ShowDialog() -eq 'OK') {
        $global:selectedFolder = $folderBrowser.SelectedPath
        Add-LogEntry "Selected folder: $global:selectedFolder"
        Show-MessageBox "Selected folder: $global:selectedFolder"
    }
}

# Add click event handler for the "Select Folder" button
$buttonSelectFolder.Add_Click({
    Browse-Folder
})

# Add selection changed event handler for the list box
$listBox.Add_SelectedIndexChanged({
    $selectedPreset = $listBox.SelectedItem
    if ($selectedPreset) {
        Update-PresetDescription -presetName $selectedPreset
    }
})

# Add and configure the progress bar
$progressBar = New-Object System.Windows.Forms.ProgressBar
$progressBar.Location = New-Object System.Drawing.Point(10, 660)  # Adjust the location as needed
$progressBar.Size = New-Object System.Drawing.Size(460, 20)
$progressBar.Minimum = 0
$progressBar.Maximum = 100
$progressBar.Value = 0
$form.Controls.Add($progressBar)

# Add click event handler for the "Install or Change Preset" button
$buttonInstall.Add_Click({
    Start-LogCapture

    $selectedPreset = $listBox.SelectedItem
    if (-not $selectedPreset) {
        Show-MessageBox "Please select a preset from the list."
        Stop-LogCapture
        return
    }

    if (-not $global:selectedFolder) {
        Show-MessageBox "Please select a folder to install the preset."
        Stop-LogCapture
        return
    }

    Add-LogEntry "Selected preset: $selectedPreset"
    Add-LogEntry "Installation folder: $global:selectedFolder"

    $gameDir = Get-GameDirectory
    if (-not $gameDir) {
        Stop-LogCapture
        return
    }

    # Update presets from GitHub before proceeding
    $updatePresets = Update-PresetsFromGitHub
    if (-not $updatePresets) {
        Show-MessageBox "Failed to update presets from GitHub. Exiting."
        Stop-LogCapture
        return
    }

    if (-not (Is-ReShadeInstalled $gameDir)) {
        Add-LogEntry "ReShade is not installed. Proceeding with installation."
        
        $progressBar.Value = 10
        $form.Refresh()

        $reshadeInstalled = Install-ReShade $gameDir

        $progressBar.Value = 50
        $form.Refresh()

        if (-not $reshadeInstalled) {
            Show-MessageBox "ReShade installation failed. Exiting."
            Stop-LogCapture
            return
        }
    } else {
        Add-LogEntry "ReShade is already installed. Proceeding with preset update only."
    }

    $progressBar.Value = 70  # Update progress to 70% after installation logic
    $form.Refresh()

    # Copy preset files
    $progressBar.Value = 85
    $form.Refresh()

    Copy-Item -Path "$presetDir\$selectedPreset" -Destination $global:selectedFolder -Force

    $progressBar.Value = 95
    $form.Refresh()

    $presetSet = Set-PresetPathInReShadeIni -gameDir $gameDir -presetPath (Join-Path -Path $global:selectedFolder -ChildPath $selectedPreset)

    if ($presetSet) {
        Add-LogEntry "Preset installed successfully!"
        Show-MessageBox "Preset installed successfully!"
        
        $progressBar.Value = 100
        $form.Refresh()

        # Reload the preset list
        Reload-PresetList
    } else {
        Add-LogEntry "Failed to set the preset path in ReShade.ini."
        Show-MessageBox "Failed to set the preset path in ReShade.ini."
        
        $progressBar.Value = 0
        $form.Refresh()
    }

    # Reset the progress bar after tasks are done
    $progressBar.Value = 0
    $form.Refresh()
    Stop-LogCapture
})

# Add version and developer info label
$infoLabel = New-Object System.Windows.Forms.Label
$infoLabel.Location = New-Object System.Drawing.Point(10, 687)
$infoLabel.Size = New-Object System.Drawing.Size(460, 20)
$infoLabel.Text = "v1.0.3b - Developed by Joolace"
$infoLabel.ForeColor = [System.Drawing.Color]::White
$infoLabel.Font = New-Object System.Drawing.Font($font.FontFamily, 8, [System.Drawing.FontStyle]::Regular)
$infoLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$infoLabel.Cursor = [System.Windows.Forms.Cursors]::Hand
$infoLabel.Add_Click({
    Start-Process "https://github.com/Joolace/dbd-reshade"
})
$form.Controls.Add($infoLabel)

$form.Add_Shown({$form.Activate()})
[void]$form.ShowDialog()