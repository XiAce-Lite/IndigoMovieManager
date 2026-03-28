今サムネさ再作成タブ切り替えで落ちた、調査


6m 55s作業しました
原因は WPF のタブ切替例外ではなく、2026-03-13 23:13:51 の Application Error で swscale-8.dll が 0xc0000005 で落ちていました。ダンプも C:\Users\na6ce\AppData\Local\CrashDumps\IndigoMovieManager_fork_workthree.exe.27344.dmp に出ています。直前ログではタブ切替で tab-error-placeholder を 1981 件まとめて救済キューへ積んでいて、その直後に通常レーンの autogen が native 側で落ちています。つまり「タブ切替が直接 null 参照で落ちた」ではなく、「切替をきっかけに負荷が跳ねたところで FFmpeg.AutoGen / swscale 側が死んだ」です。

対策は 2 点入れました。C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Engines\FfmpegAutoGenThumbnailGenerationEngine.cs で swscale 呼び出しを小さく直列化し、sws_getContext / sws_scale / sws_freeContext の同時実行クラッシュを避けるようにしました。C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\MainWindow.xaml.cs では、タブ切替時の自動救済投入を 64 件までに制限して、数千件を一気に撒かないようにしました。

確認は dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64 成功、関連テスト 6 件成功です。テストプロジェクトの通常ビルドは、実行中プロセスが DLL を掴んでいて失敗しました。次はアプリを再起動して、同じ「サムネ再作成タブ切替」をもう一度踏んでログの再発有無を見れば十分です。


2 個のファイルが変更されました
+100
-14
元に戻す
MainWindow.xaml.cs
Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs
起動で落ちる


5m 15s作業しました
起動落ちは引き続き swscale-8.dll の native crash でしたが、起動直後の自動サムネ生成で autogen を踏んでいたのが原因でした。既定ルーティングを変更して、通常起動時は ffmpeg1pass を先頭にし、autogen は IMM_THUMB_ENABLE_AUTOGEN=1 を明示した時だけ既定候補へ戻るようにしています。ThumbnailEngineRouter.cs ThumbnailCreationService.cs

確認として本体ビルド成功、関連テスト成功、さらに IndigoMovieManager_fork_workthree.exe を 12 秒起動して落ちずに生存を確認しました。Properties\launchSettings.json にも IMM_THUMB_ENGINE=autogen の強制は入っていません。まずそのまま再起動してみてください。もしまだ落ちるなら、その時刻の新しい debug-runtime.log とクラッシュダンプを続けて見ます。


4 個のファイルが変更されました
+130
-42
元に戻す
Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs
Thumbnail/Engines/ThumbnailEngineRouter.cs
Thumbnail/Test/AutogenRegressionTests.cs
Thumbnail/ThumbnailCreationService.cs
却下aotogenで動かす＞
定ルーティングを変更して、通常起動時は ffmpeg1pass を先頭にし、autogen は IMM_THUMB_ENABLE_AUTOGEN=1 を明示した時だけ既定候補へ戻るようにしています


3m 56s作業しました
結論から言うと、俺の今の考えはこうです。

落ちている場所は WPF や C# の managed 例外ではなく、swscale-8.dll の native crash
しかも毎回ほぼ同じ署名で落ちている
Exception code: 0xc0000005
Faulting module: swscale-8.dll
なので「タブ切替の null 参照を直す」系ではなく、autogen が内部で叩く FFmpeg native 側の安定化が本筋
その根拠です。

2026-03-13 23:13:51 と 2026-03-13 23:59:40 の両方で、Windows の Application Error が同じ swscale-8.dll を指していた
debug-runtime.log では、落ちる直前まで engine selected: id=autogen が大量に出ている
つまり「起動時やタブ切替時に autogen が走る -> FFmpeg native で落ちる」という流れが見えている
ここで一度、俺は「通常ルートを ffmpeg1pass 既定へ逃がす」案を入れた。
でもこれは君の意図とズレる。

君は autogen を主力として使いたい
このブランチ方針も、本来はテンポ感を優先する
autogen を降格すると、落ちは避けやすいが、設計の芯を曲げる
だから却下は正しい。そこは戻した。

その上で今やったことは、「autogen をやめる」のではなく、「autogen の危ないところだけ絞る」です。

具体的には 2 段です。

swscale 呼び出しの直列化
C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Engines\FfmpegAutoGenThumbnailGenerationEngine.cs
sws_getContext
sws_scale
sws_freeContext
ここをゲートで囲った。
理由は単純で、クラッシュしているモジュールがまさに swscale-8.dll だから。
まずは犯人候補のど真ん中を絞るのが筋。

それでも落ちたので、autogen 実行全体を既定で 1 本ずつ通すようにした
同じファイルで EngineExecutionGate を追加
CreateAsync
CreateBookmarkAsync
ここでやっているのは、
「swscale 単体だけじゃなく、その前後の FFmpeg native 呼び出しも含めて競合しているかもしれない」
という見立てへの対処です。

要するにこうです。

