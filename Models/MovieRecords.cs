using System.Collections.ObjectModel;
using System.ComponentModel;

namespace IndigoMovieManager
{
    /// <summary>
    /// UI（WPF）画面にデータを叩き込むための表示超特化モデル！🖥️✨
    /// DBの1行分に相当しつつ、サムネイル画像のパスや階層構造など、画面表示に直結する「加工済みデータ」を全部抱え込んだ欲張り仕様だ！
    /// </summary>
    public class MovieRecords : INotifyPropertyChanged
    {
        private ObservableCollection<MovieRecords> _Children = null;

        private long movie_id = 0;
        private string movie_name = "";
        private string movie_body = "";
        private string movie_path = "";
        private string movie_path_normalized = "";
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
        private int thumbnailErrorMarkerCount = 0;
        private string drive = "";
        private string dir = "";
        private bool isExists = true;
        private string ext = "";

        /// <summary>
        /// DBに刻まれた唯一無二のナンバー！動画IDだ！🆔✨
        /// </summary>
        public long Movie_Id
        {
            get { return movie_id; }
            set
            {
                movie_id = value;
                OnPropertyChanged(nameof(Movie_Id));
            }
        }

        /// <summary>
        /// 動画の顔！みんなが知ってる表示名だぜ！😀
        /// </summary>
        public string Movie_Name
        {
            get { return movie_name; }
            set
            {
                movie_name = value;
                OnPropertyChanged(nameof(Movie_Name));
            }
        }

        /// <summary>
        /// もぎたて果実のような、純度100%のファイル名本体！拡張子なんてしゃらくせえ！🍎
        /// </summary>
        public string Movie_Body
        {
            get { return movie_body; }
            set
            {
                movie_body = value;
                OnPropertyChanged(nameof(Movie_Body));
            }
        }

        /// <summary>
        /// 動画の命のパス！生パスはUI表示やユーザー入力の「真実」としてそのまま抱え込むぜ！
        /// さらにsetterが発動した瞬間、外部ライブラリ向けの安全な正規化パス『Movie_Path_Normalized』も裏でこっそり自動更新するデキるヤツ！😎
        /// </summary>
        public string Movie_Path
        {
            get { return movie_path; }
            // 生パスは表示/保存の事実値として保持する。
            // 併せて正規化パスを更新し、問題のあるライブラリ呼び出し時だけ使えるようにする。
            set
            {
                movie_path = value ?? "";
                movie_path_normalized = MovieCore.NormalizeMoviePath(value);
                OnPropertyChanged(nameof(Movie_Path));
                OnPropertyChanged(nameof(Movie_Path_Normalized));
            }
        }

        /// <summary>
        /// OpenCV等のライブラリへ特攻する時だけ使う、バグ避けの正規化パスだ！🛡️
        /// </summary>
        public string Movie_Path_Normalized
        {
            get { return movie_path_normalized; }
        }

        /// <summary>
        /// 画面上で映える「00:00:00」形式のきらびやかな再生時間だ！⏰✨
        /// </summary>
        public string Movie_Length
        {
            get { return movie_length; }
            set
            {
                movie_length = value;
                OnPropertyChanged(nameof(Movie_Length));
            }
        }

        /// <summary>
        /// KB単位で丸め込まれた、ユーザーフレンドリーなファイルサイズ！🗜️
        /// </summary>
        public long Movie_Size
        {
            get { return movie_size; }
            set
            {
                movie_size = value;
                OnPropertyChanged(nameof(Movie_Size));
            }
        }

        /// <summary>
        /// 最後に愛でた（再生した）あの日あの時！最終アクセス日時だ！📅💖
        /// </summary>
        public string Last_Date
        {
            get { return last_date; }
            set
            {
                last_date = value;
                OnPropertyChanged(nameof(Last_Date));
            }
        }

        /// <summary>
        /// ファイルが産声を上げた日！更新日時だ！🎂
        /// </summary>
        public string File_Date
        {
            get { return file_date; }
            set
            {
                file_date = value;
                OnPropertyChanged(nameof(File_Date));
            }
        }

        /// <summary>
        /// アプリに迎え入れられた運命の日！登録日時だ！🎉
        /// </summary>
        public string Regist_Date
        {
            get { return regist_date; }
            set
            {
                regist_date = value;
                OnPropertyChanged(nameof(Regist_Date));
            }
        }

        /// <summary>
        /// ユーザーの愛の深さを示す、熱きレーティングスコア！🌟
        /// </summary>
        public long Score
        {
            get { return score; }
            set
            {
                score = value;
                OnPropertyChanged(nameof(Score));
            }
        }

        /// <summary>
        /// 何度見ても飽きない！魂の再生回数だ！🔄🔥
        /// </summary>
        public long View_Count
        {
            get { return viewCount; }
            set
            {
                viewCount = value;
                OnPropertyChanged(nameof(View_Count));
            }
        }

