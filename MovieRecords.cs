using System.Collections.ObjectModel;
using System.ComponentModel;

namespace IndigoMovieManager
{
    public class MovieRecords : INotifyPropertyChanged
    {
        private ObservableCollection<MovieRecords> _Children = null;

        private long movie_id = 0;
        private string movie_name = "";
        private string movie_body = "";
        private string movie_path = "";
        private string movie_length = "";
        private long movie_size = 0;
        private string last_date = "";
        private string file_date = "";
        private string regist_date = "";
        private long score = 0;
        private long viewCount = 0;
        private string hash = "";
        private string container = "";
        private string video = "";
        private string audio = "";
        private string extra = "";
        private string title = "";
        private string artist = "";
        private string album = "";
        private string grouping = "";
        private string writer = "";
        private string genre = "";
        private string track = "";
        private string camera = "";
        private string create_time;
        private string kana = "";
        private string roma = "";
        private string tags = "";
        private List<string> tag = [];
        private string comment1 = "";
        private string comment2 = "";
        private string comment3 = "";
        private string thumbPathSmall = "";
        private string thumbPathBig = "";
        private string thumbPathGrid = "";
        private string thumbPathList = "";
        private string thumbPathBig10 = "";
        private string thumbDetail = "";
        private string drive = "";
        private string dir = "";
        private bool isExists = true;
        private string ext = "";

        public long Movie_Id
        {
            get { return movie_id; }
            set { movie_id = value; OnPropertyChanged(nameof(Movie_Id)); }
        }

        public string Movie_Name
        {
            get { return movie_name; }
            set { movie_name = value; OnPropertyChanged(nameof(Movie_Name)); }
        }

        public string Movie_Body
        {
            get { return movie_body; }
            set { movie_body = value; OnPropertyChanged(nameof(Movie_Body)); }
        }

        public string Movie_Path { 
            get { return movie_path; }
            set { movie_path = value; OnPropertyChanged(nameof(Movie_Path)); }
        }

        public string Movie_Length
        {
            get { return movie_length; }
            set { movie_length = value; OnPropertyChanged(nameof(Movie_Length)); }
        }

        public long Movie_Size
        {
            get { return movie_size; }
            set { movie_size = value; OnPropertyChanged(nameof(Movie_Size)); }
        }

        public string Last_Date
        {
            get { return last_date; }
            set { last_date = value; OnPropertyChanged(nameof(Last_Date)); }
        }

        public string File_Date
        {
            get { return file_date; }
            set { file_date = value; OnPropertyChanged(nameof(File_Date)); }
        }

        public string Regist_Date { 
            get { return regist_date; }
            set { regist_date = value; OnPropertyChanged(nameof(Regist_Date)); }
        }

        public long Score
        {
            get { return score; }
            set { score = value; OnPropertyChanged(nameof(Score));}
        }

        public long View_Count
        {
            get { return viewCount; }
            set { viewCount = value; OnPropertyChanged(nameof(View_Count)); }
        }

        public string Hash
        {
            get { return hash; }
            set { hash = value; OnPropertyChanged(nameof(Hash)); }
        }

        public string Container
        {
            get { return container; }
            set { container = value; OnPropertyChanged(nameof(Container)); }
        }

        public string Video
        {
            get { return video; }
            set { video = value; OnPropertyChanged(nameof(Video));}
        }

        public string Audio
        {
            get { return audio; }
            set { audio = value; OnPropertyChanged(nameof(Audio)); }
        }

        public string Extra
        {
            get { return extra; }
            set { extra = value; OnPropertyChanged(nameof(Extra));}
        }

        public string Title
        {
            get { return title; }
            set { title = value; OnPropertyChanged(nameof(Title)); }
        }

        public string Album
        {
            get { return album; }
            set { album = value; OnPropertyChanged(nameof(Album));}
        }

        public string Artist
        {
            get { return artist; }
            set { artist = value; OnPropertyChanged(nameof(Artist)); }
        }

        public string Grouping
        {
            get { return grouping; }
            set { grouping = value; OnPropertyChanged(nameof(Grouping)); }
        }

        public string Writer
        {
            get { return writer; }
            set { writer = value; OnPropertyChanged(nameof(Writer)); }
        }

        public string Genre
        {
            get { return genre; }
            set { genre = value; OnPropertyChanged(nameof(Genre)); }
        }

        public string Track
        {
            get { return track; }
            set { track = value; OnPropertyChanged(nameof(Track)); }
        }

        public string Camera
        {
            get { return camera; }
            set { camera = value; OnPropertyChanged(nameof(Camera)); }  
        }

        public string Create_Time
        {
            get { return create_time; }
            set { create_time = value; OnPropertyChanged(nameof(Create_Time)); }
        }

        public string Kana
        {
            get { return kana;}
            set { kana = value; OnPropertyChanged(nameof(Kana)); }
        }

        public string Roma
        {
            get { return roma; }
            set { roma = value; OnPropertyChanged(nameof(roma)); }
        }

        public string Tags
        {
            get { return tags; }
            set { tags = value; OnPropertyChanged(nameof(Tags)); }
        }

        public List<string> Tag
        {
            get { return tag; }
            set { tag = value; OnPropertyChanged(nameof(Tag));}
        }

        public string Comment1
        {
            get { return comment1; }
            set { comment1 = value; OnPropertyChanged(nameof(Comment1));}
        }

        public string Comment2
        {
            get { return comment2; }
            set { comment2 = value; OnPropertyChanged(nameof(Comment2)); }
        }

        public string Comment3
        {
            get { return comment3; }
            set { comment3 = value; OnPropertyChanged(nameof(Comment3)); }
        }

        public string ThumbPathSmall
        {
            get { return thumbPathSmall; }
            set { thumbPathSmall = value; OnPropertyChanged(nameof(ThumbPathSmall)); }
        }

        public string ThumbPathBig
        {
            get { return thumbPathBig; }
            set { thumbPathBig = value; OnPropertyChanged(nameof(ThumbPathBig)); }
        }

        public string ThumbPathGrid
        {
            get { return thumbPathGrid; }
            set { thumbPathGrid = value; OnPropertyChanged(nameof(ThumbPathGrid)); }
        }

        public string ThumbPathList
        {
            get { return thumbPathList; }
            set { thumbPathList = value; OnPropertyChanged(nameof(ThumbPathList)); }
        }

        public string ThumbPathBig10
        {
            get { return thumbPathBig10; }
            set { thumbPathBig10 = value; OnPropertyChanged(nameof(thumbPathBig10)); }
        }

        public string ThumbDetail
        {
            get { return thumbDetail; }
            set { thumbDetail = value; OnPropertyChanged(nameof(ThumbDetail)); }
        }

        public string Drive
        {
            get { return drive; }
            set { drive = value; OnPropertyChanged(nameof(Drive)); }
        }

        public string Dir
        {
            get { return dir; }
            set { dir = value; OnPropertyChanged(nameof(Dir));}
        }

        public bool IsExists
        {
            get { return isExists; } set { isExists = value; OnPropertyChanged(nameof(IsExists)); } 
        }

        public string Ext
        {
            get { return ext; }
            set { ext = value; OnPropertyChanged(nameof(Ext)); }
        }

        public ObservableCollection<MovieRecords> Children
        {
            get { return _Children; }
            set { _Children = value; OnPropertyChanged(nameof(Children)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Add(MovieRecords child)
        {
            if (null == Children) Children = [];
            Children.Add(child);
        }
    }
}
