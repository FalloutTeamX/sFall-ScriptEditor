using System;
using System.Text;
using System.Collections.Generic;
using System.Windows.Forms;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Document;
using System.Text.RegularExpressions;
using Path = System.IO.Path;
using File = System.IO.File;
using Directory = System.IO.Directory;
using SearchOption = System.IO.SearchOption;
using ScriptEditor.CodeTranslation;
using ScriptEditor.TextEditorUI;
using System.Drawing;

namespace ScriptEditor
{
    partial class TextEditor : Form
    {
        private const string parseoff = "Parser: Disabled";
        private const string unsaved = "unsaved.ssl";
        private readonly List<string> TREEPROCEDURES = new List<string>{ "Global Procedures", "Local Script Procedures" };
        private readonly List<string> TREEVARIABLES = new List<string>{ "Global Variables", "Local Script Variables" };

        private DateTime timerNext;
        private DateTime timer2Next;
        private Timer timer;
        private Timer timer2;
        private readonly List<TabInfo> tabs = new List<TabInfo>();
        private TabInfo currentTab;
        private ToolStripLabel parserLabel;
        private volatile bool parserRunning;

        private SearchForm sf;
        private GoToLine goToLine;
        private int previousTabIndex = -1;
        private int minimizelogsize;
        public static string sHeaderfile;
        private PositionType PosChangeType;
        private int moveActive = -1;

        private Encoding EncCodePage = (Settings.encoding == 1) ? Encoding.GetEncoding("cp866") : Encoding.Default;

        private TreeView VarTree = new TreeView();
        private TabPage VarTab = new TabPage("Variables");

        public TextEditor()
        {
            InitializeComponent();
            Settings.SetupWindowPosition(SavedWindows.Main, this);
            SearchTextComboBox.Items.AddRange(File.ReadAllLines(Settings.SearchHistoryPath));
            SearchToolStrip.Visible = false;
            defineToolStripMenuItem.Checked = Settings.allowDefine;
            if (Settings.encoding == 1) EncodingDOSmenuItem.Checked = true;
            // highlighting
            FileSyntaxModeProvider fsmProvider = new FileSyntaxModeProvider(Settings.ResourcesFolder); // Create new provider with the highlighting directory.
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
            VarTree.AfterSelect += TreeView_AfterSelect;
            VarTree.Dock = DockStyle.Fill;
            VarTab.Padding = new Padding(3, 3, 3, 3);
            VarTab.BackColor = SystemColors.ControlLightLight;
            VarTab.Controls.Add(VarTree);
            if (Settings.PathScriptsHFile == null) {
                Headers_toolStripSplitButton.Enabled = false;
            }
            HandlerProcedure.CreateProcHandlers(ProcMnContext, this);
            ProgramInfo.LoadOpcodes();
        }

