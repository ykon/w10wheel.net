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
open System.Collections.Generic

open Mouse
open Keyboard

let private ICON_RUN_NAME = "TrayIcon-Run.ico"
let private ICON_STOP_NAME = "TrayIcon-Stop.ico"

let private selectedProperties: string ref = ref Properties.DEFAULT_DEF

let setSelectedProperties name =
    Volatile.Write(selectedProperties, name)

let private getSelectedProperties () =
    Volatile.Read(selectedProperties)

let private firstTrigger: Trigger ref = ref LRTrigger
let private pollTimeout = ref 200
let private processPriority = ref ProcessPriority.AboveNormal
let private sendMiddleClick = ref false

let private keyboardHook = ref false
let private targetVKCode = ref (Keyboard.getVKCode("VK_NONCONVERT"))

let private dpiCorrection = ref 1.00
let private dpiAware = ref false

let isDpiAware () =
    Volatile.Read(dpiAware)

let private getDpiCorrection () =
    Volatile.Read(dpiCorrection)

let isKeyboardHook () =
    Volatile.Read(keyboardHook)

let getTargetVKCode () =
    Volatile.Read(targetVKCode)

let isTriggerKey (ke: KeyboardEvent) =
    ke.VKCode = getTargetVKCode()

let isNoneTriggerKey () =
    getTargetVKCode() = 0

let isSendMiddleClick () =
    Volatile.Read(sendMiddleClick)

let private notifyIcon = new System.Windows.Forms.NotifyIcon()
let mutable private passModeMenuItem: ToolStripMenuItem = null

let private getTrayText b =
    sprintf "%s - %s" AppDef.PROGRAM_NAME_NET (if b then "Stopped" else "Runnable")

let private getIcon (name: string) =
    let stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
    new Drawing.Icon(stream)

let private getTrayIcon b =
    getIcon(if b then ICON_STOP_NAME else ICON_RUN_NAME)

let private changeNotifyIcon b =
    notifyIcon.Text <- (getTrayText b)
    notifyIcon.Icon <- (getTrayIcon b)

type private Pass() =
    [<VolatileField>] static let mutable mode = false

    static member Mode
        with get() = mode
        and set b =
            mode <- b
            changeNotifyIcon b
            passModeMenuItem.Checked <- b

    static member toggleMode () =
        Pass.Mode <- not mode

type VHAdjusterMethod =
    | Fixed
    | Switching

    member self.Name =
        Mouse.getUnionCaseName(self)

type private VHAdjuster() =
    [<VolatileField>] static let mutable mode = false
    [<VolatileField>] static let mutable _method: VHAdjusterMethod = Switching
    [<VolatileField>] static let mutable firstPreferVertical = true
    [<VolatileField>] static let mutable firstMinThreshold = 5
    [<VolatileField>] static let mutable switchingThreshold = 50

    static member Mode
        with get() = mode
        and set b = mode <- b
        
    static member Method
        with get() = _method
        and set m = _method <- m
        
    static member FirstPreferVertical
        with get() = firstPreferVertical
        and set b = firstPreferVertical <- b
        
    static member FirstMinThreshold
        with get() = firstMinThreshold
        and set n = firstMinThreshold <- n
        
    static member SwitchingThreshold
        with get() = switchingThreshold
        and set n = switchingThreshold <- n    

let isVhAdjusterMode () =
    VHAdjuster.Mode

let private getVhAdjusterMethod () =
    VHAdjuster.Method

let isVhAdjusterSwitching () =
    getVhAdjusterMethod() = Switching

let isFirstPreferVertical () =
    VHAdjuster.FirstPreferVertical

let getFirstMinThreshold () =
    VHAdjuster.FirstMinThreshold

let getSwitchingThreshold () =
    VHAdjuster.SwitchingThreshold

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
    | LeftEvent(_) -> isTrigger(LeftDragTrigger)
    | RightEvent(_) -> isTrigger(RightDragTrigger)
    | MiddleEvent(_) -> isTrigger(MiddleDragTrigger)
    | X1Event(_) -> isTrigger(X1DragTrigger)
    | X2Event(_) -> isTrigger(X2DragTrigger)
    | _ -> raise (ArgumentException())

let isSingleTrigger () =
    getFirstTrigger().IsSingle

let isDoubleTrigger () =
    getFirstTrigger().IsDouble

let isDragTrigger () =
    getFirstTrigger().IsDrag

