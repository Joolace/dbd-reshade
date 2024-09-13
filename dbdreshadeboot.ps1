# Import Windows Forms to create the form and components
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Create the main form with a black background and disable resizing
$form = New-Object System.Windows.Forms.Form
$form.Text = "ReShade Installer - Main Menu"
$form.Size = New-Object System.Drawing.Size(400, 450)  # Maintain the window size
$form.BackColor = [System.Drawing.Color]::Black
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.MinimizeBox = $false

# Load Montserrat Regular font
$fontFamily = New-Object System.Drawing.Text.PrivateFontCollection
$fontFamily.AddFontFile("$PSScriptRoot\\media\\Montserrat-Regular.ttf")  # Ensure the regular font file is in the 'media' folder
$montserratRegularFont = New-Object System.Drawing.Font($fontFamily.Families[0], 12)

# Add the dbdreshade logo at the top, maintaining the aspect ratio and increasing size
$logoPictureBox = New-Object System.Windows.Forms.PictureBox
$logoPictureBox.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom  # Use 'Zoom' to maintain the aspect ratio
$logoPictureBox.Size = New-Object System.Drawing.Size(350, 80)  # Increased size for the logo
$logoPictureBox.Location = New-Object System.Drawing.Point(20, 10)
$logoPictureBox.Image = [System.Drawing.Image]::FromFile("$PSScriptRoot\\dbdreshade_logo.png")  # Path to the logo image in the main directory
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
$button2.Location = New-Object System.Drawing.Point(50, 170)  # Center horizontally
$button2.Text = "Open Presets Manager"
$button2.Font = $montserratRegularFont
$button2.BackColor = [System.Drawing.Color]::White
$button2.ForeColor = [System.Drawing.Color]::Black
$form.Controls.Add($button2)

# Add version label at the bottom
$versionLabel = New-Object System.Windows.Forms.Label
$versionLabel.Size = New-Object System.Drawing.Size(400, 20)
$versionLabel.Location = New-Object System.Drawing.Point(0, 380)
$versionLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter  # Correct alignment
$versionLabel.Font = New-Object System.Drawing.Font($fontFamily.Families[0], 8)
$versionLabel.ForeColor = [System.Drawing.Color]::White
$versionLabel.Text = "v1.0.5 - Developed by Joolace"
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
$discordIcon.Image = [System.Drawing.Image]::FromFile("$PSScriptRoot\\media\\discord.png")  # Path to Discord icon in the media folder
$discordIcon.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
$discordIcon.Cursor = [System.Windows.Forms.Cursors]::Hand
$discordIcon.Add_Click({ Start-Process "https://discord.gg/mC7Eabu3QW" })
$form.Controls.Add($discordIcon)

# Add GitHub icon link
$githubIconXPosition = $startXPosition + $iconSpacing + 32
$githubIcon = New-Object System.Windows.Forms.PictureBox
$githubIcon.Size = New-Object System.Drawing.Size(32, 32)
$githubIcon.Location = New-Object System.Drawing.Point($githubIconXPosition, 320)  # Centered
$githubIcon.Image = [System.Drawing.Image]::FromFile("$PSScriptRoot\\media\\github.png")  # Path to GitHub icon in the media folder
$githubIcon.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
$githubIcon.Cursor = [System.Windows.Forms.Cursors]::Hand
$githubIcon.Add_Click({ Start-Process "https://github.com/Joolace/dbd-reshade" })
$form.Controls.Add($githubIcon)

# Add Instagram icon link
$instagramIconXPosition = $githubIconXPosition + $iconSpacing + 32
$instagramIcon = New-Object System.Windows.Forms.PictureBox
$instagramIcon.Size = New-Object System.Drawing.Size(32, 32)
$instagramIcon.Location = New-Object System.Drawing.Point($instagramIconXPosition, 320)  # Centered
$instagramIcon.Image = [System.Drawing.Image]::FromFile("$PSScriptRoot\\media\\instagram.png")  # Path to Instagram icon in the media folder
$instagramIcon.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
$instagramIcon.Cursor = [System.Windows.Forms.Cursors]::Hand
$instagramIcon.Add_Click({ Start-Process "https://www.instagram.com/joolace" })
$form.Controls.Add($instagramIcon)

# Add click event to button1 to run dbdreshade.ps1
$button1.Add_Click({
    & powershell.exe -File "$PSScriptRoot\\dbdreshade.ps1"  # Path to dbdreshade.ps1 in the main directory
})

# Add click event to button2 to run dbdreshadepresets.ps1
$button2.Add_Click({
    & powershell.exe -File "$PSScriptRoot\\dbdreshadepresets.ps1"  # Path to dbdreshadepresets.ps1 in the main directory
})

# Show the form
$form.ShowDialog()