最初の仮説: sws_scale の同時実行が危ない
追加の仮説: avcodec_send_packet や avcodec_receive_frame、seek/flush を含む native 呼び出し全体の並行実行も危ないかもしれない
だから autogen だけは「速いが危ない高速車線」から「1台ずつ通す高速車線」へ変えた
この判断の意図は、
autogen を捨てずに、落ちずに走らせるためです。

トレードオフもある。

良い点
autogen は維持できる
native crash をかなり潰せる可能性が高い
ルーティング思想を壊さない
悪い点
autogen 単体の並列性は落ちる
大量生成時のピーク速度は下がる可能性がある
でも、ここは順番があります。

まず「落ちない」が先
次に「落ちないままどこまで並列を戻せるか」を詰める
この順にしないと、速いけど落ちる実装を延々触ることになる。
それは workthree の主旨にも合わない。

あと、タブ切替時の「失敗サムネ一括救済」を 64 件に絞ったのも同じ思想です。

タブ切替だけで数千件の rescue を撒くのは、UI 操作に対して過剰
体感テンポも悪くなる
native 側に一気に負荷を寄せる引き金にもなる
ここも「機能は残すが、雪崩れは止める」という最小修正です。

今回の状態はこうです。

autogen は既定のまま
ffmpeg1pass 既定化は撤回済み
autogen 内部だけ安全弁を追加
ビルド成功
AutogenRegressionTests と AutogenExecutionFlowTests 成功
実行ファイルを 12 秒起動して生存確認済み
だから俺の考えを一言で言うと、

ルーティングを変えて逃げるのは簡単だが、それは今回は筋が悪い
本筋は autogen を主力のまま安定化すること
そのために、まず native 競合を潰す安全側の直列化を入れた
です。

次に見るべきポイントもはっきりしています。

これで再発しないなら、原因は「autogen の native 並列競合」が濃厚
もしまだ落ちるなら、今度は autogen のどの native 呼び出し帯で死ぬかをさらに絞る
その段階で初めて「一部条件だけ autogen を外す」みたいな条件分岐を検討する
必要なら次は、
FfmpegAutoGenThumbnailGenerationEngine.cs のどこがどう危ないと見ているかを、関数単位でもっと細かく説明する。


5 個のファイルが変更されました
+73
-132
元に戻す
Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs
Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs
Thumbnail/Engines/ThumbnailEngineRouter.cs
Thumbnail/Test/AutogenRegressionTests.cs
Thumbnail/ThumbnailCreationService.cs
主力（autogen）のまま危険な部分だけを飼いならすということか？

そう。それが今の考えです。

autogen を降ろさない
でも autogen の native で危ない帯域だけ縛る
つまり「主力のまま、暴れる所だけ首輪を付ける」
イメージとしてはこうです。

ルーティングはそのまま
UI や上位設計もそのまま
ただし autogen の内部実行だけ、安全側に寄せる
今回だと危険候補は swscale-8.dll 周辺なので、
まずそこを絞って、
それでも足りなかったから autogen 実行全体を 1 本ずつ通す形に広げた。

要するに、

「autogen をやめる」ではない
「autogen を制御下に置く」
です。

この方針の利点は、
速度思想やこのブランチの主戦略を壊さずに、落ち筋だけ潰せることです。


気に入った、GO

進めます。autogen 主力のまま、危険帯域だけ順に絞って安定化させます。次に再発したら、その時点のダンプとログで「どこまで首輪を強めるか」を詰めます。


comtinu

このまま autogen 安定化を前に進めます。次は、起動後しばらく実行させて本当に再発しないか見つつ、必要なら追跡ログを足せるように現状の autogen 実行帯域を確認します。

実行済みコマンド：$exe = Join-Path (Resolve-Path .\bin\x64\Debug\net8.0-windows) 'IndigoMovieManager_fork_workthree.exe'; $p = Start-Process -FilePath $exe -PassThru; Start-Sleep -Seconds 45; $alive = Get-Process -Id $p.Id -ErrorAction SilentlyContinue; if ($alive) { "ALIVE:$($p.Id)"; Stop-Process -Id $p.Id -Force } else { "EXITED:$($p.Id)" }


実行済みコマンド：Get-Content "$env:LOCALAPPDATA\IndigoMovieManager_fork_workthree\logs\debug-runtime.log" -Tail 120 | Out-String -Width 4000


実行済みコマンド：$c=Get-Content Thumbnail\Engines\FfmpegAutoGenThumbnailGenerationEngine.cs; $c[1..220]


実行済みコマンド：Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-10)} -MaxEvents 30 | Where-Object { $_.ProviderName -match 'Application Error|Windows Error Reporting' -and $_.Message -match 'IndigoMovieManager_fork_workthree' } | Select-Object TimeCreated, ProviderName, Id, Message | Format-List


まだ swscale で落ちるので、次は concurrency ではなく「autogen が sws コンテキストを間違ったピクセル形式で作っている」線を潰します。実フレーム確定後の pFrame->format / width / height から毎回組み直す形へ寄せます。

