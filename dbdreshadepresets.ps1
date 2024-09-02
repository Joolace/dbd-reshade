# Add necessary assemblies for the GUI
Add-Type -AssemblyName PresentationFramework, System.Windows.Forms, System.Drawing

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

# Create the main window
$form = New-Object System.Windows.Forms.Form
$form.Text = "DBD Reshade Preset Manager"
$form.Size = New-Object System.Drawing.Size(500, 500)  # Increased height to accommodate log box
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
$buttonSelectIni.Location = New-Object System.Drawing.Point(10, 200)  # Padding of 10 pixels from the left
$buttonSelectIni.Width = $form.ClientSize.Width - 20  # Full width minus padding
$form.Controls.Add($buttonSelectIni)

$openFileDialog = New-Object System.Windows.Forms.OpenFileDialog
$openFileDialog.Filter = "INI Files (*.ini)|*.ini"

$buttonSelectIni.Add_Click({
    if ($openFileDialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $script:filePath = $openFileDialog.FileName
        Add-LogEntry "Selected .ini file: $filePath"
    }
})

# Button to save the preset with proper padding and full width
$buttonSavePreset = New-Object System.Windows.Forms.Button
$buttonSavePreset.Text = "Save Preset"
$buttonSavePreset.BackColor = [System.Drawing.Color]::White
$buttonSavePreset.ForeColor = [System.Drawing.Color]::Black
$buttonSavePreset.Font = New-Object System.Drawing.Font("Montserrat", 10)
$buttonSavePreset.Location = New-Object System.Drawing.Point(10, 260)  # Padding of 10 pixels from the left
$buttonSavePreset.Width = $form.ClientSize.Width - 20  # Full width minus padding
$form.Controls.Add($buttonSavePreset)

$buttonSavePreset.Add_Click({
    $descriptionValid = $textBoxDesc.Text.Trim() -ne ""
    $videoLinkValid = $textBoxVideo.Text.Trim() -ne ""
    $fileSelected = $filePath -ne ""

    Add-LogEntry "Description Valid: $descriptionValid"
    Add-LogEntry "Video Link Valid: $videoLinkValid"
    Add-LogEntry "File Selected: $fileSelected"

    if ($fileSelected -and $descriptionValid -and $videoLinkValid) {
        $presetName = [System.IO.Path]::GetFileName($filePath)  # Include the extension in the preset name
        $destPath = Join-Path -Path (Join-Path $PSScriptRoot "Presets") -ChildPath "$presetName"
        Copy-Item -Path $filePath -Destination $destPath

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

        $newPreset = @{
            "description" = $textBoxDesc.Text
            "videoLink"   = $textBoxVideo.Text
        }

        # Add the new preset to the hashtable
        $jsonData[$presetName] = $newPreset

        # Save the updated JSON data back to the file
        $jsonData | ConvertTo-Json -Depth 4 | Set-Content -Path $jsonFilePath
        Show-MessageBox "Preset saved successfully!"
        Add-LogEntry "Preset $presetName saved successfully."
    } else {
        Show-MessageBox "Please fill out all fields."
        Add-LogEntry "Error: Missing fields."
    }
})

# Add the log box at the bottom
$logBox = New-Object System.Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ScrollBars = "Vertical"
$logBox.BackColor = [System.Drawing.Color]::Black
$logBox.ForeColor = [System.Drawing.Color]::White
$logBox.Font = New-Object System.Drawing.Font("Montserrat", 8)
$logBox.Location = New-Object System.Drawing.Point(10, 320)

# Correctly calculate the width for the log box with padding
$logBoxWidth = $form.ClientSize.Width - 20
$logBoxHeight = 150
$logBox.Size = New-Object System.Drawing.Size($logBoxWidth, $logBoxHeight)

$logBox.ReadOnly = $true
$form.Controls.Add($logBox)

# Start the form
$form.Add_Shown({$form.Activate()})
[void]$form.ShowDialog()