let isNoneTrigger () =
    getFirstTrigger().IsNone

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

let mutable private initScroll: (unit -> unit) = (fun () -> ())

let setInitScroll (f: unit -> unit) =
    initScroll <- f

type private Scroll() =
    [<VolatileField>] static let mutable starting = false
    [<VolatileField>] static let mutable mode = false
    [<VolatileField>] static let mutable stime = 0u
    [<VolatileField>] static let mutable sx = 0
    [<VolatileField>] static let mutable sy = 0
    [<VolatileField>] static let mutable locktime = 200
    [<VolatileField>] static let mutable cursorChange = true
    [<VolatileField>] static let mutable reverse = false
    [<VolatileField>] static let mutable horizontal = true
    [<VolatileField>] static let mutable draggedLock = false
    [<VolatileField>] static let mutable swap = false
    [<VolatileField>] static let mutable releasedMode = false

    static let monitor = new Object()

    static let setStartPoint () =
        let scale = if isDpiAware() then 1.0 else getDpiCorrection()
        sx <- int ((double Cursor.Position.X) * scale)
        sy <- int ((double Cursor.Position.Y) * scale)

    static member Start (info: HookInfo) = lock monitor (fun () ->
        stime <- info.time
        setStartPoint()
        initScroll()

        if cursorChange && not (isDragTrigger()) then
            WinCursor.changeV()

        mode <- true
        starting <- false
    )

    static member Start (info: KHookInfo) = lock monitor (fun () ->
        stime <- info.time
        setStartPoint()
        initScroll()

        if cursorChange then
            WinCursor.changeV()

        mode <- true
        starting <- false
    )

    static member Exit () = lock monitor (fun () ->
        mode <- false
        releasedMode <- false

        if cursorChange then
            WinCursor.restore() |> ignore
    )

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
    static member DraggedLock
        with get() = draggedLock
        and set b = draggedLock <- b
    static member Swap
        with get() = swap
        and set b = swap <- b
    static member ReleasedMode
        with get() = releasedMode
        and set b = releasedMode <- b

    static member SetStarting () = lock monitor (fun () ->
        starting <- (not mode)
    )

    static member IsStarting with get() = starting

let isScrollMode () = Scroll.IsMode
let startScrollMode (info: HookInfo): unit = Scroll.Start info
let startScrollModeK (info: KHookInfo) = Scroll.Start info

let exitScrollMode (): unit = Scroll.Exit()
let checkExitScroll (time: uint32) = Scroll.CheckExit time
let getScrollStartPoint () = Scroll.StartPoint
let getScrollLockTime () = Scroll.LockTime
let isCursorChange () = Scroll.CursorChange
let isReverseScroll () = Scroll.Reverse
let isHorizontalScroll () = Scroll.Horizontal
let isDraggedLock () = Scroll.DraggedLock
let isSwapScroll () = Scroll.Swap

let isReleasedScrollMode () = Scroll.ReleasedMode
let isPressedScrollMode () = Scroll.IsMode && not (Scroll.ReleasedMode)
let setReleasedScrollMode () = Scroll.ReleasedMode <- true

let setStartingScrollMode () = Scroll.SetStarting()
let isStartingScrollMode () = Scroll.IsStarting