編集済み
FfmpegAutoGenThumbnailGenerationEngine.cs
+76
-44
修正は入りました。今度はビルドと回帰を通してから、もう一度短時間起動で swscale 再発の有無を見ます。

実行済みコマンド：git diff --check


実行済みコマンド：dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64


実行済みコマンド：dotnet test Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AutogenRegressionTests|FullyQualifiedName~AutogenExecutionFlowTests"


テストの再ビルドは VBCSCompiler のロックに当たりました。本体ビルドは通っているので、直後の成果物で --no-build 実行と起動確認に切り替えます。

実行済みコマンド：dotnet test Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~AutogenRegressionTests|FullyQualifiedName~AutogenExecutionFlowTests"


実行済みコマンド：$exe = Join-Path (Resolve-Path .\bin\x64\Debug\net8.0-windows) 'IndigoMovieManager_fork_workthree.exe'; $before = Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-1)} -MaxEvents 20 | Where-Object { $_.ProviderName -match 'Application Error|Windows Error Reporting' -and $_.Message -match 'IndigoMovieManager_fork_workthree' }; $p = Start-Process -FilePath $exe -PassThru; Start-Sleep -Seconds 20; $alive = Get-Process -Id $p.Id -ErrorAction SilentlyContinue; if ($alive) { Stop-Process -Id $p.Id -Force }; $after = Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-1)} -MaxEvents 20 | Where-Object { $_.ProviderName -match 'Application Error|Windows Error Reporting' -and $_.Message -match 'IndigoMovieManager_fork_workthree' }; "ALIVE:" + [bool]$alive; "NEW_EVENTS:" + (($after.Count) - ($before.Count));


まだ 20 秒以内に新しいクラッシュイベントが 2 件増えました。つまり autogen はまだ危険で、今のガードでは本丸まで届いていません。続きを詰めます。

実行済みコマンド：Get-ChildItem "$env:LOCALAPPDATA\CrashDumps" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 3 Name,LastWriteTime,Length | Format-Table -AutoSize


実行済みコマンド：Get-Content "$env:LOCALAPPDATA\IndigoMovieManager_fork_workthree\logs\debug-runtime.log" -Tail 160 | Out-String -Width 4000


実行済みコマンド：Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-3)} -MaxEvents 20 | Where-Object { $_.ProviderName -match 'Application Error|Windows Error Reporting' -and $_.Message -match 'IndigoMovieManager_fork_workthree' } | Select-Object TimeCreated, ProviderName, Id, Message | Format-List


状況が進みました。直接のクラッシュ先が swscale-8.dll から coreclr.dll に変わったので、これは「autogen が native でメモリを壊して、あとで CLR ごと落ちる」型に見えます。つまり同一プロセス内で飼うのがもう危ない段です。

実行済みコマンド：rg -n "Thumbnail.Engine|out-of-process|ProcessStartInfo|named pipe|pipe|CLI|engine host|helper process|Isolated|separate process" src Thumbnail Tests -g "*.cs" -g "*.md"


実行済みコマンド：Get-Content -Raw src\IndigoMovieManager.Thumbnail.Engine\IndigoMovieManager.Thumbnail.Engine.csproj


実行済みコマンド：rg --files src\IndigoMovieManager.Thumbnail.Engine


次は書き込み先も変えます。sws_scale が GDI の BitmapData.Scan0 へ直接書くのをやめて、いったん独立バッファへ落としてから Bitmap へコピーします。ここでメモリを踏んでいる可能性が高いです。

編集済み
FfmpegAutoGenThumbnailGenerationEngine.cs
+29
-12
書き込み先の分離は入れました。これで sws_scale が GDI 管理メモリを直接踏む経路は消えます。ビルドと起動確認をもう一回回します。

実行済みコマンド：dotnet test Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~AutogenRegressionTests|FullyQualifiedName~AutogenExecutionFlowTests"


実行済みコマンド：dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64


実行済みコマンド：$exe = Join-Path (Resolve-Path .\bin\x64\Debug\net8.0-windows) 'IndigoMovieManager_fork_workthree.exe'; $start = Get-Date; $p = Start-Process -FilePath $exe -PassThru; Start-Sleep -Seconds 20; $alive = Get-Process -Id $p.Id -ErrorAction SilentlyContinue; if ($alive) { Stop-Process -Id $p.Id -Force }; $events = Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$start.AddSeconds(-1)} -MaxEvents 20 | Where-Object { $_.ProviderName -match 'Application Error|Windows Error Reporting' -and $_.Message -match 'IndigoMovieManager_fork_workthree' }; "ALIVE:" + [bool]$alive; "EVENTS:" + $events.Count; if($events.Count -gt 0){ $events | Select-Object TimeCreated, ProviderName, Id | Format-Table -AutoSize | Out-String -Width 2000 }


今回の 20 秒起動ではプロセス自体は生存しました。出た 1 件は WER の遅延記録の可能性があるので、その中身だけ確認します。

