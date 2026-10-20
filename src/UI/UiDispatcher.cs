using System;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Brings results from the DDC thread back to the UI thread.
    sealed class UiDispatcher : IDisposable
    {
        readonly Control invoker = new Control();

        public UiDispatcher()
        {
            var unused = invoker.Handle;   // the window that receives the posted calls
        }

        public void Post(Action action)
        {
            try { if (!invoker.IsDisposed) invoker.BeginInvoke(action); }
            catch (InvalidOperationException) { }   // closing: the window is gone (ObjectDisposedException included)
        }

        public void Dispose() { invoker.Dispose(); }
    }
}