        private void CreateTabVarTree()
        {
            tabControl3.TabPages.Insert(1, VarTab);
        }

#if !DEBUG
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == SingleInstanceManager.WM_SFALL_SCRIPT_EDITOR_OPEN) {
                ShowMe();
                var commandLineArgs = SingleInstanceManager.LoadCommandLine();
                foreach (var file in commandLineArgs) {
                    Open(file, OpenType.File);
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
#else
        void DEBUGINFO(string line)
        {
            tbOutput.Text = line + "\r\n" + tbOutput.Text;
        }
#endif

        private void TextEditor_Load(object sender, EventArgs e)
        {
            if (!Settings.showLog){
                showLogWindowToolStripMenuItem.Checked = Settings.showLog;
                splitContainer1.Panel2Collapsed = true;
            }
            splitContainer1.SplitterDistance = Size.Height;
            minimizelogsize = Size.Height-(Size.Height/5);
            if (Settings.editorSplitterPosition2 != -1) {
                splitContainer2.SplitterDistance = Settings.editorSplitterPosition2;
            }
            splitContainer2.Panel2Collapsed = true;
            if (Settings.enableParser) CreateTabVarTree();
        }

        private void TextEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (sf != null) {
                sf.Close();
            }
            System.IO.StreamWriter sw = new System.IO.StreamWriter(Settings.SearchHistoryPath);
            foreach (var item in SearchTextComboBox.Items) sw.WriteLine(item.ToString());
            sw.Close();
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
            int count = Open_toolStripSplitButton.DropDownItems.Count-1;
            for (int i = 3; i <= count; i++) {
                Open_toolStripSplitButton.DropDownItems.RemoveAt(3);
            }
            for (int i = items.Length - 1; i >= 0; i--) {
                Open_toolStripSplitButton.DropDownItems.Add(items[i], null, recentItem_Click);
            }
        }

        private void SetTabText(int i)
        {
            //tabControl1.TabPages[i].Text = tabs[i].filename + (tabs[i].changed ? " *" : "");
            tabControl1.TabPages[i].ToolTipText = tabs[i].filepath;
            tabControl1.TabPages[i].ImageIndex = (tabs[i].changed ? 1 : 0);
        }

        public enum OpenType { None, File, Text }

        public TabInfo Open(string file, OpenType type, bool addToMRU = true, bool alwaysNew = false, bool recent = false, bool seltab = true)
        {
            if (type == OpenType.File) {
                if (!Path.IsPathRooted(file)) {
                    file = Path.GetFullPath(file);
                }
                // Check recent file
                bool Exists = false;
                if (File.Exists(file)) {
                    Exists = true;
                    recent = false; // false - delete not found file from recent list
                }
                //Add this file to the recent files list
                if (addToMRU) {
                    Settings.AddRecentFile(file, recent);
                    UpdateRecentList();
                    if (recent) {
                        MessageBox.Show("This file was not found.", "Error");
                    }
                }
                if (!Exists) return null;
                //If this is an int, decompile
                if (string.Compare(Path.GetExtension(file), ".int", true) == 0) {
                    var compiler = new Compiler();
                    string decomp = compiler.Decompile(file);
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
                            if (seltab) tabControl1.SelectTab(i);
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
            if (Settings.encoding == 1) te.Document.TextEditorProperties.Encoding = System.Text.Encoding.GetEncoding("cp866");
            if (type == OpenType.File && string.Compare(Path.GetExtension(file), ".msg", true) == 0) {
                te.SetHighlighting("msg");
            } else
                te.SetHighlighting("ssl"); // Activate the highlighting, use the name from the SyntaxDefinition node.
            if (type == OpenType.File)
                te.LoadFile(file, false, true);
            else if (type == OpenType.Text)
                te.Text = file;
            te.TextChanged += textChanged;
            te.ActiveTextAreaControl.TextArea.MouseDown += delegate(object a1, MouseEventArgs a2) {
                if (a2.Button == MouseButtons.Left)
                    UpdateEditorToolStripMenu();
                lbAutocomplete.Hide();
            };
            te.ActiveTextAreaControl.TextArea.KeyPress += KeyPressed;
            te.HorizontalScroll.Visible = false;
            te.ActiveTextAreaControl.TextArea.PreviewKeyDown += delegate(object sender, PreviewKeyDownEventArgs a2) {
                PosChangeType = PositionType.SaveChange; // Save position change for navigation, if key was pressed
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
            te.ActiveTextAreaControl.Caret.PositionChanged += new EventHandler(Caret_PositionChanged);
            TabInfo ti = new TabInfo();
            ti.history.linePosition = new TextLocation[0];
            ti.history.pointerCur = -1;
            ti.textEditor = te;
            ti.changed = false;
            if (type == OpenType.File ) { //&& !alwaysNew
                if (alwaysNew) {
                    string temp = Path.Combine(Settings.SettingsFolder, unsaved);
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
            te.ActiveTextAreaControl.TextArea.ToolTipRequest += new ToolTipRequestEventHandler(TextArea_ToolTipRequest);
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
                    ti.shouldParse = true;
                    ti.needsParse = true; // set 'true' only edit text
                    if (Settings.autoOpenMsgs && ti.filepath != null) 
                        AssossciateMsg(ti, false);
                    FirstParseScript(ti); // First Parse
                }
            }
            if (tabControl1.TabPages.Count > 1) {
                if (seltab) tabControl1.SelectTab(tp);
            } else {
                tabControl1_Selected(null, null);
            }
            return ti;
        }

        private void EnableFormControls()
        {
            TabClose_button.Visible = true;
            Split_button.Visible = true;
            splitDocumentToolStripMenuItem.Enabled = true;
            openAllIncludesScriptToolStripMenuItem.Enabled = true;
            GotoProc_StripButton.Enabled = true;
            Search_toolStripButton.Enabled = true;
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
            if (hc == null || hc.Color == System.Drawing.Color.Green || hc.Color == System.Drawing.Color.Brown || hc.Color == System.Drawing.Color.DarkGreen)
                return;
            string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, currentTab.textEditor.Document.PositionToOffset(e.LogicalPosition));
            if (word.Length == 0 ) return;
            if (currentTab.msgFileTab != null) {
                int msg;
                if (int.TryParse(word, out msg) && currentTab.messages.ContainsKey(msg)) {
                    e.ShowToolTip(currentTab.messages[msg]);
                    return;
                }
            }
            string lookup = ProgramInfo.LookupOpcodesToken(word); // show opcodes help
            if (lookup == null && currentTab.parseInfo != null ) lookup = currentTab.parseInfo.LookupToken(word, currentTab.filepath, e.LogicalPosition.Line + 1);
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
                File.WriteAllText(tab.filepath, tab.textEditor.Text, EncCodePage);
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
            if (Settings.outputDir == null || tab.filepath == null || tab.msgFileTab != null)
                return;
            string path = Path.Combine(Settings.outputDir, "..\\text\\" + Settings.language + "\\dialog\\");
            if (!Directory.Exists(path)) {
                MessageBox.Show("Failed to open or create associated message file in directory\r\n" + path, "Error: Directory does not exist");
                return;
            }
            path = Path.Combine(path, Path.ChangeExtension(tab.filename, ".msg"));
            if (!File.Exists(path)) {
                if (!create) return;
                else {
                    if (MessageBox.Show("The associated message file this script could not be found.\r\nDo you want to create a new file?", "Warning", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                        File.Create(path).Close();
                    } 
                }       
            }
            tab.msgFileTab = Open(path, OpenType.File, false);
        }

        private bool Compile(TabInfo tab, out string msg, bool showMessages = true, bool preprocess = false)
        {
            msg = "";
            if (Settings.outputDir == null) {
                if (showMessages)
                    MessageBox.Show("No output path selected.\nPlease select your scripts directory before compiling", "Compile Error");
                return false;
            }
            if (tab.changed) Save(tab);
            if (tab.changed || tab.filepath == null)
                return false;
            if (string.Compare(Path.GetExtension(tab.filename), ".ssl", true) != 0) {
                if (showMessages)
                    MessageBox.Show("You cannot compile this file.", "Compile Error");
                return false;
            }
            List<Error> errors = new List<Error>();
            var compiler = new Compiler();
            string file = compiler.OverrideIncludeSSLCompile(tab.filepath);
            bool success = compiler.Compile(file, out msg, errors, preprocess);
            if (Settings.overrideIncludesPath) File.Delete(Settings.SettingsFolder + '\\' + Path.GetFileName(file));
            foreach (ErrorType et in new ErrorType[] { ErrorType.Error, ErrorType.Warning, ErrorType.Message }) {
                foreach (Error e in errors) {
                    if (e.type == et)
                        dgvErrors.Rows.Add(e.type.ToString(), Path.GetFileName(e.fileName), e.line, e);
                }
            }
            if (!success) {
                tabControl2.SelectedIndex = 1;
                maximize_log();
                if (showMessages && Settings.warnOnFailedCompile) {
                    MessageBox.Show("Script " + tab.filename + " failed to compile.\r\nSee the output window for details", "Compile Script Error");
                } else {
                    parserLabel.Text = "Failed to compiled: " + currentTab.filename;
                    parserLabel.ForeColor = Color.Firebrick;
                }
            } else {
                parserLabel.Text = "Successfully compiled: " + currentTab.filename + " at " + DateTime.Now.ToString();
                parserLabel.ForeColor = Color.DarkGreen;
            }
            return success;
        }

        // Create names for procedures and variables in treeview
        private void UpdateNames()
        {
            if (currentTab == null) return;
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
                foreach (Procedure p in currentTab.parseInfo.procs) {
                    TreeNode tn = new TreeNode(p.name); //TreeNode(p.ToString(false));
                    tn.Tag = p;
                    foreach (Variable var in p.variables) {
                        TreeNode tn2 = new TreeNode(var.name);
                        tn2.Tag = var;
                        tn2.ToolTipText = var.ToString();
                        tn.Nodes.Add(tn2);
                    }
                    if (p.filename.ToLower() != currentTab.filename.ToLower()) {
                        tn.ToolTipText = p.ToString() + "\ndefine file: " + p.filename;
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
                            tn.ToolTipText = var.ToString() + "\ndefine file: " + var.filename;
                            VarTree.Nodes[0].Nodes.Add(tn);
                            VarTree.Nodes[0].Expand();
                        } else {
                            tn.ToolTipText = var.ToString();
                            VarTree.Nodes[1].Nodes.Add(tn);
                            VarTree.Nodes[1].Expand();
                        }
                    }
                }
            }
            VarTree.EndUpdate();
            ProcTree.EndUpdate();
            if (ProcTree.Nodes.Count > 1) {
                ProcTree.Nodes[1].EnsureVisible();
            }
        }

        // Click on node tree Procedures/Variables
        private void TreeView_AfterSelect(object sender, TreeViewEventArgs e)
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

        // Goto script text of selected Variable or Procedure in treeview
        private void SelectLine(string file, int line, int column = -1)
        {
            bool not_this = false;
            if (file != currentTab.filepath) {
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
            } else {
                start = new TextLocation(column - 1, ls.LineNumber);
            }
            // Expand closed folding procedure (�������� ��� ������ ������� � �� ����� foreach)
            foreach (FoldMarker fm in currentTab.textEditor.Document.FoldingManager.FoldMarker) {
                if (fm.FoldType == FoldType.MemberBody && fm.StartLine == start.Line) {
                    fm.IsFolded = false;
                    break;
                }
            }
            // Scroll to end text document
            //currentTab.textEditor.ActiveTextAreaControl.ScrollTo(currentTab.textEditor.Document.TotalNumberOfLines);
            // Scroll and select procedure
            end = new TextLocation(ls.Length, ls.LineNumber);
            currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SetSelection(start, end);
            currentTab.textEditor.ActiveTextAreaControl.Caret.Position = start;
            if (!not_this) { // fix bug - Focus does not on control
                currentTab.textEditor.ActiveTextAreaControl.CenterViewOn(start.Line + 10, 0);
                currentTab.textEditor.ActiveTextAreaControl.Focus();
            } else currentTab.textEditor.ActiveTextAreaControl.CenterViewOn(start.Line - 15, 0); 
            currentTab.textEditor.Focus();
        }

        private void KeyPressed(object sender, KeyPressEventArgs e)
        {
            if (!Settings.autocomplete)
                return;
            var caret = currentTab.textEditor.ActiveTextAreaControl.Caret;
            if (e.KeyChar == '(') {
                if (lbAutocomplete.Visible) {
                    lbAutocomplete.Hide();
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
            } else if (e.KeyChar == ')') {
                if (toolTipAC.Active)
                    toolTipAC.Hide(panel1);
            } else {
                string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, caret.Offset - 1) + e.KeyChar.ToString();
                if (word != null && word.Length > 1) {
                    var matches = (currentTab.parseInfo != null)
                        ? currentTab.parseInfo.LookupAutosuggest(word)
                        : ProgramInfo.LookupOpcode(word);

                    if (matches.Count > 0) {
                        lbAutocomplete.Items.Clear();
                        var select = currentTab.textEditor.ActiveTextAreaControl.Caret;
                        foreach (string item in matches) {
                            int sep = item.IndexOf("|");
                            AutoCompleteItem acItem = new AutoCompleteItem(item, "");
                            if (sep != -1) {
                                acItem.name = item.Substring(0, sep);
                                acItem.hint = item.Substring(sep + 1);
                            }
                            lbAutocomplete.Items.Add(acItem);
                        }
                        var caretPos = currentTab.textEditor.ActiveTextAreaControl.Caret.ScreenPosition;
                        var tePos = currentTab.textEditor.ActiveTextAreaControl.FindForm().PointToClient(currentTab.textEditor.ActiveTextAreaControl.Parent.PointToScreen(currentTab.textEditor.ActiveTextAreaControl.Location));
                        tePos.Offset(caretPos);
                        tePos.Offset(15, 15);
                        lbAutocomplete.Location = tePos;
                        lbAutocomplete.Height = lbAutocomplete.ItemHeight * (lbAutocomplete.Items.Count + 1);
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

#region ParseFunction
        // Parse first open script
        private void FirstParseScript(TabInfo cTab)
        {
            Parser.InternalParser(cTab);
            cTab.textEditor.Document.FoldingManager.UpdateFoldings(cTab.filename, cTab.parseInfo);
            cTab.textEditor.Document.FoldingManager.NotifyFoldingsChanged(null);
        }

        private void ParseScript(int delay = 1)
        {
            if (!Settings.enableParser) {
                if (delay > 1) timer2Next = DateTime.Now + TimeSpan.FromSeconds(2);
                if (!timer2.Enabled) timer2.Start();
            }
            timerNext = DateTime.Now + TimeSpan.FromSeconds(delay);
            if (!timer.Enabled) timer.Start(); // External Parser begin
        }

        // Delay timer for internal parsing
        void timer2_Tick(object sender, EventArgs e)
        {
            if (currentTab == null /*|| !currentTab.shouldParse*/) {
                timer2.Stop();
                return;
            }
            if (DateTime.Now > timer2Next) {
                timer2.Stop();
                Parser.InternalParser(currentTab);
                currentTab.textEditor.Document.FoldingManager.UpdateFoldings(currentTab.filename, currentTab.parseInfo);
                currentTab.textEditor.Document.FoldingManager.NotifyFoldingsChanged(null);
                UpdateNames();
            }
        }

        private void ParseMessages(TabInfo ti)
        {
            ti.messages.Clear();
            char[] split = new char[] { '}' };
            for (int i = 0; i < ti.msgFileTab.textEditor.Document.TotalNumberOfLines; i++)
            {
                string[] line = ti.msgFileTab.textEditor.Document.GetText(ti.msgFileTab.textEditor.Document.GetLineSegment(i)).Split(split, StringSplitOptions.RemoveEmptyEntries);
                if (line.Length != 3)
                    continue;
                for (int j = 0; j < 3; j += 2)
                {
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

        // Timer for parsing
        void timer_Tick(object sender, EventArgs e)
        {
            if (currentTab == null || !currentTab.shouldParse) {
                timer.Stop();
                return;
            }
            if (DateTime.Now > timerNext && !bwSyntaxParser.IsBusy){
                parserLabel.Text = (Settings.enableParser) ? "Parser: Working" : "Parser: Get only macros";
                parserRunning = true;
                bwSyntaxParser.RunWorkerAsync(new WorkerArgs(currentTab.textEditor.Document.TextContent, currentTab));
                timer.Stop();
            }
        }
        
        // Parse Start
        private void bwSyntaxParser_DoWork(object sender, System.ComponentModel.DoWorkEventArgs eventArgs)
        {
            WorkerArgs args = (WorkerArgs)eventArgs.Argument;
            var compiler = new Compiler();
            args.tab.parseInfo = compiler.Parse(args.text, args.tab.filepath, args.tab.parseInfo);
            eventArgs.Result = args.tab;
            parserRunning = false;
        }
        
        // Parse Stop
        private void bwSyntaxParser_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            if (File.Exists("errors.txt")){
                tbOutputParse.Text = File.ReadAllText("errors.txt");
                File.Delete("errors.txt");
            }
            if (!(e.Result is TabInfo)) {
                throw new Exception("TabInfo is expected!");
            }
            var tab = e.Result as TabInfo;
            if (currentTab == tab) {
                if (tab.filepath != null) {
                    if (tab.parseInfo.parsed) {
                        currentTab.textEditor.Document.FoldingManager.UpdateFoldings(currentTab.filename, tab.parseInfo);
                        currentTab.textEditor.Document.FoldingManager.NotifyFoldingsChanged(null);
                        Outline_toolStripButton.Enabled = true;
                        UpdateNames(); // Update Tree Variables/Pocedures
                        parserLabel.Text = (Settings.enableParser) ? "Parser: Complete": parseoff + " [only macros]";
                        currentTab.needsParse = false;
                    } else {
                        parserLabel.Text = (Settings.enableParser) ? "Parser: Failed parsing (see parser errors tab)" : parseoff + " [only macros]";
                        //currentTab.needsParse = true; // ��������� ����������
                    }
                } else {
                    parserLabel.Text = (Settings.enableParser) ? "Parser: Only local macros" : parseoff;
                }
            }
        }

        private void textChanged(object sender, EventArgs e)
        {
            if (!currentTab.changed) {
                currentTab.changed = true;
                SetTabText(currentTab.index);
            }
            if (currentTab.shouldParse /*&& Settings.enableParser*/) { // if the parser is disabled then nothing
                if (currentTab.shouldParse && !currentTab.needsParse) {
                    currentTab.needsParse = true;
                    parserLabel.Text = "Parser: Out of date";
                }
                // Update parse info
                ParseScript(4);
            }
            var caret = currentTab.textEditor.ActiveTextAreaControl.Caret;
            string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, caret.Offset - 1);
            if (word.Length < 2) {
                if (lbAutocomplete.Visible) {
                    lbAutocomplete.Hide();
                }
                if (toolTipAC.Active) {
                    toolTipAC.Hide(panel1);
                }
            }
        }
#endregion

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
                }
                currentTab = tabs[tabControl1.SelectedIndex];
                if (!Settings.enableParser && currentTab.parseInfo != null) currentTab.parseInfo.parseData = false;
                if (currentTab.msgFileTab != null) ParseMessages(currentTab);
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
            }
        }

        private void ControlFormStateOn_Off()
        {
            if (currentTab.parseInfo != null && currentTab.parseInfo.procs.Length > 0) {
                Outline_toolStripButton.Enabled = true;
            } else Outline_toolStripButton.Enabled = false; SetBackForwardButtonState();
            string ext = Path.GetExtension(currentTab.filename).ToLower();
            if (ext == ".ssl" || ext == ".h") {
                DecIndentStripButton.Enabled = true;
                CommentStripButton.Enabled = true;
                UnCommentStripButton.Enabled = true;
            } else {
                DecIndentStripButton.Enabled = false;
                CommentStripButton.Enabled = false;
                UnCommentStripButton.Enabled = false;
            }
        }

        // No selected text tabs
        private void SetFormControlsOff() {
            splitContainer2.Panel2Collapsed = true;
            TabClose_button.Visible = false;
            openAllIncludesScriptToolStripMenuItem.Enabled = false;
            Split_button.Visible = false;
            splitDocumentToolStripMenuItem.Enabled = false;
            Back_toolStripButton.Enabled = false;
            Forward_toolStripButton.Enabled = false;
            GotoProc_StripButton.Enabled = false;
            Search_toolStripButton.Enabled = false;
            if (SearchToolStrip.Visible) Search_Panel(null, null);
            DecIndentStripButton.Enabled = false;
            CommentStripButton.Enabled = false;
            UnCommentStripButton.Enabled = false;
        }

# region SearchFunction
        private bool Search(string text, string str, Regex regex, int start, bool restart, out int mstart, out int mlen)
        {
            if (start >= text.Length) start = 0;
            mstart = 0;
            mlen = str.Length;
            if (regex != null) {
                Match m = regex.Match(text, start);
                if (m.Success) {
                    mstart = m.Index;
                    mlen = m.Length;
                    return true;
                }
                if (!restart) return false;
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
                if (!restart) return false;
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
            if (Search(tab.textEditor.Text, sf.tbSearch.Text, regex, tab.textEditor.ActiveTextAreaControl.Caret.Offset + 1, true, out start, out len)) {
                FindSelected(tab, start, len);
                return true;
            }
            return false;
        }

        private void FindSelected(TabInfo tab, int start, int len, string replace = null)
        {
            PosChangeType = PositionType.NoSave;
            TextLocation locstart = tab.textEditor.Document.OffsetToPosition(start);
            TextLocation locend = tab.textEditor.Document.OffsetToPosition(start + len);
            tab.textEditor.ActiveTextAreaControl.SelectionManager.SetSelection(locstart, locend);
            if (replace != null) {
                tab.textEditor.ActiveTextAreaControl.Document.Replace(start, len, replace);
                locend = tab.textEditor.Document.OffsetToPosition(start + replace.Length);
                tab.textEditor.ActiveTextAreaControl.SelectionManager.SetSelection(locstart, locend);
            }
            tab.textEditor.ActiveTextAreaControl.Caret.Position = locstart;
            tab.textEditor.ActiveTextAreaControl.CenterViewOn(locstart.Line, 0);
        }
        
        private void SearchForAll(TabInfo tab, Regex regex, DataGridView dgv, List<int> offsets, List<int> lengths)
        {
            int start, len, line, lastline = -1;
            int offset = 0;
            while (Search(tab.textEditor.Text, sf.tbSearch.Text, regex, offset, false, out start, out len))
            {
                offset = start + 1;
                line = tab.textEditor.Document.OffsetToPosition(start).Line;
                if (offsets != null) {
                    offsets.Add(start);
                    lengths.Add(len);
                }
                if (line != lastline) {
                    lastline = line;
                    var message = TextUtilities.GetLineAsString(tab.textEditor.Document, line);
                    Error error = new Error(message, tab.filepath, line + 1);
                    dgv.Rows.Add(tab.filename, error.line.ToString(), error);
                    maximize_log();
                }
            }
        }

        private void SearchForAll(string[] text, string file, Regex regex, DataGridView dgv)
        {
            bool matched;
            for (int i = 0; i < text.Length; i++)
            {
                if (regex != null) {
                    matched = regex.IsMatch(text[i]);
                } else {
                    matched = text[i].IndexOf(sf.tbSearch.Text, StringComparison.OrdinalIgnoreCase) != -1;
                }
                if (matched) {
                    Error error = new Error(text[i], file, i + 1);
                    dgv.Rows.Add(Path.GetFileName(file), (i + 1).ToString(), error);
                }
            }
        }

        private bool bSearchInternal(List<int> offsets, List<int> lengths)
        {
            Regex regex = null;
            if (sf.cbRegular.Checked)
            {
                regex = new Regex(sf.tbSearch.Text);
            }
            if (sf.rbFolder.Checked && Settings.lastSearchPath == null)
            {
                MessageBox.Show("No search path set.", "Error");
                return false;
            }
            if (!sf.cbFindAll.Checked)
            {
                if (sf.rbCurrent.Checked || (sf.rbAll.Checked && tabs.Count < 2))
                {
                    if (currentTab == null)
                    {
                        return false;
                    }
                    if (SearchAndScroll(currentTab, regex))
                    {
                        return true;
                    }
                }
                else if (sf.rbAll.Checked)
                {
                    int starttab = currentTab == null ? 0 : currentTab.index;
                    int endtab = starttab == 0 ? tabs.Count - 1 : starttab - 1;
                    int tab = starttab - 1;
                    do
                    {
                        if (++tab == tabs.Count)
                            tab = 0;
                        if (SearchAndScroll(tabs[tab], regex))
                        {
                            if (currentTab == null || currentTab.index != tab)
                                tabControl1.SelectTab(tab);
                            return true;
                        }
                    } while (tab != endtab);
                }
                else
                {
                    SearchOption so = sf.cbSearchSubfolders.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    List<string> files = new List<string>(Directory.GetFiles(Settings.lastSearchPath, "*.ssl", so));
                    files.AddRange(Directory.GetFiles(Settings.lastSearchPath, "*.h", so));
                    files.AddRange(Directory.GetFiles(Settings.lastSearchPath, "*.msg", so));
                    for (int i = 0; i < files.Count; i++)
                    {
                        string text = File.ReadAllText(files[i]);
                        if (Search(text, sf.tbSearch.Text, regex))
                        {
                            SearchAndScroll(Open(files[i], OpenType.File), regex);
                            return true;
                        }
                    }
                }
                MessageBox.Show("Search string not found");
                return false;
            }
            else
            {
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
                dgv.BackgroundColor = SystemColors.ControlLight;
                dgv.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
                dgv.Columns.Add(c1);
                dgv.Columns.Add(c2);
                dgv.Columns.Add(c3);
                dgv.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
                dgv.GridColor = SystemColors.ControlLight;
                dgv.MultiSelect = false;
                dgv.ReadOnly = true;
                dgv.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
                dgv.DoubleClick += new System.EventHandler(this.dgvErrors_DoubleClick);
                dgv.RowHeadersVisible = false;

                if (sf.rbCurrent.Checked || (sf.rbAll.Checked && tabs.Count < 2))
                {
                    if (currentTab == null)
                        return false;
                    SearchForAll(currentTab, regex, dgv, offsets, lengths);
                }
                else if (sf.rbAll.Checked)
                {
                    for (int i = 0; i < tabs.Count; i++)
                        SearchForAll(tabs[i], regex, dgv, offsets, lengths);
                }
                else
                {
                    SearchOption so = sf.cbSearchSubfolders.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    List<string> files = new List<string>(Directory.GetFiles(Settings.lastSearchPath, "*.ssl", so));
                    files.AddRange(Directory.GetFiles(Settings.lastSearchPath, "*.h", so));
                    files.AddRange(Directory.GetFiles(Settings.lastSearchPath, "*.msg", so));
                    for (int i = 0; i < files.Count; i++)
                    {
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

        private int SearchPanel(string text, string find, int start, bool icase, bool back = false)
        {
            int z = -1;
            if (!icase) {
                if (back) z = text.LastIndexOf(find, start, StringComparison.OrdinalIgnoreCase); 
                else z = text.IndexOf(find, start, StringComparison.OrdinalIgnoreCase);
            } else {
                if (back) z = text.LastIndexOf(find, start);
                else z= text.IndexOf(find, start);
            }
            return z; 
        }
#endregion

        private void UpdateEditorToolStripMenu()
        {
            openIncludeToolStripMenuItem.Enabled = false;
            if (currentTab.parseInfo == null)
            {
                findReferencesToolStripMenuItem.Enabled = false;
                findDeclerationToolStripMenuItem.Enabled = false;
                findDefinitionToolStripMenuItem.Enabled = false;
            }
            else
            {
                NameType nt = NameType.None;
                IParserInfo item = null;
                if (ProcTree.Focused)
                {
                    TreeNode node = ProcTree.SelectedNode;
                    if (node.Tag is Variable)
                    {
                        Variable var = (Variable)node.Tag;
                        nt = var.Type();
                        item = var;
                    }
                    else if (node.Tag is Procedure)
                    {
                        Procedure proc = (Procedure)node.Tag;
                        nt = proc.Type();
                        item = proc;
                    }
                }
                else
                {
                    TextLocation tl = currentTab.textEditor.ActiveTextAreaControl.Caret.Position;
                    editorMenuStrip.Tag = tl;
                    HighlightColor hc = currentTab.textEditor.Document.GetLineSegment(tl.Line).GetColorForPosition(tl.Column);
                    if (hc == null
                        || hc.Color == System.Drawing.Color.Green
                        || hc.Color == System.Drawing.Color.Brown
                        || hc.Color == System.Drawing.Color.DarkGreen)
                    {
                        nt = NameType.None;
                    }
                    else
                    {
                        string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, currentTab.textEditor.Document.PositionToOffset(tl));
                        item = currentTab.parseInfo.Lookup(word, currentTab.filename, tl.Line);
                        if (item != null)
                        {
                            nt = item.Type();
                        }
                        //nt=currentTab.parseInfo.LookupTokenType(word, currentTab.filename, tl.Line);
                    }
                    string line = TextUtilities.GetLineAsString(currentTab.textEditor.Document, tl.Line).Trim();
                    if (line.StartsWith("#include "))
                    {
                        openIncludeToolStripMenuItem.Enabled = true;
                    }
                }
                switch (nt)
                {
                    case NameType.LVar:
                    case NameType.GVar:
                        findReferencesToolStripMenuItem.Enabled = true;
                        findDeclerationToolStripMenuItem.Enabled = true;
                        findDefinitionToolStripMenuItem.Enabled = false;
                        break;
                    case NameType.Proc:
                        {
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
/*
 * 
 * 
 * 
 *     MENU EVENTS 
 * 
 * 
 * 
 */
#region Menu control events
        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            (new SettingsDialog()).ShowDialog();
            if (!Settings.enableParser){
                parserLabel.Text = parseoff;
                if (tabControl3.TabPages.Count > 2 ) {
                    if (currentTab == null) {
                        tabControl3.TabPages.RemoveAt(1);
                    } else if (!currentTab.parseInfo.parseData) {
                        tabControl3.TabPages.RemoveAt(1);
                    }
                }
            } else {
                if (tabControl3.TabPages.Count < 3){
                    CreateTabVarTree();
                    parserLabel.Text = "Parser: Enabled";
                }
            }
            if (Settings.PathScriptsHFile != null) {
                Headers_toolStripSplitButton.Enabled = true;
            }
            if (currentTab != null) {
                //currentTab.shouldParse = true;
                //if (currentTab.filepath != null) currentTab.needsParse = true;
                tabControl1_Selected(null, null);
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
            }            
        }

        private void redoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null) {
                return;
            }
            currentTab.textEditor.Redo();
        }

        private void outlineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null) {
                return;
            }
            foreach (FoldMarker fm in currentTab.textEditor.Document.FoldingManager.FoldMarker) {
                if (fm.FoldType == FoldType.MemberBody) {
                    fm.IsFolded = !fm.IsFolded;
                }
            }
            currentTab.textEditor.Document.FoldingManager.NotifyFoldingsChanged(null);
            currentTab.textEditor.ActiveTextAreaControl.CenterViewOn(currentTab.textEditor.ActiveTextAreaControl.Caret.Position.Line, 0);
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
#endregion

#region Search&Replace function
        private void findToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null) { return; }
            if (sf == null)
            {
                sf = new SearchForm();
                sf.Owner = this;
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
                    sf.bcChange = true;
                    if (sf.fbdSearchFolder.ShowDialog() != DialogResult.OK)
                        return;
                    Settings.lastSearchPath = sf.fbdSearchFolder.SelectedPath;
                    sf.textBox1.Text = Settings.lastSearchPath;
                };
                sf.bSearch.Click += new EventHandler(bSearch_Click);
                sf.bReplace.Click += new EventHandler(bReplace_Click);
                sf.Show();
            } else {
                sf.WindowState = FormWindowState.Normal;
                sf.Focus();
                sf.tbSearch.Focus();
            }
            string str = "";
            if (currentTab != null) {
                str = currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SelectedText;
            }
            if (str.Length == 0 || str.Length > 255) {
                str = Clipboard.GetText();
            }
            if (str.Length > 0 && str.Length < 255) {
                sf.tbSearch.Text = str;
                sf.tbSearch.SelectAll();
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
                    currentTab.textEditor.Document.Replace(offsets[i], lengths[i], sf.tbReplace.Text);
                }
            } else {
                if (!bSearchInternal(null, null))
                    return;
                ISelection selected = currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SelectionCollection[0];
                currentTab.textEditor.Document.Replace(selected.Offset, selected.Length, sf.tbReplace.Text);
            }
        }

        // Search for quick panel
        private void FindForwardButton_Click(object sender, EventArgs e)
        {
            string find = SearchTextComboBox.Text.Trim();
            int z = SearchPanel(currentTab.textEditor.Text, find, currentTab.textEditor.ActiveTextAreaControl.Caret.Offset + 1, CaseButton.Checked);
            if (z != -1) FindSelected(currentTab, z, find.Length);
            addSearchTextComboBox(find);
        }

        private void FindBackButton_Click(object sender, EventArgs e)
        {
            string find = SearchTextComboBox.Text.Trim();
            int offset = currentTab.textEditor.ActiveTextAreaControl.Caret.Offset;
            string text = currentTab.textEditor.Text.Remove(offset);
            int z = SearchPanel(text, find, offset - 1, CaseButton.Checked, true);
            if (z != -1) FindSelected(currentTab, z, find.Length);
            addSearchTextComboBox(find);
        }

        private void ReplaceButton_Click(object sender, EventArgs e)
        {
            string replace = ReplaceTextBox.Text.Trim();
            string find = SearchTextComboBox.Text.Trim();
            int z = SearchPanel(currentTab.textEditor.Text, find, currentTab.textEditor.ActiveTextAreaControl.Caret.Offset, CaseButton.Checked);
            if (z != -1) FindSelected(currentTab, z, find.Length, replace);
            addSearchTextComboBox(find);
        }

        private void ReplaceAllButton_Click(object sender, EventArgs e)
        {
            string replace = ReplaceTextBox.Text.Trim();
            string find = SearchTextComboBox.Text.Trim();
            int z, offset = 0;
            do {
                z = SearchPanel(currentTab.textEditor.Text, find, offset, CaseButton.Checked);
                if (z != -1) currentTab.textEditor.ActiveTextAreaControl.Document.Replace(z, find.Length, replace);
                offset = z + 1;
            } while (z != -1);
            addSearchTextComboBox(find);
        }

        private void SendtoolStripButton_Click(object sender, EventArgs e)
        {
            string word = currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SelectedText;
            if (word == string.Empty) word = TextUtilities.GetWordAt(currentTab.textEditor.Document, currentTab.textEditor.ActiveTextAreaControl.Caret.Offset);
            if (word != string.Empty) SearchTextComboBox.Text = word;
        }
#endregion

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
            RegisterScript.Registration(null);
        }

        private void associateMsgToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab == null)
                return;
            AssossciateMsg(currentTab, true);
        }

#region References/DeclerationDefinition & Include function
        private void findReferencesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextLocation tl = (TextLocation)editorMenuStrip.Tag;
            string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, currentTab.textEditor.Document.PositionToOffset(tl));