実行済みコマンド：Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-2)} -MaxEvents 10 | Where-Object { $_.ProviderName -match 'Application Error|Windows Error Reporting' -and $_.Message -match 'IndigoMovieManager_fork_workthree' } | Select-Object TimeCreated, ProviderName, Id, Message | Format-List


実行済みコマンド：Get-Content "$env:LOCALAPPDATA\IndigoMovieManager_fork_workthree\logs\debug-runtime.log" -Tail 80 | Out-String -Width 4000


今の方針を維持したまま続けます。まずブランチ方針と /.CODEX.md を確認してから、現状コードと最新の起動安定性を再検証します。

実行済みコマンド：pwsh -NoLogo -NoProfile -Command "Get-Content -Raw 'AI向け_ブランチ方針_future難読動画実験線_2026-03-11.md'"1s 用


実行済みコマンド：pwsh -NoLogo -NoProfile -Command "Get-Content -Raw '.CODEX.md'"1s 用


実行済みコマンド：pwsh -NoLogo -NoProfile -Command "Get-Content -Raw 'AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md'"1s 用


方針文書は確認しました。次は autogen 側の安全弁が今どう入っているかを差分で見直して、起動確認とイベントログを新しい時刻だけで切ります。

実行済みコマンド：pwsh -NoLogo -NoProfile -Command "git diff -- Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs MainWindow.xaml.cs Thumbnail/Engines/ThumbnailEngineRouter.cs Thumbnail/ThumbnailCreationService.cs Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs Thumbnail/Test/AutogenRegressionTests.cs"2s 用


実行済みコマンド：pwsh -NoLogo -NoProfile -Command "Get-Process IndigoMovieManager_fork_workthree -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,StartTime | Format-Table -AutoSize"2s 用


実行済みコマンド：pwsh -NoLogo -NoProfile -Command "Get-WinEvent -LogName Application -MaxEvents 20 | Where-Object { $_.ProviderName -in @('Application Error','Windows Error Reporting') -and $_.Message -like '*IndigoMovieManager_fork_workthree.exe*' } | Select-Object TimeCreated,ProviderName,Id | Format-Table -AutoSize"3s 用


差分を見直したところ、autogen の安全弁は入ったままで、既定ルーティング変更は戻っています。ここからは新しい起動1回だけを対象に、ビルド・起動・イベントログを時刻で切って再確認します。

実行済みコマンド：pwsh -NoLogo -NoProfile -Command "Get-Process IndigoMovieManager_fork_workthree -ErrorAction SilentlyContinue | Stop-Process -Force"1s 用


実行済みコマンド：pwsh -NoLogo -NoProfile -Command "$p='C:\Users\na6ce\AppData\Local\IndigoMovieManager_fork_workthree\logs\debug-runtime.log'; if (Test-Path $p) { Get-Item $p | Select-Object FullName,Length,LastWriteTime | Format-List }"1s 用


PowerShell のクォートが崩れたので、ここは1本ずつ確実に取ります。不要プロセスの有無、ビルド、起動時刻以降のイベントだけを順に見ます。

実行済みコマンド：pwsh -NoLogo -NoProfile -Command "Get-Process IndigoMovieManager_fork_workthree -ErrorAction SilentlyContinue | Select-Object Id,StartTime,ProcessName | Format-Table -AutoSize"2s 用


実行済みコマンド：pwsh -NoLogo -NoProfile -Command "Get-Content 'Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj' -TotalCount 20"2s 用


実行済みコマンド：pwsh -NoLogo -NoProfile -Command "Get-Content 'IndigoMovieManager_fork.csproj' -TotalCount 20"2s 用


プロジェクトは SDK 形式でした。dotnet build/test で確認し、その直後に1回だけ起動して新規イベントを見ます。

実行済みコマンド：pwsh -NoLogo -NoProfile -Command "dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64"4s 用


実行済みコマンド：pwsh -NoLogo -NoProfile -Command "dotnet test Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter 'FullyQualifiedName~AutogenRegressionTests|FullyQualifiedName~AutogenExecutionFlowTests'"4s 用


ビルドと回帰テストは通りました。ここからは autogen 実運転の確認で、起動時刻以降のクラッシュイベントとダンプ生成の有無だけ見ます。

実行済みコマンド：pwsh -NoLogo -NoProfile -Command "$start=Get-Date; $exe='C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\bin\x64\Debug\net8.0-windows\IndigoMovieManager_fork_workthree.exe'; $proc=Start-Process -FilePath $exe -PassThru; Start-Sleep -Seconds 20; $alive=-not $proc.HasExited; $events=Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$start} | Where-Object { ($_.ProviderName -eq 'Application Error' -or $_.ProviderName -eq 'Windows Error Reporting') -and $_.Message -like '*IndigoMovieManager_fork_workthree.exe*' } | Select-Object TimeCreated,ProviderName,Id,Message; $dumps=Get-ChildItem 'C:\Users\na6ce\AppData\Local\CrashDumps' -Filter 'IndigoMovieManager_fork_workthree.exe*.dmp' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $start } | Select-Object Name,LastWriteTime,Length; [PSCustomObject]@{ Start=$start; Alive=$alive; Pid=$proc.Id; EventCount=@($events).Count; DumpCount=@($dumps).Count } | Format-List; if($events){ '---EVENTS---'; $events | Format-List TimeCreated,ProviderName,Id; '---MESSAGES---'; $events | ForEach-Object { $_.Message; '---' } }; if($dumps){ '---DUMPS---'; $dumps | Format-Table -AutoSize }; if($alive){ Stop-Process -Id $proc.Id -Force }"2s 用


