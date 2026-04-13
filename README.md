# Process Core Optimizer
**Advanced CPU Affinity Manager & Gaming Performance Stabilizer**

## 🎯 Project Overview
**Process Core Optimizer** is a high-performance utility built on **.NET 10.0** designed to give users granular control over their processor's logical cores. By manually managing CPU Affinity, users can isolate demanding applications (like games or renderers) to specific cores, significantly reducing micro-stuttering and improving 1% low FPS stability.

## ✨ Key Features
* **Real-time Core Analytics:** Live visual monitoring of every logical thread on your CPU with specialized tagging.
* **Smart Affinity Engine:** Create and save custom profiles. The app automatically detects launched processes and applies your preferred core configuration in the background.
* **Windows 11 Game Mode Sync:** Built-in diagnostics to detect conflicts with system-level Game Mode settings. Game Mode may cause conflicts with manual configurations by attempting to automate core priority; this constant "tug-of-war" between system and user settings can counteract stability tweaks and induce noticeable micro-stuttering.
* **Minimalist Tray Execution:** Runs silently in the System Tray with near-zero resource impact.

## 📖 UI Guide & Legend
To maximize performance, the app uses a smart tagging system to help you identify core types at a glance:

| Tag | Meaning | Recommendation |
| :--- | :--- | :--- |
| **[P]** | **Physical Core** | Best for gaming. Assign your main game process here. |
| **[T]** | **Thread (SMT/HT)** | Logical threads. Often disabled to reduce cache latency and boost 1% lows. |
| **[E]** | **Efficiency Core** | (Intel Only) Ideal for background tasks, but usually avoided for high-performance gaming. |

### Quick-Action Buttons:
* **Apply Affinity:** Immediately forces the selected process to the checked cores.
* **SMT/HT Off:** Instantly unchecks all logical threads, leaving only physical cores active.
* **Disable E-Cores:** (Intel Hybrid CPU Only) A dynamic toggle that instantly excludes Efficiency Cores from the selected process's affinity. This forces the application to focus all its computational power exclusively on high-performance P-Cores.
* **Save Profile:** Stores the configuration. The app will "watch" for this process and apply settings automatically upon launch.
* **Remove Profile:** Permanently deletes a saved configuration for a specific application.
* **Interactive Profiles List:** Displays all your saved configurations. Selecting a profile from this list allows you to view and edit its core assignment even when the application or game is not currently running.

## 💻 Compatibility & Hardware Note
The application logic has been specifically updated to handle both uniform and hybrid architectures.

* **Verified Hardware (AMD):** Ryzen 7 7800X3D, Ryzen 7 5700X.
* **Intel Hybrid Support (v1.0.5+):** Full logic for P-Core/E-Core detection and management has been implemented. 
> [!IMPORTANT]
> **Status:** Intel Hybrid functionality is currently in the **experimental phase**. While the core logic is complete, verification on 13th, 14th and newer gen Intel CPUs is ongoing. Use with caution and please report any feedback.

## 🛠 Technical Stack
* **Framework:** .NET 10.0 (LTS)
* **Language:** C#
* **Platform:** Windows 11 / 10 (x64)

## 📥 Installation & Usage

### 1. Download
Navigate to the **[Releases](https://github.com/9Erza/ProcessCoreOptimizer/releases)** tab and download the latest `ProcessCoreOptimizer_Setup.exe`.

### 🛠️ 2. Installation & Launching
1. **Download** the `ProcessCoreOptimizer_Setup.exe` from the latest assets.
2. **Run the installer**. It will safely install the app and register the required fonts in your system.
> [!IMPORTANT]
> **Windows SmartScreen:** Since this is an unsigned community tool, Windows might show a warning. Click **"More info"** and then **"Run anyway"** to proceed.
3. **Launch the app** via the desktop shortcut or Start Menu.

### 3. Basic Workflow
1. **Find Process:** Select your target game or application from the active processes list.
2. **Assign Cores:** Toggle checkboxes based on the **[P]**, **[T]**, or **[E]** tags.
3. **Apply Settings:** Click **"Set Affinity"** to apply changes immediately.
4. **Save Profile:** Click **"Save Profile"** so the app remembers your choice for future sessions.
5. **Background Mode:** Minimize to tray; the app will stay active to monitor and manage your profiles automatically.

---
### 👨‍💻 Developer Info
Created by **9Erza**. This project is open-source under the MIT License.  
*Targeting high-end optimization for the Windows ecosystem.*

**[⭐ Star this repository if you find it useful!]**
