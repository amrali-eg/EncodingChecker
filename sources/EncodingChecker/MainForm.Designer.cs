namespace EncodingChecker;

partial class MainForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
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
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        System.Windows.Forms.Label lblBaseDirectory;
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
        System.Windows.Forms.Label lblFileMasks;
        System.Windows.Forms.Label lblValidCharsets;
        System.Windows.Forms.ColumnHeader colEncoding;
        System.Windows.Forms.ColumnHeader colFileName;
        System.Windows.Forms.ColumnHeader colFileExt;
        System.Windows.Forms.ColumnHeader colDirectory;
        btnBrowseDirectories = new System.Windows.Forms.Button();
        chkIncludeSubdirectories = new System.Windows.Forms.CheckBox();
        txtFileMasks = new System.Windows.Forms.TextBox();
        lstValidCharsets = new System.Windows.Forms.CheckedListBox();
        btnValidate = new System.Windows.Forms.Button();
        lstResults = new System.Windows.Forms.ListView();
        imgsResults = new System.Windows.Forms.ImageList(components);
        dlgBrowseDirectories = new System.Windows.Forms.FolderBrowserDialog();
        statusBar = new System.Windows.Forms.StatusStrip();
        tlnkHelp = new System.Windows.Forms.ToolStripStatusLabel();
        tlnkAbout = new System.Windows.Forms.ToolStripStatusLabel();
        btnExportReport = new System.Windows.Forms.ToolStripDropDownButton();
        actionProgress = new System.Windows.Forms.ToolStripProgressBar();
        actionStatus = new System.Windows.Forms.ToolStripStatusLabel();
        btnView = new System.Windows.Forms.Button();
        lstBaseDirectory = new System.Windows.Forms.ComboBox();
        lblConvert = new System.Windows.Forms.Label();
        lstConvert = new System.Windows.Forms.ComboBox();
        btnConvert = new System.Windows.Forms.Button();
        chkSelectDeselectAll = new System.Windows.Forms.CheckBox();
        chkCreateBackup = new System.Windows.Forms.CheckBox();
        chkPreviewChanges = new System.Windows.Forms.CheckBox();
        btnCancel = new System.Windows.Forms.Button();
        lblBaseDirectory = new System.Windows.Forms.Label();
        lblFileMasks = new System.Windows.Forms.Label();
        lblValidCharsets = new System.Windows.Forms.Label();
        colEncoding = new System.Windows.Forms.ColumnHeader();
        colFileName = new System.Windows.Forms.ColumnHeader();
        colFileExt = new System.Windows.Forms.ColumnHeader();
        colDirectory = new System.Windows.Forms.ColumnHeader();
        statusBar.SuspendLayout();
        SuspendLayout();
        // 
        // lblBaseDirectory
        // 
        resources.ApplyResources(lblBaseDirectory, "lblBaseDirectory");
        lblBaseDirectory.Name = "lblBaseDirectory";
        // 
        // lblFileMasks
        // 
        resources.ApplyResources(lblFileMasks, "lblFileMasks");
        lblFileMasks.Name = "lblFileMasks";
        // 
        // lblValidCharsets
        // 
        resources.ApplyResources(lblValidCharsets, "lblValidCharsets");
        lblValidCharsets.Name = "lblValidCharsets";
        // 
        // colEncoding
        // 
        resources.ApplyResources(colEncoding, "colEncoding");
        // 
        // colFileName
        // 
        resources.ApplyResources(colFileName, "colFileName");
        // 
        // colFileExt
        // 
        resources.ApplyResources(colFileExt, "colFileExt");
        // 
        // colDirectory
        // 
        resources.ApplyResources(colDirectory, "colDirectory");
        // 
        // btnBrowseDirectories
        // 
        resources.ApplyResources(btnBrowseDirectories, "btnBrowseDirectories");
        btnBrowseDirectories.Name = "btnBrowseDirectories";
        btnBrowseDirectories.UseVisualStyleBackColor = true;
        btnBrowseDirectories.Click += OnBrowseDirectories;
        // 
        // chkIncludeSubdirectories
        // 
        resources.ApplyResources(chkIncludeSubdirectories, "chkIncludeSubdirectories");
        chkIncludeSubdirectories.Name = "chkIncludeSubdirectories";
        chkIncludeSubdirectories.UseVisualStyleBackColor = true;
        // 
        // txtFileMasks
        // 
        txtFileMasks.AcceptsReturn = true;
        resources.ApplyResources(txtFileMasks, "txtFileMasks");
        txtFileMasks.Name = "txtFileMasks";
        // 
        // lstValidCharsets
        // 
        lstValidCharsets.CheckOnClick = true;
        lstValidCharsets.FormattingEnabled = true;
        resources.ApplyResources(lstValidCharsets, "lstValidCharsets");
        lstValidCharsets.Name = "lstValidCharsets";
        // 
        // btnValidate
        // 
        resources.ApplyResources(btnValidate, "btnValidate");
        btnValidate.Name = "btnValidate";
        btnValidate.UseVisualStyleBackColor = true;
        btnValidate.Click += OnAction;
        // 
        // lstResults
        // 
        resources.ApplyResources(lstResults, "lstResults");
        lstResults.CheckBoxes = true;
        lstResults.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { colEncoding, colFileName, colFileExt, colDirectory });
        lstResults.FullRowSelect = true;
        lstResults.GridLines = true;
        lstResults.Name = "lstResults";
        lstResults.SmallImageList = imgsResults;
        lstResults.UseCompatibleStateImageBehavior = false;
        lstResults.View = System.Windows.Forms.View.Details;
        lstResults.ColumnClick += OnResultColumnClick;
        lstResults.ItemChecked += OnResultItemChecked;
        // 
        // imgsResults
        // 
        imgsResults.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
        imgsResults.ImageStream = (System.Windows.Forms.ImageListStreamer)resources.GetObject("imgsResults.ImageStream");
        imgsResults.TransparentColor = System.Drawing.Color.Transparent;
        imgsResults.Images.SetKeyName(0, "Successful");
        imgsResults.Images.SetKeyName(1, "Failed");
        imgsResults.Images.SetKeyName(2, "Warning");
        // 
        // dlgBrowseDirectories
        // 
        resources.ApplyResources(dlgBrowseDirectories, "dlgBrowseDirectories");
        // 
        // statusBar
        // 
        statusBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { tlnkHelp, tlnkAbout, btnExportReport, actionProgress, actionStatus });
        resources.ApplyResources(statusBar, "statusBar");
        statusBar.Name = "statusBar";
        // 
        // tlnkHelp
        // 
        tlnkHelp.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
        tlnkHelp.IsLink = true;
        tlnkHelp.Name = "tlnkHelp";
        resources.ApplyResources(tlnkHelp, "tlnkHelp");
        tlnkHelp.Click += OnHelp;
        // 
        // tlnkAbout
        // 
        tlnkAbout.IsLink = true;
        tlnkAbout.Name = "tlnkAbout";
        resources.ApplyResources(tlnkAbout, "tlnkAbout");
        tlnkAbout.Click += OnAbout;
        // 
        // btnExportReport
        // 
        btnExportReport.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
        resources.ApplyResources(btnExportReport, "btnExportReport");
        btnExportReport.AutoSize = true;
        btnExportReport.ForeColor = System.Drawing.Color.Blue;
        btnExportReport.Name = "btnExportReport";
        btnExportReport.ShowDropDownArrow = true;
        // 
        // actionProgress
        // 
        actionProgress.Name = "actionProgress";
        resources.ApplyResources(actionProgress, "actionProgress");
        actionProgress.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
        // 
        // actionStatus
        // 
        actionStatus.Name = "actionStatus";
        resources.ApplyResources(actionStatus, "actionStatus");
        // 
        // btnView
        // 
        resources.ApplyResources(btnView, "btnView");
        btnView.Name = "btnView";
        btnView.UseVisualStyleBackColor = true;
        btnView.Click += OnAction;
        // 
        // lstBaseDirectory
        // 
        lstBaseDirectory.AllowDrop = true;
        resources.ApplyResources(lstBaseDirectory, "lstBaseDirectory");
        lstBaseDirectory.FormattingEnabled = true;
        lstBaseDirectory.Name = "lstBaseDirectory";
        lstBaseDirectory.DragDrop += OnBaseDirectoryDragDrop;
        lstBaseDirectory.DragEnter += OnBaseDirectoryDragEnter;
        // 
        // lblConvert
        // 
        resources.ApplyResources(lblConvert, "lblConvert");
        lblConvert.Name = "lblConvert";
        // 
        // lstConvert
        // 
        lstConvert.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        resources.ApplyResources(lstConvert, "lstConvert");
        lstConvert.FormattingEnabled = true;
        lstConvert.Name = "lstConvert";
        // 
        // btnConvert
        // 
        resources.ApplyResources(btnConvert, "btnConvert");
        btnConvert.Name = "btnConvert";
        btnConvert.UseVisualStyleBackColor = true;
        btnConvert.Click += OnConvert;
        // 
        // chkSelectDeselectAll
        // 
        resources.ApplyResources(chkSelectDeselectAll, "chkSelectDeselectAll");
        chkSelectDeselectAll.Name = "chkSelectDeselectAll";
        chkSelectDeselectAll.UseVisualStyleBackColor = true;
        chkSelectDeselectAll.CheckedChanged += OnSelectDeselectAll;
        // 
        // chkCreateBackup
        // 
        resources.ApplyResources(chkCreateBackup, "chkCreateBackup");
        chkCreateBackup.Name = "chkCreateBackup";
        chkCreateBackup.UseVisualStyleBackColor = true;
        // 
        // chkPreviewChanges
        // 
        resources.ApplyResources(chkPreviewChanges, "chkPreviewChanges");
        chkPreviewChanges.Name = "chkPreviewChanges";
        chkPreviewChanges.UseVisualStyleBackColor = true;
        // 
        // btnCancel
        // 
        resources.ApplyResources(btnCancel, "btnCancel");
        btnCancel.Name = "btnCancel";
        btnCancel.UseVisualStyleBackColor = true;
        btnCancel.Click += OnCancelAction;
        // 
        // MainForm
        // 
        AllowDrop = true;
        resources.ApplyResources(this, "$this");
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        Controls.Add(chkCreateBackup);
        Controls.Add(chkPreviewChanges);
        Controls.Add(btnCancel);
        Controls.Add(chkSelectDeselectAll);
        Controls.Add(btnConvert);
        Controls.Add(lstConvert);
        Controls.Add(lblConvert);
        Controls.Add(lstBaseDirectory);
        Controls.Add(btnView);
        Controls.Add(statusBar);
        Controls.Add(lstResults);
        Controls.Add(btnValidate);
        Controls.Add(lstValidCharsets);
        Controls.Add(lblValidCharsets);
        Controls.Add(txtFileMasks);
        Controls.Add(lblFileMasks);
        Controls.Add(chkIncludeSubdirectories);
        Controls.Add(btnBrowseDirectories);
        Controls.Add(lblBaseDirectory);
        DoubleBuffered = true;
        Name = "MainForm";
        FormClosing += OnFormClosing;
        Load += OnFormLoad;
        DragDrop += OnBaseDirectoryDragDrop;
        DragEnter += OnBaseDirectoryDragEnter;
        statusBar.ResumeLayout(false);
        statusBar.PerformLayout();
        ResumeLayout(false);
        PerformLayout();

    }

    #endregion

    private System.Windows.Forms.Button btnBrowseDirectories;
    private System.Windows.Forms.CheckBox chkIncludeSubdirectories;
    private System.Windows.Forms.TextBox txtFileMasks;
    private System.Windows.Forms.CheckedListBox lstValidCharsets;
    private System.Windows.Forms.Button btnValidate;
    private System.Windows.Forms.ListView lstResults;
    private System.Windows.Forms.FolderBrowserDialog dlgBrowseDirectories;
    private System.Windows.Forms.ToolStripProgressBar actionProgress;
    private System.Windows.Forms.ToolStripStatusLabel actionStatus;
    private System.Windows.Forms.StatusStrip statusBar;
    private System.Windows.Forms.Button btnView;
    private System.Windows.Forms.ComboBox lstBaseDirectory;
    private System.Windows.Forms.Label lblConvert;
    private System.Windows.Forms.ComboBox lstConvert;
    private System.Windows.Forms.Button btnConvert;
    private System.Windows.Forms.CheckBox chkSelectDeselectAll;
    private System.Windows.Forms.ToolStripStatusLabel tlnkAbout;
    private System.Windows.Forms.Button btnCancel;
    private System.Windows.Forms.ImageList imgsResults;
    private System.Windows.Forms.ToolStripStatusLabel tlnkHelp;
    private System.Windows.Forms.ToolStripDropDownButton btnExportReport;
    private System.Windows.Forms.CheckBox chkCreateBackup;
    private System.Windows.Forms.CheckBox chkPreviewChanges;
}

