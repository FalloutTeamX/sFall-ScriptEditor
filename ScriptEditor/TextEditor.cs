using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

using ScriptEditor.CodeTranslation;
using ScriptEditor.TextEditorUI;

using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Document;

using ICSharpCode.ClassDiagram;

namespace ScriptEditor
{
    partial class TextEditor : Form
    {
        private const string SSE = "Sfall Script Editor - ";
        private const string parseoff = "Parser: Disabled";
        private const string unsaved = "unsaved.ssl";
        private static readonly List<string> TREEPROCEDURES = new List<string>{ "Global Procedures", "Local Procedures" };
        private static readonly List<string> TREEVARIABLES = new List<string>{ "Global Variables", "Script Variables" };
        private static readonly System.Media.SoundPlayer DontFind = new System.Media.SoundPlayer(Properties.Resources.DontFind);
        private static readonly System.Media.SoundPlayer CompileFail = new System.Media.SoundPlayer(Properties.Resources.CompileError);

        private DateTime timerNext, timer2Next;
        private Timer timer, timer2;
        private readonly List<TabInfo> tabs = new List<TabInfo>();
        private TabInfo currentTab;
        private ToolStripLabel parserLabel;
        public static volatile bool parserRunning;

        private SearchForm sf;
        private GoToLine goToLine;
        private int previousTabIndex = -1;
        private int minimizelogsize;
        private PositionType PosChangeType;
        private int moveActive = -1;
        private int fuctionPanel = -1;
        private FormWindowState wState;
        private readonly string[] commandsArgs;
        private bool lbAutocompleteShiftCaret;
        private bool SplitEvent;
        internal static bool ParsingErrors = true;

        private TreeView VarTree = new TreeView();
        private TabPage VarTab = new TabPage("Variables");

#region Main form control
        public TextEditor(string[] args)
        {
            InitializeComponent();
            commandsArgs = args;
            Settings.SetupWindowPosition(SavedWindows.Main, this);
            if (!Settings.firstRun) WindowState = FormWindowState.Maximized;
            pDefineStripComboBox.Items.AddRange(File.ReadAllLines(Settings.PreprocDefPath));
            pDefineStripComboBox.Text = Settings.preprocDef;
            SearchTextComboBox.Items.AddRange(File.ReadAllLines(Settings.SearchHistoryPath));
            SearchToolStrip.Visible = false;
            defineToolStripMenuItem.Checked = Settings.allowDefine;
            msgAutoOpenEditorStripMenuItem.Checked = Settings.openMsgEditor;
            if (Settings.encoding == (byte)EncodingType.OEM866) {
                EncodingDOSmenuItem.Checked = true;
                windowsDefaultMenuItem.Checked = false;
            }
            // highlighting
            FileSyntaxModeProvider fsmProvider = new FileSyntaxModeProvider(SyntaxFile.SyntaxFolder); // Create new provider with the highlighting directory.
            HighlightingManager.Manager.AddSyntaxModeFileProvider(fsmProvider); // Attach to the text editor.
            // folding timer
            timer = new Timer();
            timer.Interval = 100;
            timer.Tick += new EventHandler(timer_Tick);
            timer2 = new Timer();
            timer2.Interval = 10;
            timer2.Tick += new EventHandler(timer2_Tick);
            // Recent files
            UpdateRecentList();
            // Templates
            foreach (string file in Directory.GetFiles(Path.Combine(Settings.ResourcesFolder, "templates"), "*.ssl")) {
                ToolStripMenuItem mi = new ToolStripMenuItem(Path.GetFileNameWithoutExtension(file));
                mi.Tag = file;
                mi.Click += new EventHandler(Template_Click); // Open Templates file
                New_toolStripDropDownButton.DropDownItems.Add(mi);
            }
            // Parser
            parserLabel = new ToolStripLabel((Settings.enableParser) ? "Parser: No file" : parseoff);
            parserLabel.Alignment = ToolStripItemAlignment.Right;
            parserLabel.Click += delegate(object sender, EventArgs e) { ParseScript(); };
            parserLabel.ToolTipText = "Click - Run update parser info.";
            parserLabel.TextChanged += delegate(object sender, EventArgs e) { parserLabel.ForeColor = Color.Black; };
            ToolStrip.Items.Add(parserLabel);
            tabControl1.tabsSwapped += delegate(object sender, TabsSwappedEventArgs e) {
                TabInfo tmp = tabs[e.aIndex];
                tabs[e.aIndex] = tabs[e.bIndex];
                tabs[e.aIndex].index = e.aIndex;
                tabs[e.bIndex] = tmp;
                tabs[e.bIndex].index = e.bIndex;
            };
            //Create Variable Tab
            VarTree.ShowNodeToolTips = true;
            VarTree.ShowRootLines = false;
            VarTree.Indent = 16;
            VarTree.ItemHeight = 14;
            VarTree.AfterSelect += TreeView_AfterSelect;
            VarTree.AfterCollapse += AfterCollapse;
            VarTree.Dock = DockStyle.Fill;
            VarTree.BackColor = Color.FromArgb(250, 250, 255);
            VarTab.Padding = new Padding(0, 2, 2, 2);
            VarTab.BackColor = SystemColors.ControlLightLight;
            VarTab.Controls.Add(VarTree);
            ProcTree.AfterCollapse += AfterCollapse;
            if (Settings.PathScriptsHFile == null) {
                Headers_toolStripSplitButton.Enabled = false;
            }
            HandlerProcedure.CreateProcHandlers(ProcMnContext, this);
            Functions.CreateTree(FunctionsTree);
            ProgramInfo.LoadOpcodes();
            DEBUGINFO("***** Sfall Script Editor v." + Application.ProductVersion + " *****");
        }

#if !DEBUG
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == SingleInstanceManager.WM_SFALL_SCRIPT_EDITOR_OPEN) {
                TabInfo result = null;
                var commandLineArgs = SingleInstanceManager.LoadCommandLine();
                foreach (var file in commandLineArgs) {
                    result = Open(file, OpenType.File, true, false, false, true, true);
                }
                if (result != null) ShowMe();
            }
            base.WndProc(ref m);
        }

        // activate form only for open ssl file
        private void ShowMe()
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = wState;
            Activate();
            // get our current "TopMost" value (ours will always be false though)
            //bool top = TopMost;
            // make our form jump to the top of everything
            //TopMost = true;
            // set it back to whatever it was
            //TopMost = top;
        }
#endif

        void DEBUGINFO(string line) { tbOutput.Text = line + "\r\n" + tbOutput.Text; }

        private void TextEditor_Load(object sender, EventArgs e)
        {
            splitContainer3.Panel1Collapsed = true;
            splitContainer2.Panel2Collapsed = true;
            splitContainer1.Panel2Collapsed = true;
            splitContainer2.Panel1MinSize = 300;
            splitContainer2.Panel2MinSize = 150;
            splitContainer1.SplitterDistance = Size.Height;
            if (Settings.editorSplitterPosition == -1) {
                minimizelogsize = Size.Height - (Size.Height / 5);
            } else minimizelogsize = Settings.editorSplitterPosition;
            if (Settings.editorSplitterPosition2 != -1) {
                splitContainer2.SplitterDistance = Settings.editorSplitterPosition2;
            } else splitContainer2.SplitterDistance = Size.Width - 200;
            showLogWindowToolStripMenuItem.Checked = Settings.showLog;
            if (Settings.enableParser) CreateTabVarTree();
        }

        private void TextEditor_Shown(object sender, EventArgs e)
        {
            if (!Settings.firstRun) Settings_ToolStripMenuItem.PerformClick();
            // open documents passed from command line
            foreach (string s in commandsArgs) {
                Open(s, TextEditor.OpenType.File, true, false, false, true, true);
            }
            this.Activated += TextEditor_Activated;
            this.Deactivate += TextEditor_Deactivate;
        }

        private void TextEditor_Resize(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized) wState = WindowState;
        }

        private void TextEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            for (int i = 0; i < tabs.Count; i++) {
                if (tabs[i].changed) {
                    switch (MessageBox.Show("Save changes to " + tabs[i].filename + "?", "Message", MessageBoxButtons.YesNoCancel)) {
                        case DialogResult.Yes:
                            Save(tabs[i]);
                            if (tabs[i].changed) {
                                e.Cancel = true;
                                return;
                            }
                            break;
                        case DialogResult.No:
                            break;
                        default:
                            e.Cancel = true;
                            return;
                    }
                }
            }
            if (bwSyntaxParser.IsBusy) {
                e.Cancel = true;
                return;
            }
            if (sf != null) sf.Close();
            splitContainer3.Panel1Collapsed = true;
            Settings.editorSplitterPosition2 = splitContainer2.SplitterDistance;
            Settings.SaveSettingData(this);
            SyntaxFile.DeleteSyntaxFile();
        }
