﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Document;

namespace ScriptEditor.TextEditorUI
{    
    /// <summary>
    /// Class for text functions.
    /// </summary>
    class Utilities
    {
        // for selected code
        public static void FormattingCode(TextEditorControl TE) 
        {
            string textCode;
            int offset; 
            if (TE.ActiveTextAreaControl.SelectionManager.HasSomethingSelected) {
                textCode = TE.ActiveTextAreaControl.SelectionManager.SelectedText;
                offset = TE.ActiveTextAreaControl.SelectionManager.SelectionCollection[0].Offset;
            } else {
                textCode = TextUtilities.GetLineAsString(TE.Document, TE.ActiveTextAreaControl.Caret.Line);
                offset = TE.ActiveTextAreaControl.Caret.Offset - TE.ActiveTextAreaControl.Caret.Column;
            }
            TE.Document.Replace(offset, textCode.Length, FormattingCode(textCode));
        }
                
        public static string FormattingCode(string textCode) 
        {
            string[] pattern = { ":=", "!=", "==", ">=", "<=", "+=", "-=", "*=", "/=", "%=", ",", ">", "<", "+", "-", "*", "/", "%" };
            char[] excludeR = { ' ', '=', '+', '-', '*', '/' };
            char[] excludeL = { ' ', '=', '>', '<', '+', '-', '!', ':', '*', '/', '%' };
            char[] excludeD = { ' ', ',' };
            const string space = " ";

            string[] linecode = textCode.Split('\n');
            for (int i = 0; i < linecode.Length; i++) {
                string tmp = linecode[i].TrimStart();
                if (tmp.Length < 3 || tmp.StartsWith("//") || tmp.StartsWith("/*")) continue;
                int openQuotes = linecode[i].IndexOf('"');
                int closeQuotes = (openQuotes != -1) ? linecode[i].IndexOf('"', openQuotes + 1) : -1; 
                foreach (string p in pattern) {
                    int n = 0;
                    do {
                        n = linecode[i].IndexOf(p, n);
                        // skip string "..."
                        if (openQuotes > 0 && (n > openQuotes && n < closeQuotes)) {
                            n = closeQuotes + 1;
                            if (n < linecode[i].Length) {
                                openQuotes = linecode[i].IndexOf('"', n);
                                closeQuotes = (openQuotes != -1) ? linecode[i].IndexOf('"', openQuotes + 1) : -1;
                            } else openQuotes = -1;
                            continue;
                        }
                        if (n > 0) {
                            // insert right space
                            if (linecode[i].Substring(n + p.Length, 1) != space) {
                                if (p.Length == 2)
                                    linecode[i] = linecode[i].Insert(n + 2, space);
                                else {
                                    if (linecode[i].Substring(n + 1, 1).IndexOfAny(excludeR) == -1) {
                                        if ((p == "-" && Char.IsDigit(char.Parse(linecode[i].Substring(n + 1, 1)))
                                        && linecode[i].Substring(n - 1, 1).IndexOfAny(excludeD) != -1) == false       // check NegDigit
                                        && ((p == "+" || p == "-") && linecode[i].Substring(n - 1, 1) == p) == false) // check '++/--'
                                            linecode[i] = linecode[i].Insert(n + 1, space);
                                    }
                                }
                            }
                            // insert left space
                            if (p != "," && linecode[i].Substring(n - 1, 1) != space) {
                                if (p.Length == 2)
                                    linecode[i] = linecode[i].Insert(n, space);
                                else {
                                    if (linecode[i].Substring(n - 1, 1).IndexOfAny(excludeL) == -1) {
                                        if (((p == "+" || p == "-") && (linecode[i].Substring(n + 1, 1)) == p) == false) // check '++/--'
                                            linecode[i] = linecode[i].Insert(n, space);
                                    }
                                }
                            }
                        } else break;
                        n += p.Length;
                    } while (n < linecode[i].Length);
                }
            }
            return string.Join("\n", linecode);
        }

        public static void HighlightingSelectedText(TextEditorControl TE)
        {
            List<TextMarker> marker = TE.Document.MarkerStrategy.GetMarkers(0, TE.Document.TextLength);
            foreach (TextMarker m in marker) {
                if (m.TextMarkerType == TextMarkerType.SolidBlock)
                    TE.Document.MarkerStrategy.RemoveMarker(m); 
            }
            if (!TE.ActiveTextAreaControl.SelectionManager.HasSomethingSelected) return;
            string sWord = TE.ActiveTextAreaControl.SelectionManager.SelectedText.Trim();
            int wordLen = sWord.Length;
            if (wordLen == 0 || (wordLen < 3 && !Char.IsLetterOrDigit(Convert.ToChar((sWord.Substring(0,1)))))) return;
            int seek = 0;
            while (seek < TE.Document.TextLength) {
                seek = TE.Text.IndexOf(sWord, seek);
                if (seek == -1) break;
                char chS = (seek > 0) ? TE.Document.GetCharAt(seek - 1) : ' ';
                char chE = ((seek + wordLen) < TE.Document.TextLength) ? TE.Document.GetCharAt(seek + wordLen): ' ';
                if (!(Char.IsLetter(chS) || chS == '_') && !(Char.IsLetter(chE) || chE == '_'))
                    TE.Document.MarkerStrategy.AddMarker(new TextMarker(seek, sWord.Length, TextMarkerType.SolidBlock, Color.GreenYellow, Color.Black));
                seek += wordLen;
            }
        }
    }
}
