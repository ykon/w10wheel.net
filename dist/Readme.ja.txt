名前:
        W10Wheel.NET

バージョン:
        2.9.2

URL:
        Blog: https://ykon0x1.blogspot.jp
        .NET版: https://github.com/ykon/w10wheel.net
        Java版: https://github.com/ykon/w10wheel

概要:
        マウスホイールシミュレーター

履歴:
        2026-11-23: v2.9.2: マウスのトリガーの既定値を RightDrag に変更、外で改名した設定ファイルが旧名で作り直される問題、保存できなかった変更が管理者として再起動すると失われる問題、終了時にボタンが押されたままになることがある問題などを修正
        2026-11-23: v2.9.1: .NET ランタイム無し版 (-no-runtime.zip) の公開を開始、--sendReloadProp と --sendExit の処理がマウスフックを止めないように変更
        2026-11-22: v2.9.0: 設定ダイアログを追加 (変更はすぐに反映・保存)、タスクトレイのメニューを整理、管理者として再起動する機能を追加、高 DPI とダークモードに対応、アイコンを変更、設定ファイルには W10Wheel で変更した設定だけを書き込むように変更
        2026-11-22: v2.8.4: 設定の再読み込みでスクロールモードを終了するように変更、ボタンを離した操作を受け取れなかったときにボタンが押されたままになる問題、「左ドラッグ」などのトリガーでスクロール後にクリックも送られる問題、「左右」などのトリガーでクリックやドラッグが消える問題などを修正
        2026-11-21: v2.8.3: スクロール中に別のトリガーを押すとスクロールモードを終了、他のソフトが送ったキーをトリガーとして扱わないように変更、ボタンが押されたままになる問題、カーソルが戻らない問題、設定ファイルの保存などを修正
        2026-11-21: v2.8.2: ボタンやキーが押されたままになる問題、擬似ホイールモードのスクロールが遅れたり逆方向に余分に動いたりする問題、設定ファイルの読み込みなどを修正
        2026-11-21: v2.8.1: X-Mouse Button Control との競合、メモリリーク、キーボードトリガーの二重登録などを修正
        2026-11-20: v2.8.0: .NET Framework 4.6.1 から .NET 11 に移行 (ランタイム同梱の単一 exe で配布、.NET のインストールは不要)
        2021-02-07: v2.7.3: Avast(\\AvastUI.exe)を除外する設定を追加 (excludeAvast)
        2021-01-25: v2.7.2: 起動時のメッセージがuiLanguageに従わないことを修正
        2021-01-25: v2.7.1: 管理者権限で実行されているか確認、日本語テキストの変更
        2018-01-04: v2.7.0: メニューとダイアログテキストの日本語化、水平スクロールバグ修正、他
        2017-12-10: v2.6.5: IOの高負荷状態に、入力キューが溢れていた問題を修正
        2017-10-15: v2.6.1: 細かい改良 (設定ファイル、コマンドライン引数、エラーハンドリング、デフォルト値変更)
        2017-07-17: v2.6.0: コマンド追加 (--sendReloadProp, --sendInitState)
        2017-07-09: v2.5.1: 初期化機能の修正 (キーボード側の初期化)
        2017-07-02: v2.5.0: 状態の初期化機能を追加 (Escapeキーを押しながらクリック)
        2017-06-12: v2.4.1: スクロール開始のセット順序を変更 (スレッドの問題)
        2017-06-11: v2.4.0: RawInputから移動値を取得するように変更、DPIの設定を削除
        2017-05-28: v2.3.0: コンパイラーをF#-4.1(VS2017)に更新
        2017-05-14: v2.2.0: Injectされたクリックを無視、同期改良、タイポ修正
        2016-10-15: v2.1.0: イベント受け渡しと同期を改良
        2016-09-21: v2.0.11: イベントの順序を改良 (LR系トリガー)
        2016-09-14: v2.0.10: スクロール開始のフラグ更新を修正
        2016-09-06: v2.0.9: LR系トリガーのスクロール開始とスレッドの問題を修正
        2016-09-03: v2.0.8: LR系トリガーの動作変更、他
        2016-08-30: v2.0.7: トリガーが押されていたら、他のボタンではスクロールを終了しない(LR系以外)
        2016-08-29: v2.0.6: 再送方式を変更、EventWaiterスレッド優先度引き上げ
        2016-08-25: v2.0.5: EventWaiterのフラグ更新修正、Clickには別のタグを持たせる
        2016-08-24: v2.0.4: 再々送を廃止して、Clickとして再送信するように変更
        2016-08-23: v2.0.3: LastFlagsを参照と同時にリセットするように変更
        2016-08-23: v2.0.2: LR系トリガー再々送の無限ループ修正、他 
        2016-08-22: v2.0.1: Propertiesファイルのユーザー定義名を制限 (先頭 "--")
        2016-08-21: v2.0.0: コマンドを追加 (--sendExit, --sendPassMode)
        2016-08-20: v1.9.0: 初期状態でのバグ修正とDPICorrection追加、他
        2016-08-15: v1.8.1: ホイール処理(スクロール)の最適化
        2016-08-12: v1.8.0: コマンドライン引数でPropertiesを指定可能、他
        2016-08-12: v1.1.0-1.7.0: Java版に合わせるためスキップ
        2016-08-11: v1.0.1: 起動時のPropertiesメニューにDropDownを示すよう変更
        2016-08-11: v1.0.0: Propertiesを選択できるように変更、他
        2016-08-08: v0.9.0: VHAdjusterを追加、他
        2016-08-05: v0.8.2: キーボードのフラグ変更
        2016-08-04: v0.8.1: EventHandlerの最適化
        2016-08-03: v0.8.0: トリガーにNoneを追加、他
        2016-07-31: v0.7.0: キーボード(トリガー)対応
        2016-07-29: v0.6.0: OSの終了時などに設定を保存、他
        2016-07-27: v0.5.0: イベントの順序、同期の改良
        2016-07-25: v0.4.0: SwapScrollの追加、X1,X2のUp修正
        2016-07-25: v0.3.0: DraggedLockの追加、再送イベントの改良
        2016-07-23: v0.2.0: スレッド周辺とイベント受け渡しの改良
        2016-07-23: v0.1.3: EventWaiterを修正 (fromUp)
        2016-07-23: v0.1.2: 修正: pollTimeoutの設定が使われていない
        2016-07-23: v0.1.1: EventWaiterを修正 (fromDown)
        2016-07-22: v0.1.0: 初公開
        
