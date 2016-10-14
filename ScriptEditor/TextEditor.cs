using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Document;
using System.Text.RegularExpressions;
using Path = System.IO.Path;
using File = System.IO.File;
using Directory = System.IO.Directory;
using SearchOption = System.IO.SearchOption;

namespace ScriptEditor
{
    partial class TextEditor : Form
    {
        private const string unsaved = "<unsaved>";
        private DateTime timerNext;
        private Timer timer;
        private readonly List<TabInfo> tabs = new List<TabInfo>();
        private TabInfo currentTab;
        private ToolStripLabel parserLabel;
        private volatile bool parserRunning;

        private SearchForm sf;
        private GoToLine goToLine;
        private int previousTabIndex = -1;

        public TextEditor()
        {
            InitializeComponent();
            Settings.SetupWindowPosition(SavedWindows.Main, this);
            // highlighting
            FileSyntaxModeProvider fsmProvider = new FileSyntaxModeProvider(".//resources//"); // Create new provider with the highlighting directory.
            HighlightingManager.Manager.AddSyntaxModeFileProvider(fsmProvider); // Attach to the text editor.
            // folding timer
            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += new EventHandler(timer_Tick);
            // Recent files
            UpdateRecentList();
            // Templates
            foreach (string file in Directory.GetFiles("resources\\templates", "*.ssl")) {
                ToolStripMenuItem mi = new ToolStripMenuItem(Path.GetFileNameWithoutExtension(file), null, delegate(object sender, EventArgs e) {
                    Open(file, OpenType.File, false, true);
                });
                templatesToolStripMenuItem.DropDownItems.Add(mi);
            }
            parserLabel = new ToolStripLabel("Parser: No file");
            parserLabel.Alignment = ToolStripItemAlignment.Right;
            MainMenu.Items.Add(parserLabel);
            tabControl1.tabsSwapped += delegate(object sender, TabsSwappedEventArgs e) {
                TabInfo tmp = tabs[e.aIndex];
                tabs[e.aIndex] = tabs[e.bIndex];
                tabs[e.aIndex].index = e.aIndex;
                tabs[e.bIndex] = tmp;
                tabs[e.bIndex].index = e.bIndex;
            };
            splitContainer2.Panel2Collapsed = !Settings.enableParser;
            parserLabel.Visible = Settings.enableParser;
            ProgramInfo.LoadOpcodes();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_SFALL_SCRIPT_EDITOR_OPEN) {
                ShowMe();
                if (File.Exists(NativeMethods.CommandLineFile)) {
                    var commandLineArgs = File.ReadAllLines(NativeMethods.CommandLineFile);
                    foreach (var file in commandLineArgs) {
                        Open(file, OpenType.File);
                    }
                }
            }
            base.WndProc(ref m);
        }

        private void ShowMe()
        {
            if (WindowState == FormWindowState.Minimized) {
                WindowState = FormWindowState.Normal;
            }
            // get our current "TopMost" value (ours will always be false though)
            bool top = TopMost;
            // make our form jump to the top of everything
            TopMost = true;
            // set it back to whatever it was
            TopMost = top;
        }

        private void TextEditor_Load(object sender, EventArgs e)
        {
            if (Settings.editorSplitterPosition != -1) {
                splitContainer1.SplitterDistance = Settings.editorSplitterPosition;
            }
            if (Settings.editorSplitterPosition2 != -1) {
                splitContainer2.SplitterDistance = Settings.editorSplitterPosition2;
            }
        }

