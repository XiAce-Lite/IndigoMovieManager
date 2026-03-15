using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.ViewModels
{
    // サムネ失敗タブ上部の件数サマリだけを軽く管理する。
    public sealed class ThumbnailErrorProgressViewState : INotifyPropertyChanged
    {
        private string visibleCountText = "0";
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
