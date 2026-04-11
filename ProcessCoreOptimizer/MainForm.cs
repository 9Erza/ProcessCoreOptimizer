using Microsoft.Win32;
using Newtonsoft.Json;
using System.Data;
using System.Diagnostics;
using System.Net.Http;
using System.IO;

namespace ProcessCoreOptimizer
{
    /// <summary>
    /// Main application logic for Process Core Optimizer.
    /// Handles CPU affinity, process monitoring, and system settings.
    /// </summary>
    public partial class MainForm : Form
    {
        // --- FIELDS ---
        private List<Process> displayedProcesses = new List<Process>();
        private AppSettings settings = new AppSettings();
        private string settingsPath = "settings.json";

        private Dictionary<int, TimeSpan> lastCpuTime = new Dictionary<int, TimeSpan>();
        private DateTime lastSampleTime = DateTime.Now;

        private List<ProgressBar> coreBars = new List<ProgressBar>();
        private List<PerformanceCounter> coreCounters = new List<PerformanceCounter>();

        // --- INITIALIZATION ---

        public MainForm()
        {
            InitializeComponent();
            InitializeApplication();
        }

        private void InitializeApplication()
        {
            LoadSettings();
            RefreshProcessTable();
            LoadCores();
            SetupCoreVisuals();

            trayIcon.Text = "Process Core Optimizer";
            string currentVersion = Application.ProductVersion.Split('+')[0];
            lblVersion.Text = $"Application Version: {currentVersion}";

            UpdateGameModeStatus();
            AddLog("Application initialized successfully.");
            CheckForUpdates();
        }

        // --- LOGGING SYSTEM ---

        /// <summary>
        /// Adds a timestamped message to the UI log and maintains a limit of 50 entries.
        /// </summary>
        private void AddLog(string message)
        {
            if (listLog == null) return;

            string time = DateTime.Now.ToString("HH:mm:ss");
            listLog.Items.Insert(0, $"[{time}] {message}");

            if (listLog.Items.Count > 50)
                listLog.Items.RemoveAt(50);
        }

        // --- SETTINGS MANAGEMENT ---

        private void SaveSettings()
        {
            settings.AutoApplyEnabled = cbAutoApply.Checked;
            settings.MinimizeToTrayEnabled = cbMinimizeToTray.Checked;
            settings.StartWithWindows = cbStartWithWindows.Checked;

            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(settingsPath, json);
        }

        private void LoadSettings()
        {
            if (!File.Exists(settingsPath)) return;

            try
            {
                string json = File.ReadAllText(settingsPath);
                settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();

                cbAutoApply.Checked = settings.AutoApplyEnabled;
                cbMinimizeToTray.Checked = settings.MinimizeToTrayEnabled;
                cbStartWithWindows.Checked = settings.StartWithWindows;
            }
            catch { settings = new AppSettings(); }
        }

        private void SetStartup(bool start)
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (rk == null) return;

                    if (start)
                        rk.SetValue("ProcessCoreOptimizer", $"\"{Application.ExecutablePath}\"");
                    else
                        rk.DeleteValue("ProcessCoreOptimizer", false);

