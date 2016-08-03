名前:
        W10Wheel.NET

バージョン:
        0.8

URL:
        https://github.com/ykon/w10wheel.net

概要:
        マウスホイールシミュレーター

履歴:
        2016-08-03: Version 0.8.0: トリガーにNoneを追加、他
        2016-07-31: Version 0.7.0: キーボード(トリガー)対応
        2016-07-29: Version 0.6.0: OSの終了時などに設定を保存、他
        2016-07-27: Version 0.5.0: イベントの順序、同期の改良
        2016-07-25: Version 0.4.0: SwapScrollの追加、X1,X2のUp修正
        2016-07-25: Version 0.3.0: DraggedLockの追加、再送イベントの改良
        2016-07-23: Version 0.2.0: スレッド周辺とイベント受け渡しの改良
        2016-07-23: Version 0.1.3: EventWaiterを修正 (fromUp)
        2016-07-23: Version 0.1.2: 修正: pollTimeoutの設定が使われていない
        2016-07-23: Version 0.1.1: EventWaiterを修正 (fromDown)
        2016-07-22: Version 0.1.0: 初公開
        
移植:
        こちらは、W10Wheel (Java version) を .NET Framework に移植したものです。
        https://github.com/ykon/w10wheel
        
        実行環境が違うだけで、ほとんどの動作は同一になります。
        設定ファイルも共有します。
        
対応環境:
        .NET Framework (4.6.1) システム要件
         https://msdn.microsoft.com/ja-jp/library/8z6watww(v=vs.110).aspx
        
        Windows 7, 8.1:
                最新の.NET Frameworkをインストールしてください。 
                https://www.microsoft.com/ja-JP/download/details.aspx?id=49981
                WizMouse など、非アクティブウィンドウをスクロール可能にするソフトが必要です。
                http://forest.watch.impress.co.jp/docs/serial/okiniiri/587890.html
        
        Windows 10:
                オプションを有効にしてください。
                http://www.lifehacker.jp/2015/09/150909_window_scrolling.html
        
互換性:
        Logitech(ロジクール) の SetPoint は問題ありません。
        ボタンの切り替え(入れ替え)をしてもうまく動きます。
        フィルタドライバのレベルで動いているものは安全だと思います。
        
        グローバルフックのレベルで動いているものは競合しますが、
        WizMouse など安全に使えるものも存在しています。
        起動の順序を考慮することで使えるものもあるかもしれません。
        
        WheelBall や Nadesath など同じ機能のソフトは同時に起動しないでください。
        動作が競合して操作不能になったりします。
        
回復:
        操作が効かなくなったりした時は、まず Ctrl-Alt-Delete でタスクマネージャーを開いてください。
        それだけで制御を取り戻せると思います、効かない場合は何回か繰り返してください。
        その後は、タスクマネージャーでアイコンを探して終了させてください。
        WheelBall などと同時に起動すると、何故か終了できなくなることもあります。
        この場合の終了方法は OS の再起動しかありません。
        # 他に何か方法があったら教えてください。
        
使用方法:
        W10Wheel.exe を実行してください。
        タスクトレイに exe ファイルと同様のアイコンが発生するはずです。
        こちらから右クリックメニューで設定を変更できます。
        詳しい方は、設定ファイル (.W10Wheel.properties) を直接編集してください。
        一度起動して終了すれば、設定ファイルはユーザーディレクトリに生成されます。
        
        あとの使い方は WheelBall など同種のソフトと同様になります。
        各トリガーを押すとスクロールモードに移行します。
        マウスまたはボールの操作でスクロールするはずです。
        スクロールモードは何かのボタンを押すと解除されます。
        
        トリガーを押したままスクロールして、離したら止めることもできます。
        同時押しでは、両方を押さえたままにするのではなく、片方を先に離すと使いやすくなります。
        
        *Dragのトリガーではドラッグしている間だけスクロールします。
        スクロールモードに固定されません。(Dragged Lock が無効の場合)
        
        終了するには、タスクトレイのアイコンをダブルクリックか
        右クリックメニューから Exit を選択してください。
        
