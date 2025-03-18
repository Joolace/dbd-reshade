
# Import Windows Forms to create the form and components
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# URL of the repository to check releases
$repoUrl = "https://api.github.com/repos/Joolace/dbd-reshade/releases/latest"

# Make a request to GitHub API to get the latest release
$response = Invoke-RestMethod -Uri $repoUrl -Headers @{"User-Agent"="Powershell Script"}

# Get the version number of the latest release
$latestVersion = $response.tag_name
$releaseUrl = $response.html_url

# Current installed version (replace this with the actual current version)
$currentVersion = "1.3.0"

# Compare the current version with the latest version
if ($currentVersion -ne $latestVersion) {
    # Create a form window
    $form = New-Object Windows.Forms.Form
    $form.Text = "Update Available"
    $form.Size = New-Object Drawing.Size(400,200)
    $form.StartPosition = "CenterScreen"
    $form.TopMost = $true
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false

    # Create a label to show update message
    $label = New-Object Windows.Forms.Label
    $label.Text = "A new version $latestVersion is available. Would you like to update?"
    $label.AutoSize = $true
    $label.Location = New-Object Drawing.Point(10,20)
    $label.BringToFront()
    $form.Controls.Add($label)

    # Create another label to show the current version
    $infoLabel = New-Object Windows.Forms.Label
    $infoLabel.Text = "$currentVersion - Developed by Joolace"
    $infoLabel.AutoSize = $true
    $infoLabel.Location = New-Object Drawing.Point(10,50)
    $form.Controls.Add($infoLabel)

    # Create a button to open the browser
    $button = New-Object Windows.Forms.Button
    $button.Text = "Go to Download"
    $button.Size = New-Object Drawing.Size(100,40)
    $button.Location = New-Object Drawing.Point(150,100)
    $button.Add_Click({
        # Open the release URL in the default browser
        Start-Process $releaseUrl
        $form.Close()
    })
    $form.Controls.Add($button)

    # Show the form
    $form.ShowDialog()
} else {
    [System.Windows.Forms.MessageBox]::Show("You already have the latest version: $currentVersion")
}

# Create the main form with a black background and disable resizing
$form = New-Object System.Windows.Forms.Form
$form.Text = "ReShade Installer - Main Menu"
$form.Size = New-Object System.Drawing.Size(400, 500)  # Adjust size to accommodate icons
$form.BackColor = [System.Drawing.Color]::Black
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.MinimizeBox = $false

# Import the AddFontResource function from the gdi32.dll library
Add-Type @"
using System;
using System.Runtime.InteropServices;

public class FontLoader {
    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    public static extern int AddFontResource(string lpFileName);
}
"@

# Define the font file path
$fontFilePath = "$PSScriptRoot\media\Montserrat-Regular.ttf"

# Check if the font file exists
if (Test-Path $fontFilePath) {
    # Load the font into the system
    [FontLoader]::AddFontResource($fontFilePath)

    # Now you can use the font for controls
    $montserratRegularFont = New-Object System.Drawing.Font("Montserrat", 12)
} else {
    Write-Output "Font file not found. Using Arial as fallback."
    # If the font is not found, use Arial as a fallback
    $montserratRegularFont = New-Object System.Drawing.Font("Arial", 12)
}

# Add the dbdreshade logo at the top, maintaining the aspect ratio and increasing size
$logoPictureBox = New-Object System.Windows.Forms.PictureBox
$logoPictureBox.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom  # Use 'Zoom' to maintain the aspect ratio
$logoPictureBox.Size = New-Object System.Drawing.Size(350, 80)  # Increased size for the logo
$logoPictureBox.Location = New-Object System.Drawing.Point(20, 10)
$logoPictureBox.Image = [System.Drawing.Image]::FromFile("$PSScriptRoot\dbdreshade_logo.png")
$form.Controls.Add($logoPictureBox)

# Create a button to open dbdreshade.ps1 with white background, black text, and regular font
$button1 = New-Object System.Windows.Forms.Button
$button1.Size = New-Object System.Drawing.Size(300, 40)
$button1.Location = New-Object System.Drawing.Point(50, 110)  # Center horizontally
$button1.Text = "Open ReShade Installer"
$button1.Font = $montserratRegularFont
$button1.BackColor = [System.Drawing.Color]::White
$button1.ForeColor = [System.Drawing.Color]::Black
$form.Controls.Add($button1)

# Create a button to open dbdreshadepresets.ps1 with white background, black text, and regular font
$button2 = New-Object System.Windows.Forms.Button
$button2.Size = New-Object System.Drawing.Size(300, 40)
$button2.Location = New-Object System.Drawing.Point(50, 160)  # Center horizontally
$button2.Text = "Open Presets Manager"
$button2.Font = $montserratRegularFont
$button2.BackColor = [System.Drawing.Color]::White
$button2.ForeColor = [System.Drawing.Color]::Black
$form.Controls.Add($button2)

# Create a button to open dbdreshadepresets.ps1 with white background, black text, and regular font
$button3 = New-Object System.Windows.Forms.Button
$button3.Size = New-Object System.Drawing.Size(300, 40)
$button3.Location = New-Object System.Drawing.Point(50, 210)  # Center horizontally
$button3.Text = "Copy All Presets"
$button3.Font = $montserratRegularFont
$button3.BackColor = [System.Drawing.Color]::White
$button3.ForeColor = [System.Drawing.Color]::Black
$form.Controls.Add($button3)

# Add a Click event to the button to open a new window for preset selection
# Declare a script-wide variable to store the destination folder
Set-Variable -Name destinationFolder -Value "" -Scope Script

