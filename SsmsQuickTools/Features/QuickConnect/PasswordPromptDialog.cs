using System.Drawing;
using System.Windows.Forms;

namespace SsmsQuickTools.Features.QuickConnect
{
    /// <summary>
    /// Pide el password de una conexion SQL Server cuando no hay ninguno guardado (o cuando el blob
    /// DPAPI del archivo no se pudo descifrar en esta cuenta/maquina).
    /// </summary>
    internal sealed class PasswordPromptDialog : Form
    {
        private readonly TextBox _passwordBox;
        private readonly CheckBox _rememberBox;

        public PasswordPromptDialog(ConnectionEntry entry)
        {
            Text = "SSMS Quick Tools - Password de SQL Server";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(460, 190);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(14),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var header = new Label
            {
                Text = $"{entry.Name}\r\n{entry.Server} · {entry.Database} · usuario {entry.UserName}",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10),
            };
            layout.Controls.Add(header, 0, 0);
            layout.SetColumnSpan(header, 2);

            var passwordLabel = new Label
            {
                Text = "Password:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 6, 0),
            };
            layout.Controls.Add(passwordLabel, 0, 1);

            _passwordBox = new TextBox
            {
                UseSystemPasswordChar = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 0, 6),
            };
            layout.Controls.Add(_passwordBox, 1, 1);

            _rememberBox = new CheckBox
            {
                Text = "Guardar el password (cifrado con DPAPI para este usuario de Windows)",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 4, 0, 0),
            };
            layout.Controls.Add(_rememberBox, 1, 2);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 0),
            };

            var okButton = new Button { Text = "Conectar", AutoSize = true, MinimumSize = new Size(90, 28) };
            var cancelButton = new Button
            {
                Text = "Cancelar",
                AutoSize = true,
                MinimumSize = new Size(90, 28),
                DialogResult = DialogResult.Cancel,
            };
            okButton.Click += OnOkClick;

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);
            layout.Controls.Add(buttons, 0, 3);
            layout.SetColumnSpan(buttons, 2);

            Controls.Add(layout);
            AcceptButton = okButton;
            CancelButton = cancelButton;

            Shown += (s, e) => _passwordBox.Focus();
        }

        public string Password => _passwordBox.Text;

        public bool RememberPassword => _rememberBox.Checked;

        private void OnOkClick(object sender, System.EventArgs e)
        {
            if (_passwordBox.Text.Length == 0)
            {
                MessageBox.Show(this, "Escriba el password.", "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _passwordBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
