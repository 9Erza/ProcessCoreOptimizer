# Process Core Optimizer

<p align="center">
  <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/refs/heads/main/ProcessCoreOptimizer/Images/processcoreoptimizer.ico" alt="Process Core Optimizer Logo">
</p>

<p align="center">
  <img src="https://img.shields.io/github/license/9Erza/ProcessCoreOptimizer?style=flat-square" alt="License">
  <img src="https://img.shields.io/github/v/release/9Erza/ProcessCoreOptimizer?style=flat-square" alt="Version">
  <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Platform">
</p>

**Process Core Optimizer (PCO)** is a professional-grade system utility built on the WPF framework, engineered to grant you absolute control over Windows thread scheduling and CPU resource allocation. Whether you are a competitive gamer or a power user, PCO ensures your critical applications get the performance they deserve.

---

The latest version introduces a sophisticated 3-tier optimization logic, allowing you to choose how the Windows Scheduler treats your processes:

- **🔵 Affinity (Standard):** Strict hard-binding of threads to specific cores. Ideal for legacy software and standard applications.
- **🟢 CPU Sets (Smart):** A modern "soft-affinity" approach. It prioritizes selected cores for your game, providing superior frame pacing and reduced micro-stutters without starving the OS.
- **🟡 Exclusive (Hardcore):** Pure core isolation. PCO actively evicts background processes (browsers, Discord, system bloat) from your game's assigned cores, creating a high-performance "Clean Core Environment."

---

## 🛡️ Anti-Cheat Stealth Integration

* **Smart Optimization:** By utilizing **CPU Sets** instead of traditional affinity locking, PCO avoids compatibility issues with aggressive anti-cheats (FaceitAC (CS2), Vanguard (Valorant)) while still delivering performance benefits.
* **Stealth Integration:** v1.1.1 uses low-level Windows API calls (`PROCESS_SET_LIMITED_INFORMATION`) to seamlessly apply CPU Sets to protected processes without triggering anti-cheat alarms, "Access Denied" errors, or system crashes.

---

## 🌟 Key Features

### 🛠️ Strategic Process Control
- **P/E-Core Management:** Force your games onto high-frequency Performance cores and away from Efficiency cores (E-Cores) to eliminate latency spikes.
- **Dynamic Priority Adjustments:** Switch process priorities on the fly (from Idle to Real-Time) to ensure your active tasks are never bottlenecked.
- **Persistent Profiles:** Save custom configurations that are automatically applied whenever your game is detected in the background.

### 🎨 Intelligent UX/UI
- **Color-Coded Feedback:** The process list features dynamic tags: **[Affinity]**, **[CPU Sets]**, and **[Exclusive]** to provide instant visual confirmation of active optimizations.
- **Modern Dark Interface:** A sleek, high-DPI aware WPF design with smooth transitions and organized layouts.
- **System Tray Integration:** Minimize the app to the tray for silent, automated background management.
- **Bilingual Support:** Fully localized in **English** and **Polish**.

### 📊 Real-Time Hardware Telemetry
Track your system's vital signs with built-in monitoring (powered by LibreHardwareMonitor):
- **CPU:** Usage per core, Temperature, Average Clock Speed, and Power Draw (TDP).
- **GPU:** Load, Core/Memory Clocks, Hot Spot, VRAM Temperature, and Power Draw.
- **RAM:** Total utilization % and detailed breakdown of Used vs. Available memory in GB.

---

## ⚠️ Important: Version Compatibility
Version 1.1.1 uses a new, more secure method for saving profiles. If you are upgrading from an older version and your profiles do not appear correctly, please delete `profiles.json` in the application folder and recreate them to take full advantage of the new engine.

---

## 📸 Screenshots

<details>
  <summary><b>📷 Click to expand / hide gallery</b></summary>
  <br>
  <table align="center">
    <tr>
      <td align="center" width="50%">
        <b>🖥️ System Processes (with Color Tags)</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_system-processes.png" width="100%" alt="System Processes" />
      </td>
      <td align="center" width="50%">
        <b>💾 Enhanced Profiles Manager</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_saved-profiles.png" width="100%" alt="Saved Profiles" />
      </td>
    </tr>
    <tr>
      <td align="center">
        <b>📊 Hardware Monitor (CPU/GPU)</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_hardware-monitor-1.png" width="100%" alt="Hardware Monitor 1" />
      </td>
      <td align="center">
        <b>📊 Detailed Telemetry</b><br>
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

## 📥 Installation

1. Navigate to the [**Releases**](https://github.com/9Erza/ProcessCoreOptimizer/releases) section.
2. Download `ProcessCoreOptimizer_Setup.exe`.
3. Run the installer.
4. **Note:** Enable the **"Run as Administrator"** option in the app settings to allow the bypass of system-level restrictions for protected games.

---

## 🛠️ Tech Stack
- **Language:** C#
- **Framework:** .NET 10
- **UI Engine:** WPF (Windows Presentation Foundation)

---

## 👤 Author
Developed by **[Eryk / 9Erza](https://github.com/9Erza)**.

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
