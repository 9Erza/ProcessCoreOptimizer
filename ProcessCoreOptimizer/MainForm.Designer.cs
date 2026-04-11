namespace ProcessCoreOptimizer
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            listCores = new CheckedListBox();
            btnApply = new Button();
            dgvProcesses = new DataGridView();
            colName = new DataGridViewTextBoxColumn();
            colCPU = new DataGridViewTextBoxColumn();
            colRAM = new DataGridViewTextBoxColumn();
            colID = new DataGridViewTextBoxColumn();
            refreshTimer = new System.Windows.Forms.Timer(components);
            btnSelectAll = new Button();
            btnNone = new Button();
            btnSmtOff = new Button();
            btnRefresh = new Button();
            btnSaveProfile = new Button();
            btnRemoveProfile = new Button();
            cbStartWithWindows = new CheckBox();
            cbMinimizeToTray = new CheckBox();
            cbAutoApply = new CheckBox();
            trayIcon = new NotifyIcon(components);
            contextMenuStrip1 = new ContextMenuStrip(components);
            openToolStripMenuItem = new ToolStripMenuItem();
            exitToolStripMenuItem = new ToolStripMenuItem();
            panelCores = new GroupBox();
            listLog = new ListBox();
            lblGameModeStatus = new Label();
            btnToggleGameMode = new Button();
            lblGameModeWarning = new Label();
            lblLogs = new Label();
            lblCorePanel = new Label();
            lblProcessList = new Label();
            lblCoreSelection = new Label();
            lblVersion = new Label();
            ((System.ComponentModel.ISupportInitialize)dgvProcesses).BeginInit();
            contextMenuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // listCores
            // 
            listCores.CheckOnClick = true;
            listCores.FormattingEnabled = true;
            listCores.Location = new Point(900, 28);
            listCores.MultiColumn = true;
            listCores.Name = "listCores";
            listCores.ScrollAlwaysVisible = true;
            listCores.Size = new Size(156, 436);
            listCores.TabIndex = 1;
            // 
            // btnApply
            // 
            btnApply.Font = new Font("Segoe UI", 16F);
            btnApply.Location = new Point(900, 563);
            btnApply.Name = "btnApply";
            btnApply.Size = new Size(352, 106);
            btnApply.TabIndex = 2;
            btnApply.Text = "Set Affinity";
            btnApply.UseVisualStyleBackColor = true;
            btnApply.Click += btnApply_Click;
            // 
            // dgvProcesses
            // 
            dgvProcesses.AllowUserToAddRows = false;
            dgvProcesses.AllowUserToDeleteRows = false;
            dgvProcesses.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvProcesses.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvProcesses.Columns.AddRange(new DataGridViewColumn[] { colName, colCPU, colRAM, colID });
            dgvProcesses.Location = new Point(12, 28);
            dgvProcesses.MultiSelect = false;
            dgvProcesses.Name = "dgvProcesses";
            dgvProcesses.ReadOnly = true;
            dgvProcesses.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvProcesses.Size = new Size(882, 641);
            dgvProcesses.TabIndex = 3;
            dgvProcesses.SelectionChanged += dgvProcesses_SelectionChanged;
            // 
            // colName
            // 
            colName.HeaderText = "Process Name";
            colName.Name = "colName";
            colName.ReadOnly = true;
            // 
            // colCPU
            // 
            colCPU.HeaderText = "CPU %";
            colCPU.Name = "colCPU";
            colCPU.ReadOnly = true;
            // 
            // colRAM
            // 
            colRAM.HeaderText = "RAM (MB)";
            colRAM.Name = "colRAM";
            colRAM.ReadOnly = true;
            // 
            // colID
            // 
            colID.HeaderText = "ID";
            colID.Name = "colID";
            colID.ReadOnly = true;
            // 
            // refreshTimer
            // 
            refreshTimer.Enabled = true;
            refreshTimer.Interval = 1000;
            refreshTimer.Tick += refreshTimer_Tick;
            // 
            // btnSelectAll
            // 
            btnSelectAll.AccessibleName = "";
            btnSelectAll.Font = new Font("Segoe UI", 12F);
            btnSelectAll.Location = new Point(1096, 28);
            btnSelectAll.Name = "btnSelectAll";
            btnSelectAll.Size = new Size(156, 49);
            btnSelectAll.TabIndex = 4;
            btnSelectAll.Text = "Select All";
            btnSelectAll.UseVisualStyleBackColor = true;
            btnSelectAll.Click += btnSelectAll_Click;
            // 
            // btnNone
            // 
            btnNone.Font = new Font("Segoe UI", 12F);
            btnNone.Location = new Point(1096, 83);
            btnNone.Name = "btnNone";
            btnNone.Size = new Size(156, 49);
            btnNone.TabIndex = 5;
            btnNone.Text = "Clear All";
            btnNone.UseVisualStyleBackColor = true;
            btnNone.Click += btnNone_Click;
            // 
            // btnSmtOff
            // 
            btnSmtOff.Font = new Font("Segoe UI", 12F);
            btnSmtOff.Location = new Point(1096, 138);
            btnSmtOff.Name = "btnSmtOff";
            btnSmtOff.Size = new Size(156, 49);
            btnSmtOff.TabIndex = 6;
            btnSmtOff.Text = "SMT / HT OFF";
            btnSmtOff.UseVisualStyleBackColor = true;
            btnSmtOff.Click += btnSmtOff_Click;
            // 
            // btnRefresh
            // 
            btnRefresh.Font = new Font("Segoe UI", 12F);
            btnRefresh.Location = new Point(900, 690);
            btnRefresh.Name = "btnRefresh";
            btnRefresh.Size = new Size(140, 50);
            btnRefresh.TabIndex = 7;
            btnRefresh.Text = "Manual Refresh";
            btnRefresh.UseVisualStyleBackColor = true;
            btnRefresh.Click += btnRefresh_Click;
            // 
            // btnSaveProfile
            // 
            btnSaveProfile.Font = new Font("Segoe UI", 12F);
            btnSaveProfile.Location = new Point(1096, 488);
            btnSaveProfile.Name = "btnSaveProfile";
            btnSaveProfile.Size = new Size(156, 49);
            btnSaveProfile.TabIndex = 8;
            btnSaveProfile.Text = "Save Profile";
            btnSaveProfile.UseVisualStyleBackColor = true;
            btnSaveProfile.Click += btnSaveProfile_Click;
            // 
            // btnRemoveProfile
            // 
            btnRemoveProfile.Font = new Font("Segoe UI", 12F);
            btnRemoveProfile.Location = new Point(900, 488);
            btnRemoveProfile.Name = "btnRemoveProfile";
            btnRemoveProfile.Size = new Size(156, 49);
            btnRemoveProfile.TabIndex = 9;
            btnRemoveProfile.Text = "Remove Profile";
            btnRemoveProfile.UseVisualStyleBackColor = true;
            btnRemoveProfile.Click += btnRemoveProfile_Click;
            // 
            // cbStartWithWindows
            // 
            cbStartWithWindows.AutoSize = true;
            cbStartWithWindows.Font = new Font("Segoe UI", 12F);
            cbStartWithWindows.Location = new Point(1096, 690);
            cbStartWithWindows.Name = "cbStartWithWindows";
            cbStartWithWindows.Size = new Size(164, 25);
            cbStartWithWindows.TabIndex = 10;
            cbStartWithWindows.Text = "Start with Windows";
            cbStartWithWindows.UseVisualStyleBackColor = true;
            cbStartWithWindows.CheckedChanged += cbStartWithWindows_CheckedChanged;
            // 
            // cbMinimizeToTray
            // 
            cbMinimizeToTray.AutoSize = true;
            cbMinimizeToTray.Font = new Font("Segoe UI", 12F);
            cbMinimizeToTray.Location = new Point(1096, 721);
            cbMinimizeToTray.Name = "cbMinimizeToTray";
            cbMinimizeToTray.Size = new Size(144, 25);
            cbMinimizeToTray.TabIndex = 11;
            cbMinimizeToTray.Text = "Minimize to Tray";
            cbMinimizeToTray.UseVisualStyleBackColor = true;
            // 
            // cbAutoApply
            // 
            cbAutoApply.AutoSize = true;
            cbAutoApply.Font = new Font("Segoe UI", 12F);
            cbAutoApply.Location = new Point(1096, 750);
            cbAutoApply.Name = "cbAutoApply";
            cbAutoApply.Size = new Size(164, 25);
            cbAutoApply.TabIndex = 12;
            cbAutoApply.Text = "Auto-Apply Profiles";
            cbAutoApply.UseVisualStyleBackColor = true;
            // 
            // trayIcon
            // 
            trayIcon.ContextMenuStrip = contextMenuStrip1;
            trayIcon.Icon = (Icon)resources.GetObject("trayIcon.Icon");
            trayIcon.Text = "Process Core Optimizer";
            trayIcon.Visible = true;
            trayIcon.MouseDoubleClick += trayIcon_MouseDoubleClick;
            // 
            // contextMenuStrip1
            // 
            contextMenuStrip1.Items.AddRange(new ToolStripItem[] { openToolStripMenuItem, exitToolStripMenuItem });
            contextMenuStrip1.Name = "contextMenuStrip1";
            contextMenuStrip1.Size = new Size(104, 48);
            // 
            // openToolStripMenuItem
            // 
            openToolStripMenuItem.Name = "openToolStripMenuItem";
            openToolStripMenuItem.Size = new Size(103, 22);
            openToolStripMenuItem.Text = "Open";
            openToolStripMenuItem.Click += openToolStripMenuItem_Click;
            // 
            // exitToolStripMenuItem
            // 
            exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            exitToolStripMenuItem.Size = new Size(103, 22);
            exitToolStripMenuItem.Text = "Exit";
            exitToolStripMenuItem.Click += exitToolStripMenuItem_Click;
            // 
            // panelCores
            // 
            panelCores.Location = new Point(12, 682);
            panelCores.Name = "panelCores";
            panelCores.Size = new Size(457, 103);
            panelCores.TabIndex = 13;
            panelCores.TabStop = false;
            // 
            // listLog
            // 
            listLog.FormattingEnabled = true;
            listLog.Location = new Point(12, 806);
            listLog.Name = "listLog";
            listLog.Size = new Size(457, 109);
            listLog.TabIndex = 14;
            // 
            // lblGameModeStatus
            // 
            lblGameModeStatus.AutoSize = true;
            lblGameModeStatus.Font = new Font("Segoe UI", 12F);
            lblGameModeStatus.Location = new Point(487, 705);
            lblGameModeStatus.Name = "lblGameModeStatus";
            lblGameModeStatus.Size = new Size(175, 21);
            lblGameModeStatus.TabIndex = 15;
            lblGameModeStatus.Text = "Game Mode: Checking...";
            // 
            // btnToggleGameMode
            // 
            btnToggleGameMode.Font = new Font("Segoe UI", 12F);
            btnToggleGameMode.Location = new Point(754, 690);
            btnToggleGameMode.Name = "btnToggleGameMode";
            btnToggleGameMode.Size = new Size(140, 50);
            btnToggleGameMode.TabIndex = 16;
            btnToggleGameMode.Text = "Open Game Mode Settings";
            btnToggleGameMode.UseVisualStyleBackColor = true;
            btnToggleGameMode.Click += btnToggleGameMode_Click;
            // 
            // lblGameModeWarning
            // 
            lblGameModeWarning.AutoSize = true;
            lblGameModeWarning.Font = new Font("Segoe UI", 7F);
            lblGameModeWarning.Location = new Point(487, 758);
            lblGameModeWarning.Name = "lblGameModeWarning";
            lblGameModeWarning.Size = new Size(406, 12);
            lblGameModeWarning.TabIndex = 17;
            lblGameModeWarning.Text = "Warning: Game Mode might overwrite your custom core affinity settings and cause stutters.";
            // 
            // lblLogs
            // 
            lblLogs.AutoSize = true;
            lblLogs.Location = new Point(12, 788);
            lblLogs.Name = "lblLogs";
            lblLogs.Size = new Size(32, 15);
            lblLogs.TabIndex = 18;
            lblLogs.Text = "Logs";
            // 
            // lblCorePanel
            // 
            lblCorePanel.AutoSize = true;
            lblCorePanel.Location = new Point(12, 672);
            lblCorePanel.Name = "lblCorePanel";
            lblCorePanel.Size = new Size(107, 15);
            lblCorePanel.TabIndex = 19;
            lblCorePanel.Text = "CPU Load per Core";
            // 
            // lblProcessList
            // 
            lblProcessList.AutoSize = true;
            lblProcessList.Location = new Point(11, 10);
            lblProcessList.Name = "lblProcessList";
            lblProcessList.Size = new Size(68, 15);
            lblProcessList.TabIndex = 20;
            lblProcessList.Text = "Process List";
            // 
            // lblCoreSelection
            // 
            lblCoreSelection.AutoSize = true;
            lblCoreSelection.Location = new Point(900, 10);
            lblCoreSelection.Name = "lblCoreSelection";
            lblCoreSelection.Size = new Size(83, 15);
            lblCoreSelection.TabIndex = 21;
            lblCoreSelection.Text = "Core Selection";
            // 
            // lblVersion
            // 
            lblVersion.AutoSize = true;
            lblVersion.Location = new Point(1121, 900);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new Size(139, 15);
            lblVersion.TabIndex = 22;
            lblVersion.Text = "Application Version: 1.0.1";
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1264, 921);
            Controls.Add(lblVersion);
            Controls.Add(lblCoreSelection);
            Controls.Add(lblProcessList);
            Controls.Add(lblCorePanel);
            Controls.Add(lblLogs);
            Controls.Add(lblGameModeWarning);
            Controls.Add(btnToggleGameMode);
            Controls.Add(lblGameModeStatus);
            Controls.Add(listLog);
            Controls.Add(panelCores);
            Controls.Add(cbAutoApply);
            Controls.Add(cbMinimizeToTray);
            Controls.Add(cbStartWithWindows);
            Controls.Add(btnRemoveProfile);
            Controls.Add(btnSaveProfile);
            Controls.Add(btnRefresh);
            Controls.Add(btnSmtOff);
            Controls.Add(btnNone);
            Controls.Add(btnSelectAll);
            Controls.Add(dgvProcesses);
            Controls.Add(btnApply);
            Controls.Add(listCores);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "MainForm";
            Text = "Process Core Optimizer";
            ((System.ComponentModel.ISupportInitialize)dgvProcesses).EndInit();
            contextMenuStrip1.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private CheckedListBox listCores;
        private Button btnApply;
        private DataGridView dgvProcesses;
        private DataGridViewTextBoxColumn colName;
        private DataGridViewTextBoxColumn colCPU;
        private DataGridViewTextBoxColumn colRAM;
        private DataGridViewTextBoxColumn colID;
        private System.Windows.Forms.Timer refreshTimer;
        private Button btnSelectAll;
        private Button btnNone;
        private Button btnSmtOff;
        private Button btnRefresh;
        private Button btnSaveProfile;
        private Button btnRemoveProfile;
        private CheckBox cbStartWithWindows;
        private CheckBox cbMinimizeToTray;
        private CheckBox cbAutoApply;
        private NotifyIcon trayIcon;
        private GroupBox panelCores;
        private ListBox listLog;
        private ContextMenuStrip contextMenuStrip1;
        private ToolStripMenuItem openToolStripMenuItem;
        private ToolStripMenuItem exitToolStripMenuItem;
        private Label lblGameModeStatus;
        private Button btnToggleGameMode;
        private Label lblGameModeWarning;
        private Label lblLogs;
        private Label lblCorePanel;
        private Label lblProcessList;
        private Label lblCoreSelection;
        private Label lblVersion;
    }
}