                    AddLog(start ? "Autostart enabled." : "Autostart disabled.");
                }
            }
            catch (Exception ex) { AddLog($"Registry Error: {ex.Message}"); }
        }

        // --- CORE & PROCESS MONITORING ---

        private bool IsUserProcess(Process proc)
        {
            try
            {
                if (proc.MainWindowHandle == IntPtr.Zero || proc.ProcessName == "Idle") return false;
                string path = proc.MainModule.FileName.ToLower();
                return !(path.Contains("windows\\system32") || path.Contains("windows\\syswow64"));
            }
            catch { return false; }
        }

        private void RefreshProcessTable()
        {
            dgvProcesses.Rows.Clear();
            displayedProcesses.Clear();

            var allProcs = Process.GetProcesses().OrderBy(p => p.ProcessName);
            foreach (var p in allProcs)
            {
                if (IsUserProcess(p))
                {
                    long ramMB = p.WorkingSet64 / 1024 / 1024;
                    dgvProcesses.Rows.Add(p.ProcessName, "0%", $"{ramMB} MB", p.Id);
                    displayedProcesses.Add(p);
                }
            }
        }

        private void LoadCores()
        {
            listCores.Items.Clear();
            for (int i = 0; i < Environment.ProcessorCount; i++)
                listCores.Items.Add($"Core {i}");
        }

        private void SetupCoreVisuals()
        {
            panelCores.Controls.Clear();
            coreBars.Clear();
            coreCounters.Clear();

            int spacing = 6;
            int barWidth = 14;

            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                VerticalProgressBar bar = new VerticalProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Size = new Size(barWidth, panelCores.Height - 20),
                    Location = new Point(10 + (i * (barWidth + spacing)), 10),
                    Style = ProgressBarStyle.Continuous,
                    ForeColor = Color.DodgerBlue
                };

                panelCores.Controls.Add(bar);
                coreBars.Add(bar);

                PerformanceCounter pc = new PerformanceCounter("Processor", "% Processor Time", i.ToString());
                coreCounters.Add(pc);
            }
        }

        // --- MAIN TIMER LOOP ---

        private void refreshTimer_Tick(object sender, EventArgs e)
        {
            double elapsedSeconds = (DateTime.Now - lastSampleTime).TotalSeconds;
            lastSampleTime = DateTime.Now;
            var currentAllProcs = Process.GetProcesses();

            // 1. New process detection
            foreach (var p in currentAllProcs)
            {
                if (!IsUserProcess(p)) continue;

                bool exists = false;
                foreach (DataGridViewRow row in dgvProcesses.Rows)
                {
                    if (row.Cells[3].Value?.ToString() == p.Id.ToString()) { exists = true; break; }
                }

                if (!exists)
                {
                    long ramMB = p.WorkingSet64 / 1024 / 1024;
                    dgvProcesses.Rows.Add(p.ProcessName, "0%", $"{ramMB} MB", p.Id);
                    AddLog($"New process detected: {p.ProcessName}");
                }
            }

            // 2. Stats update & Auto-Apply logic
            for (int i = dgvProcesses.Rows.Count - 1; i >= 0; i--)
            {
                try
                {
                    int pid = Convert.ToInt32(dgvProcesses.Rows[i].Cells[3].Value);
                    Process proc = Process.GetProcessById(pid);

                    if (proc.HasExited) throw new Exception();

                    // RAM & CPU Update
                    dgvProcesses.Rows[i].Cells[2].Value = $"{proc.WorkingSet64 / 1024 / 1024} MB";

                    TimeSpan currentCpu = proc.TotalProcessorTime;
                    if (lastCpuTime.ContainsKey(pid))
                    {
                        double usage = (currentCpu - lastCpuTime[pid]).TotalMilliseconds;
                        double pct = (usage / (elapsedSeconds * 1000 * Environment.ProcessorCount)) * 100;
                        dgvProcesses.Rows[i].Cells[1].Value = $"{Math.Round(pct, 1)}%";
                    }
                    lastCpuTime[pid] = currentCpu;

                    // Affinity Auto-Apply
                    string pName = dgvProcesses.Rows[i].Cells[0].Value.ToString();
                    if (cbAutoApply.Checked && settings.ProcessProfiles.ContainsKey(pName))
                    {
                        long savedMask = settings.ProcessProfiles[pName];
                        if ((long)proc.ProcessorAffinity != savedMask)
                        {
                            proc.ProcessorAffinity = (IntPtr)savedMask;
                            dgvProcesses.Rows[i].DefaultCellStyle.BackColor = Color.LightGreen;
                            AddLog($"Auto-Applied mask {savedMask} for {pName}");
                        }
                    }
                }
                catch { dgvProcesses.Rows.RemoveAt(i); }
            }

            // 3. Core visuals update
            for (int i = 0; i < coreCounters.Count; i++)
            {
                try { coreBars[i].Value = (int)coreCounters[i].NextValue(); } catch { }
            }

            UpdateGameModeStatus();
        }

        // --- BUTTON HANDLERS ---

        private void btnApply_Click(object sender, EventArgs e)
        {
            if (dgvProcesses.CurrentRow == null) return;
            try
            {
                int pid = Convert.ToInt32(dgvProcesses.CurrentRow.Cells[3].Value);
                Process proc = Process.GetProcessById(pid);
                long mask = 0;
                for (int i = 0; i < listCores.Items.Count; i++)
                    if (listCores.GetItemChecked(i)) mask |= (1L << i);

                if (mask == 0) return;
                proc.ProcessorAffinity = (IntPtr)mask;
                AddLog($"Manual Affinity set for {proc.ProcessName}");
                dgvProcesses.CurrentRow.DefaultCellStyle.BackColor = Color.Cyan;
            }
            catch (Exception ex) { AddLog($"Error: {ex.Message}"); }
        }

        private void btnSaveProfile_Click(object sender, EventArgs e)
        {
            if (dgvProcesses.CurrentRow == null) return;
            string procName = dgvProcesses.CurrentRow.Cells[0].Value.ToString();
            long mask = 0;
            foreach (int index in listCores.CheckedIndices) mask |= (1L << index);

            if (mask == 0) return;
            settings.ProcessProfiles[procName] = mask;
            SaveSettings();
            AddLog($"Profile saved for {procName}");
        }

        private void btnRemoveProfile_Click(object sender, EventArgs e)
        {
            if (dgvProcesses.CurrentRow == null) return;
            string procName = dgvProcesses.CurrentRow.Cells[0].Value.ToString();

            if (settings.ProcessProfiles.Remove(procName))
            {
                SaveSettings();
                dgvProcesses.CurrentRow.DefaultCellStyle.BackColor = Color.White;
                AddLog($"Profile removed for {procName}");
            }
        }

        // --- UI HELPERS ---

        private void btnSelectAll_Click(object sender, EventArgs e) { for (int i = 0; i < listCores.Items.Count; i++) listCores.SetItemChecked(i, true); }
        private void btnNone_Click(object sender, EventArgs e) { for (int i = 0; i < listCores.Items.Count; i++) listCores.SetItemChecked(i, false); }
        private void btnSmtOff_Click(object sender, EventArgs e) { for (int i = 0; i < listCores.Items.Count; i++) listCores.SetItemChecked(i, i % 2 == 0); }
        private void btnManualRefresh_Click(object sender, EventArgs e) { RefreshProcessTable(); AddLog("Manual refresh triggered."); }

        private void dgvProcesses_SelectionChanged(object sender, EventArgs e)
        {
            if (dgvProcesses.CurrentRow == null) return;
            try
            {
                int pid = Convert.ToInt32(dgvProcesses.CurrentRow.Cells[3].Value);
                long mask = (long)Process.GetProcessById(pid).ProcessorAffinity;
                for (int i = 0; i < listCores.Items.Count; i++)
                    listCores.SetItemChecked(i, (mask & (1L << i)) != 0);
            }
            catch { }
        }

        // --- TRAY & WINDOW LOGIC ---

        private void trayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            trayIcon.Visible = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized && cbMinimizeToTray.Checked)
            {
                this.Hide();
                trayIcon.Visible = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { base.OnFormClosing(e); SaveSettings(); }

        private void openToolStripMenuItem_Click(object sender, EventArgs e) => trayIcon_MouseDoubleClick(null, null);
        private void exitToolStripMenuItem_Click(object sender, EventArgs e) { trayIcon.Visible = false; Application.Exit(); }

        // --- SYSTEM DIAGNOSTICS ---

        private void UpdateGameModeStatus()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar"))
                {
                    if (key == null) return;
                    int allowAuto = (key.GetValue("AllowAutoGameMode") as int?) ?? 0;
                    int autoEnabled = (key.GetValue("AutoGameModeEnabled") as int?) ?? 0;

                    bool isEnabled = (allowAuto == 1 || autoEnabled == 1);
                    lblGameModeStatus.Text = isEnabled ? "Game Mode: ON (May cause conflicts)" : "Game Mode: OFF (Recommended)";
                    lblGameModeStatus.ForeColor = isEnabled ? Color.Red : Color.Green;
                }
            }
            catch { lblGameModeStatus.Text = "Game Mode: Unknown"; }
        }

        private void btnToggleGameMode_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:gaming-gamemode") { UseShellExecute = true });
                AddLog("Opening Windows Game Mode settings...");
            }
            catch { AddLog("Failed to open system settings."); }
        }

        // --- DESIGNER PLACEHOLDERS ---
        private void cbAutoApply_CheckedChanged(object sender, EventArgs e) => SaveSettings();
        private void cbMinimizeToTray_CheckedChanged(object sender, EventArgs e) => SaveSettings();
        private void cbStartWithWindows_CheckedChanged(object sender, EventArgs e) { SetStartup(cbStartWithWindows.Checked); SaveSettings(); }
        private void btnRefresh_Click(object sender, EventArgs e) => RefreshProcessTable();
        private async void CheckForUpdates()
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "ProcessCoreOptimizer-Updater");

                string url = "https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/refs/heads/main/ProcessCoreOptimizer/version.txt";
                string latestVersion = (await client.GetStringAsync(url)).Trim();
                string currentVersion = Application.ProductVersion;

                if (latestVersion != currentVersion)
                {
                    AddLog($"Update available: {latestVersion}");

                    var result = MessageBox.Show(
                        $"A new version ({latestVersion}) is available. Would you like to download it now?",
                        "Update Available",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo("https://github.com/9Erza/ProcessCoreOptimizer/releases")
                        {
                            UseShellExecute = true
                        });
                    }
                }
                else
                {
                    AddLog("System is up to date.");
                }
            }
            catch
            {
                AddLog("Update check failed.");
            }
        }
    }

    public class AppSettings
    {
        public Dictionary<string, long> ProcessProfiles { get; set; } = new Dictionary<string, long>();
        public bool AutoApplyEnabled { get; set; } = false;
        public bool MinimizeToTrayEnabled { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
    }

    public class VerticalProgressBar : ProgressBar
    {
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x04; // PBS_VERTICAL
                return cp;
            }
        }
    }
}