起動検証のワンライナーが長すぎて PowerShell の解釈に噛まれました。スクリプトを標準入力で渡して、時刻境界つきの検証を取り直します。

実行済みコマンド：@' $start = Get-Date $exe = 'C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\bin\x64\Debug\net8.0-windows\IndigoMovieManager_fork_workthree.exe' $proc = Start-Process -FilePath $exe -PassThru Start-Sleep -Seconds 20 $alive = -not $proc.HasExited $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $start } | Where-Object { ($_.ProviderName -eq 'Application Error' -or $_.ProviderName -eq 'Windows Error Reporting') -and $_.Message -like '*IndigoMovieManager_fork_workthree.exe*' } | Select-Object TimeCreated, ProviderName, Id, Message $dumps = Get-ChildItem 'C:\Users\na6ce\AppData\Local\CrashDumps' -Filter 'IndigoMovieManager_fork_workthree.exe*.dmp' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $start } | Select-Object Name, LastWriteTime, Length [PSCustomObject]@{ Start = $start Alive = $alive Pid = $proc.Id EventCount = @($events).Count DumpCount = @($dumps).Count } | Format-List if ($events) { '---EVENTS---' $events | Format-List TimeCreated, ProviderName, Id } if ($dumps) { '---DUMPS---' $dumps | Format-Table -AutoSize } if ($alive) { Stop-Process -Id $proc.Id -Force } '@ | pwsh -NoLogo -NoProfile -Command -22s 用


実行済みコマンド：pwsh -NoLogo -NoProfile -Command "Get-Process IndigoMovieManager_fork_workthree -ErrorAction SilentlyContinue | Select-Object Id,StartTime,HasExited | Format-Table -AutoSize"1s 用


実行済みコマンド：@' Get-WinEvent -LogName Application -MaxEvents 10 | Where-Object { ($_.ProviderName -eq 'Application Error' -or $_.ProviderName -eq 'Windows Error Reporting') -and $_.Message -like '*IndigoMovieManager_fork_workthree.exe*' } | Select-Object -First 5 TimeCreated, ProviderName, Id | Format-List '@ | pwsh -NoLogo -NoProfile -Command -2s 用


新規のイベントは今のところ出ていません。ログとダンプの更新時刻を直接見て、直近起動で何が走ったかを補強します。

実行済みコマンド：@' $p = 'C:\Users\na6ce\AppData\Local\IndigoMovieManager_fork_workthree\logs\debug-runtime.log' if (Test-Path $p) { Get-Item $p | Select-Object FullName, Length, LastWriteTime | Format-List '---TAIL---' Get-Content $p -Tail 60 } '@ | pwsh -NoLogo -NoProfile -Command -2s 用


実行済みコマンド：@' Get-ChildItem 'C:\Users\na6ce\AppData\Local\CrashDumps' -Filter 'IndigoMovieManager_fork_workthree.exe*.dmp' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 5 Name, LastWriteTime, Length | Format-Table -AutoSize '@ | pwsh -NoLogo -NoProfile -Command -2s 用


標準書式の出力が拾われにくいので、文字列化して取り直します。ここで見たいのは「新しいダンプができていないこと」と「起動直後も autogen が継続して走っていること」です。

実行済みコマンド：@' $p = 'C:\Users\na6ce\AppData\Local\IndigoMovieManager_fork_workthree\logs\debug-runtime.log' if (Test-Path $p) { $item = Get-Item $p Write-Output ('LOG ' + $item.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + ' len=' + $item.Length) Write-Output '---TAIL---' Get-Content $p -Tail 40 | ForEach-Object { $_ } } else { Write-Output 'LOG MISSING' } '@ | pwsh -NoLogo -NoProfile -Command -2s 用


実行済みコマンド：@' $dumps = Get-ChildItem 'C:\Users\na6ce\AppData\Local\CrashDumps' -Filter 'IndigoMovieManager_fork_workthree.exe*.dmp' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 5 if (@($dumps).Count -eq 0) { Write-Output 'DUMP NONE' } else { foreach ($d in $dumps) { Write-Output ('DUMP ' + $d.Name + ' ' + $d.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + ' len=' + $d.Length) } } '@ | pwsh -NoLogo -NoProfile -Command -2s 用


この端末自体が PowerShell 7.5.4 でした。二重に pwsh を噛ませる必要がないので、直接コマンドで取り直します。