# Add a Click event to the button to open a new window for preset selection
$button3.Add_Click({

    # Create a new form for selecting presets
    $presetForm = New-Object System.Windows.Forms.Form
    $presetForm.Text = "Select Presets to Copy"
    $presetForm.Size = New-Object System.Drawing.Size(400, 400)
    $presetForm.StartPosition = "CenterScreen"
    $presetForm.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
    $presetForm.MaximizeBox = $false
    $presetForm.MinimizeBox = $false

    # Create a CheckedListBox to display available presets
    $presetListBox = New-Object System.Windows.Forms.CheckedListBox
    $presetListBox.Size = New-Object System.Drawing.Size(350, 250)
    $presetListBox.Location = New-Object System.Drawing.Point(20, 20)

    # Get all preset (.ini) files from the "Presets" folder
    $presetFolder = "$PSScriptRoot\Presets"
    if (Test-Path $presetFolder) {
        $presetFiles = Get-ChildItem -Path $presetFolder -Filter "*.ini"
        foreach ($file in $presetFiles) {
            $presetListBox.Items.Add($file.Name)
        }
    } else {
        [System.Windows.Forms.MessageBox]::Show("Presets folder not found!", "Error", "OK", "Error")
        return
    }

    # Create a button to allow the user to select the destination folder
    $destinationButton = New-Object System.Windows.Forms.Button
    $destinationButton.Text = "Select Destination"
    $destinationButton.Size = New-Object System.Drawing.Size(150, 30)
    $destinationButton.Location = New-Object System.Drawing.Point(20, 280)

    # Create a button to copy selected presets to the chosen destination
    $copyButton = New-Object System.Windows.Forms.Button
    $copyButton.Text = "Copy Selected Presets"
    $copyButton.Size = New-Object System.Drawing.Size(350, 30)
    $copyButton.Location = New-Object System.Drawing.Point(20, 320)
    $copyButton.Enabled = $false  # Initially disabled until a destination folder is selected

    # Event handler for the "Select Destination" button
    $destinationButton.Add_Click({
        $folderDialog = New-Object System.Windows.Forms.FolderBrowserDialog
        if ($folderDialog.ShowDialog() -eq "OK") {
            Set-Variable -Name destinationFolder -Value $folderDialog.SelectedPath -Scope Script
            $copyButton.Enabled = $true  # Enable the "Copy" button after selecting a destination
        }
    })

    # Event handler for the "Copy Selected Presets" button
    $copyButton.Add_Click({
        if ($destinationFolder -eq "") {
            [System.Windows.Forms.MessageBox]::Show("Please select a destination folder.", "Warning", "OK", "Warning")
            return
        }

        # Get selected presets from the CheckedListBox
        $selectedPresets = $presetListBox.CheckedItems
        if ($selectedPresets.Count -eq 0) {
            [System.Windows.Forms.MessageBox]::Show("No presets selected!", "Warning", "OK", "Warning")
            return
        }

        # Copy each selected preset to the chosen destination folder
        foreach ($preset in $selectedPresets) {
            $sourceFile = "$presetFolder\$preset"
            $destinationFile = "$destinationFolder\$preset"

            Copy-Item -Path $sourceFile -Destination $destinationFile -Force
        }

        [System.Windows.Forms.MessageBox]::Show("Selected presets copied successfully!", "Success", "OK", "Information")
        $presetForm.Close()  # Close the preset selection form after copying
    })

    # Create a "Select All" button to check/uncheck all presets
    $selectAllButton = New-Object System.Windows.Forms.Button
    $selectAllButton.Text = "Select All"
    $selectAllButton.Size = New-Object System.Drawing.Size(150, 30)
    $selectAllButton.Location = New-Object System.Drawing.Point(220, 280)

    # Event handler for "Select All" button
    $selectAllButton.Add_Click({
        $allChecked = $true

        # Check if at least one item is unchecked
        for ($i = 0; $i -lt $presetListBox.Items.Count; $i++) {
            if (-not $presetListBox.GetItemChecked($i)) {
                $allChecked = $false
                break
            }
        }

        # Toggle between selecting all and deselecting all
        for ($i = 0; $i -lt $presetListBox.Items.Count; $i++) {
            $presetListBox.SetItemChecked($i, -not $allChecked)
        }

        # Update button text
        if ($allChecked) {
            $selectAllButton.Text = "Select All"
        } else {
            $selectAllButton.Text = "Deselect All"
        }
    })

    # Add the new "Select All" button to the form
    $presetForm.Controls.Add($selectAllButton)


    # Add controls to the form
    $presetForm.Controls.Add($presetListBox)
    $presetForm.Controls.Add($destinationButton)
    $presetForm.Controls.Add($copyButton)

    # Show the form as a modal dialog
    $presetForm.ShowDialog()
})

