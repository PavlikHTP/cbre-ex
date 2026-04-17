using CBRE.Common.Mediator;
using CBRE.Editor.Tools;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CBRE.Editor.UI.Sidebar
{
    public partial class HelpSidebarPanel : UserControl, IMediatorListener
    {
        private static readonly Regex BoldRegex = new Regex(@"\*(?:\b(?=\w)|(?=\\))(.*?)\b(?!\w)\*", RegexOptions.Compiled);
        private static readonly Regex BulletRegex = new Regex(@"^\s*-\s+", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex DoubleLinesRegex = new Regex(@"(\r?\n){2,}", RegexOptions.Compiled);
        private static readonly Regex SingleLinesRegex = new Regex(@"(\r?\n)+", RegexOptions.Compiled);
        
        public HelpSidebarPanel()
        {
            InitializeComponent();

            Mediator.Subscribe(EditorMediator.ContextualHelpChanged, this);
            Mediator.Subscribe(EditorMediator.ToolSelected, this);
        }

        private void UpdateHelp()
        {
            string help = "";
            if (ToolManager.ActiveTool != null) help = ToolManager.ActiveTool.GetContextualHelp();
            HelpTextBox.ResetFont();
            HelpTextBox.Font = SystemFonts.MessageBoxFont;
            string rtf = ConvertSimpleMarkdownToRtf(help);
            HelpTextBox.Rtf = rtf;
            Size size = TextRenderer.MeasureText(HelpTextBox.Text, HelpTextBox.Font, HelpTextBox.Size, TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak);
            Height = size.Height + HelpTextBox.Margin.Vertical + HelpTextBox.Lines.Length * 5;
        }

        public void Notify(string message, object data)
        {
            Mediator.ExecuteDefault(this, message, data);
        }

        private void ContextualHelpChanged()
        {
            UpdateHelp();
        }

        private void ToolSelected()
        {
            UpdateHelp();
        }

        /// <summary>
        /// Converts simple markdown into RTF.
        /// Simple markdown is a very limited subset of markdown. It supports:
        /// - Lists, delimited with -
        /// - Bold, delimited with *
        /// - Paragraphs/new lines
        /// </summary>
        /// <param name="simpleMarkdown"></param>
        private string ConvertSimpleMarkdownToRtf(string simpleMarkdown)
        {
            if (string.IsNullOrEmpty(simpleMarkdown)) return @"{\rtf1\ansi\f0\pard }";
            
            StringBuilder sb = new StringBuilder(@"{\rtf1\ansi\f0\pard\sa60 ");
            foreach (char c in simpleMarkdown)
            {
                if (c > 127) sb.AppendFormat(@"\u{0}?", (int)c);
                else if (c == '\\') sb.Append(@"\\");
                else if (c == '{') sb.Append(@"\{");
                else if (c == '}') sb.Append(@"\}");
                else sb.Append(c);
            }

            string processed = sb.ToString();
            
            processed = BoldRegex.Replace(processed, @"{\b $1}");
            processed = BulletRegex.Replace(processed, @" \bullet  ");
            processed = DoubleLinesRegex.Replace(processed, "\\par\\par ");
            processed = SingleLinesRegex.Replace(processed, "\\par ");

            return processed + " }";
        }
        
        
    }
}