#endregion

        private void UpdateRecentList()
        { 
            string[] items = Settings.GetRecent();
            int count = Open_toolStripSplitButton.DropDownItems.Count-1;
            for (int i = 3; i <= count; i++) {
                Open_toolStripSplitButton.DropDownItems.RemoveAt(3);
            }
            for (int i = items.Length - 1; i >= 0; i--) {
                Open_toolStripSplitButton.DropDownItems.Add(items[i], null, recentItem_Click);
            }
        }

        public enum OpenType { None, File, Text }

        public TabInfo Open(string file, OpenType type, bool addToMRU = true, bool alwaysNew = false, bool recent = false, bool seltab = true, bool commandline = false)
        {
            if (type == OpenType.File) {
                if (!Path.IsPathRooted(file)) {
                    file = Path.GetFullPath(file);
                }
                if (commandline && Path.GetExtension(file).ToLower() == ".msg") {
                    if (currentTab == null) wState = FormWindowState.Minimized;
                    MessageEditor.MessageEditorOpen(file, this);
                    return null;
                }
                // Check file
                bool Exists;
                if (!FileAssociation.CheckFileAllow(file, out Exists)) return null;
                //Add this file to the recent files list
                if (addToMRU) {
                    if (!Exists && recent && MessageBox.Show("This recent file not found. Delete recent link to file?", "Open file error", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        recent = true;
                    else
                        recent = false; // don't delete file link from recent list
                    Settings.AddRecentFile(file, recent);
                    UpdateRecentList();
                }
                if (!Exists) return null;
                //If this is an int, decompile
                if (string.Compare(Path.GetExtension(file), ".int", true) == 0) {
                    var compiler = new Compiler();
                    string decomp = compiler.Decompile(file);
                    if (decomp == null) {
                        MessageBox.Show("Decompilation of '" + file + "' was not successful", "Error");
                        return null;
                    } else file = decomp;
                } else {
                    //Check if the file is already open
                    for (int i = 0; i < tabs.Count; i++) {
                        if (string.Compare(tabs[i].filepath, file, true) == 0) {
                            if (seltab) tabControl1.SelectTab(i);
                            if (MessageBox.Show("This file is already open!\nDo you want to open another one same file?", "Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) 
                                return tabs[i];
                        }
                    }
                }
            }
            //Create the text editor and set up the tab
            ICSharpCode.TextEditor.TextEditorControl te = new ICSharpCode.TextEditor.TextEditorControl();
            te.AllowCaretBeyondEOL = true;
            te.LineViewerStyle = LineViewerStyle.FullRow;
            te.ShowVRuler = true;
            te.VRulerRow = Settings.tabSize;
            te.Document.FoldingManager.FoldingStrategy = new CodeFolder();
            te.IndentStyle = IndentStyle.Smart;
            te.ConvertTabsToSpaces = Settings.tabsToSpaces;
            te.TabIndent = Settings.tabSize;
            te.Document.TextEditorProperties.IndentationSize = Settings.tabSize;
            if (type == OpenType.File && string.Compare(Path.GetExtension(file), ".msg", true) == 0) {
                if (Settings.encoding == (byte)EncodingType.OEM866) te.Document.TextEditorProperties.Encoding = Encoding.GetEncoding("cp866");
                te.SetHighlighting("Message");
            } else
                te.SetHighlighting((Settings.highlight == 0) ? "Original" : "F-Geck"); // Activate the highlighting, use the name from the SyntaxDefinition node.
            if (type == OpenType.File)
                te.LoadFile(file, false, true);
            else if (type == OpenType.Text)
                te.Text = file;
            // set tabinfo 
            TabInfo ti = new TabInfo();
            ti.history.linePosition = new TextLocation[0];
            ti.history.pointerCur = -1;
            ti.textEditor = te;
            if (type == OpenType.None) { // only for new create script
                sfdScripts.FileName = "NewScript";
                if (sfdScripts.ShowDialog() == DialogResult.OK) {
                    file = sfdScripts.FileName;
                    type = OpenType.File;
                    ti.changed = true;
                    te.Text = Properties.Resources.newScript;
                } else return null; 
            } else ti.changed = false;
            if (type == OpenType.File ) { //&& !alwaysNew
                if (alwaysNew) {
                    string temp = Path.Combine(Settings.scriptTempPath, unsaved);
                    File.Copy(file, temp, true); 
                    file = temp;
                }
                ti.filepath = file;
                ti.filename = Path.GetFileName(file);
            } else {
                ti.filepath = null;
                ti.filename = unsaved;
            }
            ti.index = tabControl1.TabCount;
            te.ContextMenuStrip = editorMenuStrip;

            tabs.Add(ti);
            TabPage tp = new TabPage(ti.filename);
            tp.ImageIndex = 0; 
            tp.Controls.Add(te);
            te.Dock = DockStyle.Fill;
            tabControl1.TabPages.Add(tp);
            if (tabControl1.TabPages.Count == 1) EnableFormControls();
            if (type == OpenType.File) {
                if (!alwaysNew) tp.ToolTipText = ti.filepath;
                System.String ext = Path.GetExtension(file).ToLower();
                if (ext == ".ssl" || ext == ".h") {
                    te.ActiveTextAreaControl.JumpTo(Settings.GetLastScriptPosition(ti.filename.ToLowerInvariant()));
                    if (formatCodeToolStripMenuItem.Checked) te.Text = Utilities.FormattingCode(te.Text);
                    ti.shouldParse = true;
                    ti.needsParse = true; // set 'true' only edit text
                    if (Settings.autoOpenMsgs && ti.filepath != null) 
                        AssossciateMsg(ti, false);
                    FirstParseScript(ti); // First Parse
                }
            }
            // TE events
            te.TextChanged += textChanged;
            SetActiveAreaEvents(te);
            //
            if (tabControl1.TabPages.Count > 1) {
                if (seltab) tabControl1.SelectTab(tp);
            } else {
                tabControl1_Selected(null, null);
            }
            return ti;
        }

        private void Save(TabInfo tab)
        {
            if (tab != null) {
                if (tab.filepath == null) {
                    SaveAs(tab);
                    return;
                }
                while (parserRunning) {
                    System.Threading.Thread.Sleep(1); //Avoid stomping on files while the parser is running
                }
                File.WriteAllText(tab.filepath, tab.textEditor.Text, (Path.GetExtension(tab.filename) == ".msg") ?  Settings.EncCodePage: Encoding.Default);
                tab.changed = false;
                SetTabTextChange(tab.index);
                Text = SSE + tab.filepath;
            }
        }

        private void SaveAs(TabInfo tab)
        {
            if (tab != null && sfdScripts.ShowDialog() == DialogResult.OK) {
                tab.filepath = sfdScripts.FileName;
                tab.filename = System.IO.Path.GetFileName(tab.filepath);
                tabControl1.TabPages[tab.index].Text = tabs[tab.index].filename;
                tabControl1.TabPages[tab.index].ToolTipText = tabs[tab.index].filepath;
                Save(tab);
                Settings.AddRecentFile(tab.filepath);
                string ext = Path.GetExtension(tab.filepath).ToLower();
                if (Settings.enableParser && (ext == ".ssl" || ext == ".h")) {
                    tab.shouldParse = true;
                    tab.needsParse = true;
                    parserLabel.Text = "Parser: Wait for update";
                    ParseScript();
                }
            }
        }

        private void Close(TabInfo tab)
        {
            if (tab == null | tab.index == -1) {
                return;
            }
            if (tab.changed) {
                switch (MessageBox.Show("Save changes to " + tab.filename + "?", "Message", MessageBoxButtons.YesNoCancel)) {
                    case DialogResult.Yes:
                        Save(tab);
                        if (tab.changed)
                            return;
                        break;
                    case DialogResult.No:
                        break;
                    default:
                        return;
                }
            }
            if (Path.GetExtension(tab.filepath).ToLowerInvariant() == ".ssl" && tab.filename != unsaved)
                Settings.SetLastScriptPosition(tab.filename.ToLowerInvariant(), tab.textEditor.ActiveTextAreaControl.Caret.Line);
            int i = tab.index;
            if (tabControl1.TabPages.Count > 2 && i == tabControl1.SelectedIndex) {
                if (previousTabIndex != -1) {
                    tabControl1.SelectedIndex = previousTabIndex;
                } else {
                    tabControl1.SelectedIndex = tabControl1.TabCount - 2;
                }
            }
            tabControl1.TabPages.RemoveAt(i);
            tabs.RemoveAt(i);
            for (int j = i; j < tabs.Count; j++)
                tabs[j].index--;
            for (int j = 0; j < tabs.Count; j++) {
                if (tabs[j].msgFileTab == tab) {
                    tabs[j].msgFileTab = null;
                    tabs[j].messages.Clear();
                }
            }
            tab.index = -1;
            if (tabControl1.TabPages.Count == 1) {
                tabControl1_Selected(null, null);
            }
        }

        private void AssossciateMsg(TabInfo tab, bool create)
        {
            if (tab.filepath == null || tab.msgFileTab != null)
                return;
            string path;
            if (MessageFile.Assossciate(tab, create, out path)) {
                if (Settings.autoOpenMsgs && msgAutoOpenEditorStripMenuItem.Checked && !create) {
                    MessageEditor.MessageEditorInit(tab, this);
                    Focus();
                } else tab.msgFileTab = Open(path, OpenType.File, false);
            }
        }

        private bool Compile(TabInfo tab, out string msg, bool showMessages = true, bool preprocess = false)
        {
            msg = String.Empty;
            if (string.Compare(Path.GetExtension(tab.filename), ".ssl", true) != 0) {
                if (showMessages) MessageBox.Show("You cannot compile this file.", "Compile Error");
                return false;
            }
            if (!Settings.ignoreCompPath && !preprocess && Settings.outputDir == null) {
                if (showMessages) MessageBox.Show("No output path selected.\nPlease select your scripts directory before compiling", "Compile Error");
                return false;
            }
            if (tab.changed) Save(tab);
            if (tab.changed || tab.filepath == null) return false;
            List<Error> errors = new List<Error>();
            Compiler compiler = new Compiler();
            bool success = compiler.Compile(tab.filepath, out msg, errors, preprocess, tab.parseInfo.ShortCircuitEvaluation);
            foreach (ErrorType et in new ErrorType[] { ErrorType.Error, ErrorType.Warning, ErrorType.Message }) {
                foreach (Error e in errors) {
                    if (e.type == et) {
                        dgvErrors.Rows.Add(e.type.ToString(), Path.GetFileName(e.fileName), e.line, e);
                        if (et == ErrorType.Error)
                            dgvErrors.Rows[dgvErrors.Rows.Count - 1].Cells[0].Style.ForeColor = Color.Red;
                    }
                }
            }
            if (dgvErrors.RowCount > 0) dgvErrors.Rows[0].Cells[0].Selected = false;
            if (!success) {
                tabControl2.SelectedIndex = 2 - Convert.ToInt32(Settings.userCmdCompile);
                if (showMessages && Settings.warnOnFailedCompile) {
                    MessageBox.Show("Script " + tab.filename + " failed to compile.\nSee the output build and errors window log for details.", "Compile Script Error");
                } else {
                    parserLabel.Text = "Failed to compiled: " + tab.filename;
                    parserLabel.ForeColor = Color.Firebrick;
                    msg += "\r\n Compilation Failed!";
                    CompileFail.Play();
                    maximize_log();
                }
            } else {
                parserLabel.Text = "Successfully compiled: " + tab.filename + " at " + DateTime.Now.ToString("HH:mm:ss");
                parserLabel.ForeColor = Color.DarkGreen;
                msg += "\r\n Compilation Successfully!";
            }
            return success;
        }

        // Called when creating a new document and when switching tabs
        private void tabControl1_Selected(object sender, TabControlEventArgs e)
        {
            if (tabControl1.SelectedIndex == -1) {
                currentTab = null;
                parserLabel.Text = (Settings.enableParser) ? "Parser: No file" : parseoff;
                SetFormControlsOff();
            } else {
                if (currentTab != null) {
                    previousTabIndex = currentTab.index;
                    Tree_Collapse();
                }
                currentTab = tabs[tabControl1.SelectedIndex];
                if (!Settings.enableParser && currentTab.parseInfo != null) currentTab.parseInfo.parseData = false;
                if (currentTab.msgFileTab != null) MessageFile.ParseMessages(currentTab);
                // Create or Delete Variable treeview
                if (!Settings.enableParser && tabControl3.TabPages.Count > 2) {
                    if (currentTab.parseInfo != null) {
                        if (!currentTab.parseInfo.parseData) {
                            tabControl3.TabPages.RemoveAt(1);
                        }
                    } else {
                        tabControl3.TabPages.RemoveAt(1);
                    }
                } else if (tabControl3.TabPages.Count < 3 && (Settings.enableParser || currentTab.parseInfo != null)) {
                    if (currentTab.parseInfo != null && currentTab.parseInfo.parseData) {
                        CreateTabVarTree();
                    }
                }
                if (currentTab.shouldParse) {
                    if (currentTab.needsParse) {
                        parserLabel.Text = (Settings.enableParser) ? "Parser: Wait for update" : parseoff;
                        // Update parse info
                        ParseScript();
                    } else {
                        parserLabel.Text = (Settings.enableParser) ? "Parser: Idle" : parseoff;
                        //UpdateNames();
                    }
                } else {
                    parserLabel.Text = (Settings.enableParser) ? "Parser: Not an SSL file" : parseoff; 
                    //UpdateNames();
                }
                UpdateNames();
                // text editor set focus 
                currentTab.textEditor.ActiveTextAreaControl.Select();
                ShowLineNumbers(null, null);
                ControlFormStateOn_Off();
                Text = SSE + currentTab.filepath;
            }
        }

#region Tree browser control 
        private void CreateTabVarTree() { tabControl3.TabPages.Insert(1, VarTab); }

        private enum TreeStatus { idle, update }

        // Create names for procedures and variables in treeview
        private void UpdateNames()
        {
            if (currentTab == null) return;
            ProcTree.Tag = TreeStatus.update;
            ProcTree.BeginUpdate();
            ProcTree.Nodes.Clear();
            VarTree.BeginUpdate();
            VarTree.Nodes.Clear();
            if (currentTab.parseInfo != null && currentTab.shouldParse) {
                TreeNode rootNode;
                foreach (var s in TREEPROCEDURES) {
                    rootNode = ProcTree.Nodes.Add(s);
                    rootNode.ForeColor = Color.DarkBlue;
                    rootNode.NodeFont = new Font("Arial", 9, FontStyle.Bold);
                }
                ProcTree.Nodes[0].ToolTipText = "Procedures declared and located in headers files";
                ProcTree.Nodes[1].ToolTipText = "Procedures declared and located in this script";
                foreach (Procedure p in currentTab.parseInfo.procs) {
                    TreeNode tn = new TreeNode((!ViewArgsStripButton.Checked)? p.name : p.ToString(false));
                    tn.Tag = p;
                    foreach (Variable var in p.variables) {
                        TreeNode tn2 = new TreeNode(var.name);
                        tn2.Tag = var;
                        tn2.ToolTipText = var.ToString();
                        tn.Nodes.Add(tn2);
                    }
                    if (p.filename.ToLower() != currentTab.filename.ToLower()) {
                        tn.ToolTipText = p.ToString() + "\ndeclarate file: " + p.filename;
                        ProcTree.Nodes[0].Nodes.Add(tn);
                        ProcTree.Nodes[0].Expand();
                    } else {
                        tn.ToolTipText = p.ToString();
                        ProcTree.Nodes[1].Nodes.Add(tn);
                        ProcTree.Nodes[1].Expand();
                    }
                }
                if (!Settings.enableParser && !currentTab.parseInfo.parseData) {
                    ProcTree.Nodes.RemoveAt(0);
                } else {
                    foreach (var s in TREEVARIABLES) {
                        rootNode = VarTree.Nodes.Add(s);
                        rootNode.ForeColor = Color.DarkBlue;
                        rootNode.NodeFont = new Font("Arial", 9, FontStyle.Bold);
                    }
                    foreach (Variable var in currentTab.parseInfo.vars) {
                        TreeNode tn = new TreeNode(var.name);
                        tn.Tag = var;
                        if (var.filename.ToLower() != currentTab.filename.ToLower()) {
                            tn.ToolTipText = var.ToString() + "\ndeclarate file: " + var.filename;
                            VarTree.Nodes[0].Nodes.Add(tn);
                            VarTree.Nodes[0].Expand();
                        } else {
                            tn.ToolTipText = var.ToString();
                            VarTree.Nodes[1].Nodes.Add(tn);
                            VarTree.Nodes[1].Expand();
                        }
                    }
                    if (currentTab.treeExpand.VarTree.global) VarTree.Nodes[0].Collapse();
                    if (currentTab.treeExpand.VarTree.local) VarTree.Nodes[1].Collapse();
                    if (VarTree.Nodes[0].Nodes.Count == 0) VarTree.Nodes[0].ForeColor = Color.Gray;
                    if (VarTree.Nodes[1].Nodes.Count == 0) VarTree.Nodes[1].ForeColor = Color.Gray;
                }
                if (currentTab.treeExpand.ProcTree.global) ProcTree.Nodes[0].Collapse();
                if (ProcTree.Nodes[0].Nodes.Count == 0) ProcTree.Nodes[0].ForeColor = Color.Gray;
                if (ProcTree.Nodes.Count > 1)
                {
                    if (currentTab.treeExpand.ProcTree.local) ProcTree.Nodes[1].Collapse();
                    if (ProcTree.Nodes[1].Nodes.Count == 0) ProcTree.Nodes[1].ForeColor = Color.Gray;
                    ProcTree.Nodes[1].EnsureVisible();
                }
            }
            VarTree.EndUpdate();
            ProcTree.EndUpdate();
            ProcTree.Tag = TreeStatus.idle;
        }

        private void Tree_Collapse()
        {
            if (ProcTree.Nodes.Count == 0) return;
            currentTab.treeExpand.ProcTree.global = !ProcTree.Nodes[0].IsExpanded;
            if (ProcTree.Nodes.Count > 1)
                currentTab.treeExpand.ProcTree.local = !ProcTree.Nodes[1].IsExpanded;
            if (VarTree.Nodes.Count > 0) {
                currentTab.treeExpand.VarTree.global = !VarTree.Nodes[0].IsExpanded;
                currentTab.treeExpand.VarTree.local = !VarTree.Nodes[1].IsExpanded;
            }
        }

        // Click on node tree Procedures/Variables
        private void TreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Action == TreeViewAction.Unknown) return;
            string file = null;
            int line = 0;
            bool pSelect = false;
            if (e.Node.Tag is Variable) {
                Variable var = (Variable)e.Node.Tag;
                file = var.fdeclared;
                line = var.d.declared;
            } else if (e.Node.Tag is Procedure) {
                Procedure proc = (Procedure)e.Node.Tag;
                file = proc.fstart;
                line = proc.d.start;
                pSelect = true;
            }
            if (file != null) SelectLine(file, line, pSelect);
        }

        void AfterCollapse(object sender, TreeViewEventArgs e)
        {
            if ((TreeStatus)ProcTree.Tag == TreeStatus.idle) Tree_Collapse();
        }
#endregion

        // Goto script text of selected Variable or Procedure in treeview
        public void SelectLine(string file, int line, bool pselect = false, int column = -1, int sLen = -1)
        {
            if (line <= 0) return;
            bool not_this = false;
            if (currentTab == null || file != currentTab.filepath) {
                if (Open(file, OpenType.File, false) == null) {
                    MessageBox.Show("Could not open file '" + file + "'", "Error");
                    return;
                }
                not_this = true;
            }
            LineSegment ls;
            if (line > currentTab.textEditor.Document.TotalNumberOfLines) {
                ls = currentTab.textEditor.Document.GetLineSegment(currentTab.textEditor.Document.TotalNumberOfLines - 1);
            } else {
                ls = currentTab.textEditor.Document.GetLineSegment(line - 1);
            }
            
            TextLocation start, end;
            if (column == -1 || column >= ls.Length - 2) {
                start = new TextLocation(0, ls.LineNumber);
                end = new TextLocation(ls.Length, ls.LineNumber);
            } else {
                start = new TextLocation(column - 1, ls.LineNumber);
                end = new TextLocation(start.Column + sLen, ls.LineNumber);
            }
            // Expand or Collapse folding
            foreach (FoldMarker fm in currentTab.textEditor.Document.FoldingManager.FoldMarker) {
                if (OnlyProcStripButton.Checked) {
                    if (fm.FoldType == FoldType.MemberBody && fm.StartLine == start.Line)
                        fm.IsFolded = false;
                    else 
                        fm.IsFolded = true;
                } else {
                    if (fm.FoldType == FoldType.MemberBody && fm.StartLine == start.Line) {
                        fm.IsFolded = false;
                        break;
                    }
                }
            }
            // Scroll and select
            currentTab.textEditor.ActiveTextAreaControl.Caret.Position = start;
            if (not_this || !pselect || !OnlyProcStripButton.Checked) {
                currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SetSelection(start, end);
            } else currentTab.textEditor.ActiveTextAreaControl.SelectionManager.ClearSelection();
            if (!not_this) {
                if (pselect) {
                    currentTab.textEditor.ActiveTextAreaControl.TextArea.TextView.FirstVisibleLine = start.Line - 1;
                } else
                    currentTab.textEditor.ActiveTextAreaControl.CenterViewOn(start.Line + 10, 0);
            } else currentTab.textEditor.ActiveTextAreaControl.CenterViewOn(start.Line - 15, 0);
            currentTab.textEditor.Refresh();
        }

        private void KeyPressed(object sender, KeyPressEventArgs e)
        {
            if (!Settings.autocomplete) return;
            var caret = currentTab.textEditor.ActiveTextAreaControl.Caret;
            if (e.KeyChar == '"') {
                char ch = currentTab.textEditor.Document.GetCharAt(caret.Offset);
                char chL = currentTab.textEditor.Document.GetCharAt(caret.Offset - 1);
                if ((ch == ' ' || ch == '\r') && !Char.IsLetterOrDigit(chL)) 
                    currentTab.textEditor.Document.Insert(caret.Offset, "\"");
                else if (chL == '"' && ch == '"') {
                    //currentTab.textEditor.Document.UndoStack.UndoOperation(false);
                    currentTab.textEditor.Document.Remove(caret.Offset, 1);
                    //currentTab.textEditor.Document.UndoStack.UndoOperation(true);  
                }
            }
            if (e.KeyChar == '(' || e.KeyChar == '[' || e.KeyChar == '{') {
                if (lbAutocomplete.Visible) {
                    lbAutocomplete.Hide();
                }
                if (Char.IsWhiteSpace(currentTab.textEditor.Document.GetCharAt(caret.Offset + 1))) {
                    string bracket = ")";
                    if (e.KeyChar == '[') bracket = "]";
                    else if (e.KeyChar == '{') bracket = "}";
                    currentTab.textEditor.Document.Insert(caret.Offset, bracket);
                }
                if (currentTab.parseInfo == null) {
                    return;
                }
                string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, caret.Offset - 2);
                string item = currentTab.parseInfo.LookupToken(word, currentTab.filepath, caret.Line);
                if (item != null) {
                    var pos = caret.ScreenPosition;
                    var tab = tabControl1.TabPages[currentTab.index];
                    pos.Offset(tab.FindForm().PointToClient(tab.Parent.PointToScreen(tab.Location)));
                    pos.Offset(-100, 20);
                    toolTipAC.Show(item, panel1, pos);
                }
            } else if (e.KeyChar == ')'|| e.KeyChar == ']' || e.KeyChar == '}') {
                if (toolTipAC.Active) {
                    toolTipAC.Hide(panel1);
                }
                string bracket = "(";
                if (e.KeyChar == ']') bracket = "[";
                else if (e.KeyChar == '}') bracket = "{";
                if (currentTab.textEditor.Document.GetCharAt(caret.Offset - 1) == Convert.ToChar(bracket)) {
                    //currentTab.textEditor.Document.UndoStack.UndoOperation(false);
                    currentTab.textEditor.Document.Remove(caret.Offset, 1);
                    //currentTab.textEditor.Document.UndoStack.UndoOperation(true);
                }
            } else {
                string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, caret.Offset - 1) + e.KeyChar.ToString();
                if (word != null && word.Length > 1) {
                    var matches = (currentTab.parseInfo != null)
                        ? currentTab.parseInfo.LookupAutosuggest(word)
                        : ProgramInfo.LookupOpcode(word);

                    if (matches.Count > 0) {
                        lbAutocomplete.Items.Clear();
                        int maxLen = 0;
                        foreach (string item in matches) {
                            int sep = item.IndexOf("|");
                            AutoCompleteItem acItem = new AutoCompleteItem(item, "");
                            if (sep != -1) {
                                acItem.name = item.Substring(0, sep);
                                acItem.hint = item.Substring(sep + 1);
                            }
                            lbAutocomplete.Items.Add(acItem);
                            if (acItem.name.Length > maxLen) maxLen = acItem.name.Length;
                        }
                        var caretPos = currentTab.textEditor.ActiveTextAreaControl.Caret.ScreenPosition;
                        var tePos = currentTab.textEditor.ActiveTextAreaControl.FindForm().PointToClient(currentTab.textEditor.ActiveTextAreaControl.Parent.PointToScreen(currentTab.textEditor.ActiveTextAreaControl.Location));
                        tePos.Offset(caretPos);
                        tePos.Offset(5, 18);
                        lbAutocomplete.Location = tePos;
                        // size
                        lbAutocomplete.Height = lbAutocomplete.ItemHeight * (lbAutocomplete.Items.Count + 1);
                        lbAutocomplete.Width = maxLen * 9;
                        lbAutocomplete.Show();
                        lbAutocomplete.Tag = new KeyValuePair<int, string>(currentTab.textEditor.ActiveTextAreaControl.Caret.Offset + 1, word);
                    } else {
                        lbAutocomplete.Hide();
                    }
                } else if (lbAutocomplete.Visible) {
                    lbAutocomplete.Hide();
                }
            }
        }

        // Tooltip for opcodes and macros
        void TextArea_ToolTipRequest(object sender, ToolTipRequestEventArgs e)
        {
            if (currentTab == null 
             /* || currentTab.parseInfo == null */
                || sender != currentTab.textEditor.ActiveTextAreaControl.TextArea 
                || !e.InDocument) {
                return;
            }
            HighlightColor hc = currentTab.textEditor.Document.GetLineSegment(e.LogicalPosition.Line).GetColorForPosition(e.LogicalPosition.Column);
            if (hc == null || hc.Color == Color.Green || hc.Color == Color.Brown || hc.Color == Color.DarkGreen || hc.BackgroundColor == Color.FromArgb(0xFF, 0xFF, 0xD0))
                return;
            string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, currentTab.textEditor.Document.PositionToOffset(e.LogicalPosition));
            if (word.Length == 0 ) return;
            if (currentTab.messages != null) { //msgFileTab
                int msg;
                if (int.TryParse(word, out msg) && currentTab.messages.ContainsKey(msg)) {
                    e.ShowToolTip(currentTab.messages[msg]);
                    return;
                }
            }
            string lookup = ProgramInfo.LookupOpcodesToken(word); // show opcodes help
            if (lookup == null && currentTab.parseInfo != null )
                lookup = currentTab.parseInfo.LookupToken(word, currentTab.filepath, e.LogicalPosition.Line + 1);
            if (lookup != null) e.ShowToolTip(lookup);
        }

        private void UpdateEditorToolStripMenu()
        {
            TextLocation tl = currentTab.textEditor.ActiveTextAreaControl.Caret.Position;
            editorMenuStrip.Tag = tl;
            // includes
            string line = TextUtilities.GetLineAsString(currentTab.textEditor.Document, tl.Line).Trim();
            if (line.StartsWith(Parser.INCLUDE)) {
                openIncludeToolStripMenuItem.Enabled = true;
            }
            // skip for specific color text
            HighlightColor hc = currentTab.textEditor.Document.GetLineSegment(tl.Line).GetColorForPosition(tl.Column);
            if (hc != null && (hc.Color == Color.Green || hc.Color == Color.Brown || hc.Color == Color.DarkGreen 
                || hc.BackgroundColor == Color.LightGray || hc.BackgroundColor == Color.FromArgb(0xFF, 0xFF, 0xD0)))
                return; 
            //
            if (currentTab.parseInfo != null) {
                NameType nt = NameType.None;
                IParserInfo item = null;
                    string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, currentTab.textEditor.Document.PositionToOffset(tl));
                    item = currentTab.parseInfo.Lookup(word, currentTab.filename, tl.Line);
                    if (item != null) {
                        nt = item.Type();
                        renameToolStripMenuItem.Tag = item;
                        if (!currentTab.needsParse) renameToolStripMenuItem.Enabled = true; 
                    }
                    //nt=currentTab.parseInfo.LookupTokenType(word, currentTab.filename, tl.Line);   
                switch (nt) {
                    case NameType.LVar: // variable procedure
                    case NameType.GVar: // variable script
                        findReferencesToolStripMenuItem.Enabled = true;
                        findDeclerationToolStripMenuItem.Enabled = true;
                        findDefinitionToolStripMenuItem.Enabled = false;
                        renameToolStripMenuItem.Text += (nt == NameType.LVar) ? ": Local Variable" : ": Script Variable";
                        break;
                    case NameType.Proc:
                        Procedure proc = (Procedure)item;
                        findReferencesToolStripMenuItem.Enabled = true;
                        findDeclerationToolStripMenuItem.Enabled = true;
                        findDefinitionToolStripMenuItem.Enabled = !proc.IsImported();
                        renameToolStripMenuItem.Text += ": Procedure";
                        break;
                    case NameType.Macro:
                        findReferencesToolStripMenuItem.Enabled = false;
                        findDeclerationToolStripMenuItem.Enabled = true;
                        findDefinitionToolStripMenuItem.Enabled = false;
                        Macro macr = (Macro)item;
                        if (macr.fdeclared == Compiler.parserPath)
                          renameToolStripMenuItem.Text += ": Local Macros";
                        else {
                            renameToolStripMenuItem.Text += ": Global Macros";
                            renameToolStripMenuItem.Enabled = false; // TODO: for next version
                        }
                        break;
                    default:
                        renameToolStripMenuItem.Text += ": Unknown";
                        break;
                }
            }
        }

