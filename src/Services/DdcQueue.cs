using System;
using System.Collections.Generic;
using System.Threading;

namespace MonitorConfig
{
    // Single worker for all DDC/CI traffic: the bus is slow and must not be used concurrently. Writes to the same
    // control are coalesced, so dragging a slider only sends the latest value. In manual mode (tests) jobs wait
    // until RunPending(), which makes every interleaving reproducible.
    class DdcQueue : IDisposable
    {
        readonly object gate = new object();
        readonly Queue<Action> jobs = new Queue<Action>();
        readonly Dictionary<string, uint> pending = new Dictionary<string, uint>();
        readonly Thread thread;   // null in manual mode
        bool stopping;
        int running;
        public event Action<Exception> Error;

        public static DdcQueue Threaded() { return new DdcQueue(true); }
        public static DdcQueue Manual() { return new DdcQueue(false); }

        DdcQueue(bool threaded)
        {
            if (!threaded) return;
            thread = new Thread(Run) { IsBackground = true, Name = "DDC/CI" };
            thread.Start();
        }

        public int Pending { get { lock (gate) return jobs.Count + running; } }

        public void Post(Action job)
        {
            lock (gate)
            {
                if (stopping) return;
                jobs.Enqueue(job);
                Monitor.Pulse(gate);
            }
        }

        public void PostCoalesced(string key, uint value, Action<uint> job)
        {
            lock (gate)
            {
                if (stopping) return;
                bool queued = pending.ContainsKey(key);
                pending[key] = value;
                if (queued) return;
                jobs.Enqueue(delegate
                {
                    uint latest;
                    lock (gate) { latest = pending[key]; pending.Remove(key); }
                    job(latest);
                });
                Monitor.Pulse(gate);
            }
        }

        // Manual mode: runs the queued jobs (and the ones they queue) until none is left.
        public int RunPending()
        {
            int count = 0;
            while (true)
            {
                Action job;
                lock (gate)
                {
                    if (jobs.Count == 0) return count;
                    job = jobs.Dequeue();
                }
                Execute(job);
                count++;
            }
        }

        void Execute(Action job)
        {
            try { job(); }
            catch (Exception ex) { var h = Error; if (h != null) h(ex); }
        }

        void Run()
        {
            while (true)
            {
                Action job;
                lock (gate)
                {
                    while (jobs.Count == 0 && !stopping) Monitor.Wait(gate);
                    if (jobs.Count == 0) return;
                    job = jobs.Dequeue();
                    running = 1;
                }
                Execute(job);
                lock (gate) running = 0;
            }
        }

        // Finishes the queued jobs (so the last slider value still reaches the monitor) and stops.
        public void Dispose()
        {
            lock (gate) { stopping = true; Monitor.Pulse(gate); }
            if (thread != null) thread.Join(3000);
            else RunPending();
        }
    }
}
