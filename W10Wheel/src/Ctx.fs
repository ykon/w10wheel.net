module Ctx

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Configuration
open System.Diagnostics
open System.Threading
open System.Windows.Forms
open System.Reflection
open System.Resources
open System.IO
open Microsoft.VisualBasic
open System.Collections.Generic

open Mouse

let private firstTrigger: Trigger ref = ref LRTrigger
let private pollTimeout = ref 300
let private passMode = ref false
let private processPriority = ref ProcessPriority.AboveNormal

// MouseWorks by Kensington (DefaultAccelThreshold, M5, M6, M7, M8, M9)
// http://www.nanayojapan.co.jp/support/help/tmh00017.htm

[<AbstractClass>]
type AccelMultiplier(name: string, dArray: double array) =
    member self.Name = name
    member self.DArray = dArray

type M5() = inherit AccelMultiplier("M5", [|1.0; 1.3; 1.7; 2.0; 2.4; 2.7; 3.1; 3.4; 3.8; 4.1; 4.5; 4.8|])
type M6() = inherit AccelMultiplier("M6", [|1.2; 1.6; 2.0; 2.4; 2.8; 3.3; 3.7; 4.1; 4.5; 4.9; 5.4; 5.8|])
type M7() = inherit AccelMultiplier("M7", [|1.4; 1.8; 2.3; 2.8; 3.3; 3.8; 4.3; 4.8; 5.3; 5.8; 6.3; 6.7|])
type M8() = inherit AccelMultiplier("M8", [|1.6; 2.1; 2.7; 3.2; 3.8; 4.4; 4.9; 5.5; 6.0; 6.6; 7.2; 7.7|])
type M9() = inherit AccelMultiplier("M9", [|1.8; 2.4; 3.0; 3.6; 4.3; 4.9; 5.5; 6.2; 6.8; 7.4; 8.1; 8.7|])

let private getAccelMultiplierOfName name: AccelMultiplier =
    match name with
    | "M5" -> (M5() :> AccelMultiplier)
    | "M6" -> (M6() :> AccelMultiplier)
    | "M7" -> (M7() :> AccelMultiplier)
    | "M8" -> (M8() :> AccelMultiplier)
    | "M9" -> (M9() :> AccelMultiplier)
    | e -> raise (ArgumentException(e))

let private DefaultAccelThreshold = [|1; 2; 3; 5; 7; 10; 14; 20; 30; 43; 63; 91|]

type private Accel() =
    [<VolatileField>] static let mutable table = false
    [<VolatileField>] static let mutable threshold: int array = DefaultAccelThreshold
    [<VolatileField>] static let mutable multiplier: AccelMultiplier = M5() :> AccelMultiplier

    [<VolatileField>] static let mutable customDisabled = true
    [<VolatileField>] static let mutable customTable = false
    [<VolatileField>] static let mutable customThreshold: int array = null
    [<VolatileField>] static let mutable customMultiplier: double array = null

    static member Table
        with get() = table
        and set b = table <- b
    static member Threshold
        with get() = threshold
    static member Multiplier
        with get() = multiplier
        and set m = multiplier <- m
    static member CustomDisabled
        with get() = customDisabled
        and set b = customDisabled <- b
    static member CustomTable
        with get() = customTable
        and set b = customTable <- b
    static member CustomThreshold
        with get() = customThreshold
        and set a = customThreshold <- a
    static member CustomMultiplier
        with get() = customMultiplier
        and set a = customMultiplier <- a

let isAccelTable () =
    Accel.Table

let getAccelThreshold () =
    if not Accel.CustomDisabled && Accel.CustomTable then
        Accel.CustomThreshold
    else
        Accel.Threshold

let getAccelMultiplier () =
    if not Accel.CustomDisabled && Accel.CustomTable then
        Accel.CustomMultiplier
    else
        Accel.Multiplier.DArray

let private getFirstTrigger () =
    Volatile.Read(firstTrigger)

