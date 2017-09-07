﻿using System;
using System.Windows.Forms;

namespace ScriptEditor
{
    public partial class SettingsDialog : Form
    {
        private string outpath;
        private string scriptshpath;

        public SettingsDialog()
        {
            outpath = Settings.outputDir;
            scriptshpath = Settings.PathScriptsHFile;
            InitializeComponent();
            cbDebug.Checked = Settings.showDebug;
            cbIncludePath.Checked = Settings.overrideIncludesPath;
            cbOptimize.SelectedIndex = (Settings.optimize == 255 ? 1 : Settings.optimize);
            cbWarnings.Checked = Settings.showWarnings;
            cbWarnFailedCompile.Checked = Settings.warnOnFailedCompile;
            cbMultiThread.Checked = Settings.multiThreaded;
            cbAutoOpenMessages.Checked = Settings.autoOpenMsgs;
            tbLanguage.Text = Settings.language;
            cbTabsToSpaces.Checked = Settings.tabsToSpaces;
            tbTabSize.Text = Convert.ToString(Settings.tabSize);
            cbEnableParser.Checked = Settings.enableParser;
            cbShortCircuit.Checked = Settings.shortCircuit;
            cbAutocomplete.Checked = Settings.autocomplete;
            Highlight_comboBox.SelectedIndex = Settings.highlight;
            HintLang_comboBox.SelectedIndex = Settings.hintsLang;
            if (!Settings.enableParser) cbParserWarn.Enabled = false;
            cbParserWarn.Checked = Settings.parserWarn;
            cbWatcom.Checked = Settings.useWatcom;
            cbCompilePath.Checked = Settings.ignoreCompPath;
            cbUserCompile.Checked = Settings.userCmdCompile;
            foreach (var item in Settings.msgListPath)
                msgPathlistView.Items.Add(item.ToString());
            SetLabelText();
        }

        private void SetLabelText()
        {
            textBox2.Text = outpath == null ? "<unset>" : outpath;
            textBox1.Text = scriptshpath == null ? "<unset>" : scriptshpath;  
        }

        private void SettingsDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            Settings.showDebug = cbDebug.Checked;
            Settings.overrideIncludesPath = cbIncludePath.Checked;
            Settings.optimize = (byte)cbOptimize.SelectedIndex;
            Settings.showWarnings = cbWarnings.Checked;
            Settings.warnOnFailedCompile = cbWarnFailedCompile.Checked;
            Settings.multiThreaded = cbMultiThread.Checked;
            Settings.outputDir = outpath;
            Settings.autoOpenMsgs = cbAutoOpenMessages.Checked;
            Settings.PathScriptsHFile = scriptshpath;
            Settings.language = tbLanguage.Text.Length == 0 ? "english" : tbLanguage.Text;
            Settings.tabsToSpaces = cbTabsToSpaces.Checked;
            try {
                Settings.tabSize = Convert.ToInt32(tbTabSize.Text);
            } catch (System.FormatException) {
                Settings.tabSize = 3;
            }
            if (Settings.tabSize < 1 || Settings.tabSize > 30) {
                Settings.tabSize = 3;
            }
            Settings.enableParser = cbEnableParser.Checked;
            Settings.shortCircuit = cbShortCircuit.Checked;
            Settings.autocomplete = cbAutocomplete.Checked;
            Settings.highlight = (byte)Highlight_comboBox.SelectedIndex;
            Settings.hintsLang = (byte)HintLang_comboBox.SelectedIndex;
            Settings.parserWarn = cbParserWarn.Checked;
            Settings.useWatcom = cbWatcom.Checked;
            Settings.ignoreCompPath = cbCompilePath.Checked;
            Settings.userCmdCompile = cbUserCompile.Checked;
            Settings.msgListPath.Clear();
            foreach (ListViewItem item in msgPathlistView.Items)
                Settings.msgListPath.Add(item.Text);
            Settings.Save();
        }

        private void bChange_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK) {
                outpath = folderBrowserDialog1.SelectedPath;
                SetLabelText();
            }
        }

        private void bScriptsH_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK) {
                scriptshpath = System.IO.Path.GetDirectoryName(openFileDialog1.FileName);
                SetLabelText();
            }
        }

        void TbTabSizeTextChanged(object sender, EventArgs e)
        {
            int n;
            try {
                n = Convert.ToInt32(tbTabSize.Text);
            } catch (System.FormatException) {
                n = 3;
            }
            tbTabSize.Text = Convert.ToString(n);
        }

        private void cbEnableParser_CheckedChanged(object sender, EventArgs e)
        {
            cbParserWarn.Enabled = cbEnableParser.Checked;
        }

        private void addPathToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK) {
                string msgPath = folderBrowserDialog1.SelectedPath;
                if (msgPathlistView.Items.Count > 0) {
                    msgPathlistView.Items.Insert(0, msgPath);
                } else msgPathlistView.Items.Add(msgPath);
                //msgPathlistView.Items[msgPathlistView.Items.Count - 1].ToolTipText = msgPath;
            }
        }

        private void deletePathToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (msgPathlistView.Items == null) return;
            msgPathlistView.Items.RemoveAt(msgPathlistView.FocusedItem.Index);
        }

        private void moveUpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (msgPathlistView.Items == null) return;
            int sInd = msgPathlistView.FocusedItem.Index;
            if (sInd == 0) return;
            string iPath = msgPathlistView.Items[--sInd].Text;
            PathItemSub(sInd, iPath);
        }

        private void modeDownToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (msgPathlistView.Items == null) return;
            int sInd = msgPathlistView.FocusedItem.Index;
            if (sInd == msgPathlistView.Items.Count - 1) return;
            string iPath = msgPathlistView.Items[++sInd].Text;
            PathItemSub(sInd, iPath);
        }

        private void PathItemSub(int sInd, string iPath)
        {
            msgPathlistView.Items[sInd].Text = msgPathlistView.FocusedItem.Text;
            msgPathlistView.FocusedItem.Text = iPath;
            msgPathlistView.Items[sInd].Selected = true;
            msgPathlistView.Items[sInd].Focused = true;
        }

        private void bAssociate_Click(object sender, EventArgs e)
        {
            FileAssociation.Associate(true);
        }

        private void cbCompilePath_CheckedChanged(object sender, EventArgs e)
        {
            textBox2.Enabled = !cbCompilePath.Checked;
        }

        private void cbUserCompile_CheckedChanged(object sender, EventArgs e)
        {
            cbCompilePath.Enabled = !cbUserCompile.Checked;
            textBox2.Enabled = !cbUserCompile.Checked & !cbCompilePath.Checked;;
            //cbWatcom.Enabled = !cbUserCompile.Checked;
            cbOptimize.Enabled = !cbUserCompile.Checked;
            cbDebug.Enabled = !cbUserCompile.Checked;
        }
    }
}