            Reference[] refs = currentTab.parseInfo.LookupReferences(word, currentTab.filename, tl.Line);
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
            dgv.BackgroundColor = SystemColors.ControlLight;
            dgv.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgv.Columns.Add(c1);
            dgv.Columns.Add(c2);
            dgv.Columns.Add(c3);
            dgv.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
            dgv.GridColor = SystemColors.ControlLight;
            dgv.MultiSelect = false;
            dgv.ReadOnly = true;
            dgv.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
            dgv.DoubleClick += new System.EventHandler(this.dgvErrors_DoubleClick);
            dgv.RowHeadersVisible = false;

            foreach (var r in refs) {
                Error error = new Error() {
                    fileName = r.file,
                    line = r.line,
                    message = string.Compare(Path.GetFileName(r.file), currentTab.filename, true) == 0 ? TextUtilities.GetLineAsString(currentTab.textEditor.Document, r.line - 1) : word
                };
                dgv.Rows.Add(r.file, error.line.ToString(), error);
            }

            TabPage tp = new TabPage("'" + word + "' references");
            tp.Controls.Add(dgv);
            dgv.Dock = DockStyle.Fill;
            tabControl2.TabPages.Add(tp);
            tabControl2.SelectTab(tp);
            maximize_log();
            currentTab.textEditor.Select();
            currentTab.textEditor.Focus();
        }

        private void findDeclerationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextLocation tl = (TextLocation)editorMenuStrip.Tag;
            string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, currentTab.textEditor.Document.PositionToOffset(tl));
            string file;
            int line;
            currentTab.parseInfo.LookupDecleration(word, currentTab.filename, tl.Line, out file, out line);
            if (file.ToLower() == Compiler.parserPath.ToLower()) file = currentTab.filepath;
            SelectLine(file, line);
        }

        private void findDefinitionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string word, file = currentTab.filepath;
            int line;
            if (((ToolStripDropDownItem)sender).Tag != null) { //.ToString() == "Button"
                if (!currentTab.shouldParse) return;
                Parser.UpdateParseSSL(currentTab.textEditor.Text);
                TextLocation tl = currentTab.textEditor.ActiveTextAreaControl.Caret.Position;
                word = TextUtilities.GetWordAt(currentTab.textEditor.Document, currentTab.textEditor.Document.PositionToOffset(tl));
                line = Parser.GetPocedureLine(word);
                if (line != -1) line++; else return;
            } else {
                TextLocation tl = (TextLocation)editorMenuStrip.Tag;
                word = TextUtilities.GetWordAt(currentTab.textEditor.Document, currentTab.textEditor.Document.PositionToOffset(tl));
                currentTab.parseInfo.LookupDefinition(word, out file, out line);
            }
            SelectLine(file, line);
        }

        private void openIncludeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextLocation tl = (TextLocation)editorMenuStrip.Tag;
            string[] line = TextUtilities.GetLineAsString(currentTab.textEditor.Document, tl.Line).Split('"');
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
#endregion

        private void keyDown(object sender, KeyEventArgs e)
        {
            MessageBox.Show("Test " + e.KeyCode);
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

#region Autocomplete function
        void CmsAutocompleteOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }
        
        private void lbAutocomplete_PasteOpcode(object sender, MouseEventArgs e)
        {
            KeyValuePair<int, string> selection = (KeyValuePair<int, string>)lbAutocomplete.Tag;
            AutoCompleteItem item = (AutoCompleteItem)lbAutocomplete.SelectedItem;
            int startOffs = selection.Key - selection.Value.Length;
            currentTab.textEditor.Document.Replace(startOffs, selection.Value.Length, item.name);
            currentTab.textEditor.ActiveTextAreaControl.TextArea.Focus();
            currentTab.textEditor.ActiveTextAreaControl.Caret.Position = currentTab.textEditor.Document.OffsetToPosition(startOffs + item.name.Length);
            lbAutocomplete.Hide();
        }

        void LbAutocompleteKeyDown(object snd, KeyEventArgs evt)
        {
            if (evt.KeyCode == Keys.Enter && lbAutocomplete.SelectedIndex != -1) {
                lbAutocomplete_PasteOpcode(null,null);
            } else if (evt.KeyCode == Keys.Escape) {
                currentTab.textEditor.ActiveTextAreaControl.TextArea.Focus();
                //currentTab.textEditor.ActiveTextAreaControl.Caret.Position = currentTab.textEditor.Document.OffsetToPosition(selection.Key);
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

        private void lbAutocomplete_MouseMove(object sender, MouseEventArgs e)
        {
            int item = 0;
            if (e.Y != 0) {
                item = e.Y / lbAutocomplete.ItemHeight;
            }
            lbAutocomplete.SelectedIndex = item;
        }
#endregion

        private void minimize_log_button_Click(object sender, EventArgs e)
        {
            if (minimizelogsize == 0) {
                minimizelogsize = splitContainer1.SplitterDistance; 
                splitContainer1.SplitterDistance = Size.Height;
            } else {
                int hs = Size.Height-(Size.Height/4);
                if (minimizelogsize > hs) {
                    splitContainer1.SplitterDistance = hs; 
                } else {
                    splitContainer1.SplitterDistance = minimizelogsize;       
                }
                minimizelogsize = 0;
            }
        }
        
        private void maximize_log()
        {
                int hs = Size.Height - (Size.Height / 4);
                splitContainer1.SplitterDistance = hs;
        }

        private void showLogWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Settings.showLog) {
                splitContainer1.Panel2Collapsed = true;
                Settings.showLog = false;
            } else {
                splitContainer1.Panel2Collapsed = false;
                Settings.showLog = true;
            }
        }

        private void Headers_toolStripSplitButton_ButtonClick(object sender, EventArgs e)
        {
                Headers Headfrm = new Headers();
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
                foreach (string s in Parser.GetAllIncludes(currentTab.filepath)) {
                    Open(s, OpenType.File, false, false, false, false);
                }
            }
        }

        private void TextEditor_Activated(object sender, EventArgs e)
        {
            if (sHeaderfile != null && sHeaderfile.Length > 0) {
                Open(sHeaderfile, OpenType.File, false);
                sHeaderfile = null;
            }
        }

        private void SplitDoc_Click(object sender, EventArgs e)
        {
            if (currentTab != null){
                currentTab.textEditor.Split();
                currentTab.textEditor.Focus();
                currentTab.textEditor.Select();
            }
        }

        private void ShowLineNumbers(object sender, EventArgs e)
        {
            string ext = Path.GetExtension(currentTab.filename).ToLower(); 
            PosChangeType = PositionType.AddPos;
            if (ext != ".ssl" && ext != ".h") {
                currentTab.textEditor.TextEditorProperties.ShowLineNumbers = false;
                PosChangeType = PositionType.Disabled;
                splitContainer2.Panel2Collapsed = true;
            } else {
                splitContainer2.Panel2Collapsed = false;
                currentTab.textEditor.TextEditorProperties.ShowLineNumbers = textLineNumberToolStripMenuItem.Checked;
                currentTab.textEditor.Refresh();
            }
        }

        private void Search_Panel(object sender, EventArgs e)
        {
            SearchToolStrip.Visible = !SearchToolStrip.Visible;
            TabClose_button.Top += (SearchToolStrip.Visible) ? 25 : -25;
        }