#region Control set states
        private void SetTabTextChange(int i) { tabControl1.TabPages[i].ImageIndex = (tabs[i].changed ? 1 : 0); }

        private void SetActiveAreaEvents(TextEditorControl te)
        {
            te.ActiveTextAreaControl.TextArea.MouseDown += delegate(object a1, MouseEventArgs a2) {
                if (a2.Button == MouseButtons.Left)
                    Utilities.SelectedTextColorRegion(currentTab.textEditor);
                lbAutocomplete.Hide();
            };
            te.ActiveTextAreaControl.TextArea.KeyPress += KeyPressed;
            te.ActiveTextAreaControl.TextArea.MouseEnter += TextArea_SetFocus;
            te.ActiveTextAreaControl.TextArea.PreviewKeyDown += delegate(object sender, PreviewKeyDownEventArgs a2) {
                PosChangeType = PositionType.SaveChange; // Save position change for navigation, if key was pressed
                lbAutoCompleteKey(a2);
            };
            te.ActiveTextAreaControl.TextArea.MouseWheel += TextArea_MouseWheel;
            te.ActiveTextAreaControl.TextArea.MouseClick += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Middle) {
                    Utilities.HighlightingSelectedText(te);
                    currentTab.textEditor.Refresh();
                }
            };
            te.ActiveTextAreaControl.TextArea.ToolTipRequest += new ToolTipRequestEventHandler(TextArea_ToolTipRequest);
            te.ActiveTextAreaControl.Caret.PositionChanged += new EventHandler(Caret_PositionChanged);
        }

        private void EnableFormControls()
        {
            TabClose_button.Visible = true;
            Split_button.Visible = true;
            splitDocumentToolStripMenuItem.Enabled = true;
            openAllIncludesScriptToolStripMenuItem.Enabled = true;
            GotoProc_StripButton.Enabled = true;
            //Search_toolStripButton.Enabled = true;
            if (Settings.showLog) splitContainer1.Panel2Collapsed = false;
        }

        private void ControlFormStateOn_Off()
        {
            lbAutocomplete.Hide();
            if (currentTab.parseInfo != null && currentTab.parseInfo.procs.Length > 0) {
                Outline_toolStripButton.Enabled = true;
            } else Outline_toolStripButton.Enabled = false;
            SetBackForwardButtonState();
            string ext = Path.GetExtension(currentTab.filename).ToLower();
            if (ext == ".ssl" || ext == ".h") {
                DecIndentStripButton.Enabled = true;
                CommentStripButton.Enabled = true;
                UnCommentStripButton.Enabled = true;
                AlignToLeftToolStripMenuItem.Enabled = true;
                commentTextToolStripMenuItem.Enabled = true;
                uncommentTextToolStripMenuItem.Enabled = true;
            } else {
                DecIndentStripButton.Enabled = false;
                CommentStripButton.Enabled = false;
                UnCommentStripButton.Enabled = false;
                AlignToLeftToolStripMenuItem.Enabled = false;
                commentTextToolStripMenuItem.Enabled = false;
                uncommentTextToolStripMenuItem.Enabled = false;
            }
        }

        // No selected text tabs
        private void SetFormControlsOff() {
            Outline_toolStripButton.Enabled = false;
            splitContainer2.Panel2Collapsed = true;
            TabClose_button.Visible = false;
            openAllIncludesScriptToolStripMenuItem.Enabled = false;
            Split_button.Visible = false;
            splitDocumentToolStripMenuItem.Enabled = false;
            Back_toolStripButton.Enabled = false;
            Forward_toolStripButton.Enabled = false;
            GotoProc_StripButton.Enabled = false;
            //Search_toolStripButton.Enabled = false;
            if (SearchToolStrip.Visible) Search_Panel(null, null);
            DecIndentStripButton.Enabled = false;
            CommentStripButton.Enabled = false;
            UnCommentStripButton.Enabled = false;
            Text = SSE.Remove(SSE.Length - 2);
            lbAutocomplete.Hide();
        }