let private getProcessPriority () =
    Volatile.Read(processPriority)

let isTrigger (e: Trigger) =
    getFirstTrigger() = e

let isTriggerEvent (e: MouseEvent) =
    isTrigger(getTrigger e)

let isLRTrigger () =
    isTrigger LRTrigger

let isDragTriggerEvent = function
    | LeftDown(_) | LeftUp(_) -> isTrigger(LeftDragTrigger)
    | RightDown(_) | RightUp(_) -> isTrigger(RightDragTrigger)
    | MiddleDown(_) | MiddleUp(_) -> isTrigger(MiddleDragTrigger)
    | X1Down(_) | X1Up(_) -> isTrigger(X1DragTrigger)
    | X2Down(_) | X2Up(_) -> isTrigger(X2DragTrigger)
    | _ -> raise (ArgumentException())

let isSingleTrigger () =
    getFirstTrigger().IsSingle

let isDragTrigger () =
    getFirstTrigger().IsDrag


type private Threshold() =
    [<VolatileField>] static let mutable vertical = 0
    [<VolatileField>] static let mutable horizontal = 50

    static member Vertical
        with get() = vertical
        and set n = vertical <- n
    static member Horizontal
        with get() = horizontal
        and set n = horizontal <- n

let getVerticalThreshold () = Threshold.Vertical
let getHorizontalThreshold () = Threshold.Horizontal

(*
let private skip_right_down = ref false
let private skip_right_up = ref false
let private skip_left_down = ref false
let private skip_left_up = ref false
*)

type private Scroll() =
    [<VolatileField>] static let mutable mode = false
    [<VolatileField>] static let mutable stime = 0u
    [<VolatileField>] static let mutable sx = 0
    [<VolatileField>] static let mutable sy = 0
    [<VolatileField>] static let mutable locktime = 300
    [<VolatileField>] static let mutable cursorChange = true
    [<VolatileField>] static let mutable reverse = false
    [<VolatileField>] static let mutable horizontal = true

    static member Start (info: HookInfo) =
        stime <- info.time
        sx <- info.pt.x
        sy <- info.pt.y
        mode <- true

        if cursorChange && not (isDragTrigger()) then
            WinCursor.change()

    static member Exit () =
        mode <- false

        if cursorChange then
            WinCursor.restore() |> ignore

    static member CheckExit (time: uint32) =
        let dt = time - stime
        Debug.WriteLine(sprintf "scroll time: %d ms" dt)
        dt > (uint32 locktime)

    static member IsMode with get() = mode
    static member StartTime with get() = stime
    static member StartPoint with get() = (sx, sy)
    static member LockTime
        with get() = locktime
        and set n = locktime <- n
    static member CursorChange
        with get() = cursorChange
        and set b = cursorChange <- b
    static member Reverse
        with get() = reverse
        and set b = reverse <- b
    static member Horizontal
        with get() = horizontal
        and set b = horizontal <- b

let isScrollMode () = Scroll.IsMode
let startScrollMode (info: HookInfo): unit = Scroll.Start info
let exitScrollMode (): unit = Scroll.Exit()
let checkExitScroll (time: uint32) = Scroll.CheckExit time
let getScrollStartPoint () = Scroll.StartPoint
let getScrollLockTime () = Scroll.LockTime
let isCursorChange () = Scroll.CursorChange
let isReverseScroll () = Scroll.Reverse
let isHorizontalScroll () = Scroll.Horizontal

type private RealWheel() =
    [<VolatileField>] static let mutable mode = false
    [<VolatileField>] static let mutable wheelDelta = 120
    [<VolatileField>] static let mutable vWheelMove = 60
    [<VolatileField>] static let mutable hWheelMove = 60
    [<VolatileField>] static let mutable quickFirst = false
    [<VolatileField>] static let mutable quickTurn = false

    static member Mode
        with get() = mode
        and set b = mode <- b
    static member WheelDelta
        with get() = wheelDelta
        and set n = wheelDelta <- n
    static member VWheelMove
        with get() = vWheelMove
        and set n = vWheelMove <- n
    static member HWheelMove
        with get() = hWheelMove
        and set n = hWheelMove <- n
    static member QuickFirst
        with get() = quickFirst
        and set b = quickFirst <- b
    static member QuickTurn
        with get() = quickTurn
        and set b = quickTurn <- b