#region Function Back/Forward
        private enum PositionType { AddPos, NoSave, SaveChange, Disabled }
        // AddPos - ��� ����������� ��������� ����� ������� � �������.
        // NoSave - �� ��������� ��������� ����������� � �������.
        // SaveChange - �������� ��������� ����������� � ������� ������� �������.
        // Disabled - �� ��������� ��� ����������� ����������� � ������� (�� ������ ��������� �������).

        private void SetBackForwardButtonState() 
        {
            if (currentTab.history.pointerCur > 0) {
                Back_toolStripButton.Enabled = true;
            } else {
                Back_toolStripButton.Enabled = false;
            }
            if (currentTab.history.pointerCur == currentTab.history.pointerEnd || currentTab.history.pointerCur < 0) {
                Forward_toolStripButton.Enabled = false;
            } else if (currentTab.history.pointerCur > 0 || currentTab.history.pointerCur < currentTab.history.pointerEnd) { 
                Forward_toolStripButton.Enabled = true;
            }
        }

        private void Caret_PositionChanged(object sender, EventArgs e)
        {
            string ext = Path.GetExtension(currentTab.filename).ToLower();
            if (ext != ".ssl" && ext != ".h") return;

            TextLocation _position = currentTab.textEditor.ActiveTextAreaControl.Caret.Position;
            int curLine = _position.Line + 1;
            LineStripStatusLabel.Text = "Line: " + curLine;
            ColStripStatusLabel.Text = "Col: " + (_position.Column + 1);
            if (PosChangeType >= PositionType.Disabled) return;
            if (PosChangeType >= PositionType.NoSave) {
                if (PosChangeType == PositionType.SaveChange) {
                    currentTab.history.linePosition[currentTab.history.pointerCur] = _position;
                }
                PosChangeType = PositionType.AddPos; // set default
                return;
            }
            if (curLine != currentTab.history.prevPosition) {
                currentTab.history.pointerCur++;
                int size = currentTab.history.linePosition.Length;
                if (currentTab.history.pointerCur >= size) {
                    Array.Resize(ref currentTab.history.linePosition, size + 1); 
                }
                currentTab.history.linePosition[currentTab.history.pointerCur] = _position;
                currentTab.history.prevPosition = curLine;
                currentTab.history.pointerEnd = currentTab.history.pointerCur;
            }
            SetBackForwardButtonState();  
        }

        private void Back_toolStripButton_Click(object sender, EventArgs e)
        {
            if (currentTab == null || currentTab.history.pointerCur == 0) return;
            currentTab.history.pointerCur--;
            GotoViewLine(); 
        }

        private void Forward_toolStripButton_Click(object sender, EventArgs e)
        {
            if (currentTab == null || currentTab.history.pointerCur >= currentTab.history.pointerEnd) return;
            currentTab.history.pointerCur++;
            GotoViewLine();
        }

        private void GotoViewLine()
        {
            PosChangeType = PositionType.NoSave;
            currentTab.textEditor.ActiveTextAreaControl.Caret.Position = currentTab.history.linePosition[currentTab.history.pointerCur];
            currentTab.textEditor.ActiveTextAreaControl.CenterViewOn(currentTab.textEditor.ActiveTextAreaControl.Caret.Line, 0);
            currentTab.textEditor.Focus();
            //currentTab.textEditor.Select();
            SetBackForwardButtonState();
        }
