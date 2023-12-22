using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace IndigoMovieManager
{
    /// <summary>
    /// ツリービュー。パクリ元：https://araramistudio.jimdo.com/2016/10/24/wpf%E3%81%AEtreeview%E3%81%B8%E3%83%87%E3%83%BC%E3%82%BF%E3%82%92%E3%83%90%E3%82%A4%E3%83%B3%E3%83%89%E3%81%99%E3%82%8B/
    /// </summary>
    public class TreeSource : INotifyPropertyChanged
    {
        private bool _IsExpanded = true;
        private string _Text = "";
        private TreeSource _Parent = null;
        private PackIconKind _IconKind = PackIconKind.File;
        private ObservableCollection<TreeSource> _Children = null;

        public bool IsExpanded
        {
            get { return _IsExpanded; }
            set { _IsExpanded = value; OnPropertyChanged("IsExpanded"); }
        }

        public PackIconKind IconKind
        {
            get { return _IconKind; }
            set { _IconKind = value; OnPropertyChanged("IconKind"); }
        }

        public string Text
        {
            get { return _Text; }
            set { _Text = value; OnPropertyChanged("Text"); }
        }

        public TreeSource Parent
        {
            get { return _Parent; }
            set { _Parent = value; OnPropertyChanged("Parent"); }
        }

        public ObservableCollection<TreeSource> Children
        {
            get { return _Children; }
            set { _Children = value; OnPropertyChanged("Children"); }           
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Add(TreeSource child)
        {
            if (null == Children) Children = [];
            child.Parent = this;
            Children.Add(child);
        }
    }
}