let isRealWheelMode () =
    RealWheel.Mode

let getWheelDelta () =
    RealWheel.WheelDelta

let getVWheelMove () =
    RealWheel.VWheelMove

let getHWheelMove () =
    RealWheel.HWheelMove

let isQuickFirst () =
    RealWheel.QuickFirst

let isQuickTurn () =
    RealWheel.QuickTurn

type LastFlags() =
    // R = Resent
    [<VolatileField>] static let mutable ldR = false
    [<VolatileField>] static let mutable rdR = false

    // S = Suppressed
    [<VolatileField>] static let mutable ldS = false
    [<VolatileField>] static let mutable rdS = false
    [<VolatileField>] static let mutable sdS = false

    static member SetResent (down:MouseEvent): unit =
        match down with
        | LeftDown(_) -> ldR <- true
        | RightDown(_) -> rdR <- true
        | _ -> ()

    static member IsDownResent (up:MouseEvent) =
        match up with
        | LeftUp(_) -> ldR
        | RightUp(_) -> rdR
        | _ -> raise (ArgumentException())

    static member SetSuppressed (down:MouseEvent): unit =
        match down with
        | LeftDown(_) -> ldS <- true 
        | RightDown(_) -> rdS <- true
        | MiddleDown(_) | X1Down(_) | X2Down(_) -> sdS <- true
        | _ -> ()

    static member IsDownSuppressed (up:MouseEvent) =
        match up with
        | LeftUp(_) -> ldS
        | RightUp(_) -> rdS
        | MiddleUp(_) | X1Up(_) | X2Up(_) -> sdS
        | _ -> raise (ArgumentException())

    static member Reset (down:MouseEvent) =
        match down with
        | LeftDown(_) -> ldR <- false; ldS <- false
        | RightDown(_) -> rdR <- false; rdS <- false
        | MiddleDown(_) | X1Down(_) | X2Down(_) -> sdS <- false
        | _ -> raise (ArgumentException())

let getPollTimeout () =
    Volatile.Read(pollTimeout)

let isPassMode () =
    Volatile.Read(passMode)

type HookInfo = WinAPI.MSLLHOOKSTRUCT

(*
let setSkip (me:MouseEvent) (enabled:bool) =
    match me with
    | LeftDown(_) -> Volatile.Write(skip_left_down, enabled)
    | LeftUp(_) -> Volatile.Write(skip_left_up, enabled)
    | RightDown(_) -> Volatile.Write(skip_right_down, enabled)
    | RightUp(_) -> Volatile.Write(skip_right_up, enabled)
    | _ -> raise (ArgumentException())

let setSkipMC (mc:MouseClick) (enabled:bool): unit =
    match mc with
    | LeftClick(info) ->
        setSkip (LeftDown(info)) enabled
        setSkip (LeftUp(info)) enabled
    | RightClick(info) ->
        setSkip (RightDown(info)) enabled
        setSkip (RightUp(info)) enabled
    | _ -> raise (ArgumentException())


let checkSkip (me: MouseEvent): bool =
    if not (isResendTag (me.info.dwExtraInfo.ToUInt32())) then
        false
    else
        let res =
            match me with
            | LeftDown(_) -> Volatile.Read(skip_left_down)
            | LeftUp(_) -> Volatile.Read(skip_left_up)
            | RightDown(_) -> Volatile.Read(skip_right_down)
            | RightUp(_) -> Volatile.Read(skip_right_up)
            | _ -> raise (ArgumentException())

        setSkip me false |> ignore
        res
*)

