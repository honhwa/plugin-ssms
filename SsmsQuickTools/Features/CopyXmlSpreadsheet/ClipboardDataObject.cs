using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace SsmsQuickTools.Features.CopyXmlSpreadsheet
{
    /// <summary>
    /// <c>IDataObject</c> COM minimo, publicado con <c>OleSetClipboard</c>. Tercer intento de
    /// escritura al portapapeles para este comando (ver docs/PLAN.md M4): los dos anteriores
    /// fallaron en pruebas reales contra SSMS 22.6 — <see cref="System.Windows.Forms.DataObject"/>
    /// expone un <see cref="System.IO.Stream"/> via <c>TYMED_ISTREAM</c> y Excel pide
    /// <c>TYMED_HGLOBAL</c> (no matchea, Excel pega texto plano); y <c>SetClipboardData</c> crudo
    /// (portapapeles "global" de Win32) deja el formato invisible tanto para Excel como para
    /// herramientas de inspeccion de portapapeles, que consultan por el lado OLE. Implementando
    /// <c>IDataObject</c> a mano se controla exactamente que TYMED se ofrece (HGLOBAL) para cada
    /// formato, publicado por el canal OLE que Excel y esas herramientas sí consultan.
    /// </summary>
    internal sealed class ClipboardDataObject : System.Runtime.InteropServices.ComTypes.IDataObject
    {
        private const short CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        private const int S_OK = 0;
        private const int S_FALSE = 1;
        private const int E_NOTIMPL = unchecked((int)0x80004001);
        private const int DV_E_FORMATETC = unchecked((int)0x80040064);
        private const int DATA_S_SAMEFORMATETC = 0x00040130;
        private const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);

        [DllImport("ole32.dll")]
        private static extern int OleSetClipboard(System.Runtime.InteropServices.ComTypes.IDataObject pDataObj);

        [DllImport("ole32.dll")]
        private static extern int OleFlushClipboard();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        private readonly Dictionary<short, byte[]> _payloads = new Dictionary<short, byte[]>();

        private ClipboardDataObject()
        {
        }

        public static void SetXmlSpreadsheetAndText(string xml, string fallbackText)
        {
            var xmlFormatId = RegisterClipboardFormat("XML Spreadsheet");
            if (xmlFormatId == 0)
            {
                throw new InvalidOperationException("No se pudo registrar el formato de portapapeles \"XML Spreadsheet\" (error " + Marshal.GetLastWin32Error() + ").");
            }

            var dataObject = new ClipboardDataObject();
            dataObject._payloads[unchecked((short)xmlFormatId)] = new UTF8Encoding(false).GetBytes(xml);
            dataObject._payloads[CF_UNICODETEXT] = Encoding.Unicode.GetBytes(fallbackText + "\0"); // CF_UNICODETEXT requiere terminador nulo

            var hr = OleSetClipboard(dataObject);
            if (hr != S_OK)
            {
                throw new InvalidOperationException("No se pudo publicar el portapapeles (OleSetClipboard, hr=0x" + hr.ToString("X8") + ").");
            }
            OleFlushClipboard(); // sobrevive al cierre de la ventana/SSMS
        }

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            if (format.tymed != TYMED.TYMED_HGLOBAL || !_payloads.TryGetValue(format.cfFormat, out var bytes))
            {
                throw new COMException("Formato no soportado.", DV_E_FORMATETC);
            }

            var handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
            if (handle == IntPtr.Zero)
            {
                throw new OutOfMemoryException("No hay memoria disponible para escribir en el portapapeles.");
            }

            var ptr = GlobalLock(handle);
            try
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = handle, pUnkForRelease = null };
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new COMException(null, E_NOTIMPL);

        public int QueryGetData(ref FORMATETC format)
        {
            return format.tymed == TYMED.TYMED_HGLOBAL && _payloads.ContainsKey(format.cfFormat) ? S_OK : DV_E_FORMATETC;
        }

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            return DATA_S_SAMEFORMATETC;
        }

        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) => throw new COMException(null, E_NOTIMPL);

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            if (direction != DATADIR.DATADIR_GET)
            {
                throw new COMException(null, E_NOTIMPL);
            }

            var formats = new FORMATETC[_payloads.Count];
            var i = 0;
            foreach (var cfFormat in _payloads.Keys)
            {
                formats[i++] = new FORMATETC
                {
                    cfFormat = cfFormat,
                    ptd = IntPtr.Zero,
                    dwAspect = DVASPECT.DVASPECT_CONTENT,
                    lindex = -1,
                    tymed = TYMED.TYMED_HGLOBAL,
                };
            }
            return new FormatEtcEnumerator(formats, 0);
        }

        public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
        {
            connection = 0;
            return OLE_E_ADVISENOTSUPPORTED;
        }

        public void DUnadvise(int connection) => throw new COMException(null, OLE_E_ADVISENOTSUPPORTED);

        public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
        {
            enumAdvise = null;
            return OLE_E_ADVISENOTSUPPORTED;
        }

        private sealed class FormatEtcEnumerator : IEnumFORMATETC
        {
            private readonly FORMATETC[] _formats;
            private int _index;

            public FormatEtcEnumerator(FORMATETC[] formats, int startIndex)
            {
                _formats = formats;
                _index = startIndex;
            }

            public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
            {
                var fetched = 0;
                while (fetched < celt && _index < _formats.Length)
                {
                    rgelt[fetched] = _formats[_index];
                    _index++;
                    fetched++;
                }
                if (pceltFetched != null && pceltFetched.Length > 0)
                {
                    pceltFetched[0] = fetched;
                }
                return fetched == celt ? S_OK : S_FALSE;
            }

            public int Skip(int celt)
            {
                _index += celt;
                return _index <= _formats.Length ? S_OK : S_FALSE;
            }

            public int Reset()
            {
                _index = 0;
                return S_OK;
            }

            public void Clone(out IEnumFORMATETC newEnum) => newEnum = new FormatEtcEnumerator(_formats, _index);
        }
    }
}