        private void TextEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (sf != null) {
                sf.Close();
            }
            Settings.editorSplitterPosition = splitContainer1.SplitterDistance;
            Settings.editorSplitterPosition2 = splitContainer2.SplitterDistance;
            Settings.SaveWindowPosition(SavedWindows.Main, this);
            Settings.Save();
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
        }

        private void UpdateRecentList()
        {
            string[] items = Settings.GetRecent();
            recentToolStripMenuItem.DropDownItems.Clear();
            for (int i = items.Length - 1; i >= 0; i--) {
                recentToolStripMenuItem.DropDownItems.Add(items[i], null, recentItem_Click);
            }
        }

        private void SetTabText(int i)
        {
            tabControl1.TabPages[i].Text = tabs[i].filename + (tabs[i].changed ? " *" : "");
            tabControl1.TabPages[i].ToolTipText = tabs[i].filepath;
        }

        public enum OpenType { None, File, Text }

        public TabInfo Open(string file, OpenType type, bool addToMRU = true, bool alwaysNew = false)
        {
            if (type == OpenType.File) {
                if (!Path.IsPathRooted(file)) {
                    file = Path.GetFullPath(file);
                }
                //Add this file to the recent files list
                if (addToMRU) {
                    Settings.AddRecentFile(file);
                }
                UpdateRecentList();
                //If this is an int, decompile
                if (string.Compare(Path.GetExtension(file), ".int", true) == 0) {
                    string decomp = Compiler.Decompile(file);
                    if (decomp == null) {
                        MessageBox.Show("Decompilation of '" + file + "' was not successful", "Error");
                        return null;
                    } else {
                        file = decomp;
                        type = OpenType.Text;
                    }
                } else {
                    //Check if the file is already open
                    for (int i = 0; i < tabs.Count; i++) {
                        if (string.Compare(tabs[i].filepath, file, true) == 0) {
                            tabControl1.SelectTab(i);
                            return tabs[i];
                        }
                    }
                }
            }
            //Create the text editor and set up the tab
            ICSharpCode.TextEditor.TextEditorControl te = new ICSharpCode.TextEditor.TextEditorControl();
            te.ShowVRuler = false;
            te.Document.FoldingManager.FoldingStrategy = new CodeFolder();
            te.IndentStyle = IndentStyle.Smart;
            te.ConvertTabsToSpaces = Settings.tabsToSpaces;
            te.TabIndent = Settings.tabSize;
            te.Document.TextEditorProperties.IndentationSize = Settings.tabSize;
            if (type == OpenType.File)
                te.LoadFile(file, false, true);
            else if (type == OpenType.Text)
                te.Text = file;
            if (type == OpenType.File && string.Compare(Path.GetExtension(file), ".msg", true) == 0)
                te.SetHighlighting("msg");
            else
                te.SetHighlighting("ssl"); // Activate the highlighting, use the name from the SyntaxDefinition node.
            te.TextChanged += textChanged;
            te.ActiveTextAreaControl.TextArea.MouseDown += delegate(object a1, MouseEventArgs a2) {
                if (a2.Button == MouseButtons.Left)
                    UpdateEditorToolStripMenu();
                lbAutocomplete.Hide();
            };
            te.ActiveTextAreaControl.TextArea.KeyPress += KeyPressed;
            te.HorizontalScroll.Visible = false;

            te.ActiveTextAreaControl.TextArea.PreviewKeyDown += delegate(object sender, PreviewKeyDownEventArgs a2) {
                if (lbAutocomplete.Visible) {
                    if ((a2.KeyCode == Keys.Down || a2.KeyCode == Keys.Up || a2.KeyCode == Keys.Tab)) {
                        lbAutocomplete.Focus();
                        lbAutocomplete.SelectedIndex = 0;
                    } else if (a2.KeyCode == Keys.Escape) {
                        lbAutocomplete.Hide();
                    }
                } else {
                    if (toolTipAC.Active && a2.KeyCode != Keys.Left && a2.KeyCode != Keys.Right)
                        toolTipAC.Hide(panel1);
                }
            };

            TabInfo ti = new TabInfo();
            ti.te = te;
            ti.changed = false;
            if (type == OpenType.File && !alwaysNew) {
                ti.filepath = file;
                ti.filename = Path.GetFileName(file);
            } else {
                ti.filepath = null;
                ti.filename = unsaved;
            }
            ti.index = tabControl1.TabCount;
            te.ActiveTextAreaControl.TextArea.ToolTipRequest += new ToolTipRequestEventHandler(TextArea_ToolTipRequest);
            te.ContextMenuStrip = editorMenuStrip;

            tabs.Add(ti);
            TabPage tp = new TabPage(ti.filename);
            tp.Controls.Add(te);
            te.Dock = DockStyle.Fill;

            tabControl1.TabPages.Add(tp);
            if (type == OpenType.File & !alwaysNew) {
                tp.ToolTipText = ti.filepath;
                System.String ext = Path.GetExtension(file).ToLower();
                if (ext == ".ssl" || ext == ".h") {
                    ti.shouldParse = true;
                    ti.needsParse = true;
                    if (Settings.autoOpenMsgs && ti.filepath != null)
                        AssossciateMsg(ti, false);
                }
            }
            if (tabControl1.TabPages.Count > 1) {
                tabControl1.SelectTab(tp);
            } else {
                tabControl1_Selected(null, null);
            }
            return ti;
        }

        void TextArea_ToolTipRequest(object sender, ToolTipRequestEventArgs e)
        {
            if (currentTab == null 
                || currentTab.parseInfo == null 
                || sender != currentTab.te.ActiveTextAreaControl.TextArea 
                || !e.InDocument) {
                return;
            }
            HighlightColor hc = currentTab.te.Document.GetLineSegment(e.LogicalPosition.Line).GetColorForPosition(e.LogicalPosition.Column);
            if (hc == null || hc.Color == System.Drawing.Color.Green || hc.Color == System.Drawing.Color.Brown || hc.Color == System.Drawing.Color.DarkGreen)
                return;
            string word = TextUtilities.GetWordAt(currentTab.te.Document, currentTab.te.Document.PositionToOffset(e.LogicalPosition));
            if (currentTab.msg != null) {
                int msg;
                if (int.TryParse(word, out msg) && currentTab.messages.ContainsKey(msg)) {
                    e.ShowToolTip(currentTab.messages[msg]);
                    return;
                }
            }
            string lookup = currentTab.parseInfo.LookupToken(word, currentTab.filepath, e.LogicalPosition.Line + 1);
            if (lookup != null) {
                e.ShowToolTip(lookup);
                return;
            }
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
                System.IO.File.WriteAllText(tab.filepath, tab.te.Text, System.Text.Encoding.ASCII);
                tab.changed = false;
                SetTabText(tab.index);
            }
        }

        private void SaveAs(TabInfo tab)
        {
            if (tab != null && sfdScripts.ShowDialog() == DialogResult.OK) {
                tab.filepath = sfdScripts.FileName;
                tab.filename = System.IO.Path.GetFileName(tab.filepath);
                Save(tab);
                Settings.AddRecentFile(tab.filepath);
                System.String ext = Path.GetExtension(tab.filepath).ToLower();
                if (ext == ".ssl" || ext == ".h") {
                    tab.shouldParse = true;
                    tab.needsParse = true;
                    parserLabel.Text = "Parser: Out of date";
                    timerNext = DateTime.Now + TimeSpan.FromSeconds(1);
                    if (!timer.Enabled)
                        timer.Start();
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
                if (tabs[j].msg == tab) {
                    tabs[j].msg = null;
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
            if (Settings.outputDir == null || tab.filepath == null || tab.msg != null)
                return;
            string path = Path.Combine(Settings.outputDir, "..\\text\\" + Settings.language + "\\dialog\\");
            if (!Directory.Exists(path)) {
                MessageBox.Show("Failed to open or create associated message file; directory data\\text\\" + Settings.language + "\\dialog does not exist", "Error");
                return;
            }
            path = Path.Combine(path, Path.ChangeExtension(tab.filename, ".msg"));
            if (!File.Exists(path)) {
                if (!create)
                    return;
                else
                    File.Create(path).Close();
            }
            tab.msg = Open(path, OpenType.File, false);
        }

        private bool Compile(TabInfo tab, out string msg, bool showMessages = true, bool preprocess = false)
        {
            msg = "";
            if (Settings.outputDir == null) {
                if (showMessages)
                    MessageBox.Show("No output path selected.\nPlease select your scripts directory before compiling", "Error");
                return false;
            }
            Save(tab);
            if (tab.changed || tab.filepath == null)
                return false;
            if (string.Compare(Path.GetExtension(tab.filename), ".msg", true) == 0) {
                if (showMessages)
                    MessageBox.Show("You cannot compile message files");
                return false;
            }
            List<Error> errors = new List<Error>();
            bool success = Compiler.Compile(currentTab.filepath, out msg, errors, preprocess);
            foreach (ErrorType et in new ErrorType[] { ErrorType.Error, ErrorType.Warning, ErrorType.Message }) {
                foreach (Error e in errors) {
                    if (e.type == et)
                        dgvErrors.Rows.Add(e.type.ToString(), Path.GetFileName(e.fileName), e.line, e);
                }
            }
            if (!success) {
                tabControl2.SelectedIndex = 1;
            } else {
                parserLabel.Text = "Successfully compiled " + currentTab.filename + " at " + DateTime.Now.ToString();
            }
            if (!success && Settings.warnOnFailedCompile) {
                if (showMessages)
                    MessageBox.Show("Script " + tab.filename + " failed to compile. See the output window for details", "Error");
            }
            return success;
        }

        private void UpdateNames()
        {
            if (currentTab == null) {
                return;
            }
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();
            if (currentTab.parseInfo != null && currentTab.shouldParse) {
                foreach (var s in new List<string> { "Procedures", "Variables" }) {
                    var rootNode = treeView1.Nodes.Add(s);
                    rootNode.NodeFont = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold);

                }
                foreach (Procedure p in currentTab.parseInfo.procs) {
                    TreeNode tn = new TreeNode(p.ToString(false));
                    tn.Tag = p;
                    tn.ToolTipText = p.ToString() + "\n : " + p.filename;
                    foreach (Variable var in p.variables) {
                        TreeNode tn2 = new TreeNode(var.name);
                        tn2.Tag = var;
                        tn2.ToolTipText = var.ToString();
                        tn.Nodes.Add(tn2);
                    }
                    treeView1.Nodes[0].Nodes.Add(tn);
                    treeView1.Nodes[0].Expand();
                }
                foreach (Variable var in currentTab.parseInfo.vars) {
                    TreeNode tn = new TreeNode(var.name);
                    tn.Tag = var;
                    tn.ToolTipText = var.ToString();
                    treeView1.Nodes[1].Nodes.Add(tn);
                    treeView1.Nodes[1].Expand();
                }
            }
            treeView1.EndUpdate();
            if (treeView1.Nodes.Count > 0) {
                treeView1.Nodes[0].EnsureVisible();
            }
        }

        private void SelectLine(string file, int line, int column = -1)
        {
            if (Open(file, OpenType.File, false) == null) {
                MessageBox.Show("Could not open file '" + file + "'", "Error");
                return;
            }
            LineSegment ls;
            if (line > currentTab.te.Document.TotalNumberOfLines) {
                ls = currentTab.te.Document.GetLineSegment(currentTab.te.Document.TotalNumberOfLines - 1);
            } else {
                ls = currentTab.te.Document.GetLineSegment(line - 1);
            }
            TextLocation start, end;
            if (column == -1 || column >= ls.Length - 2) {
                start = new TextLocation(0, ls.LineNumber);
            } else {
                start = new TextLocation(column - 1, ls.LineNumber);
            }
            end = new TextLocation(ls.Length, ls.LineNumber);
            currentTab.te.ActiveTextAreaControl.SelectionManager.SetSelection(start, end);
            currentTab.te.ActiveTextAreaControl.Caret.Position = start;
            currentTab.te.ActiveTextAreaControl.ScrollToCaret();
            currentTab.te.ActiveTextAreaControl.Focus();
        }

        private void KeyPressed(object sender, KeyPressEventArgs e)
        {
            if (!Settings.autocomplete)
                return;
            var caret = currentTab.te.ActiveTextAreaControl.Caret;
            if (e.KeyChar == '(') {
                if (lbAutocomplete.Visible) {
                    lbAutocomplete.Hide();
                }
                if (currentTab.parseInfo == null) {
                    return;
                }
                string word = TextUtilities.GetWordAt(currentTab.te.Document, caret.Offset - 2);
                string item = currentTab.parseInfo.LookupToken(word, currentTab.filepath, caret.Line);
                if (item != null) {
                    var pos = caret.ScreenPosition;
                    var tab = tabControl1.TabPages[currentTab.index];
                    pos.Offset(tab.FindForm().PointToClient(tab.Parent.PointToScreen(tab.Location)));
                    pos.Offset(-100, 20);
                    toolTipAC.Show(item, panel1, pos);
                }
            } else if (e.KeyChar == ')') {
                if (toolTipAC.Active)
                    toolTipAC.Hide(panel1);
            } else {
                string word = TextUtilities.GetWordAt(currentTab.te.Document, caret.Offset - 1) + e.KeyChar.ToString();
                if (word != null && word.Length > 1) {
                    var matches = (currentTab.parseInfo != null)
                        ? currentTab.parseInfo.LookupAutosuggest(word)
                        : ProgramInfo.LookupOpcode(word);

                    if (matches.Count > 0) {
                        lbAutocomplete.Items.Clear();
                        var select = currentTab.te.ActiveTextAreaControl.Caret;
                        foreach (string item in matches) {
                            int sep = item.IndexOf("|");
                            AutoCompleteItem acItem = new AutoCompleteItem(item, "");
                            if (sep != -1) {
                                acItem.name = item.Substring(0, sep);
                                acItem.hint = item.Substring(sep + 1);
                            }
                            lbAutocomplete.Items.Add(acItem);
                        }
                        var caretPos = currentTab.te.ActiveTextAreaControl.Caret.ScreenPosition;
                        var tePos = currentTab.te.ActiveTextAreaControl.FindForm().PointToClient(currentTab.te.ActiveTextAreaControl.Parent.PointToScreen(currentTab.te.ActiveTextAreaControl.Location));
                        tePos.Offset(caretPos);
                        tePos.Offset(15, 15);
                        lbAutocomplete.Location = tePos;
                        lbAutocomplete.Height = lbAutocomplete.ItemHeight * (lbAutocomplete.Items.Count + 1);
                        lbAutocomplete.Show();
                        lbAutocomplete.Tag = new KeyValuePair<int, string>(currentTab.te.ActiveTextAreaControl.Caret.Offset + 1, word);
                    } else {
                        lbAutocomplete.Hide();
                    }
                } else if (lbAutocomplete.Visible) {
                    lbAutocomplete.Hide();
                }
            }
        }

        private void textChanged(object sender, EventArgs e)
        {
            if (!currentTab.changed) {
                currentTab.changed = true;
                SetTabText(currentTab.index);
            }
            if (currentTab.shouldParse) {
                if (!currentTab.needsParse) {
                    currentTab.needsParse = true;
                    parserLabel.Text = "Parser: Out of date";
                }
                timerNext = DateTime.Now + TimeSpan.FromSeconds(3);
                if (!timer.Enabled) {
                    timer.Start();
                }
            }
            var caret = currentTab.te.ActiveTextAreaControl.Caret;
            string word = TextUtilities.GetWordAt(currentTab.te.Document, caret.Offset - 1);
            if (word.Length < 2) {
                if (lbAutocomplete.Visible) {
                    lbAutocomplete.Hide();
                }
                if (toolTipAC.Active) {
                    toolTipAC.Hide(panel1);
                }
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

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close(currentTab);
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void tabControl1_Selected(object sender, TabControlEventArgs e)
        {
            if (tabControl1.SelectedIndex == -1) {
                currentTab = null;
                parserLabel.Text = "Parser: No file";
            } else {
                if (currentTab != null) {
                    previousTabIndex = currentTab.index;
                }
                currentTab = tabs[tabControl1.SelectedIndex];
                if (currentTab.msg != null)
                    ParseMessages(currentTab);
                if (currentTab.shouldParse) {
                    if (currentTab.needsParse) {
                        parserLabel.Text = "Parser: Out of date";
                        timerNext = DateTime.Now + TimeSpan.FromSeconds(1);
                        if (!timer.Enabled)
                            timer.Start();
                    } else {
                        parserLabel.Text = "Parser: Up to date";
                        UpdateNames();
                    }
                } else {
                    parserLabel.Text = "Parser: Not an ssl";
                    UpdateNames();
                }
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

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            (new SettingsDialog()).ShowDialog();
            splitContainer2.Panel2Collapsed = !Settings.enableParser;
            parserLabel.Visible = Settings.enableParser;
            if (currentTab != null)
                currentTab.shouldParse = Settings.enableParser;
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
                        if (e.Button == MouseButtons.Middle)
                            tabControl2.TabPages.RemoveAt(i--);
                        else if (e.Button == MouseButtons.Right) {
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
        }

        private void recentItem_Click(object sender, EventArgs e)
        {
            Open(((ToolStripMenuItem)sender).Text, OpenType.File);
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

        private void readmeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(".\\docs\\");
        }

        private void massCompileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Settings.outputDir == null) {
                MessageBox.Show("No output path selected.\nPlease select your scripts directory before compiling", "Error");
                return;
            }
            if (Settings.lastMassCompile != null) {
                fbdMassCompile.SelectedPath = Settings.lastMassCompile;
            }
            if (fbdMassCompile.ShowDialog() != DialogResult.OK) {
                return;
            }
            Settings.lastMassCompile = fbdMassCompile.SelectedPath;
            BatchCompiler.CompileFolder(fbdMassCompile.SelectedPath);
        }

        private void compileAllOpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Text.StringBuilder FullMsg = new System.Text.StringBuilder();
            dgvErrors.Rows.Clear();
            string msg;
            for (int i = 0; i < tabs.Count; i++) {
                FullMsg.AppendLine("*** " + tabs[i].filename);
                Compile(tabs[i], out msg, false);
                FullMsg.AppendLine(msg);
                FullMsg.AppendLine();
            }
            tbOutput.Text = FullMsg.ToString();
        }

        private void cutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab != null) {
                currentTab.te.ActiveTextAreaControl.TextArea.ClipboardHandler.Cut(null, null);
            }
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab != null) {
                currentTab.te.ActiveTextAreaControl.TextArea.ClipboardHandler.Copy(null, null);
            }
        }

        private void pasteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab != null) {
                currentTab.te.ActiveTextAreaControl.TextArea.ClipboardHandler.Paste(null, null);
            }
        }

        private void undoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab != null) {
                currentTab.te.Undo();
            }            
        }

        private void redoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null) {
                return;
            }
            currentTab.te.Redo();
        }

        private void outlineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null) {
                return;
            }
            timer_Tick(null, null);
            foreach (FoldMarker fm in currentTab.te.Document.FoldingManager.FoldMarker) {
                fm.IsFolded = fm.FoldType == FoldType.MemberBody;
            }
            currentTab.te.Document.FoldingManager.NotifyFoldingsChanged(null);
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
            string fName = Path.ChangeExtension(currentTab.filename, "int");
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
            RegisterScript.EditRegistration(fName);
        }

        private void findToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sf == null) {
                sf = new SearchForm();
                sf.FormClosing += delegate(object a1, FormClosingEventArgs a2) { sf = null; };
                sf.KeyUp += delegate(object a1, KeyEventArgs a2) {
                    if (a2.KeyCode == Keys.Escape) {
                        sf.Close();
                    }
                };
                sf.rbFolder.CheckedChanged += delegate(object a1, EventArgs a2) {
                    sf.bChange.Enabled = sf.cbSearchSubfolders.Enabled = sf.rbFolder.Checked;
                    sf.bReplace.Enabled = !sf.rbFolder.Checked;
                };
                sf.tbSearch.KeyPress += delegate(object a1, KeyPressEventArgs a2) { if (a2.KeyChar == '\r') { bSearch_Click(null, null); a2.Handled = true; } };
                sf.bChange.Click += delegate(object a1, EventArgs a2) {
                    if (sf.fbdSearchFolder.ShowDialog() != DialogResult.OK)
                        return;
                    Settings.lastSearchPath = sf.fbdSearchFolder.SelectedPath;
                    sf.label2.Text = Settings.lastSearchPath;
                };
                sf.bSearch.Click += new EventHandler(bSearch_Click);
                sf.bReplace.Click += new EventHandler(bReplace_Click);
                sf.Show();
            } else {
                sf.Focus();
                sf.tbSearch.Focus();
            }
            string str = "";
            if (currentTab != null) {
                str = currentTab.te.ActiveTextAreaControl.SelectionManager.SelectedText;
            }
            if (str.Length == 0 || str.Length > 255) {
                str = Clipboard.GetText();
            }
            if (str.Length > 0 && str.Length < 255) {
                sf.tbSearch.Text = str;
                sf.tbSearch.SelectAll();
            }
        }

        private bool Search(string text, string str, Regex regex, int start, bool restart, out int mstart, out int mlen)
        {
            if (start >= text.Length)
                start = 0;
            mstart = 0;
            mlen = str.Length;
            if (regex != null) {
                Match m = regex.Match(text, start);
                if (m.Success) {
                    mstart = m.Index;
                    mlen = m.Length;
                    return true;
                }
                if (!restart)
                    return false;
                m = regex.Match(text);
                if (m.Success) {
                    mstart = m.Index;
                    mlen = m.Length;
                    return true;
                }
            } else {
                int i = text.IndexOf(str, start, StringComparison.OrdinalIgnoreCase);
                if (i != -1) {
                    mstart = i;
                    return true;
                }
                if (!restart)
                    return false;
                i = text.IndexOf(str, StringComparison.OrdinalIgnoreCase);
                if (i != -1) {
                    mstart = i;
                    return true;
                }
            }
            return false;
        }

        private bool Search(string text, string str, Regex regex)
        {
            if (regex != null) {
                if (regex.IsMatch(text))
                    return true;
            } else {
                if (text.IndexOf(str, StringComparison.OrdinalIgnoreCase) != -1)
                    return true;
            }
            return false;
        }

        private bool SearchAndScroll(TabInfo tab, Regex regex)
        {
            int start, len;
            if (Search(tab.te.Text, sf.tbSearch.Text, regex, tab.te.ActiveTextAreaControl.Caret.Offset + 1, true, out start, out len)) {
                TextLocation locstart = tab.te.Document.OffsetToPosition(start);
                TextLocation locend = tab.te.Document.OffsetToPosition(start + len);
                tab.te.ActiveTextAreaControl.SelectionManager.SetSelection(locstart, locend);
                tab.te.ActiveTextAreaControl.Caret.Position = locstart;
                tab.te.ActiveTextAreaControl.ScrollToCaret();
                return true;
            }
            return false;
        }

        private void SearchForAll(TabInfo tab, Regex regex, DataGridView dgv, List<int> offsets, List<int> lengths)
        {
            int start, len, line, lastline = -1;
            int offset = 0;
            while (Search(tab.te.Text, sf.tbSearch.Text, regex, offset, false, out start, out len)) {
                offset = start + 1;
                line = tab.te.Document.OffsetToPosition(start).Line;
                if (offsets != null) {
                    offsets.Add(start);
                    lengths.Add(len);
                }
                if (line != lastline) {
                    lastline = line;
                    Error error = new Error();
                    error.fileName = tab.filepath;
                    error.line = line + 1;
                    error.msg = TextUtilities.GetLineAsString(tab.te.Document, line);
                    dgv.Rows.Add(tab.filename, error.line.ToString(), error);
                }
            }
        }
        private void SearchForAll(string[] text, string file, Regex regex, DataGridView dgv)
        {
            bool matched;
            for (int i = 0; i < text.Length; i++) {
                if (regex != null) {
                    matched = regex.IsMatch(text[i]);
                } else {
                    matched = text[i].IndexOf(sf.tbSearch.Text, StringComparison.OrdinalIgnoreCase) != -1;
                }
                if (matched) {
                    Error error = new Error();
                    error.fileName = file;
                    error.line = i + 1;
                    error.msg = text[i];
                    dgv.Rows.Add(Path.GetFileName(file), (i + 1).ToString(), error);
                }
            }
        }

        private bool bSearchInternal(List<int> offsets, List<int> lengths)
        {
            Regex regex = null;
            if (sf.cbRegular.Checked) {
                regex = new Regex(sf.tbSearch.Text);
            }
            if (sf.rbFolder.Checked && Settings.lastSearchPath == null) {
                MessageBox.Show("No search path set.", "Error");
                return false;
            }
            if (!sf.cbFindAll.Checked) {
                if (sf.rbCurrent.Checked || (sf.rbAll.Checked && tabs.Count < 2)) {
                    if (currentTab == null) {
                        return false;
                    }
                    if (SearchAndScroll(currentTab, regex)) {
                        return true;
                    }
                } else if (sf.rbAll.Checked) {
                    int starttab = currentTab == null ? 0 : currentTab.index;
                    int endtab = starttab == 0 ? tabs.Count - 1 : starttab - 1;
                    int tab = starttab - 1;
                    do {
                        if (++tab == tabs.Count)
                            tab = 0;
                        if (SearchAndScroll(tabs[tab], regex)) {
                            if (currentTab == null || currentTab.index != tab)
                                tabControl1.SelectTab(tab);
                            return true;
                        }
                    } while (tab != endtab);
                } else {
                    SearchOption so = sf.cbSearchSubfolders.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    List<string> files = new List<string>(Directory.GetFiles(Settings.lastSearchPath, "*.ssl", so));
                    files.AddRange(Directory.GetFiles(Settings.lastSearchPath, "*.h", so));
                    files.AddRange(Directory.GetFiles(Settings.lastSearchPath, "*.msg", so));
                    for (int i = 0; i < files.Count; i++) {
                        string text = File.ReadAllText(files[i]);
                        if (Search(text, sf.tbSearch.Text, regex)) {
                            SearchAndScroll(Open(files[i], OpenType.File), regex);
                            return true;
                        }
                    }
                }
                MessageBox.Show("Search string not found");
                return false;
            } else {
                DataGridViewTextBoxColumn c1 = new DataGridViewTextBoxColumn(), c2 = new DataGridViewTextBoxColumn(), c3 = new DataGridViewTextBoxColumn();
                c1.HeaderText = "File";
                c1.ReadOnly = true;
                c1.Width = 120;
                c2.HeaderText = "Line";
                c2.ReadOnly = true;
                c2.Width = 40;
                c3.HeaderText = "Match";
                c3.ReadOnly = true;
                c3.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                DataGridView dgv = new DataGridView();
                dgv.Name = "dgv";
                dgv.AllowUserToAddRows = false;
                dgv.AllowUserToDeleteRows = false;
                dgv.BackgroundColor = System.Drawing.SystemColors.ControlLight;
                dgv.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
                dgv.Columns.Add(c1);
                dgv.Columns.Add(c2);
                dgv.Columns.Add(c3);
                dgv.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
                dgv.GridColor = System.Drawing.SystemColors.ControlLight;
                dgv.MultiSelect = false;
                dgv.ReadOnly = true;
                dgv.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
                dgv.DoubleClick += new System.EventHandler(this.dgvErrors_DoubleClick);
                dgv.RowHeadersVisible = false;

                if (sf.rbCurrent.Checked || (sf.rbAll.Checked && tabs.Count < 2)) {
                    if (currentTab == null)
                        return false;
                    SearchForAll(currentTab, regex, dgv, offsets, lengths);
                } else if (sf.rbAll.Checked) {
                    for (int i = 0; i < tabs.Count; i++)
                        SearchForAll(tabs[i], regex, dgv, offsets, lengths);
                } else {
                    SearchOption so = sf.cbSearchSubfolders.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    List<string> files = new List<string>(Directory.GetFiles(Settings.lastSearchPath, "*.ssl", so));
                    files.AddRange(Directory.GetFiles(Settings.lastSearchPath, "*.h", so));
                    files.AddRange(Directory.GetFiles(Settings.lastSearchPath, "*.msg", so));
                    for (int i = 0; i < files.Count; i++) {
                        SearchForAll(File.ReadAllLines(files[i]), Path.GetFullPath(files[i]), regex, dgv);
                    }
                }

                TabPage tp = new TabPage("Search results");
                tp.Controls.Add(dgv);
                dgv.Dock = DockStyle.Fill;
                tabControl2.TabPages.Add(tp);
                tabControl2.SelectTab(tp);
                return true;
            }
        }

        private void bSearch_Click(object sender, EventArgs e)
        {
            bSearchInternal(null, null);
        }

        void bReplace_Click(object sender, EventArgs e)
        {
            if (sf.rbFolder.Checked)
                return;
            if (sf.cbFindAll.Checked) {
                List<int> lengths = new List<int>(), offsets = new List<int>();
                if (!bSearchInternal(offsets, lengths))
                    return;
                for (int i = offsets.Count - 1; i >= 0; i--) {
                    currentTab.te.Document.Replace(offsets[i], lengths[i], sf.tbReplace.Text);
                }
            } else {
                if (!bSearchInternal(null, null))
                    return;
                ISelection selected = currentTab.te.ActiveTextAreaControl.SelectionManager.SelectionCollection[0];
                currentTab.te.Document.Replace(selected.Offset, selected.Length, sf.tbReplace.Text);
            }
        }

        private void dgvErrors_DoubleClick(object sender, EventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            if (dgv.SelectedCells.Count != 1)
                return;
            Error error = (Error)dgv.Rows[dgv.SelectedCells[0].RowIndex].Cells[dgv == dgvErrors ? 3 : 2].Value;
            if (error.line != -1) {
                SelectLine(error.fileName, error.line, error.column);
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
            string text = Settings.GetPreprocessedFile();
            if (text != null)
                Open(text, OpenType.Text, false);
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
                Open(Settings.GetOutputPath(currentTab.filepath), OpenType.File, false);
        }

        private void editRegisteredScriptsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RegisterScript.EditRegistration(null);
        }

        private void associateMsgToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null)
                return;
            AssossciateMsg(currentTab, true);
        }

        private void ParseMessages(TabInfo ti)
        {
            ti.messages.Clear();
            char[] split = new char[] { '}' };
            for (int i = 0; i < ti.msg.te.Document.TotalNumberOfLines; i++) {
                string[] line = ti.msg.te.Document.GetText(ti.msg.te.Document.GetLineSegment(i)).Split(split, StringSplitOptions.RemoveEmptyEntries);
                if (line.Length != 3)
                    continue;
                for (int j = 0; j < 3; j += 2) {
                    line[j] = line[j].Trim();
                    if (line[j].Length == 0 || line[j][0] != '{')
                        continue;
                    line[j] = line[j].Substring(1);
                }
                int index;
                if (!int.TryParse(line[0], out index))
                    continue;
                ti.messages[index] = line[2];
            }
        }

        class WorkerArgs
        {
            public readonly string text;
            public readonly TabInfo tab;

            public WorkerArgs(string text, TabInfo tab)
            {
                this.text = text;
                this.tab = tab;
            }
        }

        void timer_Tick(object sender, EventArgs e)
        {
            if (currentTab == null || !currentTab.shouldParse || !Settings.enableParser)
                return;

            if (DateTime.Now > timerNext && !bwSyntaxParser.IsBusy) {
                parserLabel.Text = "Parser: Working";
                parserRunning = true;
                bwSyntaxParser.RunWorkerAsync(new WorkerArgs(currentTab.te.Document.TextContent, currentTab));
                timer.Stop();
            }
        }

        private void bwSyntaxParser_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            WorkerArgs wa = (WorkerArgs)e.Argument;
            ProgramInfo pi = Compiler.Parse(wa.text, wa.tab.filepath);
            if (pi != null) {
                pi.tab = wa.tab;
                e.Result = pi;
            } else {
                e.Result = wa.tab;
            }
            parserRunning = false;
        }

        private void bwSyntaxParser_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            if (File.Exists("errors.txt")) {
                tbOutputParse.Text = File.ReadAllText("errors.txt");
            }
            if (e.Result is TabInfo) {
                if (currentTab == e.Result) {
                    currentTab.needsParse = false;
                    parserLabel.Text = "Parser: Failed preprocessing (see parser errors tab)";
                }
                return;
            }
            ProgramInfo pi = (ProgramInfo)e.Result;
            if (currentTab != pi.tab) return;
            currentTab.parseInfo = pi;
            if (pi.parsed) {
                currentTab.te.Document.FoldingManager.UpdateFoldings(currentTab.filename, pi);
                currentTab.te.Document.FoldingManager.NotifyFoldingsChanged(null);
                UpdateNames();
                parserLabel.Text = "Parser: Complete";
            } else {
                parserLabel.Text = "Parser: Failed parsing";
            }
            currentTab.needsParse = false;
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            string file = null;
            int line = 0;
            if (e.Node.Tag is Variable) {
                Variable var = (Variable)e.Node.Tag;
                file = var.fdeclared;
                line = var.d.declared;
            } else if (e.Node.Tag is Procedure) {
                Procedure proc = (Procedure)e.Node.Tag;
                file = proc.fstart;
                line = proc.d.start;
            }
            if (file != null && line != -1) {
                SelectLine(file, line);
            }
        }

        private void findReferencesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextLocation tl = (TextLocation)editorMenuStrip.Tag;
            string word = TextUtilities.GetWordAt(currentTab.te.Document, currentTab.te.Document.PositionToOffset(tl));

            Ref[] refs = currentTab.parseInfo.LookupReferences(word, currentTab.filename, tl.Line);
            if (refs == null)
                return;
            if (refs.Length == 0) {
                MessageBox.Show("No references found", "Message");
                return;
            }

            DataGridViewTextBoxColumn c1 = new DataGridViewTextBoxColumn(), c2 = new DataGridViewTextBoxColumn(), c3 = new DataGridViewTextBoxColumn();
            c1.HeaderText = "File";
            c1.ReadOnly = true;
            c1.Width = 120;
            c2.HeaderText = "Line";
            c2.ReadOnly = true;
            c2.Width = 40;
            c3.HeaderText = "Match";
            c3.ReadOnly = true;
            c3.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            DataGridView dgv = new DataGridView();
            dgv.AllowUserToAddRows = false;
            dgv.AllowUserToDeleteRows = false;
            dgv.BackgroundColor = System.Drawing.SystemColors.ControlLight;
            dgv.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgv.Columns.Add(c1);
            dgv.Columns.Add(c2);
            dgv.Columns.Add(c3);
            dgv.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
            dgv.GridColor = System.Drawing.SystemColors.ControlLight;
            dgv.MultiSelect = false;
            dgv.ReadOnly = true;
            dgv.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
            dgv.DoubleClick += new System.EventHandler(this.dgvErrors_DoubleClick);
            dgv.RowHeadersVisible = false;

            foreach (Ref r in refs) {
                Error error = new Error();
                error.fileName = r.file;
                error.line = r.line;
                error.msg = string.Compare(Path.GetFileName(r.file), currentTab.filename, true) == 0 ? TextUtilities.GetLineAsString(currentTab.te.Document, r.line - 1) : word;
                dgv.Rows.Add(r.file, error.line.ToString(), error);
            }

            TabPage tp = new TabPage("'" + word + "' references");
            tp.Controls.Add(dgv);
            dgv.Dock = DockStyle.Fill;
            tabControl2.TabPages.Add(tp);
            tabControl2.SelectTab(tp);
        }

        private void findDeclerationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextLocation tl = (TextLocation)editorMenuStrip.Tag;
            string word = TextUtilities.GetWordAt(currentTab.te.Document, currentTab.te.Document.PositionToOffset(tl));
            string file;
            int line;
            currentTab.parseInfo.LookupDecleration(word, currentTab.filename, tl.Line, out file, out line);
            SelectLine(file, line);
        }

        private void findDefinitionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextLocation tl = (TextLocation)editorMenuStrip.Tag;
            string word = TextUtilities.GetWordAt(currentTab.te.Document, currentTab.te.Document.PositionToOffset(tl));
            string file;
            int line;
            currentTab.parseInfo.LookupDefinition(word, out file, out line);
            SelectLine(file, line);
        }

        private void openIncludeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextLocation tl = (TextLocation)editorMenuStrip.Tag;
            string[] line = TextUtilities.GetLineAsString(currentTab.te.Document, tl.Line).Split('"');
            if (line.Length < 2)
                return;
            if (Path.IsPathRooted(line[1]) && File.Exists(line[1]))
                Open(line[1], OpenType.File, false);
            else {
                if (currentTab.filepath == null) {
                    MessageBox.Show("Cannot open includes given via a relative path for an unsaved script", "Error");
                    return;
                }
                Open(Path.Combine(Path.GetDirectoryName(currentTab.filepath), line[1]), OpenType.File, false);
            }
        }

        private void keyDown(object sender, KeyEventArgs e)
        {
            MessageBox.Show("Test " + e.KeyCode);
        }

        private void UpdateEditorToolStripMenu()
        {
            openIncludeToolStripMenuItem.Enabled = false;
            if (currentTab.parseInfo == null) {
                findReferencesToolStripMenuItem.Enabled = false;
                findDeclerationToolStripMenuItem.Enabled = false;
                findDefinitionToolStripMenuItem.Enabled = false;
            } else {
                NameType nt = NameType.None;
                IParserInfo item = null;
                if (treeView1.Focused) {
                    TreeNode node = treeView1.SelectedNode;
                    if (node.Tag is Variable) {
                        Variable var = (Variable)node.Tag;
                        nt = var.Type();
                        item = var;
                    } else if (node.Tag is Procedure) {
                        Procedure proc = (Procedure)node.Tag;
                        nt = proc.Type();
                        item = proc;
                    }
                } else {
                    TextLocation tl = currentTab.te.ActiveTextAreaControl.Caret.Position;
                    editorMenuStrip.Tag = tl;
                    HighlightColor hc = currentTab.te.Document.GetLineSegment(tl.Line).GetColorForPosition(tl.Column);
                    if (hc == null 
                        || hc.Color == System.Drawing.Color.Green 
                        || hc.Color == System.Drawing.Color.Brown 
                        || hc.Color == System.Drawing.Color.DarkGreen) {
                        nt = NameType.None;
                    } else {
                        string word = TextUtilities.GetWordAt(currentTab.te.Document, currentTab.te.Document.PositionToOffset(tl));
                        item = currentTab.parseInfo.Lookup(word, currentTab.filename, tl.Line);
                        if (item != null) {
                            nt = item.Type();
                        }
                        //nt=currentTab.parseInfo.LookupTokenType(word, currentTab.filename, tl.Line);
                    }
                    string line = TextUtilities.GetLineAsString(currentTab.te.Document, tl.Line).Trim();
                    if (line.StartsWith("#include ")) {
                        openIncludeToolStripMenuItem.Enabled = true;
                    }
                }
                switch (nt) {
                    case NameType.LVar:
                    case NameType.GVar:
                        findReferencesToolStripMenuItem.Enabled = true;
                        findDeclerationToolStripMenuItem.Enabled = true;
                        findDefinitionToolStripMenuItem.Enabled = false;
                        break;
                    case NameType.Proc: {
                            Procedure proc = (Procedure)item;
                            findReferencesToolStripMenuItem.Enabled = true;
                            findDeclerationToolStripMenuItem.Enabled = true;
                            findDefinitionToolStripMenuItem.Enabled = !proc.IsImported();
                            break;
                        }
                    case NameType.Macro:
                        findReferencesToolStripMenuItem.Enabled = false;
                        findDeclerationToolStripMenuItem.Enabled = true;
                        findDefinitionToolStripMenuItem.Enabled = false;
                        break;
                    default:
                        findReferencesToolStripMenuItem.Enabled = false;
                        findDeclerationToolStripMenuItem.Enabled = false;
                        findDefinitionToolStripMenuItem.Enabled = false;
                        break;
                }
            }
        }

        private void editorMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (currentTab == null/* && !treeView1.Focused*/) {
                e.Cancel = true;
                return;
            }
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

        private void headsFrmPatcherToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (HeadsFrmPatcher hfp = new HeadsFrmPatcher()) {
                hfp.ShowDialog();
            }
        }

        void GoToLineToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (currentTab == null) {
                return;
            }
            goToLine = new GoToLine();
            AddOwnedForm(goToLine);
            goToLine.tbLine.Tag = currentTab.te.Document.TotalNumberOfLines;
            goToLine.tbLine.Text = "1";
            goToLine.tbLine.SelectAll();
            goToLine.bGo.Click += delegate(object a1, EventArgs a2) {
                TextAreaControl tac = currentTab.te.ActiveTextAreaControl;
                tac.Caret.Line = goToLine.GetLineNumber() - 1;
                tac.Caret.Column = 0;
                tac.ScrollToCaret();
                goToLine.Close();
            };
            goToLine.ShowDialog();
        }

        void UPPERCASEToolStripMenuItemClick(object sender, EventArgs e)
        {
            var action = new ICSharpCode.TextEditor.Actions.ToUpperCase();
            action.Execute(currentTab.te.ActiveTextAreaControl.TextArea);
        }

        void LowecaseToolStripMenuItemClick(object sender, EventArgs e)
        {
            var action = new ICSharpCode.TextEditor.Actions.ToLowerCase();
            action.Execute(currentTab.te.ActiveTextAreaControl.TextArea);
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
                currentTab.te.ActiveTextAreaControl.TextArea.AllowDrop = true;
            }
        }

        void TextEditorDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            if (currentTab != null) {
                currentTab.te.ActiveTextAreaControl.TextArea.AllowDrop = false;
            }
        }

        void CmsAutocompleteOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }

        void LbAutocompleteKeyDown(object snd, KeyEventArgs evt)
        {
            KeyValuePair<int, string> selection = (KeyValuePair<int, string>)lbAutocomplete.Tag;

            if (evt.KeyCode == Keys.Enter && lbAutocomplete.SelectedIndex != -1) {
                AutoCompleteItem item = (AutoCompleteItem)lbAutocomplete.SelectedItem;
                int startOffs = selection.Key - selection.Value.Length;
                currentTab.te.Document.Replace(startOffs, selection.Value.Length, item.name);
                currentTab.te.ActiveTextAreaControl.TextArea.Focus();
                currentTab.te.ActiveTextAreaControl.Caret.Position = currentTab.te.Document.OffsetToPosition(startOffs + item.name.Length);
                lbAutocomplete.Hide();
            } else if (evt.KeyCode == Keys.Escape) {
                currentTab.te.ActiveTextAreaControl.TextArea.Focus();
                currentTab.te.ActiveTextAreaControl.Caret.Position = currentTab.te.Document.OffsetToPosition(selection.Key);
                lbAutocomplete.Hide();
            }
        }

        void LbAutocompleteSelectedIndexChanged(object sender, EventArgs e)
        {
            AutoCompleteItem acItem = (AutoCompleteItem)lbAutocomplete.SelectedItem;
            if (acItem != null) {
                toolTipAC.Show(acItem.hint, panel1, lbAutocomplete.Left + lbAutocomplete.Width + 10, lbAutocomplete.Top, 50000);
            }
        }

        void LbAutocompleteVisibleChanged(object sender, EventArgs e)
        {
            if (toolTipAC.Active) {
                toolTipAC.Hide(panel1);
            }
        }
    }


}