(*
let testInputBox () =
    let s = Interaction.InputBox("test", "test", "0")
    Debug.WriteLine(s)
*)

let private boolMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private triggerMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private accelMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private priorityMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private numberMenuDict = new Dictionary<string, ToolStripMenuItem>()

let private getBooleanOfName (name: string): bool =
    match name with
    | "realWheelMode" -> RealWheel.Mode
    | "cursorChange" -> Scroll.CursorChange
    | "horizontalScroll" -> Scroll.Horizontal
    | "reverseScroll" -> Scroll.Reverse
    | "quickFirst" -> RealWheel.QuickFirst
    | "quickTurn" -> RealWheel.QuickTurn
    | "accelTable" -> Accel.Table
    | "customAccelTable" -> Accel.CustomTable
    | "passMode" -> Volatile.Read(passMode)
    | e -> raise (ArgumentException(e))

let private setBooleanOfName (name:string) (b:bool) =
    Debug.WriteLine(sprintf "setBoolean: %s = %s" name (b.ToString()))
    match name with
    | "realWheelMode" -> RealWheel.Mode <- b
    | "cursorChange" -> Scroll.CursorChange <- b
    | "horizontalScroll" -> Scroll.Horizontal <- b
    | "reverseScroll" -> Scroll.Reverse <- b
    | "quickFirst" -> RealWheel.QuickFirst <- b
    | "quickTurn" -> RealWheel.QuickTurn <- b
    | "accelTable" -> Accel.Table <- b
    | "customAccelTable" -> Accel.CustomTable <- b
    | "passMode" -> Volatile.Write(passMode, b)
    | e -> raise (ArgumentException(e))

let private makeSetBooleanEvent (name: String) =
    fun (sender:obj) event ->
        let item = sender :?> ToolStripMenuItem
        let b = item.Checked
        setBooleanOfName name b

let private createBoolMenuItem vName mName =
    let item = new ToolStripMenuItem(mName, null, makeSetBooleanEvent(vName))
    item.CheckOnClick <- true
    boolMenuDict.[vName] <- item
    item

let private createBoolMenuItemS vName =
    createBoolMenuItem vName vName

let private textToName (s: string): string =
    s.Split([|' '|]).[0]

let private uncheckAllItems (dict: Dictionary<string, ToolStripMenuItem>) =
    for KeyValue(name, item) in dict do
        item.CheckState <- CheckState.Unchecked

let private setTrigger (text: string) =
    let res = Mouse.getTriggerOfStr text
    Debug.WriteLine(sprintf "setTrigger: %s" res.Name)
    Volatile.Write(firstTrigger, res)     

let private createTriggerMenuItem text =
    let item = new ToolStripMenuItem(text, null)
    let name = textToName text
    triggerMenuDict.[name] <- item

    item.Click.Add (fun _ ->
        if item.CheckState = CheckState.Unchecked then
            uncheckAllItems triggerMenuDict
            item.CheckState <- CheckState.Indeterminate
            setTrigger name
    )

    item

let private addSeparator (col: ToolStripItemCollection) =
    col.Add(new ToolStripSeparator()) |> ignore

let private createTriggerMenu () =
    let menu = new ToolStripMenuItem("Tigger")
    let add name = menu.DropDownItems.Add(createTriggerMenuItem name) |> ignore

    add "LR (Left <<-->> Right)"
    add "Left (Left -->> Right)"
    add "Right (Right -->> Left)"
    add "Middle"
    add "X1"
    add "X2"
    addSeparator menu.DropDownItems

    add "LeftDrag"
    add "RightDrag"
    add "MiddleDrag"
    add "X1Drag"
    add "X2Drag"

    menu