type LastFlags() =
    // R = Resent
    [<VolatileField>] static let mutable ldR = false
    [<VolatileField>] static let mutable rdR = false

    // P = Passed
    [<VolatileField>] static let mutable ldP = false
    [<VolatileField>] static let mutable rdP = false

    // S = Suppressed
    [<VolatileField>] static let mutable ldS = false
    [<VolatileField>] static let mutable rdS = false
    [<VolatileField>] static let mutable sdS = false

    static let kdS = Array.create 256 false

    static let getAndReset (flag: bool byref) =
        let res = flag
        flag <- false
        res

    static member SetResent (down: MouseEvent): unit =
        match down with
        | LeftDown(_) -> ldR <- true
        | RightDown(_) -> rdR <- true
        | _ -> raise (ArgumentException())

    static member GetAndReset_ResentDown (up: MouseEvent) =
        match up with
        | LeftUp(_) -> getAndReset(&ldR)
        | RightUp(_) -> getAndReset(&rdR)
        | _ -> raise (ArgumentException())

    static member SetPassed (down: MouseEvent): unit =
        match down with
        | LeftDown(_) -> ldP <- true
        | RightDown(_) -> rdP <- true
        | _ -> raise (ArgumentException())

    static member GetAndReset_PassedDown (up: MouseEvent) =
        match up with
        | LeftUp(_) -> getAndReset(&ldP)
        | RightUp(_) -> getAndReset(&rdP)
        | _ -> raise (ArgumentException())

    static member SetSuppressed (down: MouseEvent): unit =
        match down with
        | LeftDown(_) -> ldS <- true 
        | RightDown(_) -> rdS <- true
        | MiddleDown(_) | X1Down(_) | X2Down(_) -> sdS <- true
        | _ -> raise (ArgumentException())

    static member SetSuppressed (down: KeyboardEvent) =
        match down with
        | KeyDown(_) -> kdS.[down.VKCode] <- true
        | _ -> raise (ArgumentException())

    static member GetAndReset_SuppressedDown (up: MouseEvent) =
        match up with
        | LeftUp(_) -> getAndReset(&ldS)
        | RightUp(_) -> getAndReset(&rdS)
        | MiddleUp(_) | X1Up(_) | X2Up(_) -> getAndReset(&sdS)
        | _ -> raise (ArgumentException())

    static member GetAndReset_SuppressedDown (up: KeyboardEvent) =
        match up with
        | KeyUp(_) -> getAndReset(&kdS.[up.VKCode])
        | _ -> raise (ArgumentException())

    static member ResetLR (down: MouseEvent) =
        match down with
        | LeftDown(_) -> ldR <- false; ldS <- false; ldP <- false
        | RightDown(_) -> rdR <- false; rdS <- false; rdP <- false
        //| MiddleDown(_) | X1Down(_) | X2Down(_) -> sdS <- false
        | _ -> raise (ArgumentException())

    (*
    static member Reset (down: KeyboardEvent) =
        match down with
        | KeyDown(_) -> kdS.[down.VKCode] <- false
        | _ -> ()
    *)

let getPollTimeout () =
    Volatile.Read(pollTimeout)

let isPassMode () =
    Pass.Mode

let setPassMode b =
    Pass.Mode <- b

type HookInfo = WinAPI.MSLLHOOKSTRUCT
type KHookInfo = WinAPI.KBDLLHOOKSTRUCT

let private boolMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private triggerMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private accelMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private priorityMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private numberMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private keyboardMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private vhAdjusterMenuDict = new Dictionary<string, ToolStripMenuItem>()
let private dpiCorrectionMenuDict = new Dictionary<string, ToolStripMenuItem>()

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
    | "draggedLock" -> Scroll.DraggedLock
    | "swapScroll" -> Scroll.Swap
    | "sendMiddleClick" -> Volatile.Read(sendMiddleClick)
    | "keyboardHook" -> Volatile.Read(keyboardHook)
    | "vhAdjusterMode" -> VHAdjuster.Mode
    | "firstPreferVertical" -> VHAdjuster.FirstPreferVertical
    | "dpiAware" -> Volatile.Read(dpiAware)
    | "passMode" -> Pass.Mode
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
    | "draggedLock" -> Scroll.DraggedLock <- b
    | "swapScroll" -> Scroll.Swap <- b
    | "sendMiddleClick" -> Volatile.Write(sendMiddleClick, b)
    | "keyboardHook" -> Volatile.Write(keyboardHook, b)
    | "vhAdjusterMode" -> VHAdjuster.Mode <- b
    | "firstPreferVertical" -> VHAdjuster.FirstPreferVertical <- b
    | "dpiAware" -> Volatile.Write(dpiAware, b)
    | "passMode" -> Pass.Mode <- b
    | e -> raise (ArgumentException(e))

let private makeSetBooleanEvent (name: String) =
    fun (sender:obj) event ->
        let item = sender :?> ToolStripMenuItem
        let b = item.Checked
        setBooleanOfName name b

let private createBoolMenuItem vName mName enabled =
    let item = new ToolStripMenuItem(mName, null, makeSetBooleanEvent(vName))
    item.CheckOnClick <- true
    item.Enabled <- enabled
    boolMenuDict.[vName] <- item
    item

let private createBoolMenuItemS vName =
    createBoolMenuItem vName vName true

let private textToName (s: string): string =
    s.Split([|' '|]).[0]

let private uncheckAllItems (dict: Dictionary<string, ToolStripMenuItem>) =
    for KeyValue(name, item) in dict do
        item.CheckState <- CheckState.Unchecked

