namespace UnderWarden.Logic.Persistence.MidRun;

/// <summary>
/// Off-critical-path autosave for the mid-run file (M1.4 §File + write discipline). The caller
/// snapshots the GameState on the game thread (SaveMidRun — deterministic, state-safe) and hands the
/// resulting immutable DTO here; the JSON serialization and the atomic .tmp→Move write happen on a
/// background worker.
///
/// LATEST-WINS, NEVER REORDERED — enforced by a monotonic sequence number, not just a single-slot
/// pending: every snapshot gets a strictly increasing seq at enqueue/flush time, and inside the write
/// gate a snapshot is DISCARDED if its seq is not newer than the last one written. So even if a
/// background write holding an older snapshot is overtaken by a synchronous FlushSync (dequeued before
/// FlushSync ran), the stale write is skipped — the file on disk always ends on the most recent turn.
/// </summary>
public sealed class MidRunAutosaveWriter : IDisposable
{
    private readonly string _path;
    private readonly Action<string>? _onError;
    private readonly object _stateLock = new();   // guards _pending / _pendingSeq / _seqCounter / _worker
    private readonly object _writeGate = new();    // serializes file writes; guards _lastWrittenSeq
    private MidRunSaveDto? _pending;
    private long _pendingSeq;
    private long _seqCounter;
    private long _lastWrittenSeq;
    private Task? _worker;
    private volatile bool _disposed;

    /// <summary>Test seam: invoked (with the snapshot's seq) after a background dequeue but BEFORE the
    /// write gate is taken, so a test can deterministically stall a worker and force the reorder
    /// interleaving. Null in production.</summary>
    public Action<long>? OnAfterDequeueForTest { get; set; }

    /// <param name="onError">Optional sink for write failures (e.g. GD.PrintErr). Autosave is
    /// best-effort — a failed write must never crash a turn — but failures are reported, not swallowed.</param>
    public MidRunAutosaveWriter(string path, Action<string>? onError = null)
    {
        _path = path;
        _onError = onError;
    }

    /// <summary>Queue a snapshot for background write. Returns immediately.</summary>
    public void RequestWrite(MidRunSaveDto dto)
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _pending = dto;                                   // supersede any unwritten snapshot
            _pendingSeq = ++_seqCounter;
            if (_worker == null || _worker.IsCompleted)
                _worker = Task.Run(DrainLoop);
        }
    }

    private void DrainLoop()
    {
        while (true)
        {
            MidRunSaveDto? dto;
            long seq;
            lock (_stateLock)
            {
                dto = _pending;
                seq = _pendingSeq;
                _pending = null;
                if (dto == null) return;
            }
            OnAfterDequeueForTest?.Invoke(seq);
            WriteAtomic(dto, seq);
        }
    }

    /// <summary>Write a snapshot synchronously NOW (floor descent, app pause), superseding anything
    /// pending. Blocks until written — the caller wants it durable before proceeding.</summary>
    public void FlushSync(MidRunSaveDto dto)
    {
        long seq;
        lock (_stateLock)
        {
            _pending = null;                                  // this write is the latest
            seq = ++_seqCounter;
        }
        WriteAtomic(dto, seq);
    }

    /// <summary>Wait for any in-flight background write to finish (e.g. before app exit).</summary>
    public void WaitForIdle()
    {
        Task? w;
        lock (_stateLock) { w = _worker; }
        try { w?.Wait(); } catch { /* best-effort */ }
    }

    private void WriteAtomic(MidRunSaveDto dto, long seq)
    {
        lock (_writeGate)                                     // only one writer touches the file at a time
        {
            if (seq <= _lastWrittenSeq) return;              // a newer snapshot already landed — discard this stale one
            try
            {
                MidRunFile.SaveMidRunToFile(dto, _path);
                _lastWrittenSeq = seq;
            }
            catch (Exception ex)
            {
                // Autosave is best-effort; never crash a turn — but report, don't swallow.
                _onError?.Invoke($"mid-run autosave write failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        WaitForIdle();
    }
}
