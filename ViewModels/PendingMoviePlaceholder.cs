using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.ViewModels
{
    // MainDB反映前にUIへ見せるための軽量プレースホルダ。
    public sealed class PendingMoviePlaceholder : INotifyPropertyChanged
    {
        private string moviePath = "";
        private string fileBody = "";
        private int tabIndex;
        private DateTime detectedAtLocal = DateTime.Now;
        private PendingMoviePlaceholderStatus status = PendingMoviePlaceholderStatus.Detected;
        private string lastError = "";
        private DateTime updatedAtLocal = DateTime.Now;

        public string MoviePath
        {
            get => moviePath;
            set => SetField(ref moviePath, value ?? "");
        }

        public string FileBody
        {
            get => fileBody;
            set => SetField(ref fileBody, value ?? "");
        }

        public int TabIndex
        {
            get => tabIndex;
            set => SetField(ref tabIndex, value);
        }

        public DateTime DetectedAtLocal
        {
            get => detectedAtLocal;
            set => SetField(ref detectedAtLocal, value);
        }

        public PendingMoviePlaceholderStatus Status
        {
            get => status;
            set => SetField(ref status, value);
        }

        public string LastError
        {
            get => lastError;
            set => SetField(ref lastError, value ?? "");
        }

        public DateTime UpdatedAtLocal
        {
            get => updatedAtLocal;
            set => SetField(ref updatedAtLocal, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

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

    // 登録待ち行の状態を最低限の段階で管理する。
    public enum PendingMoviePlaceholderStatus
    {
        Detected = 0,
        Queued = 1,
        DbCommitted = 2,
        Failed = 3,
    }
}