let private setMenuEnabled (dict:Dictionary<string, ToolStripMenuItem>) key enabled =
    if dict.ContainsKey(key) then
        dict.[key].Enabled <- enabled

let mutable private changeTrigger: (unit -> unit) = (fun () -> ())

let setChangeTrigger (f: unit -> unit) =
    changeTrigger <- f

let private setTrigger (text: string) =
    let res = Mouse.getTriggerOfStr text
    Debug.WriteLine(sprintf "setTrigger: %s" res.Name)
    Volatile.Write(firstTrigger, res)

    setMenuEnabled boolMenuDict "sendMiddleClick" res.IsSingle
    setMenuEnabled boolMenuDict "draggedLock" res.IsDrag

    changeTrigger()

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
    let menu = new ToolStripMenuItem("Trigger")
    let items = menu.DropDownItems
    let add name = items.Add(createTriggerMenuItem name) |> ignore

    add "LR (Left <<-->> Right)"
    add "Left (Left -->> Right)"
    add "Right (Right -->> Left)"
    addSeparator items

    add "Middle"
    add "X1"
    add "X2"
    addSeparator items

    add "LeftDrag"
    add "RightDrag"
    add "MiddleDrag"
    add "X1Drag"
    add "X2Drag"
    addSeparator items

    add "None"
    addSeparator items

    items.Add(createBoolMenuItem "sendMiddleClick" "Send MiddleClick" (isSingleTrigger())) |> ignore
    items.Add(createBoolMenuItem "draggedLock" "Dragged Lock" (isDragTrigger())) |> ignore

    menu

let private getOnOffText (b: bool) = if b then "ON" else "OFF"

let private createOnOffMenuItem (vname:string) (action: bool -> unit) =
    let item = new ToolStripMenuItem(getOnOffText(getBooleanOfName vname))
    item.CheckOnClick <- true
    boolMenuDict.[vname] <- item

    item.Click.Add (fun _ ->
        let b  = item.Checked
        item.Text <- getOnOffText b
        setBooleanOfName vname b
        action(b)
    )
    item

let private createOnOffMenuItemNA (vname:string) =
    createOnOffMenuItem vname (fun _ -> ())

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

    items.Add(createOnOffMenuItemNA "accelTable") |> ignore
    addSeparator items

    add "M5 (1.0 ... 4.8)"
    add "M6 (1.2 ... 5.8)"
    add "M7 (1.4 ... 6.7)"
    add "M8 (1.6 ... 7.7)"
    add "M9 (1.8 ... 8.7)"
    addSeparator items

    items.Add(createBoolMenuItem "customAccelTable" "Custom Table" (not Accel.CustomDisabled)) |> ignore
    
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
    | "firstMinThreshold" -> VHAdjuster.FirstMinThreshold
    | "switchingThreshold" -> VHAdjuster.SwitchingThreshold
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
    | "firstMinThreshold" -> VHAdjuster.FirstMinThreshold <- n
    | "switchingThreshold" -> VHAdjuster.SwitchingThreshold <- n
    | e -> raise (ArgumentException(e))

let private makeNumberText (name: string) (num: int) =
    sprintf "%s = %d" name num

