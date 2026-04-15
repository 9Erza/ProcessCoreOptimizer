# Process Core Optimizer

<p align="center">
  <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/refs/heads/main/ProcessCoreOptimizer/Images/processcoreoptimizer.ico" alt="Process Core Optimizer Logo">
</p>

![License](https://img.shields.io/github/license/9Erza/ProcessCoreOptimizer?style=flat-square)
![Version](https://img.shields.io/github/v/release/9Erza/ProcessCoreOptimizer?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows-blue?style=flat-square)

**Process Core Optimizer** is a high-performance system utility I built using WPF (Windows Presentation Foundation). It is designed to give you total control over your CPU resources. You can optimize your gaming and professional applications by manually assigning CPU Affinity, managing priorities, and automating performance profiles.

---

## 🌟 Key Features

### 🛠️ Advanced Process Management
- **CPU Affinity Control:** Pin specific applications to high-performance cores and avoid efficiency cores (E-Cores) to eliminate stuttering in games like CS2 or Valorant.
- **Set Priority:** Directly change process priority levels from "Idle" to "Real-Time" to ensure your critical apps get maximum CPU attention.
- **Automated Profiles:** Save custom optimization profiles for your favorite games. The app automatically detects when a game starts and applies your saved Affinity and Priority settings in the background.

### 📊 Hardware Monitoring
Track your system's vital signs in real-time with a dedicated metrics tab:
- **CPU Monitoring:** - Usage %, Temperature, Average Clock Speed, and Power Draw (TDP).
  - *Note: Some advanced CPU statistics require the app to run in Administrator mode.*
- **GPU Monitoring:** - Usage %, Temperature, VRAM Usage, Core Clock, Memory Clock, Hot Spot Temperature, and Power Draw.
- **RAM Monitoring:** - Total usage percentage and a detailed breakdown of Used vs. Available memory (in GB).


### ⚙️ Usability & UI
- **Modern WPF Interface:** I've completely rebuilt the UI for a sleek, dark, and high-DPI aware experience.
- **Start as Administrator:** New option to automatically launch with elevated privileges, which is essential for managing system-protected processes.
- **Bilingual Support:** Full support for both **English** and **Polish**. Switch languages on the fly!
- **System Tray & Auto-Start:** Runs discretely in the background and can launch with Windows so you can "set and forget" your optimizations.

---

## ⚠️ Important: Profile Migration
Please note that due to the complete transition from WinForms to WPF, **previous process profiles may not be compatible**. I highly recommend re-creating your profiles within this new version to ensure everything works correctly.

---

## 📸 Screenshots

<details>
  <summary><b>📷 Click to expand / hide screenshots</b></summary>
  <br>
  <table align="center">
    <tr>
      <td align="center" width="50%">
        <b>🖥️ System Processes</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_system-processes.png" width="100%" alt="System Processes" />
      </td>
      <td align="center" width="50%">
        <b>💾 Saved Profiles</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_saved-profiles.png" width="100%" alt="Saved Profiles" />
      </td>
    </tr>
    <tr>
      <td align="center">
        <b>📊 Hardware Monitor 1</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_hardware-monitor-1.png" width="100%" alt="Hardware Monitor 1" />
      </td>
      <td align="center">
        <b>📊 Hardware Monitor 2</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_hardware-monitor-2.png" width="100%" alt="Hardware Monitor 2" />
      </td>
    </tr>
    <tr>
      <td align="center" colspan="2">
        <br>
        <b>⚙️ Application Settings</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_settings.png" width="49.5%" alt="Settings" />
      </td>
    </tr>
  </table>
</details>

---

## 📥 Download & Installation

The application is distributed as a standalone installer.

1. Go to the [**Releases**](https://github.com/9Erza/ProcessCoreOptimizer/releases) tab.
2. Download the latest installer: `ProcessCoreOptimizer_Setup.exe`.
3. **Recommendation:** I highly suggest manually uninstalling any previous version before installing this one to ensure a clean setup.
4. Run the installer and follow the instructions.
5. **Note:** Enable the **"Run as Administrator"** option in the settings tab to allow the software to modify priorities and affinities for all processes.

---

## 🛠️ Tech Stack
- **Framework:** .NET 10 (C#)
- **UI Engine:** WPF (Windows Presentation Foundation)

---

## 👤 Author
Developed by **[Eryk / 9Erza](https://github.com/9Erza)**.

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