メニュー項目:
        Trigger: トリガーを変更 (設定項目を参照)
        Accel Table: 加速を有効にするか、どのテーブルを使うか # 1.0は1.0倍を表す
        Priority: プロセスの優先度を変更
        SetNumber: 数値をセット (設定項目を参照)
        Real Wheel Mode: 実際のホイールに近いスクロール (設定項目を参照)
        Keyboard: キーボードをトリガーとする
        Reload Properties: 設定ファイルを再読込
        Cursor Change: スクロールモードのカーソル変更
        Horizontal Scroll: 水平スクロール
        Reverse Scroll (Flip): スクロールの方向を逆にする (反転する)
        Swap Scroll (V.H): 垂直スクロールと水平スクロールを入れ替える (Vertical <-> Horizontal)
        Pass Mode: 全てのメッセージをそのまま通す # WheelBall の制御停止
        Info: バージョン番号を表示
        Exit: 終了
        
設定項目:
        firstTrigger: (default: LRTrigger)
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
                        ミドル(中)を押すとトリガーになります。
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
                        ミドル(中)ボタンでドラッグするとスクロールできます。
                        固定はされません、ドラッグしないで離すとミドル(中)クリックを送ります。
                X1DragTrigger: # 固定なし
                        X1ボタンでドラッグするとスクロールできます。
                        固定はされません、ドラッグしないで離すとX1クリックを送ります。
                X2DragTrigger: # 固定なし
                        X2ボタンでドラッグするとスクロールできます。
                        固定はされません、ドラッグしないで離すとX2クリックを送ります。
        
        processPriority:  (default: AboveNormal)
                High: 高
                AboveNormal: 通常以上
                Normal: 通常
                
        pollTimeout: 150-500 (default: 300)
                同時押しのイベント待ち時間(ミリ秒)  # WheelBall の 判定時間 
        scrollLocktime: 150-500 (default: 300)
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
        horizontalThreshold: 0-500 (default: 50)
                水辺スクロールの閾値
                この値をあまり小さくすると垂直(通常)スクロールが、使いづらくなります。
        reverseScroll: bool (default: false)
                スクロールの方向を逆にする (反転する)
        swapScroll: bool (default: false)
                垂直スクロールと水平スクロールを入れ替える (Vertical <-> Horizontal)
                
        sendMiddleClick: bool (default: false) # Middle, X1, X2
                Shift または Ctrl または Alt キーを押しながら
                トリガーを押すとミドル(中)クリックを送ります。
        
        draggedLock: bool (default: false) # *DragTrigger
                *DragTriggerでドラッグ後、スクロールモードに固定します。
                
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
                
        accelTable: bool (default: false) # AccelTable
                AccelTableを有効にするか
        customAccelTable: bool (default: false) # AccelTable
                CustomTableを有効にするか
        accelMultiplier: string (default: M5) # AccelTable
                選択されている乗数テーブル
        customAccelThreshold: Int Array # AccelTable
                CustomTableで使われるThreshold
        customAccelMultiplier: Double Array # AccelTable
                CustomTableで使われるMultiplier
                
        keyboardHook: bool (default: false) # Keyboard
                キーボードのトリガーを有効にする
        targetVKCode: string (default: VK_NONCONVERT) # Keyboard
                トリガーに使うキー 
                
ライセンス:
        The MIT License
        詳しくは License.txt を参照
        
ライブラリ:
        Library.txt を参照
        
アイコン:
        こちらのジェネレーターで作りました。
        http://icon-generator.net/

連絡:
        使用していて、何か問題が見つかったら
        2ch の該当スレッドに書き込んでください。
        http://echo.2ch.net/test/read.cgi/hard/1468152050/
        
        GitHub から連絡する場合は Issues などにどうぞ。
        メールを送ってきても OK ですが、答えられるとは限りません。
        
製作者:
        Yuki Ono <ykon0x1@gmail.com>
        
著作権:
        Copyright (c) 2016 Yuki Ono