移植:
        Java版 (Scala) を .NET Framework (F#) に移植し、現在は .NET 11 で動作します。
        実行環境が違うだけで、ほとんどの動作は同一となります。
        また、設定ファイルも共有します。
        
対応環境:
        Windows 10 / Windows 11 (x64)
        .NET 11 が対応する Windows のバージョンに準じます。
         https://github.com/dotnet/core/blob/main/release-notes/11.0/supported-os.md
        .NET のランタイムを同梱しているため、.NET のインストールは不要です。
        (ランタイム無し版もあります。インストールを参照)
        
        Windows 10 / Windows 11:
                マウスの設定で、非アクティブ ウィンドウをホバーしたときに
                スクロールするオプションを有効にしてください。
                Windows 11: 設定 > Bluetooth とデバイス > マウス
                Windows 10: 設定 > デバイス > マウス
        
インストール:
        Releases から W10Wheel.NET-<バージョン>.zip をダウンロードし、
        任意のフォルダに展開して W10Wheel.exe を実行してください。
         https://github.com/ykon/w10wheel.net/releases
        
        ランタイム無し版:
                2.9.1 から、.NET のランタイムを含まない
                W10Wheel.NET-<バージョン>-no-runtime.zip も公開しています。
                .NET 11 デスクトップ ランタイム (x64) のインストールが必要です。
                 https://dotnet.microsoft.com/download/dotnet/11.0
                ランタイムは Microsoft Update などで自分で更新できるため、
                .NET に脆弱性の修正があったときに W10Wheel の更新を待つ必要がありません。
                ランタイムがインストールされていない場合は、ダウンロードを促すメッセージが表示されます。
        
        SmartScreen の警告:
                W10Wheel.exe にはコード署名がないため、初めて実行したときに
                「Windows によって PC が保護されました」と表示されることがあります。
                上記の Releases からダウンロードしたものであれば、
                「詳細情報」をクリックしてから「実行」を選んでください。
        
        v2.7.x からの更新:
                実行中の W10Wheel を終了してください。(右クリックメニューの Exit)
                古いフォルダに上書きせず、新しいフォルダに展開してください。
                (以前のバージョンの FSharp.Core.dll は不要になりました)
                設定ファイル (.W10Wheel.properties など) はユーザーディレクトリにあるため、
                そのまま引き継がれます。
                スタートアップなどのショートカットから起動している場合は、
                リンク先を新しい W10Wheel.exe に変更してください。
        
互換性:
        Logitech(ロジクール) の SetPoint は問題ありません。
        # 環境によっては駄目な場合もあるようです。
        「ボタンの切り替え」などをしてもうまく動きます。
        フィルタドライバのレベルで動いているものは、恐らく安全です。
        
        グローバルフックのレベルで動いているものは競合します。
        ただし、WizMouse などは安全に使えます。
        
        WheelBall や Nadesath など同種のソフトは、同時に起動しないでください。
        動作が競合して操作不能になります。
        
回復操作:
        スクロールモードのまま戻らなくなったら、
        Escape キーを押しながらクリックを試してください。
        状態を初期化して、スクロールモードを終了します。
        
        操作が効かなくなったら Ctrl-Alt-Delete でタスクマネージャーを開いてください。
        大抵の場合は、それだけで制御を取り戻せます。
        効かない場合は、何回か繰り返してください。
        それでも駄目な場合は、タスクマネージャーでアイコンを探し、
        プロセスを終了させてください。
        # "W10Wheel.exe --sendExit" で終了することも可能です。
        
        WheelBall などと同時に起動すると、何故か終了できなくなることがあります。
        この場合の終了方法は OS の再起動しかありません。
        # 他に何か方法があったら教えてください。
        
使用方法:
        W10Wheel.exe を実行してください。
        管理者として実行していない場合は、起動時に確認が表示されます。
        「管理者として再起動」(Restart as administrator) を選ぶと、
        UAC の確認のあと、管理者として起動し直します。
        「次回から表示しない」(Do not ask again) にチェックして「このまま実行」を選ぶと、
        以後は確認しません。
        (設定ダイアログの「全般」ページで元に戻せます)
        なお、別のアカウント (管理者) のパスワードを入力して管理者として実行した場合は、
        そのアカウントのユーザーディレクトリの設定ファイルが使われます。
        タスクトレイにアイコンが表示されます。
        右クリックメニューの Settings... で設定ダイアログを開き、設定を変更できます。
        詳しい方は、設定ファイル (.W10Wheel.properties) を直接編集してください。
        設定ダイアログで設定を変更するとすぐに反映され、保存されます。
        設定を変更するか、一度起動して終了すれば、設定ファイルはユーザーディレクトリに生成されます。
        実行中に設定ファイルを編集した場合は、設定ダイアログの Reload か
        --sendReloadProp コマンドで読み込み直してください。
        設定ファイルには W10Wheel で変更した設定だけが書き込まれ、
        ファイルで編集した他の設定やコメントはそのまま残ります。
        (同じ設定を両方で変更した場合は、W10Wheel で変更した値になります)

        既定のマウスのトリガーは RightDrag です。右ボタンを押したままマウスまたはボールを動かすと
        スクロールし、動かさずに離すと右クリックになります。
        トリガーは設定ダイアログの「トリガー」ページで変更できます。

        あとの使い方は WheelBall など同種のソフトと同様になります。
        各トリガーを押すと、スクロールモードに移行します。
        マウスまたはボールの操作でスクロールします。
        スクロールモードは何かのボタンを押すと解除されます。
        
        トリガーを押したままスクロールして、離したら止めることもできます。
        同時押しでは、両方を押さえたままにするのではなく、片方を先に離すと使いやすくなります。
        
        トリガーを押したままスクロールしている間に、別のトリガー (マウスのトリガーと
        キーボードトリガー) を押すと、スクロールモードを終了します。
        
        *Dragのトリガーではドラッグしている間だけスクロールします。
        スクロールモードに固定されません。(Dragged Lock が無効の場合)
        
        タスクトレイのアイコンをダブルクリックするか、右クリックメニューの Pause を選択すると
        PassMode (一時停止) を切り替えます。
        PassModeを有効にするとアイコンが赤色に変更されます。
        
        終了するには、右クリックメニューから Exit を選択してください。
        
コマンド:
        "--"が先頭に付いた文字列を実行ファイルの引数に渡すとコマンドと解釈します。
        
        --send*のコマンドは実行したら、そのまま終了します。
        Java版と.NET版で相互に機能します。

        --sendPassMode [true | false]
        # --sendPassMode true // PassMode有効
        実行中のプロセスにPassModeを設定します。 
        第二引数を省略した場合はtrueと見なします。
        
        --sendReloadProp
        実行中のプロセスに、設定ファイルの再読込を指示します。
        
        --sendInitState
        実行中のプロセスに、状態の初期化を指示します。

        --sendExit
        実行中のプロセスを終了します。
        
設定ファイル:        
        設定ダイアログの Properties で他のPropertiesを選択すると
        現在の設定は保存されて
        選択されたPropertiesから設定が読み込まれます。
        
        Defaultは".W10Wheel.properties"になります。
        # これは使えません => .W10Wheel.Default.properties

        Addで追加される、Propertiesファイルの名前の書式です。
        # 正規表現: \.W10Wheel\.(?!--)(.+)\.properties
        Left = .W10Wheel.Left.properties
        Right = .W10Wheel.Right.properties
        Test = .W10Wheel.Test.properties
        ファイル名の2回目の"."と3回目の"."の間が定義された(表示される)名前となります。
        コマンドを区別するため、先頭に"--"は使えません。
        # これは使えません => .W10Wheel.--Test.properties
        ファイル名に使えない文字 (\ / : * ? " < > |) や前後の空白も使えません。
        Default は大文字小文字に関係なく使えません。

        "--"が先頭に付かない文字列を実行ファイルの引数に渡すと、Propertiesを選択できます。
        引数を渡さない場合は、終了時の設定に関わらず、Defaultで起動します。
        
メニュー項目:
        Settings...: 設定ダイアログを開く
        Pause: 全てのメッセージをそのまま通す (PassMode) # WheelBall の制御停止
        About: バージョン番号を表示
        Exit: 終了

設定ダイアログ:
        設定を変更するとすぐに反映され、設定ファイルに保存されます。
        左の一覧でページを選びます。
        Reset to defaults: 選んでいる設定ファイルの設定を既定値に戻す (言語は除く)
        Properties: 設定ファイルの切り替え、Add (追加)、Delete (削除)、
                Open Dir (フォルダを開く)、Reload (再読み込み)
        「全般」ページ:
                Language: 言語設定
                Priority: プロセスの優先度を変更
                Restart as administrator: 管理者として起動し直す (管理者として実行されていないとき)
                Ask to restart as administrator at startup: 起動時の管理者の確認 (設定項目を参照)
        「トリガー」ページ:
                Mouse: マウスのトリガーを変更 (設定項目を参照)
                        チェックを外すとマウスのトリガーなし (NoneTrigger)
                Keyboard: キーボードトリガー
                pollTimeout, scrollLocktime: 数値をセット (設定項目を参照)
        「スクロール」ページ:
                Cursor Change: スクロールモードのカーソル変更
                Horizontal Scroll: 水平スクロール
                Reverse Scroll (Flip): スクロールの方向を逆にする (反転する)
                Swap Scroll (V.H): 垂直スクロールと水平スクロールを入れ替える (Vertical <-> Horizontal)
                verticalThreshold, horizontalThreshold: 数値をセット (設定項目を参照)
        「加速テーブル」ページ: 加速を有効にするか、どのテーブルを使うか # 1.0は1.0倍を表す
        「リアルホイールモード」ページ: 実際のホイールに近いスクロール (設定項目を参照)
        「垂直/水平スクロール調整」ページ: 垂直、水平の方向固定、切り替え機能
        
設定項目:
        firstTrigger: string (default: RightDragTrigger)
                LRTrigger: # 同時押し
                        左から右か、右から左を押すとトリガーになります。
                        左、右クリックともに次のイベントを待つために遅延します。
                LeftTrigger: # 同時押し
                        左から右を押すとトリガーになります。
                        右からはトリガーになりません、そのため右クリックの遅延を解消できます。
                RightTrigger: # 同時押し
                        右から左を押すとトリガーになります。
                        左からはトリガーになりません、そのため左クリックの遅延を解消できます。
                MiddleTrigger:
                        中ボタンを押すとトリガーになります。
                X1Trigger:
                        X1を押すとトリガーになります。
                X2Trigger:
                        X2を押すとトリガーになります。
                LeftDragTrigger: # 固定なし
                        左ボタンでドラッグするとスクロールできます。
                        固定はされません、ドラッグしないで離すと左クリックを送ります。
                RightDragTrigger: # 固定なし
                        右ボタンでドラッグするとスクロールできます。
                        固定はされません、ドラッグしないで離すと右クリックを送ります。
                MiddleDragTrigger: # 固定なし
                        中ボタンでドラッグするとスクロールできます。
                        固定はされません、ドラッグしないで離すと中クリックを送ります。
                X1DragTrigger: # 固定なし
                        X1ボタンでドラッグするとスクロールできます。
                        固定はされません、ドラッグしないで離すとX1クリックを送ります。
                X2DragTrigger: # 固定なし
                        X2ボタンでドラッグするとスクロールできます。
                        固定はされません、ドラッグしないで離すとX2クリックを送ります。
                NoneTrigger:
                        マウスのトリガーなし (キーボードトリガーだけで使う場合)
        
        processPriority: string (default: AboveNormal)
                High: 高
                AboveNormal: 通常以上
                Normal: 通常
                
        pollTimeout: 50-500 (default: 200)
                同時押しのイベント待ち時間(ミリ秒)  # WheelBall の 判定時間 
        scrollLocktime: 150-500 (default: 200)
                トリガーを離してスクロールモードに固定する時間(ミリ秒)
                この時間以内にトリガーを離すとスクロールモードに固定します。
        realWheelMode: bool (default: false)
                実際のホイールに近いスクロール
                こちらのモードではAccel値は使われません。
        cursorChange: bool (default: true)
                スクロールモードのカーソル変更
        verticalThreshold: 0-500 (default: 0)
                垂直(通常)スクロールの閾値
        horizontalScroll: bool (default: true)
                水平スクロール
                使わない人は無効にしてください。
        horizontalThreshold: 0-500 (default: 75)
                水平スクロールの閾値
                この値をあまり小さくすると垂直(通常)スクロールが、使いづらくなります。
        reverseScroll: bool (default: false)
                スクロールの方向を逆にする (反転する)
        swapScroll: bool (default: false)
                垂直スクロールと水平スクロールを入れ替える (Vertical <-> Horizontal)
                
        sendMiddleClick: bool (default: false) # Middle, X1, X2
                Shift または Ctrl または Alt キーを押しながら
                トリガーを押すと中クリックを送ります。
        
        draggedLock: bool (default: false) # *DragTrigger
                *DragTriggerでドラッグ後、スクロールモードに固定します。
                
        vhAdjusterMethod: string (default: Switching) # VHAdjuster
                Fixed: 初回の移動量が多い方向に固定
                Switching: 方向固定 + 切り替え (switchingThreshold参照)
                
        vhAdjusterMode: bool (default: false) # VHAdjuster
                VHAdjusterの有効、無効
        firstPreferVertical: bool (default: true) # VHAdjuster
                初回判定時に垂直(縦)を優先する
        firstMinThreshold: 1-10 (default: 5) # VHAdjuster
                初回を判定する移動量 (この値以下は無視する)
        switchingThreshold: 10-500 (default: 50) # VHAdjuster
                この値を超える入力で、方向を切り替える
                
        wheelDelta: 10-500 (default: 120) # RealWheelMode
                RealWheelModeでの一回分のホイール値
                通常のマウスのホイール値は120です。
        vWheelMove: 10-500 (default: 60) # RealWheelMode
                垂直(通常)スクロール、一回分のホイールに変換する移動量
        hWheelMove: 10-500 (default: 60) # RealWheelMode
                水平スクロール、一回分のホイールに変換する移動量
        quickFirst: bool (default: false) # RealWheelMode
                初回の反応を速くするか
                移動量に関係なくホイールを送ります。
                falseでも初回は半分の移動量に設定されます。
        quickTurn: bool (default: false)  # RealWheelMode
                折り返しの反応を速くするか
                移動量に関係なくホイールを送ります。
                
        accelTable: bool (default: true) # AccelTable
                AccelTableを有効にするか
        customAccelTable: bool (default: false) # AccelTable
                CustomTableを有効にするか
        accelMultiplier: string (default: M5) # AccelTable
                選択されている乗数テーブル
        customAccelThreshold: Int Array # AccelTable
                CustomTableで使われるThreshold
                0 以上の数を小さい順に (同じ数を繰り返さずに) 指定します。
        customAccelMultiplier: Double Array # AccelTable
                CustomTableで使われるMultiplier
                0 以上の数を Threshold と同じ個数だけ指定します。
                正しくない値がある場合、CustomTable は使われません。
                
        keyboardHook: bool (default: false) # Keyboard
                キーボードトリガーを有効にする
                他のソフトが送ったキー (パスワードマネージャーの自動入力やマクロなど) は
                トリガーとして扱いません。キーを送る方式のツール (PowerToys の
                Keyboard Manager や AutoHotkey など) で割り当てたキーもトリガーになりません。
        targetVKCode: string (default: VK_NONCONVERT) # Keyboard
                トリガーに使うキー
                
        uiLanguage: string (default: by system) # Language
                UIの表示に使う言語設定
                
        excludeAvast: bool (default: false) # LRTrigger, LeftTrigger, RightTrigger
                \\AvastUI.exeがフォアグラウンドの場合は処理を行わない

        adminCheck: bool (default: true) # General
                管理者として実行されていないとき、起動時に管理者として再起動するか確認する
        
ライセンス:
        The MIT License
        詳しくは License.txt を参照
        
ライブラリ:
        Library.txt を参照
        
アイコン:
        ソースの build/IconGen で描いています。
        # 2.8.4 までのアイコンは、こちらのジェネレーターで作りました。
        # http://icon-generator.net/

連絡:
        何かの問題や、気になる点、また改良案などがあったら
        Blog、GitHub、メールなどで報告頂けると幸いです。
        
製作者:
        Yuki Ono <ykon0x1@gmail.com>
        
著作権:
        Copyright (c) 2016 Yuki Ono
