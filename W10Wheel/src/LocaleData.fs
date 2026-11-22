module LocaleData

[<Literal>]
let AdminMessage =
    "So it won't work in the windows of some programs (those running as administrator). It's highly recommended to run it as administrator."

let private JapaneseMessage =
    [
        "Double Launch?", "二重起動していませんか？";
        "W10Wheel is not running as administrator", "W10Wheel は管理者として実行されていません";
        AdminMessage, "そのため、一部のプログラム (管理者として実行されているもの) のウィンドウでは機能しません。管理者として実行することを推奨します。";
        "Restart as administrator", "管理者として再起動";
        "Windows asks for permission (User Account Control).", "UAC の確認が表示されます。";
        "Continue without administrator rights", "このまま実行";
        "More info (Wiki)", "詳細 (Wiki)";

        "Failed mouse hook install", "マウスフックのインストールに失敗しました";
        "Failed keyboard hook install", "キーボードフックのインストールに失敗しました";
        "Properties does not exist", "設定ファイルが存在しません";
        "Unknown Command", "不明なコマンド";
        "Command Error", "コマンドのエラー";

        "Error", "エラー";
        "Question", "質問";
        "Stopped", "停止中";
        "Runnable", "実行中";

        "Properties Name", "設定ファイル名";
        "Add Properties", "設定ファイル追加";
        "Invalid Name", "無効な名前";
        "Name Error", "名前のエラー";
        "Delete properties", "設定ファイル削除";

        "Settings...", "設定...";
        "Settings", "設定";
        "General", "全般";
        "Scroll", "スクロール";
        "Mouse", "マウス";
        "Enabled", "有効";
        "Changes are applied and saved at once.", "変更はすぐに反映・保存されます。";
        "Close", "閉じる";
        "Reset to defaults", "既定値に戻す";
        "Not used while Real Wheel Mode is enabled.", "リアルホイールモードが有効な間は使われません。";
        "Used when Horizontal Scroll (Scroll) is enabled.", "水平スクロール (スクロール) が有効なときに使われます。";
        "Reset all the settings to their defaults? The language is kept.", "すべての設定を既定値に戻しますか？ 言語は変わりません。";

        "Trigger", "トリガー";
        // The names of the mouse triggers stay English, as in the Readme and the properties
        // (LRTrigger...): only what is in parentheses is translated
        "LR (Left <<-->> Right)", "LR (左 <<-->> 右)";
        "Left (Left -->> Right)", "Left (左 -->> 右)";
        "Right (Right -->> Left)", "Right (右 -->> 左)";
        "X1", "X1 (拡張1)";
        "X2", "X2 (拡張2)";
        "Send MiddleClick", "中央クリック送信";
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
        
        "pollTimeout", "同時押し判定時間";
        "scrollLocktime", "スクロールモード固定判定時間";
        "verticalThreshold", "垂直スクロール閾値";
        "horizontalThreshold", "水平スクロール閾値";

        "Real Wheel Mode", "リアルホイールモード";
        "wheelDelta", "ホイール回転値";
        "vWheelMove", "垂直ホイール移動値";
        "hWheelMove", "水平ホイール移動値";
        "quickFirst", "初回の反応を速くする";
        "quickTurn", "折り返しの反応を速くする";
        
        "VH Adjuster", "垂直/水平スクロール調整";
        "Fixed", "固定";
        "Switching", "切り替え";
        "firstPreferVertical", "初回垂直スクロール優先";
        "firstMinThreshold", "初回判定閾値";
        "switchingThreshold", "切り替え閾値";
        
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
        "Name", "名前";
        "Version", "バージョン";
        
        "Exit", "終了";
    ] |> Map.ofList

let get_ja msg =
    match JapaneseMessage.TryFind msg with
    | Some ja_msg -> ja_msg
    | None -> msg