#endregion

#region Create/Rename/Delete/Move Procedure Function

        // Create Handlers Procedures
        public void CreateProcBlock(string name)
        {
            Parser.UpdateParseSSL(currentTab.textEditor.Text);
            if (Parser.CheckExistsProcedureName(name)) return;
            byte inc = 0;
            if (name == "look_at_p_proc" || name == "description_p_proc") inc++;
            ProcForm CreateProcFrm = new ProcForm();
            CreateProcFrm.ProcedureName.Text = name;
            CreateProcFrm.ProcedureName.ReadOnly = true;
            CreateProcFrm.checkBox1.Enabled = false;
            ProcTree.HideSelection = false;
            if (CreateProcFrm.ShowDialog() == DialogResult.Cancel) {
                ProcTree.HideSelection = true;
                return;
            }
            ProcBlock block = new ProcBlock();
            if (CreateProcFrm.radioButton2.Checked) {
                block = Parser.GetProcBeginEndBlock(ProcTree.SelectedNode.Text);
            }
            InsertProcedure(CreateProcFrm.ProcedureName.Text, block, CreateProcFrm.radioButton2.Checked, inc);
            CreateProcFrm.Dispose();
        }

        // Create Procedures
        private void createProcedureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcForm CreateProcFrm = new ProcForm();
            TextLocation textloc = currentTab.textEditor.ActiveTextAreaControl.Caret.Position;
            string word = TextUtilities.GetWordAt(currentTab.textEditor.Document, currentTab.textEditor.Document.PositionToOffset(textloc));
            CreateProcFrm.ProcedureName.Text = word;
            if (ProcTree.SelectedNode != null && ProcTree.SelectedNode.Tag is Procedure) {}
            else CreateProcFrm.groupBox1.Enabled = false;
            ProcTree.HideSelection = false;
            if (CreateProcFrm.ShowDialog() == DialogResult.Cancel) {
                ProcTree.HideSelection = true;
                return;
            }
            Parser.UpdateParseSSL(currentTab.textEditor.Text);
            ProcBlock block = new ProcBlock();
            if (CreateProcFrm.checkBox1.Checked || CreateProcFrm.radioButton2.Checked) {
                block = Parser.GetProcBeginEndBlock(ProcTree.SelectedNode.Text);
                block.copy = CreateProcFrm.checkBox1.Checked;
            }
            InsertProcedure(CreateProcFrm.ProcedureName.Text, block, CreateProcFrm.radioButton2.Checked);
            CreateProcFrm.Dispose();
        }

        // Create procedure block
        private void InsertProcedure(string name, ProcBlock block, bool after = false, byte overrides = 0)
        {
            if (Parser.CheckExistsProcedureName(name)) return;
            int findLine, caretline = 3;
            string procbody;
            //Copy from procedure
            if (block.copy) {
                procbody = GetSelectBlockText(block.begin + 1, block.end, 0);
                overrides = 1;
            } else procbody = "script_overrides;\r\n\r\n".PadLeft(Settings.tabSize);
            string procblock = (overrides > 0)
                       ? "\r\nprocedure " + name + "\r\nbegin\r\n" + procbody + "end\r\n"
                       : "\r\nprocedure " + name + "\r\nbegin\r\n\r\nend\r\n";
            if (after)findLine = Parser.GetDeclarationProcedureLine(ProcTree.SelectedNode.Text) + 1; 
                else findLine = Parser.GetEndLineProcDeclaration(); 
            if (findLine == -1) MessageBox.Show("The declaration procedure is written to beginning of script.", "Warning");
            currentTab.textEditor.ActiveTextAreaControl.TextArea.SelectionManager.ClearSelection();
            int offset = currentTab.textEditor.ActiveTextAreaControl.TextArea.Document.PositionToOffset(new TextLocation(0, findLine));
            currentTab.textEditor.ActiveTextAreaControl.Document.Insert(offset, "procedure " + name + ";" + Environment.NewLine);
            // proc body
            if (after) findLine = block.end + 1 ; // after current procedure
                else findLine = currentTab.textEditor.Document.TotalNumberOfLines - 1; // paste to end script
            int len = TextUtilities.GetLineAsString(currentTab.textEditor.Document, findLine).Length;
            if (len > 0) {
                procblock = Environment.NewLine + procblock;
                caretline++;
            }
            offset = currentTab.textEditor.ActiveTextAreaControl.TextArea.Document.PositionToOffset(new TextLocation(len, findLine));
            currentTab.textEditor.ActiveTextAreaControl.Document.Insert(offset, procblock);
            currentTab.textEditor.ActiveTextAreaControl.Caret.Column = 0;
            currentTab.textEditor.ActiveTextAreaControl.Caret.Line = findLine + (caretline + overrides);
            currentTab.textEditor.ActiveTextAreaControl.CenterViewOn(findLine + (caretline + overrides), 0);
            SetFocusDocument();
        }

        // Rename Procedures
        private void renameProcedureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string oldName = ProcTree.SelectedNode.Text; //original name
            // form ini
            ProcForm CreateProcFrm = new ProcForm();
            CreateProcFrm.groupBox1.Enabled = false;
            CreateProcFrm.ProcedureName.Text = oldName;  
            CreateProcFrm.Text = "Rename Procedure";
            CreateProcFrm.Create.Text = "OK";
            ProcTree.HideSelection = false;
            if (CreateProcFrm.ShowDialog() == DialogResult.Cancel) {
                ProcTree.HideSelection = true;
                return; 
            }
            string newName = CreateProcFrm.ProcedureName.Text.Trim();
            if (newName == oldName || Parser.CheckExistsProcedureName(newName)) return;
            int differ = newName.Length - oldName.Length;
            //
            // Search procedures name in script text
            //
            string search = "[= ]" + oldName + "[ ,;(\\s]";
            RegexOptions option = RegexOptions.Multiline;
            Regex s_regex = new Regex(search, option);
            MatchCollection matches = s_regex.Matches(currentTab.textEditor.Text);
            int rename_count = 0;
            foreach (Match m in matches)
            {
                int offset_replace = differ * rename_count;
                TextLocation sel_start = currentTab.textEditor.Document.OffsetToPosition(offset_replace + (m.Index + 1));
                TextLocation sel_end = currentTab.textEditor.Document.OffsetToPosition(offset_replace + ((m.Index + 1) + (m.Length - 2)));
                currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SetSelection(sel_start, sel_end);
                currentTab.textEditor.ActiveTextAreaControl.TextArea.SelectionManager.RemoveSelectedText();
                currentTab.textEditor.ActiveTextAreaControl.Document.Insert(offset_replace + (m.Index + 1), newName);
                rename_count++;
            }
            CreateProcFrm.Dispose();
            SetFocusDocument();
        }

        // Delete Procedures
        private void deleteProcedureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to delete \"" + ProcTree.SelectedNode.Text + "\" procedure?", "Warning", MessageBoxButtons.YesNo) == DialogResult.No) return;
            Parser.UpdateParseSSL(currentTab.textEditor.Text);
            string def_poc;
            DeleteProcedure(ProcTree.SelectedNode.Text, out def_poc);
            SetFocusDocument();
        }

        private void DeleteProcedure(string procName, out string def_poc) 
        {
            int declarLine = Parser.GetDeclarationProcedureLine(procName);
            int len = TextUtilities.GetLineAsString(currentTab.textEditor.Document, declarLine).Length;
            currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SetSelection(new TextLocation(0, declarLine), new TextLocation(len, declarLine));
            def_poc = currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SelectedText;
            currentTab.textEditor.ActiveTextAreaControl.SelectionManager.RemoveSelectedText();
            ProcBlock block = Parser.GetProcBeginEndBlock(procName);
            block.begin = Parser.GetPocedureLine(procName);
            currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SetSelection(new TextLocation(0, block.begin), new TextLocation(1000, block.end));
            currentTab.textEditor.ActiveTextAreaControl.SelectionManager.RemoveSelectedText();
            int offset = currentTab.textEditor.ActiveTextAreaControl.TextArea.Document.PositionToOffset(new TextLocation(0, block.begin + 1));
            currentTab.textEditor.ActiveTextAreaControl.TextArea.Document.Remove(offset, 2);
        }

        private string GetSelectBlockText(int _begin, int _end, int _ecol = 1000, int _bcol = 0)
        {
            currentTab.textEditor.ActiveTextAreaControl.TextArea.SelectionManager.SetSelection(new TextLocation(_bcol, _begin), new TextLocation(_ecol, _end));
            return currentTab.textEditor.ActiveTextAreaControl.TextArea.SelectionManager.SelectedText;
        }

        //Update Procedure Tree
        private void SetFocusDocument()
        { 
            currentTab.textEditor.Focus();
            currentTab.textEditor.Select();
            if (Settings.enableParser) {
                timerNext = DateTime.Now;
                timer.Start(); // Parser begin
            } else {
                ParseScript();
                Outline_toolStripButton.Enabled = true;
            }
            ProcTree.HideSelection = true;
        }

        private void ProcMnContext_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (ProcTree.SelectedNode != null && ProcTree.SelectedNode.Tag is Procedure) {
                ProcMnContext.Items[1].Enabled = true;
                ProcMnContext.Items[3].Enabled = true; // moved disabled
                ProcMnContext.Items[4].Enabled = true;
                ProcMnContext.Items[4].Text = "Delete: " + ProcTree.SelectedNode.Text;
            } else {
                ProcMnContext.Items[1].Enabled = false;
                ProcMnContext.Items[3].Enabled = false;
                ProcMnContext.Items[4].Enabled = false;
                ProcMnContext.Items[4].Text = "Delete procedure";
            }
        }

        private void MoveProcedure(int sIndex)
        {
            int root = ProcTree.Nodes.Count - 1;
            Parser.UpdateParseSSL(currentTab.textEditor.Text);
            string moveName = ProcTree.Nodes[root].Nodes[moveActive].Text;
            ProcBlock block = Parser.GetProcBeginEndBlock(moveName);
            block.begin = Parser.GetPocedureLine(moveName);
            string copy_procbody = GetSelectBlockText(block.begin, block.end, 1000);
            string copy_defproc;
            DeleteProcedure(moveName, out copy_defproc);
            // insert declration
            Parser.UpdateParseSSL(currentTab.textEditor.Text);
            string name = ProcTree.Nodes[root].Nodes[sIndex].Text;
            int p_def = Parser.GetDeclarationProcedureLine(name);
            int p_begin = Parser.GetPocedureLine(name) + 1;
            //paste proc block
            int offset = currentTab.textEditor.ActiveTextAreaControl.Document.PositionToOffset(new TextLocation(0, p_def));
            currentTab.textEditor.ActiveTextAreaControl.Document.Insert(offset, copy_defproc + Environment.NewLine);
            offset = currentTab.textEditor.ActiveTextAreaControl.Document.PositionToOffset(new TextLocation(0, p_begin));
            currentTab.textEditor.ActiveTextAreaControl.Document.Insert(offset, copy_procbody + "\r\n\r\n");
            //
            TreeNode nd = ProcTree.Nodes[root].Nodes[moveActive];
            ProcTree.Nodes[root].Nodes.RemoveAt(moveActive);
            ProcTree.Nodes[root].Nodes.Insert(sIndex, nd);
            ProcTree.SelectedNode = ProcTree.Nodes[root].Nodes[sIndex];
            ProcTree.Focus();
            ProcTree.Select();
            Parser.UpdateProcInfo(ref currentTab.parseInfo, currentTab.textEditor.Text, currentTab.filepath);
            currentTab.textEditor.Document.FoldingManager.UpdateFoldings(currentTab.filename, currentTab.parseInfo);
            currentTab.textEditor.Document.FoldingManager.NotifyFoldingsChanged(null);
        }

        private void moveProcedureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (moveActive == -1) {
                moveActive = ProcTree.SelectedNode.Index;
                ProcTree.SelectedNode.ForeColor = Color.Red;
                ProcTree.SelectedNode = ProcTree.Nodes[0];
                ProcTree.Cursor = Cursors.Hand;
                ProcTree.AfterSelect -= TreeView_AfterSelect;
                ProcTree.AfterSelect += new TreeViewEventHandler(ProcTree_AfterSelect);
            }
        }

        private void ProcTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Parent == null || e.Node.Parent.Text != TREEPROCEDURES[1]) return;
            ProcTree.AfterSelect -= ProcTree_AfterSelect;
            currentTab.textEditor.TextChanged -= textChanged;
            MoveProcedure(e.Node.Index);
            currentTab.textEditor.TextChanged += textChanged;
            ProcTree.AfterSelect += TreeView_AfterSelect;
            ProcTree.SelectedNode.ForeColor = Color.Black;
            ProcTree.Cursor = Cursors.Default;
            moveActive = -1;
        }

        private void ProcTree_MouseLeave(object sender, EventArgs e)
        {
            if (moveActive != -1) {
                ProcTree.AfterSelect -= ProcTree_AfterSelect;
                ProcTree.AfterSelect += TreeView_AfterSelect;
                ProcTree.Nodes[ProcTree.Nodes.Count - 1].Nodes[moveActive].ForeColor = Color.Black;
                ProcTree.Cursor = Cursors.Default;
                moveActive = -1;
            }
        }

        private void ProcTree_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) {
                ProcTree_MouseLeave(null, null);
            }
        }
