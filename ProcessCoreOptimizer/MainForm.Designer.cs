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
            DataGridViewCellStyle dataGridViewCellStyle4 = new DataGridViewCellStyle();
            DataGridViewCellStyle dataGridViewCellStyle5 = new DataGridViewCellStyle();
            DataGridViewCellStyle dataGridViewCellStyle6 = new DataGridViewCellStyle();
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
            btnSaveProfile = new Button();
            btnRemoveProfile = new Button();
            cbStartWithWindows = new CheckBox();
            cbMinimizeToTray = new CheckBox();
            cbAutoApply = new CheckBox();
            trayIcon = new NotifyIcon(components);
            contextMenuStrip1 = new ContextMenuStrip(components);
            openToolStripMenuItem = new ToolStripMenuItem();
            exitToolStripMenuItem = new ToolStripMenuItem();
            listLog = new ListBox();
            lblGameModeStatus = new Label();
            btnToggleGameMode = new Button();
            lblGameModeWarning = new Label();
            lblLogs = new Label();
            lblCorePanel = new Label();
            lblProcessList = new Label();
            lblCoreSelection = new Label();
            lblVersion = new Label();
            cbCloseToTray = new CheckBox();
            rtbSpecs = new RichTextBox();
            lblPCSpecs = new Label();
            panelCores = new Panel();
            listSavedProfiles = new ListBox();
            ((System.ComponentModel.ISupportInitialize)dgvProcesses).BeginInit();
            contextMenuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // listCores
            // 
            listCores.BackColor = Color.FromArgb(20, 20, 26);
            listCores.BorderStyle = BorderStyle.None;
            listCores.CheckOnClick = true;
            listCores.Font = new Font("Montserrat", 12F, FontStyle.Bold, GraphicsUnit.Point, 238);
            listCores.ForeColor = Color.White;
            listCores.FormattingEnabled = true;
            listCores.IntegralHeight = false;
            listCores.Location = new Point(1029, 34);
            listCores.Margin = new Padding(3, 4, 3, 4);
            listCores.MultiColumn = true;
            listCores.Name = "listCores";
            listCores.Size = new Size(308, 510);
            listCores.TabIndex = 1;
            // 
            // btnApply
            // 
            btnApply.BackColor = Color.FromArgb(74, 137, 243);
            btnApply.FlatAppearance.BorderSize = 0;
            btnApply.FlatStyle = FlatStyle.Flat;
            btnApply.Font = new Font("Montserrat ExtraBold", 16F, FontStyle.Bold);
            btnApply.Location = new Point(1029, 811);
            btnApply.Margin = new Padding(3, 4, 3, 4);
            btnApply.Name = "btnApply";
            btnApply.Size = new Size(308, 117);
            btnApply.TabIndex = 2;
            btnApply.Text = "Set Affinity";
            btnApply.UseVisualStyleBackColor = false;
            btnApply.Click += btnApply_Click;
            // 
            // dgvProcesses
            // 
            dgvProcesses.AllowUserToAddRows = false;
            dgvProcesses.AllowUserToDeleteRows = false;
            dgvProcesses.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvProcesses.BackgroundColor = Color.FromArgb(20, 20, 26);
            dgvProcesses.BorderStyle = BorderStyle.None;
            dgvProcesses.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvProcesses.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            dataGridViewCellStyle4.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle4.BackColor = Color.FromArgb(20, 20, 26);
            dataGridViewCellStyle4.Font = new Font("Montserrat ExtraBold", 8.999999F, FontStyle.Bold, GraphicsUnit.Point, 238);
            dataGridViewCellStyle4.ForeColor = Color.White;
            dataGridViewCellStyle4.SelectionBackColor = Color.FromArgb(20, 20, 26);
            dataGridViewCellStyle4.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle4.WrapMode = DataGridViewTriState.True;
            dgvProcesses.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle4;
            dgvProcesses.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvProcesses.Columns.AddRange(new DataGridViewColumn[] { colName, colCPU, colRAM, colID });
            dataGridViewCellStyle5.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle5.BackColor = Color.FromArgb(11, 11, 15);
            dataGridViewCellStyle5.Font = new Font("Montserrat ExtraBold", 9.749999F, FontStyle.Bold, GraphicsUnit.Point, 238);
            dataGridViewCellStyle5.ForeColor = Color.White;
            dataGridViewCellStyle5.SelectionBackColor = Color.FromArgb(74, 137, 243);
            dataGridViewCellStyle5.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle5.WrapMode = DataGridViewTriState.False;
            dgvProcesses.DefaultCellStyle = dataGridViewCellStyle5;
            dgvProcesses.EnableHeadersVisualStyles = false;
            dgvProcesses.GridColor = Color.FromArgb(35, 35, 42);
            dgvProcesses.ImeMode = ImeMode.On;
            dgvProcesses.Location = new Point(13, 32);
            dgvProcesses.Margin = new Padding(3, 4, 3, 4);
            dgvProcesses.MultiSelect = false;
            dgvProcesses.Name = "dgvProcesses";
            dgvProcesses.ReadOnly = true;
            dataGridViewCellStyle6.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle6.BackColor = SystemColors.Control;
            dataGridViewCellStyle6.Font = new Font("Montserrat ExtraBold", 8.999999F, FontStyle.Bold, GraphicsUnit.Point, 238);
            dataGridViewCellStyle6.ForeColor = SystemColors.WindowText;
            dataGridViewCellStyle6.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle6.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle6.WrapMode = DataGridViewTriState.True;
            dgvProcesses.RowHeadersDefaultCellStyle = dataGridViewCellStyle6;
            dgvProcesses.RowHeadersVisible = false;
            dgvProcesses.RowTemplate.DefaultCellStyle.BackColor = Color.Gray;
            dgvProcesses.RowTemplate.DefaultCellStyle.Font = new Font("Montserrat SemiBold", 8.249999F, FontStyle.Bold, GraphicsUnit.Point, 238);
            dgvProcesses.RowTemplate.DefaultCellStyle.ForeColor = Color.White;
            dgvProcesses.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvProcesses.Size = new Size(1008, 512);
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
            btnSelectAll.BackColor = Color.FromArgb(74, 137, 243);
            btnSelectAll.FlatAppearance.BorderSize = 0;
            btnSelectAll.FlatStyle = FlatStyle.Flat;
            btnSelectAll.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold, GraphicsUnit.Point, 238);
            btnSelectAll.Location = new Point(1344, 34);
            btnSelectAll.Margin = new Padding(3, 4, 3, 4);
            btnSelectAll.Name = "btnSelectAll";
            btnSelectAll.Size = new Size(247, 59);
            btnSelectAll.TabIndex = 4;
            btnSelectAll.Text = "Select All";
            btnSelectAll.UseVisualStyleBackColor = false;
            btnSelectAll.Click += btnSelectAll_Click;
            // 
            // btnNone
            // 
            btnNone.BackColor = Color.FromArgb(74, 137, 243);
            btnNone.FlatAppearance.BorderSize = 0;
            btnNone.FlatStyle = FlatStyle.Flat;
            btnNone.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold);
            btnNone.Location = new Point(1344, 100);
            btnNone.Margin = new Padding(3, 4, 3, 4);
            btnNone.Name = "btnNone";
            btnNone.Size = new Size(247, 59);
            btnNone.TabIndex = 5;
            btnNone.Text = "Clear All";
            btnNone.UseVisualStyleBackColor = false;
            btnNone.Click += btnNone_Click;
            // 
            // btnSmtOff
            // 
            btnSmtOff.BackColor = Color.FromArgb(74, 137, 243);
            btnSmtOff.FlatAppearance.BorderSize = 0;
            btnSmtOff.FlatStyle = FlatStyle.Flat;
            btnSmtOff.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold);
            btnSmtOff.Location = new Point(1344, 166);
            btnSmtOff.Margin = new Padding(3, 4, 3, 4);
            btnSmtOff.Name = "btnSmtOff";
            btnSmtOff.Size = new Size(247, 59);
            btnSmtOff.TabIndex = 6;
            btnSmtOff.Text = "SMT / HT OFF";
            btnSmtOff.UseVisualStyleBackColor = false;
            btnSmtOff.Click += btnSmtOff_Click;
            // 
            // btnSaveProfile
            // 
            btnSaveProfile.BackColor = Color.FromArgb(74, 137, 243);
            btnSaveProfile.FlatAppearance.BorderSize = 0;
            btnSaveProfile.FlatStyle = FlatStyle.Flat;
            btnSaveProfile.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold);
            btnSaveProfile.Location = new Point(1344, 561);
            btnSaveProfile.Margin = new Padding(3, 4, 3, 4);
            btnSaveProfile.Name = "btnSaveProfile";
            btnSaveProfile.Size = new Size(247, 59);
            btnSaveProfile.TabIndex = 8;
            btnSaveProfile.Text = "Save Profile";
            btnSaveProfile.UseVisualStyleBackColor = false;
            btnSaveProfile.Click += btnSaveProfile_Click;
            // 
            // btnRemoveProfile
            // 
            btnRemoveProfile.BackColor = Color.FromArgb(74, 137, 243);
            btnRemoveProfile.FlatAppearance.BorderSize = 0;
            btnRemoveProfile.FlatStyle = FlatStyle.Flat;
            btnRemoveProfile.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold);
            btnRemoveProfile.Location = new Point(1060, 561);
            btnRemoveProfile.Margin = new Padding(3, 4, 3, 4);
            btnRemoveProfile.Name = "btnRemoveProfile";
            btnRemoveProfile.Size = new Size(247, 59);
            btnRemoveProfile.TabIndex = 9;
            btnRemoveProfile.Text = "Remove Profile";
            btnRemoveProfile.UseVisualStyleBackColor = false;
            btnRemoveProfile.Click += btnRemoveProfile_Click;
            // 
            // cbStartWithWindows
            // 
            cbStartWithWindows.AutoSize = true;
            cbStartWithWindows.Font = new Font("Montserrat", 12F, FontStyle.Regular, GraphicsUnit.Point, 238);
            cbStartWithWindows.Location = new Point(1404, 811);
            cbStartWithWindows.Margin = new Padding(3, 4, 3, 4);
            cbStartWithWindows.Name = "cbStartWithWindows";
            cbStartWithWindows.Size = new Size(187, 29);
            cbStartWithWindows.TabIndex = 10;
            cbStartWithWindows.Text = "Start with Windows";
            cbStartWithWindows.UseVisualStyleBackColor = true;
            cbStartWithWindows.CheckedChanged += cbStartWithWindows_CheckedChanged;
            // 
            // cbMinimizeToTray
            // 
            cbMinimizeToTray.AutoSize = true;
            cbMinimizeToTray.Font = new Font("Montserrat", 12F, FontStyle.Regular, GraphicsUnit.Point, 238);
            cbMinimizeToTray.Location = new Point(1404, 840);
            cbMinimizeToTray.Margin = new Padding(3, 4, 3, 4);
            cbMinimizeToTray.Name = "cbMinimizeToTray";
            cbMinimizeToTray.Size = new Size(160, 29);
            cbMinimizeToTray.TabIndex = 11;
            cbMinimizeToTray.Text = "Minimize to Tray";
            cbMinimizeToTray.UseVisualStyleBackColor = true;
            cbMinimizeToTray.CheckedChanged += cbMinimizeToTray_CheckedChanged;
            // 
            // cbAutoApply
            // 
            cbAutoApply.AutoSize = true;
            cbAutoApply.Font = new Font("Montserrat", 12F, FontStyle.Regular, GraphicsUnit.Point, 238);
            cbAutoApply.Location = new Point(1404, 899);
            cbAutoApply.Margin = new Padding(3, 4, 3, 4);
            cbAutoApply.Name = "cbAutoApply";
            cbAutoApply.Size = new Size(184, 29);
            cbAutoApply.TabIndex = 12;
            cbAutoApply.Text = "Auto-Apply Profiles";
            cbAutoApply.UseVisualStyleBackColor = true;
            cbAutoApply.CheckedChanged += cbAutoApply_CheckedChanged;
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
            // listLog
            // 
            listLog.BackColor = Color.FromArgb(20, 20, 26);
            listLog.BorderStyle = BorderStyle.None;
            listLog.Font = new Font("Montserrat SemiBold", 8.999999F, FontStyle.Bold, GraphicsUnit.Point, 238);
            listLog.ForeColor = Color.White;
            listLog.FormattingEnabled = true;
            listLog.Location = new Point(5, 771);
            listLog.Margin = new Padding(3, 4, 3, 4);
            listLog.Name = "listLog";
            listLog.Size = new Size(522, 108);
            listLog.TabIndex = 14;
            // 
            // lblGameModeStatus
            // 
            lblGameModeStatus.AutoSize = true;
            lblGameModeStatus.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold, GraphicsUnit.Point, 238);
            lblGameModeStatus.Location = new Point(546, 611);
            lblGameModeStatus.Name = "lblGameModeStatus";
            lblGameModeStatus.Size = new Size(219, 25);
            lblGameModeStatus.TabIndex = 15;
            lblGameModeStatus.Text = "Game Mode: Checking...";
            // 
            // btnToggleGameMode
            // 
            btnToggleGameMode.BackColor = Color.FromArgb(74, 137, 243);
            btnToggleGameMode.FlatAppearance.BorderSize = 0;
            btnToggleGameMode.FlatStyle = FlatStyle.Flat;
            btnToggleGameMode.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold);
            btnToggleGameMode.Location = new Point(546, 661);
            btnToggleGameMode.Margin = new Padding(3, 4, 3, 4);
            btnToggleGameMode.Name = "btnToggleGameMode";
            btnToggleGameMode.Size = new Size(243, 60);
            btnToggleGameMode.TabIndex = 16;
            btnToggleGameMode.Text = "Open settings";
            btnToggleGameMode.UseVisualStyleBackColor = false;
            btnToggleGameMode.Click += btnToggleGameMode_Click;
            // 
            // lblGameModeWarning
            // 
            lblGameModeWarning.AutoSize = true;
            lblGameModeWarning.Font = new Font("Montserrat ExtraBold", 6.75F, FontStyle.Bold, GraphicsUnit.Point, 238);
            lblGameModeWarning.Location = new Point(546, 725);
            lblGameModeWarning.Name = "lblGameModeWarning";
            lblGameModeWarning.Size = new Size(459, 14);
            lblGameModeWarning.TabIndex = 17;
            lblGameModeWarning.Text = "Warning: Game Mode might overwrite your custom core affinity settings and cause stutters.";
            // 
            // lblLogs
            // 
            lblLogs.AutoSize = true;
            lblLogs.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold);
            lblLogs.ForeColor = Color.FromArgb(50, 100, 180);
            lblLogs.Location = new Point(6, 739);
            lblLogs.Name = "lblLogs";
            lblLogs.Size = new Size(58, 25);
            lblLogs.TabIndex = 18;
            lblLogs.Text = "LOGS";
            // 
            // lblCorePanel
            // 
            lblCorePanel.AutoSize = true;
            lblCorePanel.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold);
            lblCorePanel.ForeColor = Color.FromArgb(50, 100, 180);
            lblCorePanel.Location = new Point(6, 561);
            lblCorePanel.Name = "lblCorePanel";
            lblCorePanel.Size = new Size(197, 25);
            lblCorePanel.TabIndex = 19;
            lblCorePanel.Text = "CPU LOAD PER CORE";
            // 
            // lblProcessList
            // 
            lblProcessList.AutoSize = true;
            lblProcessList.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold);
            lblProcessList.ForeColor = Color.FromArgb(50, 100, 180);
            lblProcessList.Location = new Point(13, 8);
            lblProcessList.Name = "lblProcessList";
            lblProcessList.Size = new Size(133, 25);
            lblProcessList.TabIndex = 20;
            lblProcessList.Text = "PROCESS LIST";
            // 
            // lblCoreSelection
            // 
            lblCoreSelection.AutoSize = true;
            lblCoreSelection.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold);
            lblCoreSelection.ForeColor = Color.FromArgb(50, 100, 180);
            lblCoreSelection.Location = new Point(1029, 8);
            lblCoreSelection.Name = "lblCoreSelection";
            lblCoreSelection.Size = new Size(161, 25);
            lblCoreSelection.TabIndex = 21;
            lblCoreSelection.Text = "CORE SELECTION";
            // 
            // lblVersion
            // 
            lblVersion.AutoSize = true;
            lblVersion.Font = new Font("Montserrat", 8.999999F, FontStyle.Regular, GraphicsUnit.Point, 238);
            lblVersion.Location = new Point(5, 934);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new Size(0, 18);
            lblVersion.TabIndex = 22;
            // 
            // cbCloseToTray
            // 
            cbCloseToTray.AutoSize = true;
            cbCloseToTray.Font = new Font("Montserrat", 12F, FontStyle.Regular, GraphicsUnit.Point, 238);
            cbCloseToTray.Location = new Point(1404, 871);
            cbCloseToTray.Margin = new Padding(3, 4, 3, 4);
            cbCloseToTray.Name = "cbCloseToTray";
            cbCloseToTray.Size = new Size(130, 29);
            cbCloseToTray.TabIndex = 23;
            cbCloseToTray.Text = "Close to Tray";
            cbCloseToTray.UseVisualStyleBackColor = true;
            cbCloseToTray.CheckedChanged += cbCloseToTray_CheckedChanged;
            // 
            // rtbSpecs
            // 
            rtbSpecs.BackColor = Color.FromArgb(20, 20, 26);
            rtbSpecs.BorderStyle = BorderStyle.None;
            rtbSpecs.Font = new Font("Montserrat", 9.749999F, FontStyle.Regular, GraphicsUnit.Point, 238);
            rtbSpecs.ForeColor = Color.White;
            rtbSpecs.Location = new Point(546, 771);
            rtbSpecs.Margin = new Padding(3, 4, 3, 4);
            rtbSpecs.Name = "rtbSpecs";
            rtbSpecs.Size = new Size(467, 108);
            rtbSpecs.TabIndex = 24;
            rtbSpecs.Text = "";
            // 
            // lblPCSpecs
            // 
            lblPCSpecs.AutoSize = true;
            lblPCSpecs.Font = new Font("Montserrat ExtraBold", 12F, FontStyle.Bold);
            lblPCSpecs.ForeColor = Color.FromArgb(50, 100, 180);
            lblPCSpecs.Location = new Point(546, 744);
            lblPCSpecs.Name = "lblPCSpecs";
            lblPCSpecs.Size = new Size(96, 25);
            lblPCSpecs.TabIndex = 25;
            lblPCSpecs.Text = "PC SPECS";
            // 
            // panelCores
            // 
            panelCores.BackColor = Color.FromArgb(25, 25, 30);
            panelCores.Location = new Point(6, 589);
            panelCores.Name = "panelCores";
            panelCores.Size = new Size(521, 147);
            panelCores.TabIndex = 26;
            // 
            // listSavedProfiles
            // 
            listSavedProfiles.BackColor = Color.FromArgb(25, 25, 30);
            listSavedProfiles.BorderStyle = BorderStyle.None;
            listSavedProfiles.Font = new Font("Montserrat SemiBold", 8.999999F, FontStyle.Bold, GraphicsUnit.Point, 238);
            listSavedProfiles.ForeColor = Color.White;
            listSavedProfiles.FormattingEnabled = true;
            listSavedProfiles.Location = new Point(1029, 632);
            listSavedProfiles.Name = "listSavedProfiles";
            listSavedProfiles.Size = new Size(564, 162);
            listSavedProfiles.TabIndex = 27;
            listSavedProfiles.SelectedIndexChanged += listSavedProfiles_SelectedIndexChanged;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(8F, 18F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(35, 35, 40);
            ClientSize = new Size(1605, 961);
            Controls.Add(listSavedProfiles);
            Controls.Add(panelCores);
            Controls.Add(lblPCSpecs);
            Controls.Add(rtbSpecs);
            Controls.Add(cbCloseToTray);
            Controls.Add(lblVersion);
            Controls.Add(lblCoreSelection);
            Controls.Add(lblProcessList);
            Controls.Add(lblCorePanel);
            Controls.Add(lblLogs);
            Controls.Add(lblGameModeWarning);
            Controls.Add(btnToggleGameMode);
            Controls.Add(lblGameModeStatus);
            Controls.Add(listLog);
            Controls.Add(cbAutoApply);
            Controls.Add(cbMinimizeToTray);
            Controls.Add(cbStartWithWindows);
            Controls.Add(btnRemoveProfile);
            Controls.Add(btnSaveProfile);
            Controls.Add(btnSmtOff);
            Controls.Add(btnNone);
            Controls.Add(btnSelectAll);
            Controls.Add(dgvProcesses);
            Controls.Add(btnApply);
            Controls.Add(listCores);
            Font = new Font("Montserrat", 9F);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Margin = new Padding(3, 4, 3, 4);
            MaximizeBox = false;
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
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
        private Button btnSaveProfile;
        private Button btnRemoveProfile;
        private CheckBox cbStartWithWindows;
        private CheckBox cbMinimizeToTray;
        private CheckBox cbAutoApply;
        private NotifyIcon trayIcon;
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
        private CheckBox cbCloseToTray;
        private RichTextBox rtbSpecs;
        private Label lblPCSpecs;
        private Panel panelCores;
        private ListBox listSavedProfiles;
    }
}
