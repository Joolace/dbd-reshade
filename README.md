# ReShade Installer for Dead by Daylight

## Overview

The **ReShade Installer for Dead by Daylight** is a PowerShell script designed to simplify the installation and configuration of ReShade for the game *Dead by Daylight*. This tool automates the process of downloading and installing ReShade, setting up the preset files, and configuring the necessary settings.

## Features

- **Automatic Detection**: Finds the Dead by Daylight installation directory from both Steam and Epic Games.
- **ReShade Installation**: Downloads and installs ReShade if not already present.
- **Preset Management**: Allows users to select and apply ReShade presets.
- **GUI Interface**: Provides a user-friendly graphical interface for managing presets and installation.

## Requirements

- **PowerShell**: This script is designed to run on PowerShell (Windows only).
- **.NET Framework**: Required for Windows Forms.
- **Dead by Daylight**: The game must be installed either via Steam or Epic Games.

## Installation

1. **Clone the Repository**:
    ```bash
    git clone https://github.com/Joolace/dbd-reshade.git
    ```

2. **Navigate to the Script Directory**:
    ```bash
    cd dbd-reshade
    ```

3. **Run the Script**:
    Open PowerShell as Administrator and execute the script:
    ```powershell
    .\ReShadeInstaller.ps1
    ```

## Usage

1. **Launch the GUI**: When you run the script, a GUI window will appear.
   
2. **Select a Preset**: Choose a preset from the list in the GUI.

3. **Select Installation Folder**: Click on the "Select Folder" button to choose the destination folder for the preset.

4. **Install or Change Preset**: Click on the "Install or Change Preset" button to apply the selected preset.

5. **Log Output**: The GUI includes a log box to display real-time log information during installation.

## Troubleshooting

- **Error Retrieving ReShade URL**: Ensure you have an active internet connection and the URL to ReShade is accessible.
- **ReShade Installation Failure**: Make sure you have sufficient permissions and the game directory is correctly identified.
- **Missing Presets or Media**: Verify that the `presets` and `media` directories are present in the same location as the script.

## Contributing

Contributions are welcome! If you have any bug reports, feature requests, or improvements, please open an issue or submit a pull request.

## License

This project is licensed under the GNU General Public License (GPL) - see the [LICENSE](LICENSE) file for details.

## Contact

For any questions or feedback, you can reach out to the developer:

- **Twitch**: [Joolace_](https://twitch.tv/joolace_)
- **GitHub**: [Joolace](https://github.com/Joolace)