let private createOnOffMenuItem (vname: string) =
    let getOnOff (b: bool) = if b then "ON" else "OFF"
    let item = new ToolStripMenuItem(getOnOff(getBooleanOfName vname))
    item.CheckOnClick <- true
    boolMenuDict.[vname] <- item

    item.Click.Add (fun _ ->
        let b  = item.Checked
        item.Text <- getOnOff b
        setBooleanOfName vname b
    )
    item

let private setAccelMultiplier name =
    Debug.WriteLine(sprintf "setAccelMultiplier %s" name)
    Accel.Multiplier <- getAccelMultiplierOfName name

let private createAccelMenuItem text =
    let item = new ToolStripMenuItem(text, null)
    let name = textToName text
    accelMenuDict.[name] <- item

    item.Click.Add (fun _ ->
        if item.CheckState = CheckState.Unchecked then
            uncheckAllItems accelMenuDict
            item.CheckState <- CheckState.Indeterminate
            setAccelMultiplier name
    )

    item

let private createAccelTableMenu () =
    let menu = new ToolStripMenuItem("Accel Table")
    let items = menu.DropDownItems
    let add name = items.Add(createAccelMenuItem name) |> ignore

    items.Add(createOnOffMenuItem "accelTable") |> ignore
    addSeparator items

    add "M5 (1.0 ... 4.8)"
    add "M6 (1.2 ... 5.8)"
    add "M7 (1.4 ... 6.7)"
    add "M8 (1.6 ... 7.7)"
    add "M9 (1.8 ... 8.7)"
    addSeparator items

    let vName = "customAccelTable"
    items.Add(createBoolMenuItem vName "Custom Table") |> ignore
    boolMenuDict.[vName].Enabled <- not Accel.CustomDisabled
    
    menu

let private setPriority name =
    let p = ProcessPriority.getPriority name
    Debug.WriteLine(sprintf "setPriority: %s" p.Name)
    Volatile.Write(processPriority, p)
    ProcessPriority.setPriority p

let private createPriorityMenuItem name =
    let item = new ToolStripMenuItem(name, null)
    priorityMenuDict.[name] <- item

    item.Click.Add (fun _ ->
        if item.CheckState = CheckState.Unchecked then
            uncheckAllItems priorityMenuDict
            item.CheckState <- CheckState.Indeterminate
            setPriority name
    )

    item

let private createPriorityMenu () =
    let menu = new ToolStripMenuItem("Priority")
    let add name = menu.DropDownItems.Add(createPriorityMenuItem name) |> ignore

    add "High"
    add "Above Normal"
    add "Normal"

    menu

let private getNumberOfName (name: string): int =
    match name with
    | "pollTimeout" -> Volatile.Read(pollTimeout)
    | "scrollLocktime" -> Scroll.LockTime
    | "verticalThreshold" -> Threshold.Vertical
    | "horizontalThreshold" -> Threshold.Horizontal
    | "wheelDelta" -> RealWheel.WheelDelta
    | "vWheelMove" -> RealWheel.VWheelMove
    | "hWheelMove" -> RealWheel.HWheelMove
    | e -> raise (ArgumentException(e))

let private setNumberOfName (name: string) (n: int): unit =
    Debug.WriteLine(sprintf "setNumber: %s = %d" name n)
    match name with
    | "pollTimeout" -> Volatile.Write(pollTimeout, n)
    | "scrollLocktime" -> Scroll.LockTime <- n
    | "verticalThreshold" -> Threshold.Vertical <- n
    | "horizontalThreshold" -> Threshold.Horizontal <- n
    | "wheelDelta" -> RealWheel.WheelDelta <- n
    | "vWheelMove" -> RealWheel.VWheelMove <- n
    | "hWheelMove" -> RealWheel.HWheelMove <- n
    | e -> raise (ArgumentException(e))

let private makeNumberText (name: string) (num: int) =
    sprintf "%s = %d" name num

let private isValidNumber input low up =
    match Int32.TryParse(input)  with
    | (true, res) -> res >= low && res <= up 
    | _ -> false

