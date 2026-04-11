# 🚀 Process Core Optimizer
**Advanced CPU Affinity Manager & Gaming Performance Stabilizer**

## 🎯 Project Overview
**Process Core Optimizer** is a high-performance utility built on **.NET 10.0** designed to give users granular control over their processor's logical cores. By manually managing CPU Affinity, users can isolate demanding applications (like games or renderers) to specific cores, significantly reducing micro-stuttering and improving 1% low FPS stability.

## ✨ Key Features
* **Real-time Core Analytics:** Live visual monitoring of every logical thread on your CPU.
* **Smart Affinity Engine:** Create and save custom profiles. The app automatically detects launched processes and applies your preferred core configuration.
* **Windows 11 Game Mode Sync:** Built-in diagnostics to detect conflicts with system-level Game Mode settings.
* **Minimalist Tray Execution:** Runs silently in the System Tray with near-zero resource impact.

## 🛠 Technical Stack
* **Framework:** .NET 10.0 (LTS)
* **Language:** C#
* **Platform:** Windows 11 / 10 (x64)
* **Libraries:** Newtonsoft.Json for profile management.

## 📥 Installation & Usage (How-to)

### 1. Download
Navigate to the **[Releases](https://github.com/9Erza/ProcessCoreOptimizer/releases)** tab on the right and download the latest `ProcessCoreOptimizer_Setup.exe
`.

### 2. Launching
* If prompted by SmartScreen, click "More info" -> "Run anyway" (since this is an unsigned community tool).

### 3. Basic Workflow
1. **Find Process:** Select your target game or application from the active processes list.
2. **Assign Cores:** Toggle the checkboxes for the specific CPU cores you want the application to use.
3. **Apply Settings:** Click **"Set Affinity"** to immediately apply the core constraints to the running process.
4. **Save Profile:** Click **"Save Profile"** so the app automatically remembers and applies these settings every time it detects this process in the future.
5. **Background Mode:** Minimize the window; the app will stay active in your System Tray (if you check "Minimize to Tray") to monitor and manage your profiles.

---
### 👨‍💻 Developer Info
Created by **9Erza**. This project is open-source under the MIT License. 
*Targeting high-end optimization for the 2026 Windows ecosystem.*

**[⭐ Star this repository if you find it useful!]**