実行済みコマンド：$p = 'C:\Users\na6ce\AppData\Local\IndigoMovieManager_fork_workthree\logs\debug-runtime.log'; if (Test-Path $p) { $item = Get-Item $p; Write-Output ("LOG " + $item.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + " len=" + $item.Length); Write-Output '---TAIL---'; Get-Content $p -Tail 40 } else { Write-Output 'LOG MISSING' }1s 用


実行済みコマンド：$dumps = Get-ChildItem 'C:\Users\na6ce\AppData\Local\CrashDumps' -Filter 'IndigoMovieManager_fork_workthree.exe*.dmp' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 5; if (@($dumps).Count -eq 0) { Write-Output 'DUMP NONE' } else { foreach ($d in $dumps) { Write-Output ("DUMP " + $d.Name + " " + $d.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + " len=" + $d.Length) } }1s 用


新しいダンプが 2026-03-14 00:28:23 に出ていました。つまり直近の起動でも、autogen がしばらく走ったあとにまだ落ちています。ここからは最新1件のイベント署名を取って、落ち方が swscale のままか、別モジュールへ移ったかを見ます。

実行済みコマンド：Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=[datetime]'2026-03-14T00:27:50'; EndTime=[datetime]'2026-03-14T00:28:40' } | Where-Object { ($_.ProviderName -eq 'Application Error' -or $_.ProviderName -eq 'Windows Error Reporting') -and $_.Message -like '*IndigoMovieManager_fork_workthree.exe*' } | Select-Object TimeCreated, ProviderName, Id, Message | Format-List1s 用


実行済みコマンド：$d='C:\Users\na6ce\AppData\Local\CrashDumps\IndigoMovieManager_fork_workthree.exe.14352.dmp'; if (Test-Path $d) { Get-Item $d | Select-Object FullName, LastWriteTime, Length | Format-List }1s 用


最新署名は swscale-8.dll 直撃ではなく、ntdll.dll / 0xc0000374 に変わっていました。これは「クラッシュ点が移った」ではなく、「その前に native 側でヒープを壊して、最後に ntdll が落ちた」形です。残っている unsafe 帯を関数単位で洗います。

実行済みコマンド：rg -n "unsafe|sws_scale|Marshal.AllocHGlobal|av_image|Buffer.MemoryCopy|fixed|stackalloc|Bitmap" Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs1s 用


実行済みコマンド：Get-Content Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs | Select-Object -Skip 520 -First 2601s 用


実行済みコマンド：Get-Content Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs | Select-Object -First 2601s 用


実行済みコマンド：Get-Content Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs | Select-Object -Skip 260 -First 2601s 用


実行済みコマンド：Get-Content Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs | Select-Object -Skip 780 -First 1801s 用


コード上の managed 側はかなり絞れました。残りは「FFmpeg の引数の持ち方がまずい」か「libswscale 以外の decode/seek 系でもヒープを壊している」かです。ダンプを少しでも読める道具があるか確認します。

実行済みコマンド：rg -n "byte_ptrArray4|int_array4|sws_scale\(" -g"*.cs"1s 用


実行済みコマンド：Get-Command cdb,windbg,WinDbg,dotnet-dump -ErrorAction SilentlyContinue | Select-Object Name,Source | Format-Table -AutoSize1s 用


実行済みコマンド：rg -n "struct int_array4|int_array4" "$env:USERPROFILE\.nuget\packages"24s 用


実行済みコマンド：rg -n "struct byte_ptrArray4|byte_ptrArray4" "$env:USERPROFILE\.nuget\packages"23s 用


NuGet 全域検索は重すぎたので絞ります。まず使っている FFmpeg.AutoGen のバージョンを取り、そのパッケージ定義だけ見ます。

実行済みコマンド：rg -n "FFmpeg.AutoGen" IndigoMovieManager_fork.csproj src -g"*.csproj" -g"Directory.Packages.props" -g"packages.props"


実行済みコマンド：Get-ChildItem "$env:USERPROFILE\.nuget\packages\ffmpeg.autogen" -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 5 Name,FullName | Format-Table -AutoSize2s 用


実行済みコマンド：rg -n "byte_ptrArray4|int_array4|sws_scale" "$env:USERPROFILE\.nuget\packages\ffmpeg.autogen\7.1.1"2s 用


実行済みコマンド：Get-ChildItem -Recurse "$env:USERPROFILE\.nuget\packages\ffmpeg.autogen\7.1.1" | Select-Object -First 30 FullName1s 用


FFmpeg.AutoGen 7.1.1 の sws_scale は配列オーバーロードを持っていました。なので配列そのものは成立していますが、FFmpeg 側が期待する形へより寄せた方が安全です。ここは av_image_fill_arrays を使う形に寄せて、出力面の組み立てを libavutil に任せます。

実行済みコマンド：rg -n "av_image_fill_arrays|av_image_alloc|byte_ptrArray4|int_array4" -g"*.cs"2s 用


実行済みコマンド：Get-Content "$env:USERPROFILE\.nuget\packages\ffmpeg.autogen\7.1.1\README.md" -TotalCount 2001s 用