        /// <summary>
        /// ファイルの同一性を暴き出す、真実のハッシュ値！🔍
        /// </summary>
        public string Hash
        {
            get { return hash; }
            set
            {
                hash = value;
                OnPropertyChanged(nameof(Hash));
            }
        }

        /// <summary>
        /// 動画の器！コンテナフォーマット（MP4、MKVなど）だ！📦
        /// </summary>
        public string Container
        {
            get { return container; }
            set
            {
                container = value;
                OnPropertyChanged(nameof(Container));
            }
        }

        /// <summary>
        /// 映像の心臓部！ビデオコーデック情報だ！🎥
        /// </summary>
        public string Video
        {
            get { return video; }
            set
            {
                video = value;
                OnPropertyChanged(nameof(Video));
            }
        }

        /// <summary>
        /// 魂の叫び！オーディオコーデック情報だ！🎸
        /// </summary>
        public string Audio
        {
            get { return audio; }
            set
            {
                audio = value;
                OnPropertyChanged(nameof(Audio));
            }
        }

        /// <summary>
        /// おまけじゃないぜ！エクストラなストリーム情報だ！🎁
        /// </summary>
        public string Extra
        {
            get { return extra; }
            set
            {
                extra = value;
                OnPropertyChanged(nameof(Extra));
            }
        }

        /// <summary>
        /// 誇り高きタイトルの名だ！🏆
        /// </summary>
        public string Title
        {
            get { return title; }
            set
            {
                title = value;
                OnPropertyChanged(nameof(Title));
            }
        }

        /// <summary>
        /// 音源を束ねるアルバム名だ！💿
        /// </summary>
        public string Album
        {
            get { return album; }
            set
            {
                album = value;
                OnPropertyChanged(nameof(Album));
            }
        }

        /// <summary>
        /// 魂を込めたアーティスト名だ！🎤
        /// </summary>
        public string Artist
        {
            get { return artist; }
            set
            {
                artist = value;
                OnPropertyChanged(nameof(Artist));
            }
        }

        /// <summary>
        /// 仲間たちの集い！グルーピング情報だ！🤝
        /// </summary>
        public string Grouping
        {
            get { return grouping; }
            set
            {
                grouping = value;
                OnPropertyChanged(nameof(Grouping));
            }
        }

        /// <summary>
        /// 物語の創造主！ライター情報だ！✍️
        /// </summary>
        public string Writer
        {
            get { return writer; }
            set
            {
                writer = value;
                OnPropertyChanged(nameof(Writer));
            }
        }

        /// <summary>
        /// 作品の魂のベクトル！ジャンルだ！🎭
        /// </summary>
        public string Genre
        {
            get { return genre; }
            set
            {
                genre = value;
                OnPropertyChanged(nameof(Genre));
            }
        }

        /// <summary>
        /// 駆け抜けた軌跡！トラック情報だ！🏁
        /// </summary>
        public string Track
        {
            get { return track; }
            set
            {
                track = value;
                OnPropertyChanged(nameof(Track));
            }
        }

        /// <summary>
        /// その目に焼き付けたカメラ情報だ！📸
        /// </summary>
        public string Camera
        {
            get { return camera; }
            set
            {
                camera = value;
                OnPropertyChanged(nameof(Camera));
            }
        }

        /// <summary>
        /// この世界に生を享けた爆誕の刻！作成日時だ！🕰️
        /// </summary>
        public string Create_Time
        {
            get { return create_time; }
            set
            {
                create_time = value;
                OnPropertyChanged(nameof(Create_Time));
            }
        }

        /// <summary>
        /// 検索用のヨミガナ！カナ検索も任せろ！🔤
        /// </summary>
        public string Kana
        {
            get { return kana; }
            set
            {
                kana = value;
                OnPropertyChanged(nameof(Kana));
            }
        }

        /// <summary>
        /// ローマ字表記！インターナショナルな検索にも対応だ！🌐
        /// </summary>
        public string Roma
        {
            get { return roma; }
            set
            {
                roma = value;
                OnPropertyChanged(nameof(roma));
            }
        }

        /// <summary>
        /// 動画を飾るユーザーの愛の結晶（タグ文字列）だ！🏷️
        /// </summary>
        public string Tags
        {
            get { return tags; }
            set
            {
                tags = value;
                OnPropertyChanged(nameof(Tags));
            }
        }

        /// <summary>
        /// タグをバラしてリスト化した、扱いやすさMAXのタグコレクション！🔖
        /// </summary>
        public List<string> Tag
        {
            get { return tag; }
            set
            {
                tag = value;
                OnPropertyChanged(nameof(Tag));
            }
        }

        /// <summary>
        /// ユーザーの愛のコメント1！自由に語れ！💬
        /// </summary>
        public string Comment1
        {
            get { return comment1; }
            set
            {
                comment1 = value;
                OnPropertyChanged(nameof(Comment1));
            }
        }

