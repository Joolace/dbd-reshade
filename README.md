# ReShade Installer for Dead by Daylight

![Project Logo](https://raw.githubusercontent.com/Joolace/dbd-reshade/main/dbdreshade_logo.png)

## Overview
[![DeepSource](https://app.deepsource.com/gh/Joolace/dbd-reshade.svg/?label=code+coverage&show_trend=true&token=35IVmOwzbF1HoSmqHcUTnKes)](https://app.deepsource.com/gh/Joolace/dbd-reshade/) [![DeepSource](https://app.deepsource.com/gh/Joolace/dbd-reshade.svg/?label=active+issues&show_trend=true&token=35IVmOwzbF1HoSmqHcUTnKes)](https://app.deepsource.com/gh/Joolace/dbd-reshade/) [![DeepSource](https://app.deepsource.com/gh/Joolace/dbd-reshade.svg/?label=resolved+issues&show_trend=true&token=35IVmOwzbF1HoSmqHcUTnKes)](https://app.deepsource.com/gh/Joolace/dbd-reshade/) [![Codacy Badge](https://app.codacy.com/project/badge/Grade/948e6cfe64064b90abc7ccca25817af3)](https://app.codacy.com/gh/Joolace/dbd-reshade/dashboard?utm_source=gh&utm_medium=referral&utm_content=&utm_campaign=Badge_grade) [![CodeFactor](https://www.codefactor.io/repository/github/joolace/dbd-reshade/badge)](https://www.codefactor.io/repository/github/joolace/dbd-reshade) [![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg?style=flat-square)](https://makeapullrequest.com) [![GitHub issues by-label bug](https://img.shields.io/github/issues/Joolace/dbd-reshade/bug?label=bugs)](https://github.com/Joolace/dbd-reshade/issues?q=is%3Aissue+is%3Aopen+label%3Abug)

The **ReShade Installer for Dead by Daylight** is a PowerShell script designed to simplify the installation and configuration of ReShade for the game *Dead by Daylight*. This tool automates the process of downloading and installing ReShade, setting up the preset files, and configuring the necessary settings.
3
## Screenshots

### 1. Boot Screen
![Boot Screen](./screenshots/bootscreen.png)

### 2. ReShade Boot
![ReShade Boot](./screenshots/dbdreshadeboot.png)

### 3. ReShade Preset Boot
![ReShade Preset Boot](./screenshots/dbdreshadepresetboot.png)

### 4. ReShade Preset Installed
![ReShade Preset Installed](./screenshots/dbdreshadepresetinstalled.png)

### 5. ReShade Preset Saved
![ReShade Preset Saved](./screenshots/dbdreshadepresetsaved.png)

### 6. ReShade Preset Save Success
![ReShade Preset Save Success](./screenshots/dbdreshadepresetsavedsuccess.png)

### 7. ReShade Preset Selection
![ReShade Preset Selection](./screenshots/dbdreshadeselectpreset.png)

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
    .\dbdreshade.ps1
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

## Disclaimer

- **Responsibility**: I do not assume any responsibility for bans or other consequences resulting from the use of this tool. Use it at your own risk.
- **False Positives**: The executable file may be flagged as a false positive by some antivirus programs. Ensure that you download the file from the official repository and use it with caution.

## Credits

A special thanks to the creators of the following presets for *Dead by Daylight*, which are included in this repository:

- **STX**: [steaxss](https://github.com/steaxss/STEAXS-FILTER-PACK)
- **April**: [april](https://www.youtube.com/watch?v=2_YQ_rWiKFE)
- **Aroz**: [Aroz](https://www.youtube.com/watch?v=4TArEDvT_ec&t=30s)
- **Azef**: [azef](https://www.youtube.com/watch?v=FUelIy0sGOk)
- **Faelayis**: [Faelayis](https://github.com/Faelayis/dbd-reshade)
- **Henz**: [Henz](https://discord.com/invite/HxjbEKuvZY)
- **KnightLight**: [KnightLight](https://www.twitch.tv/knightlight)
- **Koda**: [Koda](https://discord.com/invite/bNvWEde5Vr)
- **MomoSeventh**: [MomoSeventh](https://www.twitch.tv/momoseventh/)
- **Trolling Prophets**: [Trolling Prophets](https://discord.com/invite/bNvWEde5Vr)
- **NUGGETZ**: [NUGGETZ](https://www.youtube.com/watch?v=Qs28LJTro70)

Please refer to the respective preset files in the [Presets directory](https://github.com/Joolace/dbd-reshade/tree/main/Presets) for detailed credits and information.

## Special Thanks

- A huge thanks to [steve02081504](https://github.com/steve02081504/ps12exe) for providing the tool `ps12exe` which was used to create the executable file for this project.

## Important Notice About Antivirus Detection

Some antivirus programs may flag the executable created by this tool as a potential virus. This is primarily due to the fact that in the past, tools like `ps12exe` have been misused by others to create malware. Rest assured, this file is safe if downloaded from this official repository. Always use caution and make sure to download from trusted sources.

## Contributing

Contributions are welcome! If you have any bug reports, feature requests, or improvements, please open an issue or submit a pull request.

## License

This project is licensed under the GNU General Public License (GPL) - see the [LICENSE](LICENSE) file for details.

## Contact

For any questions or feedback, you can reach out to the developer:

- **Twitch**: [Joolace_](https://twitch.tv/joolace_)
- **GitHub**: [Joolace](https://github.com/Joolace)

## Join Us on Discord

Feel free to join our Discord community for support and discussions:

[![Join our Discord](https://img.shields.io/badge/Join_Discord-7289DA?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/RB85R838K9)
