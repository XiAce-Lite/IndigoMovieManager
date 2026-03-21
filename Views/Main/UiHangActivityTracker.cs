namespace IndigoMovieManager
{
    internal enum UiHangActivityKind
    {
        None = 0,
        Watch = 1,
        Database = 2,
        Thumbnail = 3,
        Startup = 4,
    }

    internal readonly record struct UiHangActivitySnapshot(UiHangActivityKind Kind, bool HasActiveScope);

    internal sealed class UiHangActivityTracker
    {
        private readonly object _gate = new();
        private readonly Dictionary<UiHangActivityKind, ActivityEntry> _entries = [];
        private long _sequence;

        // いま走っている代表処理だけを 1 つ返し、通知文言の切替に使う。
        internal UiHangActivitySnapshot GetCurrentSnapshot()
        {
            lock (_gate)
            {
                UiHangActivityKind selectedKind = UiHangActivityKind.None;
                long selectedSequence = 0;

                foreach (KeyValuePair<UiHangActivityKind, ActivityEntry> pair in _entries)
                {
                    if (pair.Value.Count < 1)
                    {
                        continue;
                    }

                    if (pair.Value.LastSequence < selectedSequence)
                    {
                        continue;
                    }

                    selectedKind = pair.Key;
                    selectedSequence = pair.Value.LastSequence;
                }

                return new UiHangActivitySnapshot(
                    selectedKind,
                    selectedKind != UiHangActivityKind.None
                );
            }
        }

        internal IDisposable Begin(UiHangActivityKind kind)
        {
            if (kind == UiHangActivityKind.None)
            {
                return NoopDisposable.Instance;
            }

            long sequence;
            lock (_gate)
            {
                sequence = ++_sequence;
                if (_entries.TryGetValue(kind, out ActivityEntry entry))
                {
                    entry.Count++;
                    entry.LastSequence = sequence;
                    return new Scope(this, kind);
                }

                _entries[kind] = new ActivityEntry { Count = 1, LastSequence = sequence };
            }

            return new Scope(this, kind);
        }

        private void End(UiHangActivityKind kind)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(kind, out ActivityEntry entry))
                {
                    return;
                }

                entry.Count--;
                if (entry.Count < 1)
                {
                    _entries.Remove(kind);
                }
            }
        }

        private sealed class ActivityEntry
        {
            internal int Count { get; set; }

            internal long LastSequence { get; set; }
        }

        private sealed class Scope : IDisposable
        {
            private readonly UiHangActivityTracker _owner;
            private readonly UiHangActivityKind _kind;
            private int _disposed;

            internal Scope(UiHangActivityTracker owner, UiHangActivityKind kind)
            {
                _owner = owner;
                _kind = kind;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                _owner.End(_kind);
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            internal static readonly NoopDisposable Instance = new();

            public void Dispose() { }
        }
    }
}
