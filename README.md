# Process Core Optimizer

<p align="center">
  <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/refs/heads/main/ProcessCoreOptimizer/Images/processcoreoptimizer.ico" alt="Process Core Optimizer Logo">
</p>

<p align="center">
  <img src="https://img.shields.io/github/license/9Erza/ProcessCoreOptimizer?style=flat-square" alt="License">
  <img src="https://img.shields.io/github/v/release/9Erza/ProcessCoreOptimizer?style=flat-square" alt="Version">
  <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Platform">
</p>

**Process Core Optimizer (PCO)** is a Windows utility for advanced users who want to manage process priority, classic CPU affinity and Windows CPU Sets from a clean WPF interface.

PCO is intended for experimentation, troubleshooting and personal performance tuning. Performance gains are workload-dependent and are not guaranteed.

---

## Optimization modes

- **Affinity:** Strictly binds selected processes to selected logical processors by using classic Windows process affinity.
- **CPU Sets:** Uses documented Windows CPU Sets as a softer scheduling hint. This is usually safer than hard affinity for modern Windows scheduling.

The previous **Exclusive** mode has been removed because moving unrelated background processes away from selected cores can reduce system stability and is not worth the risk for a normal desktop utility. Old profiles containing `Exclusive` are automatically migrated to `Affinity`.

---

## Key features

- **Persistent profiles:** Save affinity / CPU Sets / priority settings per process name and apply them automatically when the process starts.
- **Priority management:** Change priority from Idle to High. RealTime priority is hidden by default and must be explicitly enabled in settings.
- **P/E-core awareness:** Uses Windows CPU Sets topology data where available to label Performance cores, Efficiency cores and SMT/HT threads more accurately.
- **Per-core selection:** Select all cores, clear all cores, disable SMT/HT threads or disable E-cores.
- **Real-time telemetry:** CPU, GPU and RAM metrics through LibreHardwareMonitor.
- **Bilingual UI:** English and Polish.
- **User-data safe storage:** Profiles, settings and logs are stored under `%APPDATA%\ProcessCoreOptimizer`.

---

## Important limitations

- The UI currently targets the first Windows processor group / first 64 logical processors. Very large workstation CPUs may require future multi-group support.
- Some protected processes or games may reject external priority, affinity or CPU Set changes.
- Compatibility with anti-cheat systems is not guaranteed. Use with online games at your own risk.
- RealTime priority can make the system unresponsive. It is disabled in the UI unless explicitly enabled in settings.

---

## Data files

PCO stores user data here:

```text
%APPDATA%\ProcessCoreOptimizer\settings.json
%APPDATA%\ProcessCoreOptimizer\profiles.json
%APPDATA%\ProcessCoreOptimizer\ProcessCoreOptimizer.log
```

If older versions stored `settings.json` or `profiles.json` next to the `.exe`, PCO migrates them automatically on first launch.

---

## Screenshots

<details>
  <summary><b>Click to expand / hide gallery</b></summary>
  <br>
  <table align="center">
    <tr>
      <td align="center" width="50%">
        <b>System Processes</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_system-processes.png" width="100%" alt="System Processes" />
      </td>
      <td align="center" width="50%">
        <b>Profiles Manager</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_saved-profiles.png" width="100%" alt="Saved Profiles" />
      </td>
    </tr>
    <tr>
      <td align="center">
        <b>Hardware Monitor</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_hardware-monitor-1.png" width="100%" alt="Hardware Monitor 1" />
      </td>
      <td align="center">
        <b>Detailed Telemetry</b><br>
        <img src="https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/main/ProcessCoreOptimizer/Screenshots/screen_hardware-monitor-2.png" width="100%" alt="Hardware Monitor 2" />
      </td>
    </tr>
  </table>
</details>

---

## Build

Open the solution in Visual Studio Community and run:

```text
Build > Rebuild Solution
```

The project targets Windows WPF on .NET.

---

## Tech stack

- C#
- WPF
- Windows CPU Sets API
- LibreHardwareMonitor

---

## Author

Developed by **[Eryk / 9Erza](https://github.com/9Erza)**.

## License

MIT License. See [LICENSE](LICENSE).