let private createNumberMenuItem name low up =
    let item = new ToolStripMenuItem(name, null)
    numberMenuDict.[name] <- item

    item.Click.Add (fun _ ->
        let cur = getNumberOfName name
        let num = Dialog.openNumberInputBox name low up cur
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

    items.Add(createOnOffMenuItemNA "realWheelMode") |> ignore
    addSeparator items

    addNum "wheelDelta" 10 500
    addNum "vWheelMove" 10 500
    addNum "hWheelMove" 10 500
    addSeparator items
        
    addBool "quickFirst"
    addBool "quickTurn"
        
    menu

let private getVhAdjusterMethodOfName name =
    match name with
    | "Fixed" -> Fixed
    | "Switching" -> Switching
    | _ -> raise (ArgumentException())

let private setVhAdjusterMethod name =
    Debug.WriteLine(sprintf "setVhAdjusterMethod: %s" name)
    VHAdjuster.Method <- (getVhAdjusterMethodOfName name)

let private createVhAdjusterMenuItem name =
    let item = new ToolStripMenuItem(name, null)
    vhAdjusterMenuDict.[name] <- item

    item.Click.Add (fun _ ->
        if item.CheckState = CheckState.Unchecked then
            uncheckAllItems vhAdjusterMenuDict
            item.CheckState <- CheckState.Indeterminate
            setVhAdjusterMethod name
    )

    item

let private createVhAdjusterMenu () =
    let menu = new ToolStripMenuItem("VH Adjuster")
    let items = menu.DropDownItems
    let add name = items.Add(createVhAdjusterMenuItem name) |> ignore
    let addNum name low up = items.Add(createNumberMenuItem name low up) |> ignore
    let addBool name = items.Add(createBoolMenuItemS name) |> ignore

    items.Add(createOnOffMenuItemNA "vhAdjusterMode") |> ignore
    boolMenuDict.["vhAdjusterMode"].Enabled <- Scroll.Horizontal
    addSeparator items

    add "Fixed"
    add "Switching"
    addSeparator items

    addBool "firstPreferVertical"
    addNum "firstMinThreshold" 1 10
    addNum "switchingThreshold" 10 500

    menu

let private createDpiCorrectionMenuItem (scale: double) =
    let text = scale.ToString("F")
    let item = new ToolStripMenuItem(text, null)
    dpiCorrectionMenuDict.[text] <- item

    item.Click.Add (fun _ ->
        if item.CheckState = CheckState.Unchecked then
            uncheckAllItems dpiCorrectionMenuDict
            item.CheckState <- CheckState.Indeterminate
            Volatile.Write(dpiCorrection, scale)
    )

    item

let mutable private dpiCorrectionMenu: ToolStripMenuItem = null

let private createDpiCorrectionMenu () =
    let menu = new ToolStripMenuItem("DPI Correction")
    menu.Enabled <- not (isDpiAware())
    let items = menu.DropDownItems
    let add scale = items.Add(createDpiCorrectionMenuItem scale) |> ignore

    add 1.00
    add 1.25
    add 1.50
    add 1.75
    add 2.00

    dpiCorrectionMenu <- menu
    menu

let private setTargetVKCode name =
    Debug.WriteLine(sprintf "setTargetVKCode: %s" name)
    Volatile.Write(targetVKCode, Keyboard.getVKCode name)

let private createKeyboardMenuItem text =
    let item = new ToolStripMenuItem(text, null)
    let name = textToName text
    keyboardMenuDict.[name] <- item

    item.Click.Add (fun _ ->
        if item.CheckState = CheckState.Unchecked then
            uncheckAllItems keyboardMenuDict
            item.CheckState <- CheckState.Indeterminate
            setTargetVKCode name
    )

    item

let private createKeyboardMenu () =
    let menu = new ToolStripMenuItem("Keyboard")
    let items = menu.DropDownItems
    let add text = items.Add(createKeyboardMenuItem text) |> ignore

    items.Add(createOnOffMenuItem "keyboardHook" WinHook.setOrUnsetKeyboardHook) |> ignore
    addSeparator items

    add "VK_TAB (Tab)"
    add "VK_PAUSE (Pause)"
    add "VK_CAPITAL (Caps Lock)"
    add "VK_CONVERT (Henkan)"
    add "VK_NONCONVERT (Muhenkan)"
    add "VK_PRIOR (Page Up)"
    add "VK_NEXT (Page Down)"
    add "VK_END (End)"
    add "VK_HOME (Home)"
    add "VK_SNAPSHOT (Print Screen)"
    add "VK_INSERT (Insert)"
    add "VK_DELETE (Delete)"
    add "VK_LWIN (Left Windows)"
    add "VK_RWIN (Right Windows)"
    add "VK_APPS (Application)"
    add "VK_NUMLOCK (Number Lock)"
    add "VK_SCROLL (Scroll Lock)"
    add "VK_LSHIFT (Left Shift)"
    add "VK_RSHIFT (Right Shift)"
    add "VK_LCONTROL (Left Ctrl)"
    add "VK_RCONTROL (Right Ctrl)"
    add "VK_LMENU (Left Alt)"
    add "VK_RMENU (Right Alt)"
    addSeparator items

    add "None"

    menu

let private createCursorChangeMenuItem () =
    createBoolMenuItem "cursorChange" "Cursor Change" true

let private createHorizontalScrollMenuItem () =
    let item = createBoolMenuItem "horizontalScroll" "Horizontal Scroll" true
    item.Click.Add(fun _ ->
        boolMenuDict.["vhAdjusterMode"].Enabled <- item.Checked
    )
    item

let private createReverseScrollMenuItem () =
    createBoolMenuItem "reverseScroll" "Reverse Scroll (Flip)" true

let private createSwapScrollMenuItem () =
    createBoolMenuItem "swapScroll" "Swap Scroll (V.H)" true

let private createPassModeMenuItem () =
    let event = makeSetBooleanEvent "passMode"
    let item = new ToolStripMenuItem("Pass Mode", null, event)
    item.CheckOnClick <- true
    passModeMenuItem <- item
    item

let private createInfoMenuItem () =
    let item = new ToolStripMenuItem("Info")

    item.Click.Add (fun _ ->
        let msg = sprintf "Name: %s / Version: %s" AppDef.PROGRAM_NAME_NET AppDef.PROGRAM_VERSION
        MessageBox.Show(msg, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information) |> ignore
    )
    item

let exitAction () =
    notifyIcon.Visible <- false
    Application.Exit()

let private createExitMenuItem (): ToolStripMenuItem =
    let item = new ToolStripMenuItem("Exit", null, fun _ _ -> exitAction())
    item

let private setDefaultPriority () =
    Debug.WriteLine("setDefaultPriority")
    //ProcessPriority.setPriority(getProcessPriority())
    setPriority (getProcessPriority().Name)

let private setDefaultTrigger () =
    setTrigger(getFirstTrigger().Name)

let private NumberNames: string array =
    [|"pollTimeout"; "scrollLocktime";
      "verticalThreshold"; "horizontalThreshold";
      "wheelDelta"; "vWheelMove"; "hWheelMove";
      "firstMinThreshold"; "switchingThreshold"|]

let private BooleanNames: string array =
    [|"realWheelMode"; "cursorChange";
     "horizontalScroll"; "reverseScroll";
     "quickFirst"; "quickTurn";
     "accelTable"; "customAccelTable";
     "draggedLock"; "swapScroll";
     "sendMiddleClick"; "keyboardHook";
     "vhAdjusterMode"; "firstPreferVertical"|]

let private OnOffNames: string array =
    [|"realWheelMode"; "accelTable"; "keyboardHook"; "vhAdjusterMode"|]

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

let private resetKeyboardMenuItems () =
    for KeyValue(name, item) in keyboardMenuDict do
        item.CheckState <-
            if Keyboard.getVKCode name = getTargetVKCode() then
                CheckState.Indeterminate
            else
                CheckState.Unchecked

let private resetVhAdjusterMenuItems () =
    boolMenuDict.["vhAdjusterMode"].Enabled <- Scroll.Horizontal

    for KeyValue(name, item) in vhAdjusterMenuDict do
        item.CheckState <-
            if getVhAdjusterMethodOfName name = getVhAdjusterMethod() then
                CheckState.Indeterminate
            else
                CheckState.Unchecked

let private resetOnOffMenuItems () =
    OnOffNames |> Array.iter (fun name ->
        let item = boolMenuDict.[name]
        item.Text <- getOnOffText(getBooleanOfName name)
    )

let private resetDpiCorrectionMenuItems () =
    dpiCorrectionMenu.Enabled <- not (isDpiAware())

    for KeyValue(name, item) in dpiCorrectionMenuDict do
        item.CheckState <-
            if name = getDpiCorrection().ToString("F") then
                CheckState.Indeterminate
            else
                CheckState.Unchecked

let private resetMenuItems () =
    resetTriggerMenuItems()
    resetKeyboardMenuItems()
    resetAccelMenuItems()
    resetPriorityMenuItems()
    resetNumberMenuItems()
    resetBoolNumberMenuItems()
    resetVhAdjusterMenuItems()
    resetOnOffMenuItems()
    resetDpiCorrectionMenuItems()

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

let private setVKCodeOfProperty (): unit =
    try
        setTargetVKCode (prop.GetString "targetVKCode")
    with
        | :? KeyNotFoundException as e -> Debug.WriteLine(sprintf "Not found %s" e.Message)
        | :? ArgumentException as e -> Debug.WriteLine(sprintf "Match error %s" e.Message)

let private setVhAdjusterMethodOfProperty (): unit =
    try
        setVhAdjusterMethod (prop.GetString "vhAdjusterMethod")
    with
        | :? KeyNotFoundException as e -> Debug.WriteLine(sprintf "Not found %s" e.Message)
        | :? ArgumentException as e -> Debug.WriteLine(sprintf "Match error %s" e.Message)

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

let private setDpiCorrectionOfProperty () =
    try
        Volatile.Write(dpiCorrection, prop.GetDouble "dpiCorrection")
    with
        | :? KeyNotFoundException as e -> Debug.WriteLine(sprintf "Not found %s" e.Message)
        | :? ArgumentException as e -> Debug.WriteLine(sprintf "Match error %s" e.Message) 

let private setDpiAwareOfProperty () =
    try
        Volatile.Write(dpiAware, prop.GetBool "dpiAware")
    with
        | :? KeyNotFoundException as e -> Volatile.Write(dpiAware, false) 

let private getSelectedPropertiesPath () =
    Properties.getPath (getSelectedProperties())

let mutable private loaded = false

let loadProperties (): unit =
    loaded <- true
    try
        prop.Load(getSelectedPropertiesPath())

        Debug.WriteLine("Start load")

        setTriggerOfProperty()
        setAccelOfProperty()
        setCustomAccelOfProperty()
        setPriorityOfProperty()
        setVKCodeOfProperty()
        setVhAdjusterMethodOfProperty()
        
        setDpiCorrectionOfProperty()
        setDpiAwareOfProperty()

        BooleanNames |> Array.iter (fun n -> setBooleanOfProperty n)
        WinHook.setOrUnsetKeyboardHook (Volatile.Read(keyboardHook))

        let setNum = setNumberOfProperty
        setNum "pollTimeout" 50 500
        setNum "scrollLocktime" 150 500
        setNum "verticalThreshold" 0 500
        setNum "horizontalThreshold" 0 500
            
        setNum "wheelDelta" 10 500
        setNum "vWheelMove" 10 500
        setNum "hWheelMove" 10 500

        setNum "firstMinThreshold" 1 10
        setNum "switchingThreshold" 10 500
    with
        | :? FileNotFoundException ->
            Debug.WriteLine("Properties file not found")
            setDefaultPriority()
            setDefaultTrigger()
        | e -> Debug.WriteLine(sprintf "load: %s" (e.ToString()))

let private isChangedProperties () =
    try
        prop.Load(getSelectedPropertiesPath())

        let isChangedBoolean () =
            BooleanNames |>
            Array.map (fun n -> (prop.GetBool n) <> getBooleanOfName n) |>
            Array.contains true
        let isChangedNumber () =
            NumberNames |>
            Array.map (fun n -> (prop.GetInt n) <> getNumberOfName n) |>
            Array.contains true

        let check n v = prop.GetString(n) <> v
        check "firstTrigger" (getFirstTrigger().Name) ||
        check "accelMultiplier" (Accel.Multiplier.Name) ||
        check "processPriority" (getProcessPriority().Name) ||
        check "targetVKCode" (Keyboard.getName(getTargetVKCode())) ||
        check "vhAdjusterMethod" (getVhAdjusterMethod().Name) ||
        check "dpiCorrection" (getDpiCorrection().ToString("F")) ||
        isChangedBoolean() || isChangedNumber() 
    with
        | :? FileNotFoundException -> Debug.WriteLine("First write properties"); true
        | :? KeyNotFoundException as e -> Debug.WriteLine(sprintf "Not found %s" e.Message); true
        | e -> Debug.WriteLine(sprintf "isChanged: %s" (e.ToString())); true

let storeProperties () =
    try
        if not loaded || not (isChangedProperties()) then
            Debug.WriteLine("Not changed properties")
        else
            let set key value = prop.[key] <- value
            set "firstTrigger" (getFirstTrigger().Name)
            set "accelMultiplier" (Accel.Multiplier.Name)
            set "processPriority" (getProcessPriority().Name)
            set "targetVKCode" (Keyboard.getName (getTargetVKCode()))
            set "vhAdjusterMethod" (getVhAdjusterMethod().Name)

            prop.SetDouble("dpiCorrection", getDpiCorrection())

            BooleanNames |> Array.iter (fun n -> prop.SetBool(n, (getBooleanOfName n)))
            NumberNames |> Array.iter (fun n -> prop.SetInt(n, (getNumberOfName n)))

            prop.Store(getSelectedPropertiesPath())
    with
        | e -> Debug.WriteLine(sprintf "store: %s" (e.ToString()))

let private createReloadPropertiesMenuItem () =
    let item = new ToolStripMenuItem("Reload")

    item.Click.Add (fun _ ->
        loadProperties()
        resetMenuItems()
    )

    item

let private createSavePropertiesMenuItem () =
    let item = new ToolStripMenuItem("Save")
    item.Click.Add (fun _ -> storeProperties())
    item

let private createOpenDirMenuItem (dir: string) =
    let item = new ToolStripMenuItem("Open Dir")
    item.Click.Add(fun _ ->
        Process.Start(dir) |> ignore
    )
    item

let private DEFAULT_DEF = Properties.DEFAULT_DEF

let private isValidPropertiesName name =
    (name <> DEFAULT_DEF) && not (name.StartsWith("--"))

let private createAddPropertiesMenuItem () =
    let item = new ToolStripMenuItem("Add")
    item.Click.Add(fun _ ->
        let res = Dialog.openTextInputBox "Properties Name" "Add Properties"

        try
            res |> Option.iter (fun name ->
                if isValidPropertiesName name then
                    storeProperties()
                    Properties.copy (getSelectedProperties()) name
                    setSelectedProperties name
                else
                    Dialog.errorMessage ("Invalid Name: " + name) "Name Error"
            )
        with
            | e -> Dialog.errorMessageE e 
    )
    item

let private openYesNoMessage msg =
    let res = MessageBox.Show(msg, "Question", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
    res = DialogResult.Yes

let private setProperties name =
    if getSelectedProperties() <> name then
        Debug.WriteLine(sprintf "setProperties: %s" name)

        setSelectedProperties name
        loadProperties()
        resetMenuItems()

let private createDeletePropertiesMenuItem () =
    let item = new ToolStripMenuItem("Delete")
    let name = getSelectedProperties()
    item.Enabled <- (name <> DEFAULT_DEF)
    item.Click.Add(fun _ ->
        try
            if openYesNoMessage (sprintf "Delete the '%s' properties?" name) then
                Properties.delete name
                setProperties DEFAULT_DEF
        with
            | e -> Dialog.errorMessageE e
    )
    item

let private createPropertiesMenuItem (name: string) =
    let item = new ToolStripMenuItem(name)
    item.CheckState <-
        if name = getSelectedProperties() then
            CheckState.Indeterminate
        else
            CheckState.Unchecked

    item.Click.Add(fun _ ->
        storeProperties()
        setProperties name
    )
    item

let private createPropertiesMenu () =
    let menu = new ToolStripMenuItem("Properties")
    let items = menu.DropDownItems
    addSeparator items

    let addItem (menuItem: ToolStripMenuItem) = items.Add(menuItem) |> ignore
    let addDefault () = addItem (createPropertiesMenuItem DEFAULT_DEF)
    let add path = addItem (createPropertiesMenuItem (Properties.getUserDefName path))

    menu.DropDownOpening.Add(fun _ ->
        items.Clear()

        addItem (createReloadPropertiesMenuItem())
        addItem (createSavePropertiesMenuItem())
        addSeparator items

        addItem (createOpenDirMenuItem(Properties.USER_DIR))
        addItem (createAddPropertiesMenuItem())
        addItem (createDeletePropertiesMenuItem())
        addSeparator items

        addDefault()
        addSeparator items

        Properties.getPropFiles() |> Array.iter add
    )

    menu

let private createContextMenuStrip (): ContextMenuStrip =
    let menu = new ContextMenuStrip()
    let add (item: ToolStripMenuItem) = menu.Items.Add(item) |> ignore
    add (createTriggerMenu())
    add (createKeyboardMenu())
    addSeparator menu.Items

    add (createAccelTableMenu())
    add (createPriorityMenu())
    add (createSetNumberMenu())
    add (createRealWheelModeMenu())
    add (createVhAdjusterMenu())
    add (createDpiCorrectionMenu())
    addSeparator menu.Items

    add (createPropertiesMenu())
    addSeparator menu.Items
    
    add (createCursorChangeMenuItem())
    add (createHorizontalScrollMenuItem())
    add (createReverseScrollMenuItem())
    add (createSwapScrollMenuItem())
    add (createPassModeMenuItem())
    add (createInfoMenuItem())
    add (createExitMenuItem())

    resetMenuItems()

    menu

let mutable private contextMenu: ContextMenuStrip = null

let setSystemTray (): unit =
    let menu = createContextMenuStrip()

    let ni = notifyIcon
    ni.Icon <- getTrayIcon false
    ni.Text <- getTrayText false
    ni.Visible <- true
    ni.ContextMenuStrip <- menu
    ni.DoubleClick.Add (fun _ -> Pass.toggleMode())