let private openInputBox name low up: int option =
    let msg = sprintf "%s (%d - %d)" name low up
    let dvalue = (getNumberOfName name).ToString()
    let input = Interaction.InputBox(msg, "Set Number", dvalue)
    if isValidNumber input low up then
        Some(Int32.Parse(input))
    else
        None

let private createNumberMenuItem name low up =
    let item = new ToolStripMenuItem(name, null)
    numberMenuDict.[name] <- item

    item.Click.Add (fun _ ->
        let num = openInputBox name low up
        num |> Option.iter (fun n ->
            setNumberOfName name n
            item.Text <- makeNumberText name n
        )
        ()
    )

    item

let private createSetNumberMenu () =
    let menu = new ToolStripMenuItem("Set Number")
    let add name low up = menu.DropDownItems.Add(createNumberMenuItem name low up) |> ignore

    add "pollTimeout" 150 500
    add "scrollLocktime" 150 500
    addSeparator menu.DropDownItems

    add "verticalThreshold" 0 500
    add "horizontalThreshold" 0 500

    menu

let private createRealWheelModeMenu () =
    let menu = new ToolStripMenuItem("Real Wheel Mode")
    let items = menu.DropDownItems
    let addNum name low up = items.Add(createNumberMenuItem name low up) |> ignore
    let addBool name = items.Add(createBoolMenuItemS name) |> ignore

    items.Add(createOnOffMenuItem "realWheelMode") |> ignore
    addSeparator items

    addNum "wheelDelta" 10 500
    addNum "vWheelMove" 10 500
    addNum "hWheelMove" 10 500
    addSeparator items
        
    addBool "quickFirst"
    addBool "quickTurn"
        
    menu

let private createCursorChangeMenuItem () =
    createBoolMenuItem "cursorChange" "Cursor Change"

let private createHorizontalScrollMenuItem () =
    createBoolMenuItem "horizontalScroll" "Horizontal Scroll"

let private createReverseScrollMenuItem () =
    createBoolMenuItem "reverseScroll" "Reverse Scroll"

let private createPassModeMenuItem () =
    let event = makeSetBooleanEvent "passMode"
    let item = new ToolStripMenuItem("Pass Mode", null, event)
    item.CheckOnClick <- true
    item

let private createInfoMenuItem () =
    let item = new ToolStripMenuItem("Info")

    item.Click.Add (fun _ ->
        let msg = sprintf "Name: %s / Version: %s" AppDef.PROGRAM_NAME_NET AppDef.PROGRAM_VERSION
        MessageBox.Show(msg, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information) |> ignore
    )
    item

let private notifyIcon = new System.Windows.Forms.NotifyIcon()

let private exitAction () =
    notifyIcon.Visible <- false
    Application.Exit()

let private createExitMenuItem (): ToolStripMenuItem =
    let item = new ToolStripMenuItem("Exit", null, fun _ _ -> exitAction())
    item

let private setDefaultPriority () =
    Debug.WriteLine("setDefaultPriority")
    ProcessPriority.setPriority(getProcessPriority())

let private NumberNames: string array =
    [|"pollTimeout"; "scrollLocktime";
      "verticalThreshold"; "horizontalThreshold";
      "wheelDelta"; "vWheelMove"; "hWheelMove"|]

let private BooleanNames: string array =
    [|"realWheelMode"; "cursorChange";
     "horizontalScroll"; "reverseScroll";
     "quickFirst"; "quickTurn";
     "accelTable"; "customAccelTable"|]

let private resetTriggerMenuItems () =
    for KeyValue(name, item) in triggerMenuDict do
        item.CheckState <-
            if Mouse.getTriggerOfStr name = getFirstTrigger() then
                CheckState.Indeterminate
            else
                CheckState.Unchecked

let private resetAccelMenuItems () =
    for KeyValue(name, item) in accelMenuDict do
        item.CheckState <-
            if name = Accel.Multiplier.Name then
                CheckState.Indeterminate
            else
                CheckState.Unchecked

