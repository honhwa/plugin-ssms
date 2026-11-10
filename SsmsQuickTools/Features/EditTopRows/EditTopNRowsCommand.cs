using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.Shell;
using SsmsQuickTools.Features.ScriptObject;
using SsmsQuickTools.Ssms;

namespace SsmsQuickTools.Features.EditTopRows
{
    /// <summary>
    /// "Edit Top N Rows" sobre el nombre de tabla seleccionado (o la palabra bajo el cursor):
    /// abre el editor de datos nativo de SSMS con las primeras <see cref="RowCount"/> filas
    /// (spec 04). Mismo origen de texto y parseo que ScriptObjectCommands / LocateObjectCommand.
    /// </summary>
    public sealed class EditTopNRowsCommand
    {
        /// <summary>
        /// Cantidad de filas a editar. Fija en 200 (spec 04): hacerla configurable es otro spec.
        /// </summary>
        public const int RowCount = 200;

        private const string Caption = "SSMS Quick Tools";

        private readonly AsyncPackage _package;
        private readonly OleMenuCommandService _commandService;

        public EditTopNRowsCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            _commandService = commandService;
        }

        public void Register()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var commandId = new CommandID(PackageGuids.QuickToolsCmdSet, (int)PkgCmdId.EditTopNRowsCommand);
            var command = new OleMenuCommand(Execute, commandId);
            command.BeforeQueryStatus += OnBeforeQueryStatus;
            _commandService.AddCommand(command);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(sender is OleMenuCommand command))
            {
                return;
            }

            // Visible solo si hay ventana de query conectada y se puede leer un nombre de objeto.
            // Que sea una tabla existente se valida al ejecutar, para no consultar la base cada
            // vez que se abre el menu.
            var connectionInfo = SsmsHost.GetActiveConnectionInfo();
            var parsed = ObjectNameParser.Parse(GetCandidateText());

            command.Visible = connectionInfo != null && parsed != null;
            command.Enabled = command.Visible;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var connectionInfo = SsmsHost.GetActiveConnectionInfo();
            if (connectionInfo == null)
            {
                Warn("No hay una ventana de query conectada.");
                return;
            }

            var parsed = ObjectNameParser.Parse(GetCandidateText());
            if (parsed == null)
            {
                Warn("Selecciona (o ubica el cursor sobre) el nombre de una tabla.");
                return;
            }

            var database = parsed.Database ?? connectionInfo.AdvancedOptions["DATABASE"];

            string connectionString;
            try
            {
                connectionString = SsmsHost.BuildConnectionString(connectionInfo, database);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo preparar la conexion: " + ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!IsUserTable(connectionString, parsed, database, out var validationError))
            {
                Warn(validationError);
                return;
            }

            if (!SsmsHost.TryEditTopRows(connectionInfo, database, parsed.Schema, parsed.Name, RowCount, out var failureReason))
            {
                Warn("No se pudo abrir la edicion de " + parsed.QuotedSchemaQualifiedName + ".\n\n" + failureReason);
            }
        }

        /// <summary>
        /// Verifica contra <c>sys.objects</c> de la base de la conexion activa que el nombre sea una
        /// tabla de usuario (<c>type = 'U'</c>). Mismo patron que <c>ObjectScripter.ResolveKind</c>.
        /// </summary>
        private static bool IsUserTable(string connectionString, ParsedObjectName name, string database, out string error)
        {
            const string query = @"
SELECT o.type
FROM sys.objects AS o
WHERE o.object_id = OBJECT_ID(@qualifiedName);";

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@qualifiedName", name.QuotedSchemaQualifiedName);
                        var typeCode = (command.ExecuteScalar() as string)?.Trim();

                        if (typeCode == null)
                        {
                            error = $"No se encontro el objeto {name.QuotedSchemaQualifiedName} en la base {database}.";
                            return false;
                        }

                        if (typeCode != "U")
                        {
                            error = $"{name.QuotedSchemaQualifiedName} no es una tabla (tipo {typeCode}). Edit Top N Rows solo admite tablas.";
                            return false;
                        }

                        error = null;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                error = "No se pudo verificar el objeto: " + ex.Message;
                return false;
            }
        }

        private static void Warn(string message)
        {
            MessageBox.Show(message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Texto seleccionado en el editor, o (si no hay seleccion) la palabra bajo el cursor.
        /// </summary>
        private static string GetCandidateText()
        {
            var selected = SsmsHost.GetActiveSelectedText();
            return string.IsNullOrWhiteSpace(selected) ? SsmsHost.GetWordUnderCursor() : selected;
        }
    }
}
