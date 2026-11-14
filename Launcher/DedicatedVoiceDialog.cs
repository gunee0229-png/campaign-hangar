using System;
using System.Drawing;
using System.Windows.Forms;

namespace CampaignHangar
{
    public sealed class DedicatedVoiceDialog : ThemeForm
    {
        readonly RadioButton korean = new RadioButton(), english = new RadioButton();
        readonly MetalButton confirm = new MetalButton(), cancel = new MetalButton();
        public bool English { get { return english.Checked; } }
        public DedicatedVoiceDialog(bool currentEnglish, bool installed)
        {
            Text = installed ? "매스리콜 음성 변경" : "매스리콜 음성 선택";
            ClientSize = new Size(620, 290); MinimumSize = new Size(540, 290);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "설치할 음성을 선택하세요.", Dock = DockStyle.Fill,
                ForeColor = GlassTheme.Text, Font = new Font(Font.FontFamily, 13, FontStyle.Bold) }, 0, 0);
            korean.Text = "한국어 음성"; english.Text = "영어 음성";
            korean.Dock = english.Dock = DockStyle.Fill;
            korean.ForeColor = english.ForeColor = GlassTheme.Text;
            korean.Checked = !currentEnglish; english.Checked = currentEnglish;
            layout.Controls.Add(korean, 0, 1); layout.Controls.Add(english, 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            confirm.Text = installed ? "음성 변경" : "설치"; cancel.Text = "취소";
            confirm.Size = cancel.Size = new Size(140, 48);
            confirm.Click += delegate { DialogResult = DialogResult.OK; };
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(cancel); actions.Controls.Add(confirm); layout.Controls.Add(actions, 0, 3);
            Controls.Add(layout); AcceptButton = cancel; CancelButton = cancel;
        }
        internal void VerifyChoice(bool value, bool accepted)
        {
            english.Checked = value; korean.Checked = !value;
            if (accepted) confirm.PerformClick(); else cancel.PerformClick();
            if (English != value || DialogResult != (accepted ? DialogResult.OK : DialogResult.Cancel))
                throw new InvalidOperationException("Voice choice or cancellation did not preserve its explicit result.");
        }
    }
}
