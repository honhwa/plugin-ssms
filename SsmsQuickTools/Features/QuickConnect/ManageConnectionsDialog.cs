using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SsmsQuickTools.Features.QuickConnect
{
    /// <summary>
    /// Administrador de Quick Connections (Quick Tools &gt; Manage Quick Connections...): lista las
    /// conexiones de connections.json y permite crear, editar, probar y eliminar sin editar el JSON
    /// a mano. Cada guardado reescribe el archivo completo (el password de SQL Server va cifrado).
    /// </summary>
    internal sealed class ManageConnectionsDialog : Form
    {
        private readonly ConnectionCatalog _catalog;

        private readonly ListBox _list = new ListBox();
        private readonly TextBox _nameBox = new TextBox();
        private readonly TextBox _serverBox = new TextBox();
        private readonly TextBox _databaseBox = new TextBox();
        private readonly TextBox _userBox = new TextBox();
        private readonly TextBox _passwordBox = new TextBox();
        private readonly ComboBox _authBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _showPasswordBox = new CheckBox { Text = "Mostrar", AutoSize = true };
        private readonly Label _statusLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        private readonly Button _deleteButton = new Button { Text = "Eliminar", AutoSize = true, MinimumSize = new Size(80, 26) };

        private List<ConnectionEntry> _entries = new List<ConnectionEntry>();

        /// <summary>Nombre con el que se cargo la entrada que esta en el editor; null = entrada nueva.</summary>
        private string _currentName;

        private bool _loading;

        public ManageConnectionsDialog(ConnectionCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            Text = "SSMS Quick Tools - Quick Connections";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(820, 480);
            MinimumSize = new Size(820, 480);

            BuildLayout();
            WireEvents();
            ReloadList(selectName: null);

            SetStatus("Archivo: " + _catalog.ConfigPath);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(12),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // --- Columna izquierda: lista + Nueva/Eliminar ---
            var listPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            listPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            listPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            listPanel.Controls.Add(new Label { Text = "Conexiones", AutoSize = true, Margin = new Padding(0, 0, 0, 4) }, 0, 0);

            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.Margin = new Padding(0, 0, 10, 6);
            listPanel.Controls.Add(_list, 0, 1);

            var listButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            var newButton = new Button { Text = "Nueva", AutoSize = true, MinimumSize = new Size(80, 26), Margin = new Padding(0, 0, 6, 0) };
            newButton.Click += (s, e) => ClearEditor();
            listButtons.Controls.Add(newButton);
            listButtons.Controls.Add(_deleteButton);
            listPanel.Controls.Add(listButtons, 0, 2);

            root.Controls.Add(listPanel, 0, 0);

            // --- Columna derecha: formulario ---
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 6; i++)
            {
                fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // hint
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // relleno

            AddField(fields, 0, "Nombre:", _nameBox);
            AddField(fields, 1, "Servidor:", _serverBox);
            AddField(fields, 2, "Base de datos:", _databaseBox);

            _authBox.Items.AddRange(new object[] { "Windows Authentication", "SQL Server Authentication" });
            _authBox.SelectedIndex = 0;
            _authBox.Margin = new Padding(0, 3, 0, 6);
            _authBox.Width = 240;
            _authBox.Anchor = AnchorStyles.Left;
            AddField(fields, 3, "Autenticacion:", _authBox, fill: false);

            AddField(fields, 4, "Usuario:", _userBox);

            _passwordBox.UseSystemPasswordChar = true;
            _passwordBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            var passwordRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            passwordRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            passwordRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _passwordBox.Margin = new Padding(0, 3, 6, 6);
            passwordRow.Controls.Add(_passwordBox, 0, 0);
            _showPasswordBox.Anchor = AnchorStyles.Left;
            _showPasswordBox.Margin = new Padding(0, 3, 0, 6);
            passwordRow.Controls.Add(_showPasswordBox, 1, 0);
            AddField(fields, 5, "Password:", passwordRow);

            var hint = new Label
            {
                Text = "El password se guarda cifrado con DPAPI (solo esta cuenta de Windows, en esta maquina). "
                    + "Si el archivo se copia a otro equipo, se vuelve a pedir al conectar.",
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 10, 0, 0),
            };
            fields.Controls.Add(hint, 0, 6);
            fields.SetColumnSpan(hint, 2);

            root.Controls.Add(fields, 1, 0);

            // --- Barra inferior: estado + acciones ---
            root.Controls.Add(_statusLabel, 0, 1);
            _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _statusLabel.Margin = new Padding(0, 8, 0, 0);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0),
            };

            var closeButton = new Button { Text = "Cerrar", AutoSize = true, MinimumSize = new Size(90, 30), DialogResult = DialogResult.Cancel };
            var saveButton = new Button { Text = "Guardar", AutoSize = true, MinimumSize = new Size(90, 30) };
            var testButton = new Button { Text = "Probar conexion", AutoSize = true, MinimumSize = new Size(120, 30) };
            saveButton.Click += OnSaveClick;
            testButton.Click += OnTestClick;

            actions.Controls.Add(closeButton);
            actions.Controls.Add(saveButton);
            actions.Controls.Add(testButton);
            root.Controls.Add(actions, 1, 1);

            Controls.Add(root);
            CancelButton = closeButton;
            AcceptButton = saveButton;
        }

        private static void AddField(TableLayoutPanel table, int row, string label, Control field, bool fill = true)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, row);
            if (fill)
            {
                field.Dock = DockStyle.Fill;
            }

            table.Controls.Add(field, 1, row);
        }

        private void WireEvents()
        {
            _list.SelectedIndexChanged += OnListSelectionChanged;
            _authBox.SelectedIndexChanged += (s, e) => UpdateAuthEnabled();
            _showPasswordBox.CheckedChanged += (s, e) => _passwordBox.UseSystemPasswordChar = !_showPasswordBox.Checked;
            _deleteButton.Click += OnDeleteClick;
        }

        private void ReloadList(string selectName)
        {
            _entries = _catalog.Connections.ToList();

            _loading = true;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var entry in _entries)
                {
                    var suffix = entry.Authentication == ConnectionAuthentication.SqlServer
                        ? "  (SQL: " + entry.UserName + ")"
                        : string.Empty;
                    _list.Items.Add(entry.Name + suffix);
                }
            }
            finally
            {
                _list.EndUpdate();
                _loading = false;
            }

            if (selectName != null)
            {
                var index = _entries.FindIndex(e => string.Equals(e.Name, selectName, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    _list.SelectedIndex = index;
                }
            }
            else if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }
            else
            {
                ClearEditor();
            }

            _deleteButton.Enabled = _list.SelectedIndex >= 0;
        }

        private void OnListSelectionChanged(object sender, EventArgs e)
        {
            if (_loading || _list.SelectedIndex < 0 || _list.SelectedIndex >= _entries.Count)
            {
                return;
            }

            LoadEntry(_entries[_list.SelectedIndex]);
            _deleteButton.Enabled = true;
        }

        private void LoadEntry(ConnectionEntry entry)
        {
            _loading = true;
            try
            {
                _currentName = entry.Name;
                _nameBox.Text = entry.Name;
                _serverBox.Text = entry.Server;
                _databaseBox.Text = entry.Database;
                _authBox.SelectedIndex = entry.Authentication == ConnectionAuthentication.SqlServer ? 1 : 0;
                _userBox.Text = entry.UserName ?? string.Empty;
                _passwordBox.Text = entry.Password ?? string.Empty;
                _showPasswordBox.Checked = false;
            }
            finally
            {
                _loading = false;
            }

            UpdateAuthEnabled();

            if (entry.NeedsPassword)
            {
                SetStatus("Esta conexion no tiene password guardado: se pedira al conectar (o escribalo aca y guarde).");
            }
        }

        private void ClearEditor()
        {
            _loading = true;
            try
            {
                _currentName = null;
                _list.ClearSelected();
                _nameBox.Text = string.Empty;
                _serverBox.Text = string.Empty;
                _databaseBox.Text = string.Empty;
                _authBox.SelectedIndex = 0;
                _userBox.Text = string.Empty;
                _passwordBox.Text = string.Empty;
                _showPasswordBox.Checked = false;
            }
            finally
            {
                _loading = false;
            }

            UpdateAuthEnabled();
            _deleteButton.Enabled = false;
            _nameBox.Focus();
        }

        private void UpdateAuthEnabled()
        {
            if (_loading)
            {
                return;
            }

            var isSql = _authBox.SelectedIndex == 1;
            _userBox.Enabled = isSql;
            _passwordBox.Enabled = isSql;
            _showPasswordBox.Enabled = isSql;
        }

        private ConnectionEntry BuildEntryFromFields()
        {
            var entry = new ConnectionEntry
            {
                Name = _nameBox.Text.Trim(),
                Server = _serverBox.Text.Trim(),
                Database = _databaseBox.Text.Trim(),
                Authentication = _authBox.SelectedIndex == 1 ? ConnectionAuthentication.SqlServer : ConnectionAuthentication.Windows,
                UserName = _userBox.Text.Trim(),
                Password = _passwordBox.Text,
            };

            if (entry.Name.Length == 0)
            {
                throw new ArgumentException("La conexion necesita un nombre.");
            }

            if (entry.Server.Length == 0)
            {
                throw new ArgumentException("La conexion necesita un servidor.");
            }

            if (entry.Database.Length == 0)
            {
                throw new ArgumentException("La conexion necesita una base de datos.");
            }

            if (entry.Authentication == ConnectionAuthentication.SqlServer && entry.UserName.Length == 0)
            {
                throw new ArgumentException("SQL Server Authentication necesita un usuario.");
            }

            return entry;
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            ConnectionEntry entry;
            try
            {
                entry = BuildEntryFromFields();
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var isRename = _currentName != null && !string.Equals(_currentName, entry.Name, StringComparison.OrdinalIgnoreCase);
            var editingSameName = _currentName != null && string.Equals(_currentName, entry.Name, StringComparison.OrdinalIgnoreCase);
            if (!editingSameName && _catalog.FindConnection(entry.Name) != null)
            {
                MessageBox.Show(
                    this,
                    "Ya existe una conexion con el nombre \"" + entry.Name + "\". Use otro nombre o edite la existente.",
                    "SSMS Quick Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (isRename)
                {
                    // Renombrar = borrar el nombre viejo + insertar el nuevo (el archivo se reescribe
                    // completo en el mismo guardado, asi que no queda un estado intermedio en disco).
                    _catalog.Remove(_currentName);
                }

                var stored = _catalog.AddOrUpdate(entry);
                ReloadList(stored.Name);
                LoadEntry(stored);
                SetStatus("Guardado: " + stored.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo guardar: " + ex.Message, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnDeleteClick(object sender, EventArgs e)
        {
            if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _entries.Count)
            {
                return;
            }

            var entry = _entries[_list.SelectedIndex];
            var confirm = MessageBox.Show(
                this,
                "Eliminar la conexion \"" + entry.Name + "\" de connections.json?",
                "SSMS Quick Tools",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            try
            {
                _catalog.Remove(entry.Name);
                _currentName = null;
                ReloadList(selectName: null);
                SetStatus("Eliminado: " + entry.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo eliminar: " + ex.Message, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnTestClick(object sender, EventArgs e)
        {
            ConnectionEntry entry;
            try
            {
                entry = BuildEntryFromFields();
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (entry.NeedsPassword)
            {
                MessageBox.Show(
                    this,
                    "Escriba el password para poder probar la conexion.",
                    "SSMS Quick Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _passwordBox.Focus();
                return;
            }

            UseWaitCursor = true;
            try
            {
                QuickConnectionFactory.Test(entry);
                MessageBox.Show(
                    this,
                    "Conexion correcta: " + entry.Server + " / " + entry.Database,
                    "SSMS Quick Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fallo la conexion:\r\n" + ex.Message, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }
    }
}