let private resetPriorityMenuItems () =
    for KeyValue(name, item) in priorityMenuDict do
        item.CheckState <-
            if ProcessPriority.getPriority name = getProcessPriority() then
                CheckState.Indeterminate
            else
                CheckState.Unchecked

let private resetNumberMenuItems () =
    for KeyValue(name, item) in numberMenuDict do
        let num = getNumberOfName name
        item.Text <- makeNumberText name num

let private resetBoolNumberMenuItems () =
    for KeyValue(name, item) in boolMenuDict do
        item.Checked <- getBooleanOfName name

let private resetMenuItems () =
    resetTriggerMenuItems()
    resetAccelMenuItems()
    resetPriorityMenuItems()
    resetNumberMenuItems()
    resetBoolNumberMenuItems()

let PROP_NAME = sprintf ".%s.properties" AppDef.PROGRAM_NAME


let private prop = Properties.Properties()

let private setTriggerOfProperty (): unit =
    try
        setTrigger(prop.GetString("firstTrigger"))
    with
        | :? KeyNotFoundException as e -> Debug.WriteLine(sprintf "Not found: %s" e.Message)
        | :? ArgumentException as e -> Debug.WriteLine(sprintf "Match error: %s" e.Message)

let private setAccelOfProperty (): unit =
    try
        setAccelMultiplier(prop.GetString "accelMultiplier")
    with
        | :? KeyNotFoundException as e -> Debug.WriteLine(sprintf "Not found: %s" e.Message)
        | :? ArgumentException as e -> Debug.WriteLine(sprintf "Match error: %s" e.Message)

let private setCustomAccelOfProperty (): unit =
    try
        let cat = prop.GetIntArray("customAccelThreshold")
        let cam = prop.GetDoubleArray("customAccelMultiplier")

        if cat.Length <> 0 && cat.Length = cam.Length then
            Debug.WriteLine(sprintf "customAccelThreshold: %A" cat)
            Debug.WriteLine(sprintf "customAccelMultiplier: %A" cam)

            Accel.CustomThreshold <- cat
            Accel.CustomMultiplier <- cam
            Accel.CustomDisabled <- false
    with
        | :? KeyNotFoundException as e -> Debug.WriteLine(sprintf "Not found: %s" e.Message)
        | :? FormatException as e -> Debug.WriteLine(sprintf "Parse error: %s" e.Message)

let private setPriorityOfProperty (): unit =
    try
        setPriority (prop.GetString "processPriority")
    with
        | :? KeyNotFoundException as e ->
            Debug.WriteLine(sprintf "Not found %s" e.Message)
            setDefaultPriority()
        | :? ArgumentException as e ->
            Debug.WriteLine(sprintf "Match error: %s" e.Message)
            setDefaultPriority()

let private setBooleanOfProperty (name: string): unit =
    try
        setBooleanOfName name (prop.GetBool name)
    with
        | :? KeyNotFoundException -> Debug.WriteLine(sprintf "Not found %s" name)
        | :? FormatException -> Debug.WriteLine(sprintf "Parse error: %s" name)
        | :? ArgumentException -> Debug.WriteLine(sprintf "Match error: %s" name)

let private setNumberOfProperty (name:string) (low:int) (up:int) =
    try
        let n = prop.GetInt name
        if n < low || n > up then
            Debug.WriteLine(sprintf "Nomber out of bounds: %s" name)
        else
            setNumberOfName name n
    with
        | :? KeyNotFoundException -> Debug.WriteLine(sprintf "Not fund: %s" name)
        | :? FormatException -> Debug.WriteLine(sprintf "Parse error: %s" name)
        | :? ArgumentException -> Debug.WriteLine(sprintf "Match error: %s" name)