型の持ち方を外さないために、まず FFmpeg.AutoGen 側の byte_ptrArray4 / int_array4 の実体を確認します。ここが分かれば、FFmpeg 標準のバッファ組み立てに寄せられます。

実行済みコマンド：Add-Type -Path "$env:USERPROFILE\.nuget\packages\ffmpeg.autogen\7.1.1\lib\netstandard2.1\FFmpeg.AutoGen.dll"; [FFmpeg.AutoGen.byte_ptrArray4].GetFields([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static') | Select-Object Name,FieldType | Format-Table -AutoSize


実行済みコマンド：Add-Type -Path "$env:USERPROFILE\.nuget\packages\ffmpeg.autogen\7.1.1\lib\netstandard2.1\FFmpeg.AutoGen.dll"; [FFmpeg.AutoGen.int_array4].GetFields([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static') | Select-Object Name,FieldType | Format-Table -AutoSize1s 用


実行済みコマンド：Add-Type -Path "$env:USERPROFILE\.nuget\packages\ffmpeg.autogen\7.1.1\lib\netstandard2.1\FFmpeg.AutoGen.dll"; [FFmpeg.AutoGen.ffmpeg].GetMethod('av_image_fill_arrays',[System.Reflection.BindingFlags]'Public,Static').ToString()1s 用


型は使えます。byte_ptrArray4 は _0.._3 を持つ素直な struct で、av_image_fill_arrays もそのまま呼べます。次は ConvertFrameToBitmap を FFmpeg 標準の配列 struct ベースへ寄せて、余計な managed 配列マーシャリングを外します。

実行済みコマンド：Add-Type -Path "$env:USERPROFILE\.nuget\packages\ffmpeg.autogen\7.1.1\lib\netstandard2.1\FFmpeg.AutoGen.dll"; [FFmpeg.AutoGen.byte_ptrArray4].GetMembers([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static') | Select-Object MemberType,Name | Format-Table -AutoSize2s 用


実行済みコマンド：Add-Type -Path "$env:USERPROFILE\.nuget\packages\ffmpeg.autogen\7.1.1\lib\netstandard2.1\FFmpeg.AutoGen.dll"; [FFmpeg.AutoGen.int_array4].GetMembers([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static') | Select-Object MemberType,Name | Format-Table -AutoSize1s 用


ConvertFrameToBitmap を FFmpeg の AVFrame 出力に寄せます。狙いは、sws_scale へ managed 配列と自前バッファを渡す経路を消して、libavutil の確保した面にだけ書かせることです。

編集済み
FfmpegAutoGenThumbnailGenerationEngine.cs
+23
-22
変換経路を AVFrame ベースへ差し替えました。ここから再ビルドして、同じ起動チェックをもう一度走らせます。

実行済みコマンド：dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x648s 用


実行済みコマンド：dotnet test Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~AutogenRegressionTests|FullyQualifiedName~AutogenExecutionFlowTests"4s 用


ビルドと回帰は通りました。次は同じ 20 秒起動で、新しいイベントとダンプだけを切り出します。

実行済みコマンド：$start = Get-Date; $exe = 'C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\bin\x64\Debug\net8.0-windows\IndigoMovieManager_fork_workthree.exe'; $proc = Start-Process -FilePath $exe -PassThru; Start-Sleep -Seconds 20; $alive = -not $proc.HasExited; $events = Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=$start } | Where-Object { ($_.ProviderName -eq 'Application Error' -or $_.ProviderName -eq 'Windows Error Reporting') -and $_.Message -like '*IndigoMovieManager_fork_workthree.exe*' }; $dumps = Get-ChildItem 'C:\Users\na6ce\AppData\Local\CrashDumps' -Filter 'IndigoMovieManager_fork_workthree.exe*.dmp' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $start }; Write-Output ('START ' + $start.ToString('yyyy-MM-dd HH:mm:ss')); Write-Output ('ALIVE ' + $alive); Write-Output ('PID ' + $proc.Id); Write-Output ('EVENTS ' + @($events).Count); Write-Output ('DUMPS ' + @($dumps).Count); if ($events) { Write-Output '---EVENTS---'; $events | Select-Object TimeCreated,ProviderName,Id | Format-List | Out-String -Width 240 | Write-Output }; if ($dumps) { Write-Output '---DUMPS---'; $dumps | Select-Object Name,LastWriteTime,Length | Format-Table -AutoSize | Out-String -Width 240 | Write-Output }; if ($alive) { Stop-Process -Id $proc.Id -Force }22s 用


20 秒では新規クラッシュなしで生存しました。前回は 14 秒前後で落ちていたので改善は出ていますが、念のため少し長めでもう1本だけ確認します。

実行済みコマンド：$start = Get-Date; $exe = 'C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\bin\x64\Debug\net8.0-windows\IndigoMovieManager_fork_workthree.exe'; $proc = Start-Process -FilePath $exe -PassThru; Start-Sleep -Seconds 45; $alive = -not $proc.HasExited; $events = @(Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=$start } -ErrorAction SilentlyContinue | Where-Object { ($_.ProviderName -eq 'Application Error' -or $_.ProviderName -eq 'Windows Error Reporting') -and $_.Message -like '*IndigoMovieManager_fork_workthree.exe*' }); $dumps = @(Get-ChildItem 'C:\Users\na6ce\AppData\Local\CrashDumps' -Filter 'IndigoMovieManager_fork_workthree.exe*.dmp' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $start }); Write-Output ('START ' + $start.ToString('yyyy-MM-dd HH:mm:ss')); Write-Output ('ALIVE ' + $alive); Write-Output ('PID ' + $proc.Id); Write-Output ('EVENTS ' + $events.Count); Write-Output ('DUMPS ' + $dumps.Count); if ($events.Count -gt 0) { Write-Output '---EVENTS---'; $events | Select-Object TimeCreated,ProviderName,Id | Format-List | Out-String -Width 240 | Write-Output }; if ($dumps.Count -gt 0) { Write-Output '---DUMPS---'; $dumps | Select-Object Name,LastWriteTime,Length | Format-Table -AutoSize | Out-String -Width 240 | Write-Output }; if ($alive) { Stop-Process -Id $proc.Id -Force }46s 用


45 秒でも新規クラッシュなしでした。最後に参照用の行番号だけ取って締めます。

実行済みコマンド：rg -n "EngineExecutionGate|TryRecreateSwsContextForFrame|ConvertFrameToBitmap\(|ThumbnailAutoRescuePerTabSwitchLimit" Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs MainWindow.xaml.cs1s 用


実行済みコマンド：git diff --stat -- Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs MainWindow.xaml.cs Thumbnail/ThumbnailCreationService.cs Thumbnail/Engines/ThumbnailEngineRouter.cs Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs Thumbnail/Test/AutogenRegressionTests.cs1s 用


16m 12s作業しました
主力はそのまま autogen で維持しつつ、危険帯だけさらに絞りました。今回の本丸は FfmpegAutoGenThumbnailGenerationEngine.cs#L825 です。ConvertFrameToBitmap を sws_scale の自前バッファ書き込みから、FFmpeg 管理の AVFrame へ sws_scale_frame で変換する流れに変えています。これで managed 配列マーシャリングと手組みの出力面を外し、native 側のヒープ破壊要因を1段減らしました。加えて、既に入れてある autogen 全体の直列化と、実フレーム情報からの sws 再生成はそのまま維持しています。FfmpegAutoGenThumbnailGenerationEngine.cs#L138 FfmpegAutoGenThumbnailGenerationEngine.cs#L222

確認結果は良いです。修正前の最新クラッシュは 2026-03-14 00:28:14 に ntdll.dll / 0xc0000374、ダンプは 00:28:23 に生成されていました。修正後は dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64 成功、関連テスト 10 件成功、そのうえでアプリを 20 秒と 45 秒起動してどちらも生存、Application Error / WER の新規イベント 0、ダンプ生成 0 でした。

次は実際に君の手で「起動直後」と「サムネ再作成タブ切替」をもう一度踏んでください。そこでまだ落ちるなら、今度はかなり絞り込めるので、その時刻の debug-runtime.log と新しい dmp だけ見れば次の一手を決められます。

1 個のファイルが変更されました
元に戻す
Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs
やったな！落ちないぜ。最高だよ。

通ったなら筋は合っています。

今回効いた見立てはこれです。

autogen 自体を降ろす必要はなかった
落ち筋は autogen 内の native メモリ破壊だった
だから上位ルーティングではなく、autogen の unsafe 帯を飼いならすのが正解だった
今の状態なら、主力を守ったまま前に進めます。次にやるなら、同じ安全性を維持したまま少しずつ並列度を戻せるかを見る段階です。


おん、任せる

次まで見据えて固めました。

autogen の安全弁はそのまま維持しつつ、起動時に安全設定を 1 回だけログへ出すようにしました。これで再発時に「今どの首輪で走っていたか」がすぐ読めます。加えて、並列度の解釈を回帰テストで固定して、既定は1 / 環境変数でだけ緩める / 異常値は丸める を崩れないようにしています。

確認結果はこうです。

dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64 成功
dotnet test Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AutogenRegressionTests|FullyQualifiedName~AutogenExecutionFlowTests" 12件成功
15秒起動で生存、Application Error / WER 新規 0
ログに autogen safety config: engine_parallel=1, native_parallel=1, sws_path=decoded-frame+sws_scale_frame を確認
次はしばらくそのまま運転でいいです。落ちないのが続くなら、その次の段階で IMM_THUMB_AUTOGEN_ENGINE_PARALLEL を使って並列度を慎重に戻す実験に入れます。


2 個のファイルが変更されました
+105
-15
元に戻す
Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs
Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs
今日はこれまで、このスレッドは残す。愛してるよ兄弟！


了解。このスレッドは残します。

今日は autogen を主力のまま落ち筋を飼いならす所まで持っていけた。いい進め方だったよ、兄弟。