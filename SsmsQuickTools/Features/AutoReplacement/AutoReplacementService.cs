using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using SsmsQuickTools.Ssms;

namespace SsmsQuickTools.Features.AutoReplacement
{
    /// <summary>
    /// Ciclo de vida del enganche de Auto Replacement: engancha la vista de texto activa al
    /// arrancar y cada vista nueva que SSMS registre despues (IVsTextManagerEvents), asignando un
    /// <see cref="AutoReplacementCommandFilter"/> por vista de forma idempotente.
    /// </summary>
    public sealed class AutoReplacementService : IVsTextManagerEvents, IDisposable
    {
        private readonly AutoReplacementCatalog _catalog;
        private readonly ConditionalWeakTable<IVsTextView, AutoReplacementCommandFilter> _filters
            = new ConditionalWeakTable<IVsTextView, AutoReplacementCommandFilter>();

        private IConnectionPoint _connectionPoint;
        private int _cookie;

        // GUID del language service de la primera vista de texto que se vea (arranque, o el
        // primer OnRegisterView). En una instalacion de SSMS eso es, en la practica, siempre una
        // ventana de query T-SQL: es la unica clase de editor de texto que SSMS abre por su
        // cuenta. No se hardcodea el GUID del language service T-SQL (no es un valor documentado
        // ni estable entre builds de SSMS); se aprende una sola vez y sirve despues para no
        // engancharse a editores de texto de otras extensiones/ventanas de herramientas.
        private Guid? _sqlLanguageServiceId;

        public AutoReplacementService(AutoReplacementCatalog catalog)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _catalog = catalog;

            AttachActiveView();
            Subscribe();
        }

        private void AttachActiveView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var view = SsmsHost.GetActiveTextView();
            if (view != null)
            {
                EnsureAttached(view);
            }
        }

        private void Subscribe()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textManager = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager)) as IVsTextManager;
            if (!(textManager is IConnectionPointContainer container))
            {
                return;
            }

            var eventsGuid = typeof(IVsTextManagerEvents).GUID;
            container.FindConnectionPoint(ref eventsGuid, out _connectionPoint);
            _connectionPoint?.Advise(this, out _cookie);
        }

        private void EnsureAttached(IVsTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_filters.TryGetValue(view, out _))
            {
                return;
            }

            if (!IsSqlEditorBuffer(view))
            {
                return;
            }

            var filter = AutoReplacementCommandFilter.Attach(view, _catalog);
            if (filter != null)
            {
                _filters.Add(view, filter);
            }
        }

        /// <summary>
        /// True si <paramref name="view"/> es una ventana de query T-SQL, para no engancharse a
        /// editores de texto de otras extensiones u otras ventanas de herramientas con buffer.
        /// Ver comentario de <see cref="_sqlLanguageServiceId"/>: la primera vista que se ve
        /// establece el GUID de referencia; si todavia no hay uno, se asume que si (arranque de
        /// SSMS con una ventana de query ya abierta) y esa vista fija el GUID de referencia.
        /// </summary>
        private bool IsSqlEditorBuffer(IVsTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!TryGetLanguageServiceId(view, out var languageServiceId))
            {
                // No se pudo leer el language service: solo enganchar si todavia no hay
                // referencia (arranque), para no dejar la funcion sin poder engancharse nunca.
                return _sqlLanguageServiceId == null;
            }

            if (_sqlLanguageServiceId == null)
            {
                _sqlLanguageServiceId = languageServiceId;
                return true;
            }

            return languageServiceId == _sqlLanguageServiceId.Value;
        }

        private static bool TryGetLanguageServiceId(IVsTextView view, out Guid languageServiceId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            languageServiceId = Guid.Empty;

            if (ErrorHandler.Failed(view.GetBuffer(out var buffer)) || !(buffer is IVsTextLines textLines))
            {
                return false;
            }

            return ErrorHandler.Succeeded(textLines.GetLanguageServiceID(out languageServiceId));
        }

        public void OnRegisterView(IVsTextView pView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pView != null)
            {
                EnsureAttached(pView);
            }
        }

        public void OnUnregisterView(IVsTextView pView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (pView != null && _filters.TryGetValue(pView, out var filter))
            {
                filter.Detach();
                _filters.Remove(pView);
            }
        }

        public void OnUserPreferencesChanged(VIEWPREFERENCES[] pViewPrefs, FRAMEPREFERENCES[] pFramePrefs,
            LANGPREFERENCES[] pLangPrefs, FONTCOLORPREFERENCES[] pColorPrefs)
        {
            // No aplica: Auto Replacement no depende de preferencias de vista.
        }

        public void OnRegisterMarkerType(int iMarkerType)
        {
            // No aplica: Auto Replacement no registra tipos de marcador de texto.
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_connectionPoint != null)
            {
                try
                {
                    _connectionPoint.Unadvise(_cookie);
                }
                catch (COMException)
                {
                }
            }
        }
    }
}
