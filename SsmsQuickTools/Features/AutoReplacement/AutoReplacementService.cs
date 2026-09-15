using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
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

            var filter = AutoReplacementCommandFilter.Attach(view, _catalog);
            if (filter != null)
            {
                _filters.Add(view, filter);
            }
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