#endregion

#region Menu control events
        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bool p = Settings.enableParser; //save prev.state
            (new SettingsDialog()).ShowDialog();
            if (currentTab != null) {
                tabControl1_Selected(null, null);
            }
            if (Settings.enableParser != p && !Settings.enableParser){
                parserLabel.Text = parseoff;
                foreach (TabInfo t in tabs) {
                    t.treeExpand.ProcTree.global = false;
                    t.treeExpand.ProcTree.local = false;
                }
                if (currentTab != null ) {
                    if (ProcTree.Nodes.Count > 0) ProcTree.Nodes[0].Expand();
                    if (tabControl3.TabPages.Count > 2) {
                        tabControl3.TabPages.RemoveAt(1);
                    }
                }
            } else if (Settings.enableParser != p) {
                parserLabel.Text = "Parser: Enabled";
                foreach (TabInfo t in tabs) {
                    t.treeExpand.ProcTree.global = false;
                    t.treeExpand.ProcTree.local = false;
                    t.treeExpand.VarTree.global = false;
                    t.treeExpand.VarTree.local = false;
                }
                if (currentTab != null) {
                    if (ProcTree.Nodes.Count > 0) {
                        ProcTree.Nodes[0].Expand();
                        ProcTree.Nodes[1].Expand();
                    }
                    if (VarTree.Nodes.Count > 0) {
                        VarTree.Nodes[0].Expand();
                        VarTree.Nodes[1].Expand();
                    }
                    if (tabControl3.TabPages.Count < 3) {
                        CreateTabVarTree();
                    }
                }
            }
            if (Settings.PathScriptsHFile != null) {
                Headers_toolStripSplitButton.Enabled = true;
            }
        }

        private void compileToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (currentTab != null) {
                dgvErrors.Rows.Clear();
                string msg;
                Compile(currentTab, out msg);
                tbOutput.Text = msg;
            }
        }

        private void tabControl1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) {
                for (int i = 0; i < tabs.Count; i++) {
                    if (tabControl1.GetTabRect(i).Contains(e.X, e.Y)) {
                        if (e.Button == MouseButtons.Middle)
                            Close(tabs[i]);
                        else if (e.Button == MouseButtons.Right) {
                            cmsTabControls.Tag = i;
                            foreach (ToolStripItem item in cmsTabControls.Items) {
                                item.Visible = true;
                            }
                            cmsTabControls.Show(tabControl1, e.X, e.Y);
                        }
                        return;
                    }
                }
            }
        }

        private void tabControl2_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) {
                for (int i = 3; i < tabControl2.TabPages.Count; i++) {
                    if (tabControl2.GetTabRect(i).Contains(e.X, e.Y)) {
                        if (e.Button == MouseButtons.Middle) {
                            int stbi = tabControl2.SelectedIndex;
                            if (stbi == i) tabControl2.Hide();
                            tabControl2.TabPages.RemoveAt(i--);
                            if (stbi == i + 1) {
                                tabControl2.SelectedIndex = (stbi == tabControl2.TabCount) ? stbi - 1 : stbi;
                                tabControl2.Show();
                            }
                        } else if (e.Button == MouseButtons.Right) {
                            cmsTabControls.Tag = i ^ 0x10000000;
                            foreach (ToolStripItem item in cmsTabControls.Items) {
                                item.Visible = (item.Text == "Close");
                            }
                            cmsTabControls.Show(tabControl2, e.X, e.Y);
                        }
                        return;
                    }
                }
            }
            else if (e.Button == MouseButtons.Left && minimizelogsize != 0 ) {
                minimizelog_button.PerformClick();
            }
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Save(currentTab);
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Open(null, OpenType.None);
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ofdScripts.ShowDialog() == DialogResult.OK) {
                foreach (string s in ofdScripts.FileNames) {
                    Open(s, OpenType.File);
                }
            }
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveAs(currentTab);
        }

        private void saveAsTemplateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null || Path.GetExtension(currentTab.filepath).ToLower() != ".ssl") return;
            SaveFileDialog sfdTemplate = new SaveFileDialog();
            sfdTemplate.Title = "Enter file name for script template";
            sfdTemplate.Filter = "Template file|*.ssl";
            string path = Path.Combine(Settings.ResourcesFolder, "templates");
            sfdTemplate.InitialDirectory = path;
            if (sfdTemplate.ShowDialog() == DialogResult.OK) {
                string fname = Path.GetFileName(sfdTemplate.FileName);
                File.WriteAllText(path + "\\" + fname, currentTab.textEditor.Text, System.Text.Encoding.ASCII);
            }
            sfdTemplate.Dispose();
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close(tabs[tabControl1.SelectedIndex]);
        }

        private void recentItem_Click(object sender, EventArgs e)
        {
            Open(((ToolStripMenuItem)sender).Text, OpenType.File, true, false, true);
        }

        private void Template_Click(object sender, EventArgs e)
        {
            Open(((ToolStripMenuItem)sender).Tag.ToString(), OpenType.File, false, true);
        }

        private void saveAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < tabs.Count; i++) {
                Save(tabs[i]);
                if (tabs[i].changed) {
                    break;
                }
            }
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            (new AboutBox()).ShowDialog();
        }

        private void helpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(".\\docs\\");
        }

        private void massCompileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Settings.outputDir == null) {
                MessageBox.Show("No output path selected.\nPlease select your scripts directory before compiling", "Error");
                return;
            }
            bool option = Settings.ignoreCompPath;
            Settings.ignoreCompPath = false;
            if (Settings.lastMassCompile != null) {
                fbdMassCompile.SelectedPath = Settings.lastMassCompile;
            }
            if (fbdMassCompile.ShowDialog() != DialogResult.OK) {
                return;
            }
            Settings.lastMassCompile = fbdMassCompile.SelectedPath;
            BatchCompiler.CompileFolder(fbdMassCompile.SelectedPath);
            Settings.ignoreCompPath = option;
        }

        private void compileAllOpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StringBuilder FullMsg = new StringBuilder();
            dgvErrors.Rows.Clear();
            string msg;
            for (int i = 0; i < tabs.Count; i++) {
                //FullMsg.AppendLine("*** " + tabs[i].filename);
                Compile(tabs[i], out msg, false);
                FullMsg.AppendLine(msg);
                FullMsg.AppendLine();
            }
            tbOutput.Text = FullMsg.ToString();
        }

        private void cutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab != null) {
                currentTab.textEditor.ActiveTextAreaControl.TextArea.ClipboardHandler.Cut(null, null);
            }
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab != null) {
                currentTab.textEditor.ActiveTextAreaControl.TextArea.ClipboardHandler.Copy(null, null);
            }
        }

        private void pasteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab != null) {
                currentTab.textEditor.ActiveTextAreaControl.TextArea.ClipboardHandler.Paste(null, null);
            }
        }

        private void undoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab != null) {
                currentTab.textEditor.Undo();
                if (!currentTab.textEditor.Document.UndoStack.CanUndo) {
                    currentTab.changed = false;
                    SetTabTextChange(currentTab.index);
                }
            }            
        }

        private void redoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab != null) {
                if (currentTab.textEditor.Document.UndoStack.CanRedo) {
                    currentTab.changed = true;
                    SetTabTextChange(currentTab.index);
                }
                currentTab.textEditor.Redo();
            }
        }

        private void outlineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null) return;
            int cline = currentTab.textEditor.ActiveTextAreaControl.Caret.Line;
            foreach (FoldMarker fm in currentTab.textEditor.Document.FoldingManager.FoldMarker) {
                if (cline >= fm.StartLine && cline <= fm.EndLine) continue;
                if (fm.FoldType == FoldType.MemberBody)
                    fm.IsFolded = !fm.IsFolded;
            }
            currentTab.textEditor.Document.FoldingManager.NotifyFoldingsChanged(null);
            currentTab.textEditor.ActiveTextAreaControl.CenterViewOn(cline, 0);
        }

        private void registerScriptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null) {
                return;
            }
            if (currentTab.filepath == null) {
                MessageBox.Show("You cannot register an unsaved script.", "Error");
                return;
            }
            string fName = Path.GetExtension(currentTab.filename).ToLower();
            if (fName != ".ssl" && fName != ".int") {
                MessageBox.Show("You cannot register this file.", "Error");
                return;
            }
            fName = Path.ChangeExtension(currentTab.filename, "int");
            if (fName.Length > 12) {
                MessageBox.Show("Script file names must be 8 characters or under to be registered.", "Error");
                return;
            }
            if (currentTab.filename.Length >= 2 && string.Compare(currentTab.filename.Substring(0, 2), "gl", true) == 0) {
                if (MessageBox.Show("This script starts with 'gl', and will be treated by sfall as a global script and loaded automatically.\n" +
                    "If it's being used as a global script, it does not need to be registered.\n" +
                    "If it isn't, the script should be renamed before registering it.\n" +
                    "Are you sure you wish to continue?", "Error") != DialogResult.Yes)
                    return;
            }
            if (fName.IndexOf(' ') != -1) {
                MessageBox.Show("Cannot register a script name that contains a space.", "Error");
                return;
            }
            RegisterScript.Registration(fName);
        }

        private void dgvErrors_DoubleClick(object sender, EventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            if (dgv.SelectedCells.Count != 1)
                return;
            Error error = (Error)dgv.Rows[dgv.SelectedCells[0].RowIndex].Cells[dgv == dgvErrors ? 3 : 2].Value;
            if (error.line != -1) {
                SelectLine(error.fileName, error.line, false, error.column, error.len);
            }
        }

        private void preprocessToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null)
                return;
            dgvErrors.Rows.Clear();
            string msg;
            bool result = Compile(currentTab, out msg, true, true);
            tbOutput.Text = msg;
            if (!result)
                return;
            string file = Compiler.GetPreprocessedFile(currentTab.filename);
            if (file != null)
                Open(file, OpenType.File, false);
            else {
                MessageBox.Show("Failed to fetch preprocessed file");
            }
        }

        private void roundtripToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null)
                return;
            dgvErrors.Rows.Clear();
            string msg;
            bool result = Compile(currentTab, out msg);
            tbOutput.Text = msg;
            if (result)
                Open(Compiler.GetOutputPath(currentTab.filepath), OpenType.File, false);
        }

        private void editRegisteredScriptsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RegisterScript.Registration(null);
        }

        private void associateMsgToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null)
                return;
            if (msgAutoOpenEditorStripMenuItem.Checked)
                MessageEditor.MessageEditorInit(currentTab, this);
            else
                AssossciateMsg(currentTab, true);
        }

        private void editorMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (currentTab == null /*&& !treeView1.Focused*/) {
                e.Cancel = true;
                return;
            }
            if (currentTab.textEditor.ActiveTextAreaControl.SelectionManager.HasSomethingSelected) {
                highlightToolStripMenuItem.Visible = true;
                renameToolStripMenuItem.Visible = false;
            } else {
                highlightToolStripMenuItem.Visible = false;
                renameToolStripMenuItem.Visible = true;
                renameToolStripMenuItem.Text = "Rename";
                renameToolStripMenuItem.Enabled = false;
                renameToolStripMenuItem.ToolTipText = (currentTab.needsParse)? "Waiting parsing..." : ""; 
            }
            openIncludeToolStripMenuItem.Enabled = false;
            findReferencesToolStripMenuItem.Enabled = false;
            findDeclerationToolStripMenuItem.Enabled = false;
            findDefinitionToolStripMenuItem.Enabled = false;
            UpdateEditorToolStripMenu();
        }

        private void closeToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            int i = (int)cmsTabControls.Tag;
            if ((i & 0x10000000) != 0)
                tabControl2.TabPages.RemoveAt(i ^ 0x10000000);
            else
                Close(tabs[i]);
        }

        void GoToLineToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (currentTab == null || goToLine != null) return;
            goToLine = new GoToLine();
            AddOwnedForm(goToLine);
            goToLine.tbLine.Maximum = currentTab.textEditor.Document.TotalNumberOfLines;
            goToLine.tbLine.Select(0, 1);
            goToLine.bGo.Click += delegate(object a1, EventArgs a2) {
                TextAreaControl tac = currentTab.textEditor.ActiveTextAreaControl;
                tac.Caret.Column = 0;
                tac.Caret.Line = Convert.ToInt32(goToLine.tbLine.Value - 1);
                tac.CenterViewOn(tac.Caret.Line, 0);
                goToLine.tbLine.Select();
            };
            goToLine.FormClosed += delegate(object a1, FormClosedEventArgs a2) { goToLine = null; };
            goToLine.Show();
        }

        void UPPERCASEToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (currentTab.textEditor.ActiveTextAreaControl.SelectionManager.HasSomethingSelected) {
                var action = new ICSharpCode.TextEditor.Actions.ToUpperCase();
                action.Execute(currentTab.textEditor.ActiveTextAreaControl.TextArea);
            }
        }

        void LowecaseToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (currentTab.textEditor.ActiveTextAreaControl.SelectionManager.HasSomethingSelected) {
                var action = new ICSharpCode.TextEditor.Actions.ToLowerCase();
                action.Execute(currentTab.textEditor.ActiveTextAreaControl.TextArea);
            }
        }

        void CloseAllToolStripMenuItemClick(object sender, EventArgs e)
        {
            for (int i = tabs.Count - 1; i >= 0; i--) {
                Close(tabs[i]);
            }
        }

        void CloseAllButThisToolStripMenuItemClick(object sender, EventArgs e)
        {
            int thisIndex = (int)cmsTabControls.Tag;
            for (int i = tabs.Count - 1; i >= 0; i--) {
                if (i != thisIndex) {
                    Close(tabs[i]);
                }
            }
        }

        void TextEditorDragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (string file in files) {
                Open(file, OpenType.File);
            }
            Activate();
            if (currentTab != null) {
                currentTab.textEditor.ActiveTextAreaControl.TextArea.AllowDrop = true;
            }
        }

        void TextEditorDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            if (currentTab != null) {
                currentTab.textEditor.ActiveTextAreaControl.TextArea.AllowDrop = false;
            }
        }

        private void minimize_log_button_Click(object sender, EventArgs e)
        {
            if (minimizelogsize == 0) {
                minimizelogsize = splitContainer1.SplitterDistance; 
                splitContainer1.SplitterDistance = Size.Height;
                Settings.editorSplitterPosition = minimizelogsize;
            } else {
                int hs = Size.Height - (Size.Height / 4);
                if (Settings.editorSplitterPosition == -1)
                    Settings.editorSplitterPosition = hs;
                if (minimizelogsize > (hs + 100))
                    splitContainer1.SplitterDistance = hs; 
                else splitContainer1.SplitterDistance = Settings.editorSplitterPosition;
                minimizelogsize = 0;
            }
        }

        private void maximize_log()
        {
            if (currentTab == null && splitContainer1.Panel2Collapsed) {
                showLogWindowToolStripMenuItem.Checked = true;
                splitContainer1.Panel2Collapsed = false;
            }
            if (minimizelogsize == 0) return;
            if (Settings.editorSplitterPosition == -1)
                Settings.editorSplitterPosition = Size.Height - (Size.Height / 4);
            splitContainer1.SplitterDistance = Settings.editorSplitterPosition;
            minimizelogsize = 0;
        }

        private void showLogWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            splitContainer1.Panel2Collapsed = !(Settings.showLog = showLogWindowToolStripMenuItem.Checked);
        }

        private void Headers_toolStripSplitButton_ButtonClick(object sender, EventArgs e)
        {
            Headers Headfrm = new Headers(this);
            Headfrm.xy_pos = Headers_toolStripSplitButton.Bounds.Location;
            Headfrm.Show();
        }

        private void openHeaderFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofdHeaders = new OpenFileDialog();
            ofdHeaders.Title = "Select header files to open";
            ofdHeaders.Filter = "Header files|*.h";
            ofdHeaders.Multiselect = true;
            ofdHeaders.RestoreDirectory = true;
            ofdHeaders.InitialDirectory = Settings.PathScriptsHFile;
            if (ofdHeaders.ShowDialog() == DialogResult.OK) {
                foreach (string s in ofdHeaders.FileNames) {
                    Open(s, OpenType.File, false);
                }
            }
            ofdHeaders.Dispose();
        }

        private void openIncludesScriptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab.filepath != null) {
                foreach (string s in Parser.GetAllIncludes(currentTab)) {
                    Open(s, OpenType.File, false, false, false, false);
                }
            }
        }

        public void AcceptHeaderFile(string sHeaderfile)
        {
            if (sHeaderfile != null) Open(sHeaderfile, OpenType.File, false);
        }

        private void SplitDoc_Click(object sender, EventArgs e)
        {
            if (currentTab != null){
                currentTab.textEditor.Split();
                if (!SplitEvent) {
                    SplitEvent = true;
                    SetActiveAreaEvents(currentTab.textEditor);
                }
                TextArea_SetFocus(null, null);
            }
        }

        private void ShowLineNumbers(object sender, EventArgs e)
        {
            if (currentTab == null) return;
            string ext = Path.GetExtension(currentTab.filename).ToLower(); 
            PosChangeType = PositionType.AddPos;
            if (ext != ".ssl" && ext != ".h") {
                currentTab.textEditor.TextEditorProperties.ShowLineNumbers = false;
                PosChangeType = PositionType.Disabled;
                splitContainer2.Panel2Collapsed = true;
            } else {
                if (browserToolStripMenuItem.Checked) splitContainer2.Panel2Collapsed = false;
                currentTab.textEditor.TextEditorProperties.ShowLineNumbers = textLineNumberToolStripMenuItem.Checked;
                currentTab.textEditor.Refresh();
            }
        }

        private void EncodingMenuItem_Click(object sender, EventArgs e)
        {
            if (((ToolStripMenuItem)sender).Tag != null /*&& ((ToolStripMenuItem)sender).Tag.ToString() == "dos"*/) {
                EncodingDOSmenuItem.Checked = true;
                windowsDefaultMenuItem.Checked = false;
                Settings.encoding = (byte)EncodingType.OEM866;
            } else {
                EncodingDOSmenuItem.Checked = false;
                windowsDefaultMenuItem.Checked = true;
                Settings.encoding = (byte)EncodingType.Default;
            }
            Settings.EncCodePage = (Settings.encoding == (byte)EncodingType.OEM866) ? Encoding.GetEncoding("cp866") : Encoding.Default;
        }

        private void defineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.allowDefine = defineToolStripMenuItem.Checked;
        }

        private void addSearchTextComboBox(string world)
        {
            if (world.Length == 0) return;
            bool addSearchText = true;
            foreach (var item in SearchTextComboBox.Items)
            {
                if (world == item.ToString()){
                    addSearchText = false;
                    break;
                }
            }
            if (addSearchText) SearchTextComboBox.Items.Add(world);
        }

        private void DecIndentStripButton_Click(object sender, EventArgs e)
        {
            Utilities.DecIndent(currentTab.textEditor);
        }

        private void CommentTextStripButton_Click(object sender, EventArgs e)
        {
            Utilities.CommentText(currentTab.textEditor);
        }

        private void UnCommentTextStripButton_Click(object sender, EventArgs e)
        {
            Utilities.UnCommentText(currentTab.textEditor);
        }

        private void AlignToLeftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Utilities.AlignToLeft(currentTab.textEditor);
        }

        private void msgFileEditorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageEditor.MessageEditorInit(null, this);
        }

        public void AcceptMsgLine(string line)
        {
            if (currentTab != null) {
                currentTab.textEditor.ActiveTextAreaControl.TextArea.InsertString(line);
                this.Focus();
            }
        }

        private void pDefineStripComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            Settings.preprocDef = pDefineStripComboBox.SelectedItem.ToString();
        }

        private void FunctionsTree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Tag != null && currentTab != null) {
                string code = e.Node.Tag.ToString();
                if (code.LastIndexOf("<cr>") > 0) {
                    code = code.Replace("<cr>", Environment.NewLine);
                } else code += " ";
                currentTab.textEditor.ActiveTextAreaControl.TextArea.InsertString(code);
            }
        }

        private void FunctionButton_Click(object sender, EventArgs e)
        {
            if (fuctionPanel > 0) {
                splitContainer3.Panel1Collapsed = true;
                fuctionPanel = 0;
            } else {
                if (fuctionPanel == -1) {
                    Functions.CreateTree(FunctionTreeLeft);
                    splitContainer3.Panel2MinSize = 900;
                    splitContainer3.SplitterDistance = 220;
                    fuctionPanel = 220;
                }
                splitContainer3.Panel1Collapsed = false;
                fuctionPanel = splitContainer3.SplitterDistance;
            }
        }

        private void funcToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FunctionButton.PerformClick();
        }

        private void browserToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!currentTab.shouldParse || currentTab == null) return;
            splitContainer2.Panel2Collapsed = !browserToolStripMenuItem.Checked;
        }

        private void formatingCodeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Utilities.FormattingCode(currentTab.textEditor);
        }

        private void GoBeginStripButton_Click(object sender, EventArgs e)
        {
            currentTab.textEditor.BeginUpdate();
            SelectLine(currentTab.filepath, 1);
            currentTab.textEditor.ActiveTextAreaControl.SelectionManager.ClearSelection();
            currentTab.textEditor.EndUpdate();
        }

        void TextArea_SetFocus(object sender, EventArgs e)
        {
            if (!this.ContainsFocus) return;
            if (lbAutocompleteShiftCaret) {
                lbAutocompleteShiftCaret = false;
                currentTab.textEditor.ActiveTextAreaControl.Caret.Position = currentTab.textEditor.Document.OffsetToPosition(((KeyValuePair<int, string>)lbAutocomplete.Tag).Key);
            }
            currentTab.textEditor.ActiveTextAreaControl.TextArea.Focus();
            currentTab.textEditor.ActiveTextAreaControl.TextArea.Select();
        }

        private void TextEditor_Deactivate(object sender, EventArgs e)
        {
            if (currentTab == null) return;
            currentTab.textEditor.ActiveTextAreaControl.TextArea.MouseEnter -= TextArea_SetFocus;
        }

        private void TextEditor_Activated(object sender, EventArgs e)
        {
            if (currentTab == null) return;
            currentTab.textEditor.ActiveTextAreaControl.TextArea.MouseEnter += TextArea_SetFocus;
        }

        private void ViewArgsStripButton_CheckedChanged(object sender, EventArgs e)
        {
            UpdateNames();
        }

        private void SearchToolStrip_Resize(object sender, EventArgs e)
        {
            int w = ((ToolStrip)sender).Width;
            int size = (w / 2) - 150; 
            SearchTextComboBox.Width = size + 50;
            ReplaceTextBox.Width = size;
        }

        private void ParsingErrorsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ParsingErrors = ParsingErrorsToolStripMenuItem.Checked;
        }
#endregion

        private void dialogNodesDiagramToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (currentTab == null) return; 
            NodeDiagram NodesView = new NodeDiagram(currentTab.textEditor.Document);
            NodesView.Show();
#endif
        }

        private void previewDialogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null) return;
            string msgPath;
            if (!MessageFile.Assossciate(currentTab, false, out msgPath)) {
                MessageBox.Show("The associated message file this script could not be found.", "Dialog preview");
                return;
            }
            DialogPreview DialogView = new DialogPreview(currentTab, msgPath);
            if (!DialogView.InitReady) {
                DialogView.Dispose();
                MessageBox.Show("This script does not contain dialog procedures.", "Dialog preview");
            }
            else
                DialogView.Show(this);
        }
    }
}