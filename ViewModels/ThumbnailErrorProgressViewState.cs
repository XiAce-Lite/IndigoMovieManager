using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.ViewModels
{
    // サムネ失敗タブ上部の件数サマリだけを軽く管理する。
    public sealed class ThumbnailErrorProgressViewState : INotifyPropertyChanged
    {
        private string visibleCountText = "0";
        private string markerCountText = "0";
        private string managedCountText = "0";
        private string unqueuedCountText = "0";
        private string pendingCountText = "0";
        private string processingCountText = "0";
        private string rescuedCountText = "0";
        private string attentionCountText = "0";

        public string VisibleCountText
        {
            get => visibleCountText;
            private set => SetField(ref visibleCountText, value);
        }

        public string MarkerCountText
        {
            get => markerCountText;
            private set => SetField(ref markerCountText, value);
        }

        public string ManagedCountText
        {
            get => managedCountText;
            private set => SetField(ref managedCountText, value);
        }

        public string UnqueuedCountText
        {
            get => unqueuedCountText;
            private set => SetField(ref unqueuedCountText, value);
        }

        public string PendingCountText
        {
            get => pendingCountText;
            private set => SetField(ref pendingCountText, value);
        }

        public string ProcessingCountText
        {
            get => processingCountText;
            private set => SetField(ref processingCountText, value);
        }

        public string RescuedCountText
        {
            get => rescuedCountText;
            private set => SetField(ref rescuedCountText, value);
        }

        public string AttentionCountText
        {
            get => attentionCountText;
            private set => SetField(ref attentionCountText, value);
        }

        public void Apply(IEnumerable<ThumbnailErrorRecordViewModel> records)
        {
            ThumbnailErrorRecordViewModel[] safeRecords = [.. records ?? []];

            VisibleCountText = safeRecords.Length.ToString();
            MarkerCountText = safeRecords.Sum(x => Math.Max(0, x?.MarkerCount ?? 0)).ToString();
            ManagedCountText = safeRecords.Count(x => IsManagedRecord(x)).ToString();
            UnqueuedCountText = CountByKey(safeRecords, "unqueued").ToString();
            PendingCountText = CountByKey(safeRecords, "pending").ToString();
            ProcessingCountText = CountByKey(safeRecords, "processing").ToString();
            RescuedCountText = CountByKey(safeRecords, "rescued").ToString();
            AttentionCountText = CountByKey(safeRecords, "attention").ToString();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private static int CountByKey(
            IEnumerable<ThumbnailErrorRecordViewModel> records,
            string summaryKey
        )
        {
            return records.Count(x =>
                string.Equals(x?.ProgressSummaryKey ?? "", summaryKey, StringComparison.Ordinal)
            );
        }

        // FailureDb 由来の進行状態があれば、実マーカーの有無とは別に救済管理中として数える。
        private static bool IsManagedRecord(ThumbnailErrorRecordViewModel record)
        {
            return !string.Equals(
                record?.ProgressSummaryKey ?? "",
                "unqueued",
                StringComparison.Ordinal
            );
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
