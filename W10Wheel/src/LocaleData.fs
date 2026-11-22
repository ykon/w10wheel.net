module LocaleData

[<Literal>]
let AdminMessage =
    "So it won't work in the windows of some programs (those running as administrator). It's highly recommended to run it as administrator."

let private JapaneseMessage =
    [
        "W10Wheel is already running. This instance will exit.", "二重起動を検出したため終了します。";
        "W10Wheel is not running as administrator", "W10Wheel は管理者として実行されていません";
        AdminMessage, "そのため、一部のプログラム (管理者として実行されているもの) のウィンドウでは機能しません。管理者として実行することを推奨します。";
        "Restart as administrator", "管理者として再起動";
        "Windows asks for permission (User Account Control).", "UAC の確認が表示されます";
        "Continue without administrator rights", "このまま実行";
        "More info (Wiki)", "詳細 (Wiki)";
        "Do not ask again", "次回から表示しない";
        "Ask to restart as administrator at startup", "起動時に管理者として再起動するか確認する";
        "Asked when W10Wheel is not running as administrator.",
            "W10Wheel が管理者として実行されていない場合に確認します";

        "Failed mouse hook install", "マウスフックのインストールに失敗しました";
        "Failed keyboard hook install", "キーボードフックのインストールに失敗しました";
        "Properties does not exist", "設定ファイルが存在しません";
        "Unknown Command", "不明なコマンド";
        "Command Error", "コマンドのエラー";

        "Error", "エラー";
        "Stopped", "停止中";
        "Runnable", "実行中";

        "Properties Name", "設定ファイル名";
        "Add Properties", "設定ファイル追加";
        "This name cannot be used (characters not allowed in file names, spaces at either end, -- at the start, or Default).",
            "この名前は使えません (ファイル名に使えない文字、前後の空白、先頭の --、Default)";
        "Properties of this name already exist.", "この名前の設定ファイルは既に存在します";
        "OK", "OK";
        "Cancel", "キャンセル";
        "Invalid Name", "無効な名前";
        "Name Error", "名前のエラー";

        "Settings...", "設定...";
        "Settings", "設定";
        "General", "全般";
        "Scroll", "スクロール";
        "Mouse", "マウス";
        "Enabled", "有効";
        "Changes are applied and saved at once.", "変更はすぐに反映・保存されます";
        "Close", "閉じる";
        "Reset to defaults", "既定値に戻す";
        "Not used while Real Wheel Mode is enabled.", "リアルホイールモードが有効な間は使われません";
        "Used when Horizontal Scroll is enabled.", "水平スクロールが有効な場合に使われます";
        "The thresholds are not used while VH Adjuster is enabled.", "垂直/水平スクロール調整が有効な間、閾値は使われません";
        "Scrolling is disabled.", "スクロール機能が無効になっています";
        "Not in the properties file.", "設定ファイルに存在しません";
        "Not used: the numbers of thresholds and multipliers differ.", "使われていません: 閾値と倍率の個数が違います";
        "Not used: the thresholds must be ascending numbers of 0 or more.", "使われていません: 閾値は 0 以上の昇順にしてください";
        "Not used: the multipliers must be numbers of 0 or more.", "使われていません: 倍率は 0 以上の数にしてください";
        "Not used: customAccelThreshold and customAccelMultiplier are both needed.",
            "使われていません: customAccelThreshold と customAccelMultiplier の両方が必要です";
        "Not used: a value is not a number.", "使われていません: 数値でない値があります";
        "Loaded: {0} steps (thresholds {1} ... {2}, multipliers {3} ... {4})", "読み込み済み: {0} 段階 (閾値 {1} ... {2}、倍率 {3} ... {4})";

        // The tips of the settings dialog (from the Readme)
        "The language of the menu and the dialogs.", "メニューとダイアログの言語です";
        "The priority of the process of W10Wheel.", "W10Wheel のプロセスの優先度です";
        "The mouse buttons that start scroll mode.\nUnchecked: no mouse trigger (the keyboard trigger only).",
            "スクロールモードを始めるマウスのボタンです\nチェックを外すとマウスのトリガーを使いません (キーボードトリガーのみの場合)";
        "The trigger pressed with Shift, Ctrl or Alt sends a middle click (Middle, X1, X2 only).",
            "Shift、Ctrl、Alt キーを押しながらトリガーを押すと中クリックを送ります (Middle、X1、X2 のみ)";
        "After a drag with a Drag trigger, scroll mode stays locked.", "Drag のトリガーでドラッグした後、スクロールモードに固定します";
        "The key that starts scroll mode.\nThe keys sent by other software (autofill, macros, key remapping tools) are not triggers.",
            "スクロールモードを始めるキーです\n他のソフトが送ったキー (自動入力、マクロ、キー割り当てツールなど) はトリガーになりません";
        "How long the LR, Left and Right triggers wait for the other button.",
            "LR、Left、Right のトリガーで、同時押しのもう一方のボタンを待つ時間です";
        "A trigger released within this time locks scroll mode.", "この時間内にトリガーを離すと、スクロールモードに固定します";
        "Changes the cursor in scroll mode.", "スクロールモードのカーソルを変更します";
        "Scrolls horizontally too.\nDisable it if you do not use it.", "水平にもスクロールします\n使わない場合は無効にしてください";
        "Reverses the direction of the scroll.", "スクロールの方向を逆にします";
        "Swaps the vertical and the horizontal scroll.", "垂直スクロールと水平スクロールを入れ替えます";
        "A vertical move up to this does not scroll.", "この値以下の垂直の移動ではスクロールしません";
        "A horizontal move up to this does not scroll.\nToo small, the vertical scroll becomes hard to use.",
            "この値以下の水平の移動ではスクロールしません\n小さすぎると垂直スクロールが使いづらくなります";
        "Accelerates the scroll by the speed of the move, with the multipliers of the table.",
            "移動の速さに応じて、テーブルの倍率でスクロールを加速します";
        "The multipliers of the table.", "テーブルの倍率です";
        "Uses customAccelThreshold and customAccelMultiplier of the properties file (only when they are valid).",
            "設定ファイルの customAccelThreshold と customAccelMultiplier を使います (正しい値が存在する場合だけ選択可能)";
        "Scrolls as a real wheel does, a notch at a time.\nThe accel table is not used.",
            "実際のホイールのように一回分ずつスクロールします\n加速テーブルは使われません";
        "The wheel value of a notch (a usual mouse sends 120).", "一回分のホイール値です (通常のマウスは 120)";
        "The vertical move made into a notch.", "一回分の垂直スクロールに変換する移動量です";
        "The horizontal move made into a notch.", "一回分の水平スクロールに変換する移動量です";
        "Sends the first notch whatever the move (else after half of the move).",
            "移動量に関係なく最初の一回分を送ります (無効の場合は半分の移動量)";
        "Sends a notch at once when the move turns back, whatever the move.", "折り返した場合、移動量に関係なく一回分を送ります";
        "Scrolls in one direction at a time, vertical or horizontal.", "垂直か水平の一方向だけにスクロールします";
        "Fixed: the direction of the larger first move.\nSwitching: also switched by a move above the switching threshold.",
            "固定: 初回の移動量が多い方向に固定します\n切り替え: さらに切り替え閾値を超える移動で方向を切り替えます";
        "The first direction prefers vertical.", "初回の方向の判定で垂直を優先します";
        "The first direction is decided by a move above this (smaller moves are ignored).",
            "この値を超える移動で初回の方向を判定します (それ以下は無視)";
        "A move above this switches the direction (Switching).", "この値を超える移動で方向を切り替えます (切り替え)";

        "Trigger", "トリガー";
        // The names of the mouse triggers stay English, as in the Readme and the properties
        // (LRTrigger...): only what is in parentheses is translated
        "LR (Left <<-->> Right)", "LR (左 <<-->> 右)";
        "Left (Left -->> Right)", "Left (左 -->> 右)";
        "Right (Right -->> Left)", "Right (右 -->> 左)";
        "X1", "X1 (拡張1)";
        "X2", "X2 (拡張2)";
        "Send MiddleClick", "中クリック送信";
        "Dragged Lock", "ドラッグ後固定";
        
        "Keyboard", "キーボード";
        "VK_CONVERT (Henkan)", "VK_CONVERT (変換)";
        "VK_NONCONVERT (Muhenkan)", "VK_NONCONVERT (無変換)";
        "VK_LWIN (Left Windows)", "VK_LWIN (左 Windows)";
        "VK_RWIN (Right Windows)", "VK_RWIN (右 Windows)";
        "VK_LSHIFT (Left Shift)", "VK_LSHIFT (左 Shift)";
        "VK_RSHIFT (Right Shift)", "VK_RSHIFT (右 Shift)";
        "VK_LCONTROL (Left Ctrl)", "VK_LCONTROL (左 Ctrl)";
        "VK_RCONTROL (Right Ctrl)", "VK_RCONTROL (右 Ctrl)";
        "VK_LMENU (Left Alt)", "VK_LMENU (左 Alt)";
        "VK_RMENU (Right Alt)", "VK_RMENU (右 Alt)";
        
        "Accel Table", "加速テーブル";
        "Custom Table", "カスタムテーブル";
        
        "Priority", "プロセス優先度";
        "High", "高";
        "Above Normal", "通常以上";
        "Normal", "通常";
        
        "Simultaneous press time", "同時押し判定時間";
        "Scroll mode lock time", "スクロールモード固定判定時間";
        "Vertical scroll threshold", "垂直スクロール閾値";
        "Horizontal scroll threshold", "水平スクロール閾値";

        "Real Wheel Mode", "リアルホイールモード";
        "Wheel delta", "ホイール回転値";
        "Vertical move per notch", "垂直ホイール移動値";
        "Horizontal move per notch", "水平ホイール移動値";
        "Quick first response", "初回の反応を速くする";
        "Quick turn response", "折り返しの反応を速くする";
        
        "VH Adjuster", "垂直/水平スクロール調整";
        "Fixed", "固定";
        "Switching", "切り替え";
        "Prefer vertical first", "初回垂直スクロール優先";
        "First direction threshold", "初回判定閾値";
        "Switching threshold", "切り替え閾値";
        
        "Properties", "設定ファイル";
        "Reload", "再読み込み";
        "Open Dir", "フォルダを開く";
        "Add", "追加";
        "Delete", "削除";
        "Default", "デフォルト";
        
        "Cursor Change", "カーソル変更";
        "Horizontal Scroll", "水平スクロール";
        "Reverse Scroll (Flip)", "スクロール反転";
        "Swap Scroll (V.H)", "垂直/水平スクロール入れ替え";
        "Pause", "一時停止";
        
        "Language", "言語";
        "English", "英語";
        "Japanese", "日本語";
        
        "About", "バージョン情報";
        "Version", "バージョン";
        "Running as administrator", "管理者として実行";
        "Yes", "はい";
        "No", "いいえ";

        "Delete these properties?", "この設定ファイルを削除しますか？";
        "Reset all the settings to their defaults?", "すべての設定を既定値に戻しますか？";
        "The language is kept.", "言語は変わりません。";

        "Exit", "終了";
    ] |> Map.ofList

let get_ja msg =
    match JapaneseMessage.TryFind msg with
    | Some ja_msg -> ja_msg
    | None -> msg

