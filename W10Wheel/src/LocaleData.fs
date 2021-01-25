module LocaleData

[<Literal>]
let AdminMessage =
    "W10Wheel is not running in admin mode, so it won't work in certain windows. It's highly recommended to run it as administrator. Click 'OK' for more info."

let private JapaneseMessage =
    [
        "Double Launch?", "二重起動していませんか？";
        AdminMessage, "W10Wheelは管理者モードで実行されていないため、\n一部のプログラムでは機能しません。\nそのため、管理者として実行することを推奨します。\n詳細を確認するには、「OK」を押してください。";

        "Failed mouse hook install", "マウスフックのインストールに失敗しました";
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
        "Invalid Number", "無効な数値";
        "Name Error", "名前のエラー";
        "Delete properties", "設定ファイル削除";
        "Set Text", "テキストを設定";
        
        "Trigger", "トリガー";
        "LR (Left <<-->> Right)", "左右 (左 <<-->> 右)";
        "Left (Left -->> Right)", "左 (左 -->> 右)";
        "Right (Right -->> Left)", "右 (右 -->> 左)";
        "Middle", "中央";
        "X1", "X1 (拡張1)";
        "X2", "X2 (拡張2)";
        "LeftDrag", "左ドラッグ";
        "RightDrag", "右ドラッグ";
        "MiddleDrag", "中央ドラッグ";
        "X1Drag", "X1 ドラッグ";
        "X2Drag", "X2 ドラッグ";
        "None", "なし";
        "Send MiddleClick", "中央クリック送信";
        "Dragged Lock", "ドラッグ後固定";
        
        "Keyboard", "キーボード";
        "ON / OFF", "有効 / 無効";
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
        
        "Set Number", "パラメーターを設定";
        "pollTimeout", "同時押し判定時間";
        "scrollLocktime", "スクロールモード固定判定時間";
        "verticalThreshold", "垂直スクロール閾値";
        "horizontalThreshold", "水平スクロール閾値";

        "Real Wheel Mode", "擬似ホイールモード";
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
        "Save", "保存";
        "Open Dir", "フォルダを開く";
        "Add", "追加";
        "Delete", "削除";
        "Default", "デフォルト";
        
        "Cursor Change", "カーソル変更";
        "Horizontal Scroll", "水平スクロール";
        "Reverse Scroll (Flip)", "垂直スクロール反転";
        "Swap Scroll (V.H)", "垂直/水平スクロール入れ替え";
        "Pass Mode", "制御停止";
        
        "Language", "言語";
        "English", "英語";
        "Japanese", "日本語";
        
        "Info", "情報";
        "Name", "名前";
        "Version", "バージョン";
        
        "Exit", "終了";
    ] |> Map.ofList

let get_ja msg =
    match JapaneseMessage.TryFind msg with
    | Some ja_msg -> ja_msg
    | None -> msg