let loadProperties (): unit =
    try
        prop.Load(PROP_NAME)

        Debug.WriteLine("Start load")

        setTriggerOfProperty()
        setAccelOfProperty()
        setCustomAccelOfProperty()
        setPriorityOfProperty()

        BooleanNames |> Array.iter (fun n -> setBooleanOfProperty n)

        let setNum = setNumberOfProperty
        setNum "pollTimeout" 50 500
        setNum "scrollLocktime" 150 500
        setNum "verticalThreshold" 0 500
        setNum "horizontalThreshold" 0 500
            
        setNum "wheelDelta" 10 500
        setNum "vWheelMove" 10 500
        setNum "hWheelMove" 10 500
    with
        | :? FileNotFoundException ->
            Debug.WriteLine("Properties file not found")
            setDefaultPriority()
        | e -> Debug.WriteLine(sprintf "load: %s" (e.ToString()))

let private isChangedProperties () =
    try
        prop.Load(PROP_NAME)

        let isChangedBoolean () =
            BooleanNames |>
            Array.map (fun n -> (prop.GetBool n) <> getBooleanOfName n) |>
            Array.contains true
        let isChangedNumber () =
            NumberNames |>
            Array.map (fun n -> (prop.GetInt n) <> getNumberOfName n) |>
            Array.contains true

        (prop.GetString "firstTrigger") <> getFirstTrigger().Name ||
        (prop.GetString "accelMultiplier") <> Accel.Multiplier.Name ||
        (prop.GetString "processPriority") <> getProcessPriority().Name ||
        isChangedBoolean() || isChangedNumber() 
    with
        | :? FileNotFoundException -> Debug.WriteLine("First write properties"); true
        | :? KeyNotFoundException as e -> Debug.WriteLine(sprintf "Not found %s" e.Message); true
        | e -> Debug.WriteLine(sprintf "isChanged: %s" (e.ToString())); true

let storeProperties () =
    try
        if not (isChangedProperties()) then
            Debug.WriteLine("Not changed properties")
        else
            Debug.WriteLine("saveConfig start")

            let add key value = prop.[key] <- value
            add "firstTrigger" (getFirstTrigger().Name)
            add "accelMultiplier" (Accel.Multiplier.Name)
            add "processPriority" (getProcessPriority().Name)

            BooleanNames |> Array.iter (fun n -> prop.SetBool(n, (getBooleanOfName n)))
            NumberNames |> Array.iter (fun n -> prop.SetInt(n, (getNumberOfName n)))

            Debug.WriteLine("Store start")
            prop.Store(PROP_NAME)
            Debug.WriteLine("Store end")
    with
        | e -> Debug.WriteLine(sprintf "store: %s" (e.ToString()))

let private createReloadPropertiesMenuItem () =
    let item = new ToolStripMenuItem("Reload Properties")

    item.Click.Add (fun _ ->
        loadProperties()
        resetMenuItems()
    )

    item

let private createContextMenuStrip (): ContextMenuStrip =
    let menu = new ContextMenuStrip()
    let add (item: ToolStripMenuItem) = menu.Items.Add(item) |> ignore
    add (createTriggerMenu())
    add (createAccelTableMenu())
    add (createPriorityMenu())
    add (createSetNumberMenu())
    add (createRealWheelModeMenu())
    addSeparator menu.Items

    add (createReloadPropertiesMenuItem())
    addSeparator menu.Items
    
    add (createCursorChangeMenuItem())
    add (createHorizontalScrollMenuItem())
    add (createReverseScrollMenuItem())
    add (createPassModeMenuItem())
    add (createInfoMenuItem())
    add (createExitMenuItem())

    resetMenuItems()

    menu

let setSystemTray (): unit =
    let menu = createContextMenuStrip()

    let ni = notifyIcon
    let icon = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("TrayIcon.ico")
    ni.Icon <- new Drawing.Icon(icon)
    ni.Text <- AppDef.PROGRAM_NAME_NET
    ni.Visible <- true
    ni.ContextMenuStrip <- menu
    ni.DoubleClick.Add (fun _ -> exitAction())