        /// <summary>
        /// ユーザーの愛のコメント2！まだまだ語れ！🗨️
        /// </summary>
        public string Comment2
        {
            get { return comment2; }
            set
            {
                comment2 = value;
                OnPropertyChanged(nameof(Comment2));
            }
        }

        /// <summary>
        /// ユーザーの愛のコメント3！最後まで語り尽くせ！🗣️
        /// </summary>
        public string Comment3
        {
            get { return comment3; }
            set
            {
                comment3 = value;
                OnPropertyChanged(nameof(Comment3));
            }
        }

        #region サムネイル画像パス群 (UI表示専用)

        /// <summary>
        /// ちっちゃくて可愛い！小サイズサムネの表示パスだ！🖼️
        /// </summary>
        public string ThumbPathSmall
        {
            get { return thumbPathSmall; }
            set
            {
                thumbPathSmall = value;
                OnPropertyChanged(nameof(ThumbPathSmall));
            }
        }

        /// <summary>
        /// 大迫力！デカサイズサムネの表示パスだ！🖼️✨
        /// </summary>
        public string ThumbPathBig
        {
            get { return thumbPathBig; }
            set
            {
                thumbPathBig = value;
                OnPropertyChanged(nameof(ThumbPathBig));
            }
        }

        /// <summary>
        /// グリッド表示の主役！一覧で映えるサムネパスだ！🟩
        /// </summary>
        public string ThumbPathGrid
        {
            get { return thumbPathGrid; }
            set
            {
                thumbPathGrid = value;
                OnPropertyChanged(nameof(ThumbPathGrid));
            }
        }

        /// <summary>
        /// リスト表示の相棒！スッキリ見せるサムネパスだ！📄
        /// </summary>
        public string ThumbPathList
        {
            get { return thumbPathList; }
            set
            {
                thumbPathList = value;
                OnPropertyChanged(nameof(ThumbPathList));
            }
        }

        /// <summary>
        /// 限界突破のデカさ！超巨大サムネ(Big10)の表示パスだ！🦖
        /// </summary>
        public string ThumbPathBig10
        {
            get { return thumbPathBig10; }
            set
            {
                thumbPathBig10 = value;
                OnPropertyChanged(nameof(thumbPathBig10));
            }
        }

        /// <summary>
        /// 詳細画面を彩る一撃！詳細用サムネパスだ！🔍
        /// </summary>
        public string ThumbDetail
        {
            get { return thumbDetail; }
            set
            {
                thumbDetail = value;
                OnPropertyChanged(nameof(ThumbDetail));
            }
        }

        /// <summary>
        /// `. #ERROR.jpg` マーカー由来の件数を保持し、一覧ソートから重い再走査を避ける。
        /// </summary>
        public int ThumbnailErrorMarkerCount
        {
            get { return thumbnailErrorMarkerCount; }
            set
            {
                thumbnailErrorMarkerCount = value < 0 ? 0 : value;
                OnPropertyChanged(nameof(ThumbnailErrorMarkerCount));
            }
        }

        #endregion

        /// <summary>
        /// 動画が住まうドライブ名！Cか？Dか？それともZか！？💽
        /// </summary>
        public string Drive
        {
            get { return drive; }
            set
            {
                drive = value;
                OnPropertyChanged(nameof(Drive));
            }
        }

        /// <summary>
        /// 動画の根城！ディレクトリパスだ！📁
        /// </summary>
        public string Dir
        {
            get { return dir; }
            set
            {
                dir = value;
                OnPropertyChanged(nameof(Dir));
            }
        }

        /// <summary>
        /// 今そこにいるのか！？リアルタイムの存在確認フラグだ！👁️‍🗨️
        /// </summary>
        public bool IsExists
        {
            get { return isExists; }
            set
            {
                isExists = value;
                OnPropertyChanged(nameof(IsExists));
            }
        }

        /// <summary>
        /// ファイルの顔つきを決める！拡張子だ！🏷️
        /// </summary>
        public string Ext
        {
            get { return ext; }
            set
            {
                ext = value;
                OnPropertyChanged(nameof(Ext));
            }
        }

        /// <summary>
        /// フォルダ毎に動画を束ねた時、ぶら下がる子要素たち（階層表示用）のリストだ！👨‍👩‍👧‍👦
        /// </summary>
        public ObservableCollection<MovieRecords> Children
        {
            get { return _Children; }
            set
            {
                _Children = value;
                OnPropertyChanged(nameof(Children));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// 階層構造のファミリーに新たな子要素（動画）を力強く迎え入れる！ウェルカム・トゥ・ザ・ファミリー！🏘️✨
        /// </summary>
        public void Add(MovieRecords child)
        {
            if (null == Children)
                Children = [];
            Children.Add(child);
        }
    }
}
