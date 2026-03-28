//---------------------------------------------------------------------------
//	sinku.dll ヘッダファイル
//	黒羽製作所
//---------------------------------------------------------------------------
#ifndef sinkuH
#define sinkuH
//---------------------------------------------------------------------------
#include <tchar.h>
//---------------------------------------------------------------------------
#define	VA_MAX_STREAM_COUNT	10
#define	EX_MAX_STREAM_COUNT	5
#define	STR_MAX_LENGTH		512
#ifdef _UNICODE
	#define	FILENAME_MAX_LENGTH	256
#else
	#define	FILENAME_MAX_LENGTH	512
#endif

enum {
	//ファイル形式

	//エラー系
	FILETYPE_ERROR = 0,

	FILETYPE_UNKNOWN,			//未対応のファイル
	FILETYPE_OPENFAILED,		//ファイルのオープンに失敗

	//エラー系終了
	FILETYPE_ERROR_END,

	//再生可能な形式
	FILETYPE_MEDIA = 10,

	//映像と音声がある形式
	FILETYPE_VIDEO_AUDIO,
	FILETYPE_AVI,				//RIFF AVI
	FILETYPE_MPEG1,				//MPEG1
	FILETYPE_MPEG1VCD,			//MPEG1(VideoCD)
	FILETYPE_OGGMEDIA,			//Ogg Media
	FILETYPE_MPEG2,				//MPEG2
	FILETYPE_MPEG2_TS,			//MPEG2-TS
	FILETYPE_WMV,				//WindowsMedia Video
	FILETYPE_REALMEDIA,			//RealMedia
	FILETYPE_QUICKTIME,			//QuickTime
	FILETYPE_MPEG4,				//QuickTime(MPEG4)
	FILETYPE_RESERVED2,			//未使用
	FILETYPE_RESERVED3,			//未使用
	FILETYPE_RESERVED4,			//未使用
	FILETYPE_MATROSKA,			//Matroska
	FILETYPE_FLASHVIDEO,		//FlashVideo

	//画像の形式(おまけ)
	FILETYPE_GRAPHIC = 50,
	FILETYPE_BMP,				//BMP
	FILETYPE_JPEG,				//JPEG/Exif
	FILETYPE_GIF,				//GIF

	//画像の形式終了
	FILETYPE_GRAPHIC_END,

	//映像と音声がある形式終了
	FILETYPE_VIDEO_AUDIO_END,

	//音声のみの形式
	FILETYPE_AUDIO = 100,
	FILETYPE_WAV,				//RIFF WAVE
	FILETYPE_MID,				//MIDI
	FILETYPE_RMI,				//RIFF MIDI
	FILETYPE_AU,				//Au
	FILETYPE_AIFF,				//AIFF
	FILETYPE_MP3,				//MP3(MPEG1～2.5 LayerI～III)
	FILETYPE_CDA,				//RIFF CDDA
	FILETYPE_RMP3,				//RIFF MP3
	FILETYPE_OGGVORBIS,			//Ogg Vorbis
	FILETYPE_OGGSPEEX,			//Ogg Speex
	FILETYPE_OGGFLAC,			//Ogg Flac
	FILETYPE_WMA,				//WindowsMedia Audio
	FILETYPE_AC3,				//Dolby AC-3
	FILETYPE_MONKEY,			//Monkey's Audio
	FILETYPE_MUSEPACK,			//MusePack
	FILETYPE_FLAC,				//Flac
	FILETYPE_AAC,				//AAC(MPEG2/MPEG4)
	FILETYPE_REALAUDIO,			//RealMedia以前の旧RealAudio
	FILETYPE_TTA,				//The True Audio
	FILETYPE_DTS14LE,			//DTS
	FILETYPE_DTS14BE,
	FILETYPE_DTS16LE,
	FILETYPE_DTS16BE,
	FILETYPE_WAVPACK,			//WavPack
	FILETYPE_OPTIMFROG,			//OptimFROG
	FILETYPE_VQ,				//TwinVQ
	FILETYPE_SWA,				//Shockwave Audio
	FILETYPE_PUREVOICE,			//QUALCOMM PureVoice
	FILETYPE_S98,				//S98
	FILETYPE_DSF,				//DSD Stream File
	FILETYPE_DSDIFF,			//Direct Stream Digital Interchange File Format
	FILETYPE_WSD,				//Wideband Single-bit Data
	FILETYPE_DTSHD,				//DTS-HD Audio Elementary Stream File Format
	FILETYPE_MSF,				//PS3 MSF
	FILETYPE_ADX,				//CRI ADX
	FILETYPE_AIX,				//CRI ADX(Multitrack)
 	FILETYPE_HCA,				//CRI HCA

	//音声のみの形式終了
	FILETYPE_AUDIO_END,

	//再生可能な形式終了
	FILETYPE_MEDIA_END,

	//以下テスト用(認識はしてもそれ以上手を付けてない等)の形式
	FILETYPE_TEST = 200,
	FILETYPE_RESERVED5,			//未使用

	//テスト用の形式終了
	FILETYPE_TEST_END,

	//プレイリスト
	FILETYPE_PLAYLIST = 300,
	PLAYLIST_SHDKL,
	PLAYLIST_PLS,
	PLAYLIST_LST,
	PLAYLIST_ZPL,
	PLAYLIST_BSL,
	PLAYLIST_M3U,
	PLAYLIST_M3U8,

	//プレイリスト終了
	FILETYPE_PLAYLIST_END,
};

typedef struct {
	TCHAR				name[FILENAME_MAX_LENGTH];					//IN     : ファイル名
	int					type;										//IN/OUT : ファイル種別
	char				container[32];								//OUT    : コンテナ名(ある場合のみ)
	char				error[STR_MAX_LENGTH];						//OUT    : エラー(ある場合のみ)
	char				video[VA_MAX_STREAM_COUNT][STR_MAX_LENGTH];	//OUT    : 映像の詳細
	int					video_count;								//OUT    : 映像の詳細の数
	char				audio[VA_MAX_STREAM_COUNT][STR_MAX_LENGTH];	//OUT    : 音声の詳細
	int					audio_count;								//OUT    : 音声の詳細の数
	char				extra[EX_MAX_STREAM_COUNT][STR_MAX_LENGTH];	//OUT    : 付属情報
	int					extra_count;								//OUT    : 付属情報の数
	double				playtime;									//OUT    : 再生時間(秒)
	unsigned __int64	size;										//OUT    : ファイルサイズ
} FILE_INFO;

typedef struct
{
	HINSTANCE	hDLL;
	int		version;

	int		(__stdcall *GetDllVersion )( void );
	void	(__stdcall *SetPath )( TCHAR *path );
	void	(__stdcall *GetFileFormat )( FILE_INFO *pFILE_INFO );
	void	(__stdcall *GetFileInfo )( FILE_INFO *pFILE_INFO );
	void	(__stdcall *GetFileInfoAuto )( FILE_INFO *pFILE_INFO );
	void	(__stdcall *ConvertOne )( FILE_INFO *pFILE_INFO, char *str );
	void	(__stdcall *ConvertFull )( FILE_INFO *pFILE_INFO, char *str );
	int		(__stdcall *ConvertOneLen )( FILE_INFO *pFILE_INFO );
	int		(__stdcall *ConvertFullLen )( FILE_INFO *pFILE_INFO );
	int		(__stdcall *Unicode )( void );
} SinkuFunc;

//---------------------------------------------------------------------------
#endif