#endregion

        private void EncodingMenuItem_Click(object sender, EventArgs e)
        {
            Settings.encoding = 0;
            if (EncodingDOSmenuItem.Checked) Settings.encoding = 1;
            EncCodePage = (Settings.encoding == 1) ? Encoding.GetEncoding("cp866") : Encoding.Default;
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
            int len;
            int indent = Settings.tabSize;
            string ReplaceText = string.Empty;
            if (currentTab.textEditor.ActiveTextAreaControl.SelectionManager.HasSomethingSelected) {
                ISelection position = currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SelectionCollection[0];
                for (int i = position.StartPosition.Line; i <= position.EndPosition.Line; i++)
                {
                    if (ReplaceText != string.Empty) ReplaceText += "\n";
                    if (SubDecIndent(i, ref indent, ref ReplaceText, out len)) return;
                }
                int offset_str = currentTab.textEditor.Document.LineSegmentCollection[position.StartPosition.Line].Offset;
                int offset_end = currentTab.textEditor.Document.PositionToOffset(new TextLocation(currentTab.textEditor.Document.LineSegmentCollection[position.EndPosition.Line].Length, position.EndPosition.Line));
                int lenBlock = offset_end - offset_str;
                currentTab.textEditor.Document.Replace(offset_str, lenBlock, ReplaceText);
                TextLocation srtSel = currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SelectionCollection[0].StartPosition;
                TextLocation endSel = currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SelectionCollection[0].EndPosition;
                srtSel.Column -= indent;
                endSel.Column -= indent;
                currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SetSelection(srtSel, endSel);
            } else {
                if (SubDecIndent(currentTab.textEditor.ActiveTextAreaControl.Caret.Line, ref indent, ref ReplaceText, out len)) return;
                int offset_str = currentTab.textEditor.Document.LineSegmentCollection[currentTab.textEditor.ActiveTextAreaControl.Caret.Line].Offset;
                currentTab.textEditor.Document.Replace(offset_str, len, ReplaceText);
                
            }
            currentTab.textEditor.ActiveTextAreaControl.Caret.Column -= indent;
            currentTab.textEditor.Refresh();
        }

        private bool SubDecIndent(int line, ref int indent, ref string ReplaceText, out int len)
        {
            string LineText = TextUtilities.GetLineAsString(currentTab.textEditor.Document, line);
            len = LineText.Length;
            int start = (len - LineText.TrimStart().Length);
            if (start < indent) {
                int z = LineText.Length;
                ReplaceText += LineText.TrimStart();
                if (z == ReplaceText.Length) return true;
                indent = z - ReplaceText.Length;
            } else ReplaceText += LineText.Remove(start - indent, indent);
            return false;
        }

        private void CommentTextStripButton_Click(object sender, EventArgs e)
        {
            if (currentTab.textEditor.ActiveTextAreaControl.SelectionManager.HasSomethingSelected) {
                currentTab.textEditor.Document.UndoStack.StartUndoGroup();
                ISelection position = currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SelectionCollection[0];
                for (int i = position.StartPosition.Line; i <= position.EndPosition.Line; i++) 
                {
                    string LineText = TextUtilities.GetLineAsString(currentTab.textEditor.Document, i);
                    if (LineText.TrimStart().StartsWith(Parser.COMMENT)) continue;
                    int offset = currentTab.textEditor.Document.LineSegmentCollection[i].Offset;
                    currentTab.textEditor.Document.Insert(offset, Parser.COMMENT); 
                }
                currentTab.textEditor.Document.UndoStack.EndUndoGroup();
                currentTab.textEditor.ActiveTextAreaControl.SelectionManager.ClearSelection();
            } else {
                string LineText = TextUtilities.GetLineAsString(currentTab.textEditor.Document, currentTab.textEditor.ActiveTextAreaControl.Caret.Line);
                if (LineText.TrimStart().StartsWith(Parser.COMMENT)) return;
                int offset_str = currentTab.textEditor.Document.LineSegmentCollection[currentTab.textEditor.ActiveTextAreaControl.Caret.Line].Offset;
                currentTab.textEditor.Document.Insert(offset_str, Parser.COMMENT);
            }
            currentTab.textEditor.ActiveTextAreaControl.Caret.Column += 2;
        }

        private void UnCommentTextStripButton_Click(object sender, EventArgs e)
        {
            if (currentTab.textEditor.ActiveTextAreaControl.SelectionManager.HasSomethingSelected) {
                currentTab.textEditor.Document.UndoStack.StartUndoGroup();
                ISelection position = currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SelectionCollection[0];
                for (int i = position.StartPosition.Line; i <= position.EndPosition.Line; i++)
                {
                    string LineText = TextUtilities.GetLineAsString(currentTab.textEditor.Document, i);
                    if (!LineText.TrimStart().StartsWith(Parser.COMMENT)) continue;
                    int n = LineText.IndexOf(Parser.COMMENT);
                    int offset_str = currentTab.textEditor.Document.LineSegmentCollection[i].Offset;
                    currentTab.textEditor.Document.Remove(offset_str + n, 2);
                }
                currentTab.textEditor.Document.UndoStack.EndUndoGroup();
                currentTab.textEditor.ActiveTextAreaControl.SelectionManager.ClearSelection();
            } else {
                string LineText = TextUtilities.GetLineAsString(currentTab.textEditor.Document, currentTab.textEditor.ActiveTextAreaControl.Caret.Line);
                if (!LineText.TrimStart().StartsWith(Parser.COMMENT)) return;
                int n = LineText.IndexOf(Parser.COMMENT);
                int offset_str = currentTab.textEditor.Document.LineSegmentCollection[currentTab.textEditor.ActiveTextAreaControl.Caret.Line].Offset;
                currentTab.textEditor.Document.Remove(offset_str + n, 2);
            }
            currentTab.textEditor.ActiveTextAreaControl.Caret.Column -= 2;
        }

        private void AlignToLeftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentTab.textEditor.ActiveTextAreaControl.SelectionManager.HasSomethingSelected) {
                ISelection position = currentTab.textEditor.ActiveTextAreaControl.SelectionManager.SelectionCollection[0];
                string LineText = TextUtilities.GetLineAsString(currentTab.textEditor.Document, position.StartPosition.Line);
                int Align = LineText.Length - LineText.TrimStart().Length; // ������ ����� �������
                currentTab.textEditor.Document.UndoStack.StartUndoGroup();
                for (int i = position.StartPosition.Line + 1; i <= position.EndPosition.Line; i++)
                {
                    LineText = TextUtilities.GetLineAsString(currentTab.textEditor.Document, i);
                    int len = LineText.Length - LineText.TrimStart().Length;
                    if (len == 0 || len <= Align) continue;
                    int offset = currentTab.textEditor.Document.LineSegmentCollection[i].Offset;
                    currentTab.textEditor.Document.Remove(offset, len-Align);
                }
                currentTab.textEditor.Document.UndoStack.EndUndoGroup();
            }
        }
    }
}