# Code from dbdreshade.ps1 for ReShade installer
function Invoke-ReShadeInstaller {
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

# Function to download and update presets and preview images from GitHub repository
function Update-PresetsAndImagesFromGitHub {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param ()

    process {
        if ($PSCmdlet.ShouldProcess("Update presets and images from GitHub")) {
            # URLs and paths
            $repoUrl = 'https://api.github.com/repos/Joolace/dbd-reshade/contents/Presets'
            $presetJsonUrl = 'https://raw.githubusercontent.com/Joolace/dbd-reshade/main/media/presets.json'
            $imageRepoUrl = 'https://api.github.com/repos/Joolace/dbd-reshade/contents/media/preview-preset'
            $presetDir = Join-Path -Path $PSScriptRoot -ChildPath "Presets"
            $imageDir = Join-Path -Path $PSScriptRoot -ChildPath "media\preview-preset"
            $localJsonPath = Join-Path -Path $PSScriptRoot -ChildPath "media\presets.json"

            # Ensure the Presets and Images directories exist
            if (-not (Test-Path -Path $presetDir)) {
                New-Item -Path $presetDir -ItemType Directory -Force
            }

            if (-not (Test-Path -Path $imageDir)) {
                New-Item -Path $imageDir -ItemType Directory -Force
            }

            # Headers for GitHub API
            $headers = @{
                "User-Agent" = "PowerShell Script" # GitHub API requires a User-Agent header
            }

            # Fetch and parse the presets JSON file from GitHub
            try {
                $remotePresetJsonResponse = Invoke-WebRequest -Uri $presetJsonUrl -Headers $headers -UseBasicP
                $remotePresetJson = $remotePresetJsonResponse.Content | ConvertFrom-Json
            } catch {
                Show-MessageBox "Error retrieving presets JSON from GitHub."
                return $false
            }

            if (-not $remotePresetJson) {
                Show-MessageBox "Error retrieving presets JSON from GitHub."
                return $false
            }

            # Fetch the list of preset files from GitHub repository
            try {
                $response = Invoke-WebRequest -Uri $repoUrl -Headers $headers -UseBasicP
                $responseJson = $response.Content | ConvertFrom-Json
            } catch {
                Show-MessageBox "Error retrieving presets list from GitHub."
                return $false
            }

            if (-not $responseJson) {
                Show-MessageBox "Error retrieving presets list from GitHub."
                return $false
            }

            # Fetch the list of image files from GitHub repository
            try {
                $imageResponse = Invoke-WebRequest -Uri $imageRepoUrl -Headers $headers -UseBasicP
                $imageResponseJson = $imageResponse.Content | ConvertFrom-Json
            } catch {
                Show-MessageBox "Error retrieving preview images from GitHub."
                return $false
            }

            if (-not $imageResponseJson) {
                Show-MessageBox "Error retrieving preview images list from GitHub."
                return $false
            }

            # Download new preset files
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

            # Download new preview images
            $imageFiles = $imageResponseJson | Where-Object { $_.name -match '\.(jpg|jpeg|png|gif)$' }
            $existingImages = Get-ChildItem -Path $imageDir | Select-Object -ExpandProperty Name
            $newImages = $imageFiles | Where-Object { $_.name -notin $existingImages }

            if ($newImages) {
                Show-MessageBox "New preview images are available for download!"
            }

            # Download new images
            foreach ($image in $imageFiles) {
                $imageName = $image.name
                $imageUrl = $image.download_url
                $localImagePath = Join-Path -Path $imageDir -ChildPath $imageName

                if ($image.name -in $existingImages) {
                    Add-LogEntry "Image $imageName already exists. Skipping download."
                    continue
                }

                Add-LogEntry "Downloading $imageName from $imageUrl"
                try {
                    Invoke-WebRequest -Uri $imageUrl -OutFile $localImagePath -ErrorAction Stop
                    Add-LogEntry "$imageName downloaded successfully."
                } catch {
                    $errorMessage = $_.Exception.Message
                    Add-LogEntry ("Error downloading " + $imageName + ": " + $errorMessage)
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

            Add-LogEntry "Presets and preview images update process completed."
            return $true
        }
    }
}


# Function to retrieve the game installation directory
function Get-GameDirectory {
    # Try to get the Steam installation path
    try {
        Add-LogEntry "Checking for Steam installation..."
        $steamGamePath = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 381210' -ErrorAction SilentlyContinue).InstallLocation
        if ($steamGamePath) {
            Add-LogEntry "Steam installation found: $steamGamePath"
            return $steamGamePath
        } else {
            Add-LogEntry "Steam installation not found or not registered."
        }
    } catch {
        Add-LogEntry "Error checking Steam installation: $_"
    }

    # Define the path to the Epic Games manifests directory
    $epicGamesManifestPath = "C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests"

    # Check if the manifest directory exists
    if (-Not (Test-Path -Path $epicGamesManifestPath)) {
        Add-LogEntry "Epic Games manifest directory not found."
        Show-MessageBox "Unable to find the Dead by Daylight installation."
        return $null
    }

    # Retrieve all `.item` files in the manifest directory
    $manifests = Get-ChildItem -Path $epicGamesManifestPath -Filter *.item -ErrorAction SilentlyContinue

    # Loop through each manifest file
    foreach ($manifest in $manifests) {
        try {
            # Read the content of the manifest file and convert it from JSON
            $manifestContent = Get-Content -Path $manifest.FullName -Raw | ConvertFrom-Json

            # Check if the InstallLocation contains "DeadByDaylight"
            if ($manifestContent.InstallLocation -like "*DeadByDaylight*") {
                Add-LogEntry "Epic Games installation found: $($manifestContent.InstallLocation)"
                return $manifestContent.InstallLocation
            }
        } catch {
            # Log any errors while processing the manifest file
            Add-LogEntry "Error processing manifest file: $($manifest.FullName) - $_"
        }
    }

    # If no valid installation path is found, display a message and return null
    Add-LogEntry "Unable to find the Dead by Daylight installation."
    Show-MessageBox "Unable to find the Dead by Daylight installation."
    return $null
}

# Function to check if ReShade is installed in the given game directory
function Test-ReShadeInstalled($gameDir) {
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
function Set-PresetPathInReShadeIni {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param (
        [Parameter(Mandatory=$true)]
        [string]$gameDir,

        [Parameter(Mandatory=$true)]
        [string]$presetPath
    )

    process {
        if ($PSCmdlet.ShouldProcess("Set PresetPath in ReShade.ini")) {
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
    }
}

# Function to start capturing log output
function Start-LogCapture {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param ()

    process {
        if ($PSCmdlet.ShouldProcess("Start capturing log")) {
            $script:logFile = "$env:TEMP\ReShadeInstallerLog.txt"
            Start-Transcript -Path $script:logFile -Append
        }
    }
}

# Function to stop capturing log output and display it
function Stop-LogCapture {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param ()

    process {
        if ($PSCmdlet.ShouldProcess("Stop Log Capture")) {
            Stop-Transcript
            if (Test-Path $script:logFile) {
                $logContent = Get-Content -Path $script:logFile -Raw
                Add-LogEntry $logContent
                Remove-Item -Path $script:logFile -Force
            }
        }
    }
}

# Function to load preset descriptions from a JSON file
function Get-PresetDescriptions {
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

# Base path for preview images relative to the script's location
$BaseImagePath = Join-Path -Path $PSScriptRoot -ChildPath "media\preview-preset"

# Function to update the preset description and preview image in the GUI
function Update-PresetDescription {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param (
        [Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
        [string]$PresetName
    )

    process {
        if ($PSCmdlet.ShouldProcess("Update description for preset: $PresetName")) {
            # Retrieve the preset descriptions from the JSON file
            $descriptions = Get-PresetDescriptions
            if ($descriptions -and $descriptions.PSObject.Properties.Match($PresetName)) {
                $preset = $descriptions.$PresetName
                if ($preset) {
                    # Update the description label
                    $description = $preset.description
                    $videoLink = $preset.videoLink
                    $imagePreview = $preset.'preset-preview'

                    if ($description) {
                        $descriptionLabel.Text = $description
                    } else {
                        $descriptionLabel.Text = "No description available."
                    }

                    # Validate and update the video link
                    try {
                        Invoke-WebRequest -Uri $videoLink -Method Head -ErrorAction Stop
                    } catch {
                        # If the video link is invalid, clear the link label
                        $descriptionLink.Text = ""
                        Add-LogEntry "Error: The video link is invalid. It will be hidden."
                        return
                    }

                    # Clear any existing links in the link label
                    $descriptionLink.Links.Clear()
                    if ($videoLink) {
                        $descriptionLink.Text = "More Info"
                        $linkStart = $descriptionLink.Text.IndexOf("More Info")
                        $linkLength = $descriptionLink.Text.Length
                        $descriptionLink.Links.Add($linkStart, $linkLength - $linkStart, $videoLink)
                        $descriptionLink.Tag = $videoLink
                    } else {
                        $descriptionLink.Text = ""
                    }

                    # Update the preview image in the PictureBox
                    if ($imagePreview) {
                        # Combine the base path with the preview image file name
                        $imagePath = Join-Path -Path $BaseImagePath -ChildPath $imagePreview
                        try {
                            $pictureBox.ImageLocation = $imagePath
                        } catch {
                            # If the image cannot be loaded, clear the PictureBox
                            $pictureBox.Image = $null
                            Add-LogEntry "Error: Could not load image preview for preset: $PresetName"
                        }
                    } else {
                        # If no image preview is available, clear the PictureBox
                        $pictureBox.Image = $null
                    }
                } else {
                    # If the preset does not exist, clear the labels and PictureBox
                    $descriptionLabel.Text = "No description available for this preset."
                    $descriptionLink.Text = ""
                    $pictureBox.Image = $null
                }
            } else {
                # If no matching preset is found, clear the labels and PictureBox
                $descriptionLabel.Text = "No description available for this preset."
                $descriptionLink.Text = ""
                $pictureBox.Image = $null
            }
        }
    }
}


# Function to reload preset list
function Update-PresetList {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param ()

    process {
        if ($PSCmdlet.ShouldProcess("Update Preset List")) {
            # Clear the current items in the list box
            $listBox.Items.Clear()

            # Load preset files into the list box
            $presetFiles = Get-ChildItem -Path $presetDir -Filter "*.ini"
            foreach ($preset in $presetFiles) {
                $listBox.Items.Add($preset.Name)
            }
        }
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
$form.KeyPreview = $true

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
    $font = New-Object System.Drawing.Font($montserratRegularFont.FontFamily, 10, [System.Drawing.FontStyle]::Regular)
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
$label.Font = New-Object System.Drawing.Font($montserratRegularFont.FontFamily, 10, [System.Drawing.FontStyle]::Bold)
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
$descriptionLabel.Font = New-Object System.Drawing.Font($montserratRegularFont.FontFamily, 8, [System.Drawing.FontStyle]::Regular)
$descriptionLabel.TextAlign = [System.Drawing.ContentAlignment]::TopLeft
$descriptionLabel.AutoSize = $false
$form.Controls.Add($descriptionLabel)

# Add and configure the description link
$descriptionLink = New-Object System.Windows.Forms.LinkLabel
$descriptionLink.Location = New-Object System.Drawing.Point(10, 340)  # Updated location
$descriptionLink.Size = New-Object System.Drawing.Size(460,20)  # Adjusted size
$descriptionLink.LinkColor = [System.Drawing.Color]::LightBlue
$descriptionLink.VisitedLinkColor = [System.Drawing.Color]::LightPink
$descriptionLink.Font = New-Object System.Drawing.Font($montserratRegularFont.FontFamily, 10, [System.Drawing.FontStyle]::Regular)
$descriptionLink.Add_LinkClicked({
    $url = $descriptionLink.Tag
    if ($url) {
        Start-Process $url
    }
})

$descriptionLink.Links.Clear()
$descriptionLink.Text = ""
$form.Controls.Add($descriptionLink)


# Create the PictureBox for displaying the image
$pictureBox = New-Object System.Windows.Forms.PictureBox
$pictureBox.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Normal
$form.Controls.Add($pictureBox)

# Create a Label for the text above the PictureBox
$imageLabel = New-Object System.Windows.Forms.Label
$imageLabel.Text = "Preset preview (click on the image or press space)" # Replace with your desired text
$imageLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$imageLabel.BackColor = [System.Drawing.Color]::Transparent # Optional: Transparent background
$imageLabel.ForeColor = [System.Drawing.Color]::White # Text color
$imageLabel.Font = New-Object System.Drawing.Font($montserratRegularFont.FontFamily, 8, [System.Drawing.FontStyle]::Bold)
$imageLabel.BringToFront()
$form.Controls.Add($imageLabel)

# Function to update PictureBox size with full width and position the label
function Update-PictureBoxSize {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param()

    if ($PSCmdlet.ShouldProcess("Update PictureBox Size")) {
        # Set PictureBox to full width
        $pictureBox.Width = $form.ClientSize.Width # Full width of the form
        $pictureBox.Height = 85 # Fixed height (adjust as needed)
        $pictureBox.Location = New-Object System.Drawing.Point(
            0, # Align to the left edge (start at X = 0)
            365 # Fixed vertical position
        )

        # Position the label above the PictureBox
        $imageLabel.Width = $form.ClientSize.Width
        $imageLabel.Height = 20
        $imageLabel.Location = New-Object System.Drawing.Point(0, 365)  # Updated location
        $imageLabel.BackColor = [System.Drawing.Color]::Transparent # Optional: Transparent background
        $imageLabel.ForeColor = [System.Drawing.Color]::White # Text color
        $imageLabel.BringToFront()
    }
}


# Function to display the image in fullscreen when clicked
function Show-FullscreenImage {
    # Create a new form for fullscreen
    $fullscreenForm = New-Object System.Windows.Forms.Form
    $fullscreenForm.WindowState = [System.Windows.Forms.FormWindowState]::Maximized
    $fullscreenForm.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
    $fullscreenForm.BackColor = [System.Drawing.Color]::Black
    $fullscreenForm.TopMost = $true

    # Create a PictureBox for the fullscreen image
    $fullscreenPictureBox = New-Object System.Windows.Forms.PictureBox
    $fullscreenPictureBox.Dock = [System.Windows.Forms.DockStyle]::Fill
    $fullscreenPictureBox.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::StretchImage
    $fullscreenPictureBox.Image = $pictureBox.Image

    # Add the PictureBox to the fullscreen form
    $fullscreenForm.Controls.Add($fullscreenPictureBox)

    # Create a ToolTip to show a message to the user
    $toolTip = New-Object System.Windows.Forms.ToolTip
    # Assign the tooltip to the PictureBox in the fullscreen form
    $toolTip.SetToolTip($fullscreenPictureBox, "Press the ESC key to exit")

    # Close the fullscreen form when the user presses the Escape key
    $fullscreenForm.Add_KeyDown({
        if ($_.KeyCode -eq [System.Windows.Forms.Keys]::Escape) {
            $fullscreenForm.Close()
        }
    })

    # Show the fullscreen form
    $fullscreenForm.ShowDialog()
}

# Attach the click event to the PictureBox
$pictureBox.Add_Click({
    Show-FullscreenImage
})

# Evento "KeyDown" a livello di form per aprire fullscreen premendo barra spaziatrice
$form.Add_KeyDown({
    if ($_.KeyCode -eq [System.Windows.Forms.Keys]::Space) {
        Show-FullscreenImage
    }
})

# Initial sizing of the PictureBox and label
Update-PictureBoxSize

# Adjust the PictureBox and label size dynamically when the form is resized
$form.Add_SizeChanged({
    Update-PictureBoxSize
})

# Add and configure the "Select Folder" button
$buttonSelectFolder = New-Object System.Windows.Forms.Button
$buttonSelectFolder.Location = New-Object System.Drawing.Point(10, 460)  # Adjusted location
$buttonSelectFolder.Size = New-Object System.Drawing.Size(460,30)
$buttonSelectFolder.Text = "Select Folder"
$buttonSelectFolder.BackColor = [System.Drawing.Color]::White
$buttonSelectFolder.ForeColor = [System.Drawing.Color]::Black
$buttonSelectFolder.Font = New-Object System.Drawing.Font($montserratRegularFont.FontFamily, 10, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($buttonSelectFolder)

# Add and configure the "Install or Change Preset" button
$buttonInstall = New-Object System.Windows.Forms.Button
$buttonInstall.Location = New-Object System.Drawing.Point(10, 500)  # Adjusted location
$buttonInstall.Size = New-Object System.Drawing.Size(460,30)
$buttonInstall.Text = "Install or Change Preset"
$buttonInstall.BackColor = [System.Drawing.Color]::White
$buttonInstall.ForeColor = [System.Drawing.Color]::Black
$buttonInstall.Font = New-Object System.Drawing.Font($montserratRegularFont.FontFamily, 10, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($buttonInstall)

# Add and configure the log box for displaying logs
$logBox = New-Object System.Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
$logBox.Location = New-Object System.Drawing.Point(10, 550)  # Updated location
$logBox.Size = New-Object System.Drawing.Size(460, 100)
$logBox.Font = $font
$logBox.BackColor = [System.Drawing.Color]::Black
$logBox.ForeColor = [System.Drawing.Color]::White
$form.Controls.Add($logBox)

# Load preset files into the list box
$presetFiles = Get-ChildItem -Path $presetDir -Filter "*.ini"
foreach ($preset in $presetFiles) {
    $listBox.Items.Add($preset.Name)
}

# Script to store selected folder path
$script:selectedFolder = ""

# Function to open a folder browser dialog and select a folder
function Select-Folder {
    $folderBrowser = New-Object System.Windows.Forms.FolderBrowserDialog
    $folderBrowser.Description = "Select the folder where you want to install the preset."
    if ($folderBrowser.ShowDialog() -eq 'OK') {
        $script:selectedFolder = $folderBrowser.SelectedPath
        Add-LogEntry "Selected folder: $script:selectedFolder"
        Show-MessageBox "Selected folder: $script:selectedFolder"
    }
}

# Add click event handler for the "Select Folder" button
$buttonSelectFolder.Add_Click({
    Select-Folder
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

    if (-not $script:selectedFolder) {
        Show-MessageBox "Please select a folder to install the preset."
        Stop-LogCapture
        return
    }

    Add-LogEntry "Selected preset: $selectedPreset"
    Add-LogEntry "Installation folder: $script:selectedFolder"

    $gameDir = Get-GameDirectory
    if (-not $gameDir) {
        Stop-LogCapture
        return
    }

    # Update presets from GitHub before proceeding
    $updatePresets = Update-PresetsAndImagesFromGitHub
    if (-not $updatePresets) {
        Show-MessageBox "Failed to update presets from GitHub. Exiting."
        Stop-LogCapture
        return
    }

    if (-not (Test-ReShadeInstalled $gameDir)) {
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

    Copy-Item -Path "$presetDir\$selectedPreset" -Destination $script:selectedFolder -Force

    $progressBar.Value = 95
    $form.Refresh()

    $presetSet = Set-PresetPathInReShadeIni -gameDir $gameDir -presetPath (Join-Path -Path $script:selectedFolder -ChildPath $selectedPreset)

    if ($presetSet) {
        Add-LogEntry "Preset installed successfully!"
        Show-MessageBox "Preset installed successfully!"
        
        $progressBar.Value = 100
        $form.Refresh()

        # Reload the preset list
        Update-PresetList
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
$infoLabel.Text = "v$currentVersion - Developed by Joolace"
$infoLabel.ForeColor = [System.Drawing.Color]::White
$infoLabel.Font = New-Object System.Drawing.Font($montserratRegularFont.FontFamily, 8, [System.Drawing.FontStyle]::Regular)
$infoLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$infoLabel.Cursor = [System.Windows.Forms.Cursors]::Hand
$infoLabel.Add_Click({
    Start-Process "https://github.com/Joolace/dbd-reshade"
})
$form.Controls.Add($infoLabel)

$form.Add_Shown({$form.Activate()})
[void]$form.ShowDialog()
}

# Code from dbdreshadepresets.ps1 for Presets Manager
function Invoke-PresetManager {
    
# Add necessary assemblies for the GUI
Add-Type -AssemblyName PresentationFramework, System.Windows.Forms, System.Drawing

# Presets Directory
$presetDirectory = "$PSScriptRoot\Presets"
$presets = Get-ChildItem -Path $presetDirectory -Filter *.ini

# Function to show a message box
function Show-MessageBox($message) {
    [System.Windows.MessageBox]::Show($message)
}

# Function to add a log entry
function Add-LogEntry($message) {
    $logBox.AppendText("$message`r`n")
}

# Function to convert a PSCustomObject to a Hashtable
function ConvertTo-Hashtable {
    param($object)
    $hashtable = @{}
    $object.PSObject.Properties | ForEach-Object {
        $hashtable[$_.Name] = $_.Value
    }
    return $hashtable
}

# Function to update the preset list with ShouldProcess support
function Update-PresetList {
    [CmdletBinding(SupportsShouldProcess=$true)]  # Add SupportsShouldProcess attribute
    param()

    # Check if the action should proceed
    if ($PSCmdlet.ShouldProcess("Updating preset list")) {
        # Get the preset directory path
        $presetDirectory = Join-Path $PSScriptRoot "Presets"

        # Clear the ListBox items to refresh the list
        $listBox.Items.Clear()

        # Fetch all preset files from the directory
        $presets = Get-ChildItem -Path $presetDirectory -Filter *.ini

        # Loop through each preset and add it to the ListBox
        foreach ($preset in $presets) {
            $listBox.Items.Add($preset.Name)
        }
    }
}

# Create the main window
$form = New-Object System.Windows.Forms.Form
$form.Text = "DBD Reshade Preset Manager"
$form.Size = New-Object System.Drawing.Size(500, 680)  # Increased height to accommodate log box
$form.StartPosition = "CenterScreen"
$form.BackColor = [System.Drawing.Color]::Black

# Prevent resizing and maximizing
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false

# Add the logo (smaller size and centered)
$logo = New-Object System.Windows.Forms.PictureBox
$logo.Image = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot "dbdreshadepresets_logo.png"))
$logo.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
$logo.Size = New-Object System.Drawing.Size(100, 100)  # Set the logo size
$form.Controls.Add($logo)

# Event handler to center the logo after form layout
$form.Add_Shown({
    $logo.Left = [math]::Round(($form.ClientSize.Width - $logo.Width) / 2)
})

# ListBox for presets (moved under the logo)
$listBox = New-Object System.Windows.Forms.ListBox
$listBox.Size = New-Object System.Drawing.Size(463, 100)
$listBox.Location = New-Object System.Drawing.Point(10, 200)
$listBox.Font = $montserratRegularFont
foreach ($preset in $presets) {
    $listBox.Items.Add($preset.Name)
}
$form.Controls.Add($listBox)

# Label and TextBox for the description
$labelDesc = New-Object System.Windows.Forms.Label
$labelDesc.Text = "Description:"
$labelDesc.ForeColor = [System.Drawing.Color]::White
$labelDesc.Font = New-Object System.Drawing.Font("Montserrat", 10)
$labelDesc.Location = New-Object System.Drawing.Point(10, 120)  # Adjusted position after reducing logo size
$form.Controls.Add($labelDesc)

$textBoxDesc = New-Object System.Windows.Forms.TextBox
$textBoxDesc.Location = New-Object System.Drawing.Point(150, 120)
$textBoxDesc.Size = New-Object System.Drawing.Size(300, 20)
$textBoxDesc.Font = New-Object System.Drawing.Font("Montserrat", 10)
$form.Controls.Add($textBoxDesc)

# Label and TextBox for the video link
$labelVideo = New-Object System.Windows.Forms.Label
$labelVideo.Text = "Video Link:"
$labelVideo.ForeColor = [System.Drawing.Color]::White
$labelVideo.Font = New-Object System.Drawing.Font("Montserrat", 10)
$labelVideo.Location = New-Object System.Drawing.Point(10, 160)
$form.Controls.Add($labelVideo)

$textBoxVideo = New-Object System.Windows.Forms.TextBox
$textBoxVideo.Location = New-Object System.Drawing.Point(150, 160)
$textBoxVideo.Size = New-Object System.Drawing.Size(300, 20)
$textBoxVideo.Font = New-Object System.Drawing.Font("Montserrat", 10)
$form.Controls.Add($textBoxVideo)

# Declare filePath as a global variable
$filePath = ""

# Button to select the .ini file with proper padding and full width
$buttonSelectIni = New-Object System.Windows.Forms.Button
$buttonSelectIni.Text = "Select .ini"
$buttonSelectIni.BackColor = [System.Drawing.Color]::White
$buttonSelectIni.ForeColor = [System.Drawing.Color]::Black
$buttonSelectIni.Font = New-Object System.Drawing.Font("Montserrat", 10)
$buttonSelectIni.Location = New-Object System.Drawing.Point(10, 520)  # Padding of 10 pixels from the left
$buttonSelectIni.Width = $form.ClientSize.Width - 20  # Full width minus padding
$form.Controls.Add($buttonSelectIni)

$openFileDialog = New-Object System.Windows.Forms.OpenFileDialog
$openFileDialog.Filter = "INI Files (*.ini)|*.ini"

# Initialize globally
$script:filePath = ""
$script:fileSelected = $false

$buttonSelectIni.Add_Click({
    if ($openFileDialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $script:filePath = $openFileDialog.FileName
        Add-LogEntry "Selected .ini file: $script:filePath"  # Log file path to verify

        # Ensure the file path is valid and exists
        if ($script:filePath -ne "" -and [System.IO.File]::Exists($script:filePath)) {
            Show-MessageBox "File selected: $script:filePath"
            $script:fileSelected = $true
        } else {
            Show-MessageBox "No valid file selected"
            $script:fileSelected = $false
        }

        Add-LogEntry "File Selected status: $script:fileSelected"  # Log file selection status
    }
})

# Button to select screenshot for preview
$buttonSelectScreenshot = New-Object System.Windows.Forms.Button
$buttonSelectScreenshot.Text = "Select Screenshot"
$buttonSelectScreenshot.BackColor = [System.Drawing.Color]::White
$buttonSelectScreenshot.ForeColor = [System.Drawing.Color]::Black
$buttonSelectScreenshot.Font = New-Object System.Drawing.Font("Montserrat", 10)
$buttonSelectScreenshot.Location = New-Object System.Drawing.Point(10, 490)  # Padding of 10 pixels from the left
$buttonSelectScreenshot.Width = $form.ClientSize.Width - 20  # Full width minus padding
$form.Controls.Add($buttonSelectScreenshot)

$openFileDialogScreenshot = New-Object System.Windows.Forms.OpenFileDialog
$openFileDialogScreenshot.Filter = "Image Files (*.jpg;*.jpeg;*.png;*.gif)|*.jpg;*.jpeg;*.png;*.gif"

$script:screenshotPath = ""

# Handle screenshot selection
$buttonSelectScreenshot.Add_Click({
    if ($openFileDialogScreenshot.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $script:screenshotPath = $openFileDialogScreenshot.FileName
        Add-LogEntry "Selected screenshot: $script:screenshotPath"  # Log file path to verify

        # Ensure the file path is valid and exists
        if ($script:screenshotPath -ne "" -and [System.IO.File]::Exists($script:screenshotPath)) {
            Show-MessageBox "Screenshot selected: $script:screenshotPath"
        } else {
            Show-MessageBox "No valid screenshot selected"
        }
    }
})

# Button to save the preset and update JSON
$buttonSavePreset = New-Object System.Windows.Forms.Button
$buttonSavePreset.Text = "Save Preset"
$buttonSavePreset.BackColor = [System.Drawing.Color]::White
$buttonSavePreset.ForeColor = [System.Drawing.Color]::Black
$buttonSavePreset.Font = New-Object System.Drawing.Font("Montserrat", 10)
$buttonSavePreset.Location = New-Object System.Drawing.Point(10, 550)  # Padding of 10 pixels from the left
$buttonSavePreset.Width = $form.ClientSize.Width - 20  # Full width minus padding
$form.Controls.Add($buttonSavePreset)

$buttonSavePreset.Add_Click({
    # Validate that the description, video link, and file path are correctly filled
    $descriptionValid = $textBoxDesc.Text.Trim() -ne ""
    $videoLinkValid = $textBoxVideo.Text.Trim() -ne ""
    $fileSelected = $script:filePath -ne ""  # Use the globally scoped file path
    $screenshotSelected = $script:screenshotPath -ne ""  # Ensure screenshot is selected

    Add-LogEntry "Description Valid: $descriptionValid"
    Add-LogEntry "Video Link Valid: $videoLinkValid"
    Add-LogEntry "File Selected: $fileSelected"
    Add-LogEntry "Screenshot Selected: $screenshotSelected"

    if ($fileSelected -and $descriptionValid -and $videoLinkValid -and $screenshotSelected) {
        # Prepare to save the preset
        $presetName = [System.IO.Path]::GetFileName($script:filePath)  # Get the preset name from the file
        $destPath = Join-Path -Path (Join-Path $PSScriptRoot "Presets") -ChildPath "$presetName"
        
        # Copy the .ini file to the Presets directory
        Copy-Item -Path $script:filePath -Destination $destPath

        # Path to the JSON file that stores the presets data
        $jsonFilePath = Join-Path $PSScriptRoot "media\presets.json"
        $jsonData = @{}

        try {
            # Attempt to read and parse existing JSON data
            $existingJsonData = Get-Content -Path $jsonFilePath -Raw | ConvertFrom-Json
            if ($existingJsonData -is [System.Collections.Hashtable]) {
                $jsonData = $existingJsonData
            } elseif ($existingJsonData -is [PSCustomObject]) {
                $jsonData = ConvertTo-Hashtable -object $existingJsonData
            }
        } catch {
            Add-LogEntry "Error reading existing JSON data: $_"
            Add-LogEntry "Initializing new JSON data."
        }

        # Create a new preset entry with description, video link, and preview image
        $newPreset = @{
            "description" = $textBoxDesc.Text
            "videoLink"   = $textBoxVideo.Text
            "preset-preview" = [System.IO.Path]::GetFileName($script:screenshotPath)  # Set the preview image path
        }

        # Assicurati che la cartella preview-preset esista
        $previewPresetDir = Join-Path -Path $PSScriptRoot "media\preview-preset"
        if (-not (Test-Path -Path $previewPresetDir)) {
        New-Item -Path $previewPresetDir -ItemType Directory -Force
        }

        # Ottieni il nome del file dell'immagine selezionata
        $imageFileName = [System.IO.Path]::GetFileName($script:screenshotPath)

        # Crea il percorso completo di destinazione nella cartella preview-preset
        $destScreenshotPath = Join-Path -Path $previewPresetDir -ChildPath $imageFileName

        # Copia l'immagine nella cartella preview-preset
        Copy-Item -Path $script:screenshotPath -Destination $destScreenshotPath

        # Add the new preset to the JSON data
        $jsonData[$presetName] = $newPreset

        # Save the updated JSON data back to the file
        $jsonData | ConvertTo-Json -Depth 4 | Set-Content -Path $jsonFilePath

        # Update the preset list after saving the preset (without clearing existing ones)
        Update-PresetList
        
        # Display success message and log it
        Show-MessageBox "Preset saved successfully!"
        Add-LogEntry "Preset $presetName saved successfully."
    } else {
        # If any field is invalid, show error
        Show-MessageBox "Please fill out all fields."
        Add-LogEntry "Error: Missing fields."
    }
})

# Button to delete the selected preset (last button)
$buttonDeletePreset = New-Object System.Windows.Forms.Button
$buttonDeletePreset.Text = "Delete Selected Preset"
$buttonDeletePreset.BackColor = [System.Drawing.Color]::Red
$buttonDeletePreset.ForeColor = [System.Drawing.Color]::White
$buttonDeletePreset.Font = New-Object System.Drawing.Font("Montserrat", 10)
$buttonDeletePreset.Location = New-Object System.Drawing.Point(10, 580)  # Below save preset button
$buttonDeletePreset.Width = $form.ClientSize.Width - 20  # Full width minus padding
$form.Controls.Add($buttonDeletePreset)

# Event handler for delete button (delete the selected preset)
$buttonDeletePreset.Add_Click({
    if ($listBox.SelectedItem) {
        $selectedPreset = $listBox.SelectedItem
        Remove-Item "$presetDirectory\$selectedPreset"
        $listBox.Items.Remove($selectedPreset)
        [System.Windows.Forms.MessageBox]::Show("Preset deleted.")
    } else {
        [System.Windows.Forms.MessageBox]::Show("Please select a preset to delete.")
    }
})

# Add the log box at the bottom
$logBox = New-Object System.Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ScrollBars = "Vertical"
$logBox.BackColor = [System.Drawing.Color]::Black
$logBox.ForeColor = [System.Drawing.Color]::White
$logBox.Font = New-Object System.Drawing.Font("Montserrat", 8)
$logBox.Location = New-Object System.Drawing.Point(10, 310)

# Correctly calculate the width for the log box with padding
$logBoxWidth = $form.ClientSize.Width - 20
$logBoxHeight = 160
$logBox.Size = New-Object System.Drawing.Size($logBoxWidth, $logBoxHeight)

$logBox.ReadOnly = $true
$form.Controls.Add($logBox)

# Add credits section at the bottom
$versionLabel = New-Object System.Windows.Forms.Label
$versionLabel.Size = New-Object System.Drawing.Size(500, 20)
$versionLabel.Location = New-Object System.Drawing.Point(-5, 610)
$versionLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$versionLabel.Font = New-Object System.Drawing.Font("Montserrat", 8)
$versionLabel.ForeColor = [System.Drawing.Color]::White
$versionLabel.Text = "v$currentVersion - Developed by Joolace"
$form.Controls.Add($versionLabel)

# Start the form
$form.Add_Shown({$form.Activate()})
[void]$form.ShowDialog()

}

# Add click event to button1 to run dbdreshade.ps1
$button1.Add_Click({
    Invoke-ReShadeInstaller
})

# Add click event to button2 to run dbdreshadepresets.ps1
$button2.Add_Click({
    Invoke-PresetManager
})

# Add version label at the bottom
$versionLabel = New-Object System.Windows.Forms.Label
$versionLabel.Size = New-Object System.Drawing.Size(400, 20)
$versionLabel.Location = New-Object System.Drawing.Point(0, 380)
$versionLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$versionLabel.Font = New-Object System.Drawing.Font($montserratRegularFont.FontFamily, 8)  
$versionLabel.ForeColor = [System.Drawing.Color]::White
$versionLabel.Text = "v$currentVersion - Developed by Joolace"
$form.Controls.Add($versionLabel)

# Centered positioning for the social icons
$formWidth = $form.ClientSize.Width  # Get the form width
$totalIconsWidth = 96  # Three icons, each 32px wide
$iconSpacing = 40  # Space between icons

# Calculate starting position to center icons
$startXPosition = ($formWidth - $totalIconsWidth - 2 * $iconSpacing) / 2

# Add Discord icon link
$discordIcon = New-Object System.Windows.Forms.PictureBox
$discordIcon.Size = New-Object System.Drawing.Size(32, 32)
$discordIcon.Location = New-Object System.Drawing.Point($startXPosition, 320)  # Centered
$discordIcon.Image = [System.Drawing.Image]::FromFile("$PSScriptRoot\media\discord.png")
$discordIcon.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
$discordIcon.Cursor = [System.Windows.Forms.Cursors]::Hand
$discordIcon.Add_Click({ Start-Process "https://discord.gg/mC7Eabu3QW" })
$form.Controls.Add($discordIcon)

# Add GitHub icon link
$githubIconXPosition = $startXPosition + $iconSpacing + 32
$githubIcon = New-Object System.Windows.Forms.PictureBox
$githubIcon.Size = New-Object System.Drawing.Size(32, 32)
$githubIcon.Location = New-Object System.Drawing.Point($githubIconXPosition, 320)  # Centered
$githubIcon.Image = [System.Drawing.Image]::FromFile("$PSScriptRoot\media\github.png")
$githubIcon.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
$githubIcon.Cursor = [System.Windows.Forms.Cursors]::Hand
$githubIcon.Add_Click({ Start-Process "https://github.com/Joolace/dbd-reshade" })
$form.Controls.Add($githubIcon)

# Add Instagram icon link
$instagramIconXPosition = $githubIconXPosition + $iconSpacing + 32
$instagramIcon = New-Object System.Windows.Forms.PictureBox
$instagramIcon.Size = New-Object System.Drawing.Size(32, 32)
$instagramIcon.Location = New-Object System.Drawing.Point($instagramIconXPosition, 320)  # Centered
$instagramIcon.Image = [System.Drawing.Image]::FromFile("$PSScriptRoot\media\instagram.png")
$instagramIcon.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
$instagramIcon.Cursor = [System.Windows.Forms.Cursors]::Hand
$instagramIcon.Add_Click({ Start-Process "https://www.instagram.com/joolace" })
$form.Controls.Add($instagramIcon)

# Show the form
$form.Add_Shown({$form.Activate()})
[void]$form.ShowDialog()