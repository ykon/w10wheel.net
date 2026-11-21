module Ctx

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open System.Threading
open System.Windows.Forms
open System.Reflection
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
    Volatile.Read(&selectedProperties.contents)

let private firstTrigger: Trigger ref = ref LRTrigger
let private pollTimeout = ref 200
let private processPriority = ref ProcessPriority.AboveNormal
let private sendMiddleClick = ref false

let private keyboardHook = ref false
let private targetVKCode = ref (Keyboard.getVKCode(DataID.VK_NONCONVERT))
let private uiLanguage = ref (Locale.getLanguage())

type MenuDict = Dictionary<string, ToolStripMenuItem>

let private boolMenuDict = new MenuDict()
let private triggerMenuDict = new MenuDict()
let private accelMenuDict = new MenuDict()
let private priorityMenuDict = new MenuDict()
let private languageMenuDict = new MenuDict()
let private numberMenuDict = new MenuDict()
let private keyboardMenuDict = new MenuDict()
let private vhAdjusterMenuDict = new MenuDict()

let isKeyboardHook () =
    Volatile.Read(&keyboardHook.contents)

let getTargetVKCode () =
    Volatile.Read(&targetVKCode.contents)

let isTriggerKey (ke: KeyboardEvent) =
    ke.VKCode = getTargetVKCode()

let isNoneTriggerKey () =
    getTargetVKCode() = 0

let isSendMiddleClick () =
    Volatile.Read(&sendMiddleClick.contents)

let private notifyIcon = new System.Windows.Forms.NotifyIcon()

let private isSeparator (item: ToolStripItem): bool =
    item :? ToolStripSeparator

let getUILanguage () =
    Volatile.Read(&uiLanguage.contents)

let convLang (msg: string) =
    Debug.WriteLine("convLang: " + msg)
    Locale.convLang (getUILanguage()) msg

type MenuData = { engText: string; id: string option }

(*
let private tagToEngText (item: ToolStripMenuItem): string =
    match item.Tag with
    | :? MenuData as tag -> tag.engText
    | _ -> raise (ArgumentException("Tag is not MenuTag."))
*)

let private tagToEngText (item: ToolStripMenuItem): string =
    match item.Tag with
    | :? string as engText -> engText
    | _ -> raise (ArgumentException("Tag is not string."))

let private getFirstWord (s: string): string =
    s.Split([|' '|]).[0]

let private isNumberMenuItem (item: ToolStripItem): bool =
     match item.Tag with
     | :? string as engText -> numberMenuDict.ContainsKey(getFirstWord engText)
     | _ -> false

let private resetMenuText (): unit =        
    let toList = fun (mc: ToolStripItemCollection) ->
        if mc.Count = 0 then
            List.empty
        else
            let array = Array.zeroCreate mc.Count
            mc.CopyTo(array, 0)
            List.ofArray array
            |> List.filter (not << isSeparator)
            |> List.filter (not << isNumberMenuItem)
            |> List.map (fun item -> item :?> ToolStripMenuItem)

    let rec loop (list: ToolStripMenuItem list): unit =
        match list with
        | [] -> ()
        | item :: rest ->
            // Not the items whose text is not translated (the names of the properties files)
            match item.Tag with
            | :? string as engText -> item.Text <- convLang engText
            | _ -> ()

            match (toList item.DropDownItems) with
            | [] -> ()
            | dropDownItems -> loop dropDownItems 

            loop rest
    
    let menu = notifyIcon.ContextMenuStrip
    loop (toList menu.Items)

let mutable private passModeMenuItem: ToolStripMenuItem = null

let private getTrayText b =
    sprintf "%s - %s" AppDef.PROGRAM_NAME_NET (convLang (if b then "Stopped" else "Runnable"))

let private getIcon (name: string) =
    use stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
    new Drawing.Icon(stream)

// Made once: a new icon for every switch of Pass Mode would not be released
let private trayIconRun = lazy (getIcon ICON_RUN_NAME)
let private trayIconStop = lazy (getIcon ICON_STOP_NAME)

let private getTrayIcon b =
    if b then trayIconStop.Value else trayIconRun.Value

let private changeNotifyIcon b =
    notifyIcon.Text <- (getTrayText b)
    notifyIcon.Icon <- (getTrayIcon b)

// Set to Scroll.SetPassMode after Scroll is defined (see below)
let mutable private setScrollPassMode: bool -> unit = (fun _ -> ())

// Set at the end of this file: resets the event handlers' state when Pass Mode is turned on
let mutable private onPassModeOn: unit -> unit = (fun () -> ())

// Pass Mode is switched under this lock, and the LR trigger checks Pass Mode and queues its
// resent down under it (see EventWaiter.fromTimeout). Held only briefly: SendInput must not
// be called under it (it waits for the low-level hooks, on the thread switching Pass Mode).
let passModeLock = new Object()

// Incremented under passModeLock whenever Pass Mode is turned on, and when the state is
// initialized (initState): the physical ups pass W10Wheel since. Recorded when the LR trigger
// starts waiting and when inputs are queued: the LR trigger neither resends nor starts scroll
// mode for a down received before, and Windows does not send the wheel events queued before,
// even if Pass Mode has been turned off again by then. (The resent downs and ups are checked
// by the hook: Windows.dropResent.)
let private passModeGeneration = ref 0
let getPassModeGeneration () = Volatile.Read(&passModeGeneration.contents)

let private newPassModeGeneration () =
    lock passModeLock (fun () ->
        Volatile.Write(passModeGeneration, passModeGeneration.Value + 1))

type private Pass() =
    [<VolatileField>] static let mutable mode = false

    static member Mode
        with get() = mode
        and set b =
            lock passModeLock (fun () ->
                if b then Volatile.Write(passModeGeneration, passModeGeneration.Value + 1)
                mode <- b)
            // Before waiting for the lock of Scroll (setScrollPassMode), which can dispatch
            // messages: switched off again meanwhile, it would reset the handlers' state after
            if b then onPassModeOn()
            setScrollPassMode b
            // As it is now, as Scroll.SetPassMode does: switched again while waiting for its lock,
            // the other call has shown it already
            changeNotifyIcon mode
            passModeMenuItem.Checked <- mode

    static member toggleMode () =
        Pass.Mode <- not mode

type private Exclusion() =
    [<VolatileField>] static let mutable avast = false

    static member Avast
        with get() = avast
        and set b =
            avast <- b

let isExcludeAvast () = Exclusion.Avast

// MouseWorks by Kensington (DefaultAccelThreshold, M5, M6, M7, M8, M9)
// http://www.nanayojapan.co.jp/support/help/tmh00017.htm

[<AbstractClass>]
type AccelMultiplier(name: string, dArray: double[]) =
    member self.Name = name
    member self.DArray = dArray

type M5() = inherit AccelMultiplier(DataID.M5, [|1.0; 1.3; 1.7; 2.0; 2.4; 2.7; 3.1; 3.4; 3.8; 4.1; 4.5; 4.8|])
type M6() = inherit AccelMultiplier(DataID.M6, [|1.2; 1.6; 2.0; 2.4; 2.8; 3.3; 3.7; 4.1; 4.5; 4.9; 5.4; 5.8|])
type M7() = inherit AccelMultiplier(DataID.M7, [|1.4; 1.8; 2.3; 2.8; 3.3; 3.8; 4.3; 4.8; 5.3; 5.8; 6.3; 6.7|])
type M8() = inherit AccelMultiplier(DataID.M8, [|1.6; 2.1; 2.7; 3.2; 3.8; 4.4; 4.9; 5.5; 6.0; 6.6; 7.2; 7.7|])
type M9() = inherit AccelMultiplier(DataID.M9, [|1.8; 2.4; 3.0; 3.6; 4.3; 4.9; 5.5; 6.2; 6.8; 7.4; 8.1; 8.7|])

let private getAccelMultiplierOfName name: AccelMultiplier =
    match name with
    | DataID.M5 -> (M5() :> AccelMultiplier)
    | DataID.M6 -> (M6() :> AccelMultiplier)
    | DataID.M7 -> (M7() :> AccelMultiplier)
    | DataID.M8 -> (M8() :> AccelMultiplier)
    | DataID.M9 -> (M9() :> AccelMultiplier)
    | e -> raise (ArgumentException(e))

let private DefaultAccelThreshold = [|1; 2; 3; 5; 7; 10; 14; 20; 30; 43; 63; 91|]

type private Accel() =
    [<VolatileField>] static let mutable table = true
    [<VolatileField>] static let mutable threshold: int[] = DefaultAccelThreshold
    [<VolatileField>] static let mutable multiplier: AccelMultiplier = M5() :> AccelMultiplier

    [<VolatileField>] static let mutable customDisabled = true
    [<VolatileField>] static let mutable customTable = false
    [<VolatileField>] static let mutable customThreshold: int[] = null
    [<VolatileField>] static let mutable customMultiplier: double[] = null

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
    Volatile.Read(&firstTrigger.contents)

let private getProcessPriority () =
    Volatile.Read(&processPriority.contents)

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
    [<VolatileField>] static let mutable horizontal = 75

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
    [<VolatileField>] static let mutable closed = false // set at exit: no more scroll mode
    [<VolatileField>] static let mutable passMode = false // Pass Mode is on: no scroll mode
    [<VolatileField>] static let mutable byKeyboard = false // started by the keyboard trigger

    // The LR trigger start was canceled before Start ran on the EventWaiter thread: Exit was
    // called while starting (e.g. both buttons released), so Start must not start afterwards
    [<VolatileField>] static let mutable startCanceled = false

    static let monitor = new Object()

    static let setStartPoint (x: int, y: int) =
        sx <- x
        sy <- y

    // Also called when not in scroll mode (e.g. initState, or the drag trigger clicked): only
    // what was done is undone (RawInput is registered in scroll mode only, see WinCursor.restore)
    static let exit () =
        if mode then RawInput.unregister() |> ignore

        mode <- false
        releasedMode <- false

        WinCursor.restore() |> ignore

    static let start (info: HookInfo) (generation: int option) =
        let sameGeneration =
            match generation with
            | Some g -> g = Volatile.Read(&passModeGeneration.contents)
            | None -> true

        // generation is given for the LR trigger only (see below)
        let canceled = generation.IsSome && startCanceled
        if generation.IsSome then startCanceled <- false

        // Not while in scroll mode: the handlers end it before, another trigger's included (for
        // the LR trigger, EventWaiter.waiterThread)
        if not closed && not passMode && sameGeneration && not canceled && not mode then
            // Without RawInput, the moves would be suppressed with no scroll: refused
            if RawInput.register() then
                stime <- info.time
                setStartPoint(info.pt.x, info.pt.y)
                initScroll()

                if cursorChange && not (isDragTrigger()) then
                    WinCursor.changeV()

                byKeyboard <- false
                mode <- true

            starting <- false
        else
            // Refused (exiting, Pass Mode or canceled): do not leave the LR trigger in the
            // starting state
            starting <- false

    static member Start (info: HookInfo) = lock monitor (fun () -> start info None)

    // For the LR trigger, started on the EventWaiter thread some time after the physical downs:
    // also refused if Pass Mode has been turned on since then (generation changed), even if it
    // has been turned off again. Checked under the lock that SetPassMode takes, so that turning
    // Pass Mode on after the check still leaves scroll mode.
    static member Start (info: HookInfo, generation: int) = lock monitor (fun () -> start info (Some generation))

    static member Start (info: KHookInfo) = lock monitor (fun () ->
        // A pending LR trigger start is superseded: cancel it, or it would start scroll mode
        // again after this one has ended
        if starting then startCanceled <- true

        if not closed && not passMode && not mode then
            if RawInput.register() then
                stime <- info.time
                setStartPoint(Cursor.Position.X, Cursor.Position.Y)
                initScroll()

                if cursorChange then
                    WinCursor.changeV()

                byKeyboard <- true
                mode <- true

            starting <- false
        else
            // Refused (exiting or Pass Mode): do not leave the LR trigger in the starting state
            starting <- false
    )

    static member Exit () = lock monitor (fun () ->
        // Exiting while the LR trigger start is pending (e.g. both buttons released before
        // Start ran): cancel it, or scroll mode would start afterwards with no button pressed
        if starting then
            startCanceled <- true
            starting <- false

        exit ()
    )

    // At exit: waits for a Start in progress (on the EventWaiter thread for LR triggers),
    // leaves scroll mode and refuses later Starts, so that the cursor is restored and
    // RawInput is not registered again afterwards.
    static member Close () = lock monitor (fun () ->
        closed <- true
        exit ()
    )

    // Pass Mode bypasses the event handlers, so scroll mode could not be left while it is on.
    // Turning it on leaves scroll mode and refuses Starts until it is turned off. Under the
    // lock, so that a Start in progress on the EventWaiter thread is not missed. Applies Pass
    // Mode as it is now: waiting for the lock on the UI thread can dispatch messages, e.g.
    // another --sendPassMode that switched it again (and applied it) meanwhile. Scroll mode is
    // left if this call turned it on (turnedOn), even if it is off again: the handlers' state
    // has been reset (onPassModeOn), the trigger could not end it.
    static member SetPassMode (turnedOn: bool) = lock monitor (fun () ->
        let b = Pass.Mode
        passMode <- b
        if b || turnedOn then
            // A pending LR trigger start is refused (Pass Mode generation): do not leave it
            // starting, the ups after Pass Mode is turned off would be taken for it
            starting <- false
            exit ()
    )

    static member CheckExit (time: uint32) =
        let dt = time - stime
        Debug.WriteLine(sprintf "scroll time: %d ms" dt)
        dt > (uint32 locktime)

    static member IsMode with get() = mode
    static member ByKeyboard with get() = byKeyboard
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

    // For the LR trigger, on the EventWaiter thread before the offer of the second down returns
    // (so before the hook handles the ups) and before Start, which resets it: set after Start,
    // it would remain. Start refuses while closed, in Pass Mode or in another generation (see
    // Start), so do not wait for it then.
    static member SetStarting (generation: int) = lock monitor (fun () ->
        let sameGeneration = generation = Volatile.Read(&passModeGeneration.contents)
        starting <- (not mode) && (not closed) && (not passMode) && sameGeneration
        if starting then startCanceled <- false
    )

    static member IsStarting with get() = starting

let isScrollMode () = Scroll.IsMode
let startScrollMode (info: HookInfo): unit = Scroll.Start info
let startScrollModeFor (generation: int) (info: HookInfo): unit = Scroll.Start(info, generation)
let startScrollModeK (info: KHookInfo) = Scroll.Start info

let exitScrollMode (): unit = Scroll.Exit()
let closeScrollMode (): unit = Scroll.Close()

// Pass.Mode (menu, tray icon and --sendPassMode) tells Scroll about Pass Mode
setScrollPassMode <- (fun b -> Scroll.SetPassMode b)

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

// Pressed scroll mode started by a mouse trigger: the ups of mouse triggers end only this one.
// The keyboard trigger can start scroll mode while their downs are handled (e.g. while the LR
// trigger start is pending); their ups are then suppressed like their downs.
let isPressedMouseScrollMode () = isPressedScrollMode() && not Scroll.ByKeyboard
let isKeyboardScrollMode () = Scroll.IsMode && Scroll.ByKeyboard
let isPressedKeyboardScrollMode () = isPressedScrollMode() && Scroll.ByKeyboard
let setReleasedScrollMode () = Scroll.ReleasedMode <- true

let setStartingScrollModeFor (generation: int) = Scroll.SetStarting(generation)
let isStartingScrollMode () = Scroll.IsStarting

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
    (isHorizontalScroll()) && VHAdjuster.Mode

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
    [<VolatileField>] static let mutable mdS = false
    [<VolatileField>] static let mutable x1S = false
    [<VolatileField>] static let mutable x2S = false

    static let kdS = Array.create 256 false
    // Keys whose down passed W10Wheel (to the application) and whose up has not come yet
    static let kdP = Array.create 256 false
    // Keys whose down was suppressed (kdS) when the state was initialized (Init) and whose up has
    // not come yet: their up alone is suppressed (the application did not get the down). Their
    // next down resets them (a repeat cannot be told from a new press, it is taken for one).
    static let kdL = Array.create 256 false

    static let getAndReset (flag: bool byref) =
        let res = flag
        flag <- false
        res

    static let isKey (vk: int) =
        vk >= 0 && vk < kdS.Length

    // Not the keys passed (kdP), whose ups pass (also in Pass Mode, see Dispatcher) and reset
    // them; reset, a key still pressed would lose it (e.g. pressed in Pass Mode: its repeats
    // would start scroll mode if it is the trigger, and its up would be suppressed). Whether a
    // key is still pressed cannot be told: GetAsyncKeyState may fail, and a key whose down was
    // suppressed is not pressed for it. A key whose up was missed passes its next down once.
    // The keys suppressed (kdS) are reset too, so that a key whose up was missed is not
    // suppressed again: only their up alone is (kdL), as the hook does for the buttons
    // (Windows.dropLoneUp).
    static member Init () =
        ldR <- false; rdR <- false
        ldP <- false; rdP <- false
        ldS <- false; rdS <- false; mdS <- false; x1S <- false; x2S <- false
        LastFlags.SuppressedKeysToLoneUp()

    // The keys suppressed (kdS) are suppressed for their up alone (kdL): a key whose up was
    // missed is not suppressed again (see Init, and KEventHandler.hookInstalled)
    static member SuppressedKeysToLoneUp () =
        for vk in 0 .. kdS.Length - 1 do
            if kdS.[vk] then kdL.[vk] <- true
        Array.fill kdS 0 (kdS.Length) false

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
        | MiddleDown(_) -> mdS <- true
        | X1Down(_) -> x1S <- true
        | X2Down(_) -> x2S <- true
        | _ -> raise (ArgumentException())

    static member SetSuppressed (down: KeyboardEvent) =
        match down with
        | KeyDown(_) -> if isKey down.VKCode then kdS.[down.VKCode] <- true
        | _ -> raise (ArgumentException())

    // The key's down was suppressed and its up has not come yet: its repeats are suppressed too
    static member IsSuppressedKey (vk: int): bool =
        isKey vk && kdS.[vk]

    // The keyboard trigger changed while the old key was pressed (its down was suppressed)
    static member SetSuppressedKey (vk: int) =
        if isKey vk then kdS.[vk] <- true

    static member SetPassedKey (vk: int) =
        if isKey vk then kdP.[vk] <- true

    // The key's down passed: its repeats and its up pass too, even if it is the trigger now
    static member IsPassedKey (vk: int): bool =
        isKey vk && kdP.[vk]

    static member GetAndReset_PassedKey (vk: int): bool =
        isKey vk && getAndReset(&kdP.[vk])

    // A key that does not repeat (Pause): its down is always a new press, its flags are reset
    // (it may send only a down, the flags of the previous press would remain). The flags of the
    // other keys are kept until their up (a repeat cannot be told from a new press for sure).
    static member NewKeyDown (vk: int) =
        if Keyboard.isNoRepeatKey vk then
            kdS.[vk] <- false
            kdP.[vk] <- false

    // A down of a key (kdL): its up is not alone any more
    static member ResetLoneUpKey (vk: int) =
        if isKey vk then kdL.[vk] <- false

    // The up of a key whose down was suppressed before Init (kdL), with no down since; resets it
    static member GetAndReset_LoneUpKey (vk: int): bool =
        isKey vk && getAndReset(&kdL.[vk])

    // The keyboard hook is installed again: a key whose up was missed while it was removed may
    // have been pressed since, its down passing (no hook): do not suppress its up
    static member ResetLoneUpKeys () =
        Array.fill kdL 0 kdL.Length false

    // The keyboard hook is removed: the ups will not be seen
    static member ResetKeys () =
        Array.fill kdS 0 kdS.Length false
        Array.fill kdP 0 kdP.Length false
        Array.fill kdL 0 kdL.Length false

    // The downs suppressed whose up has not come yet (left, right, middle, X1, X2); resets them
    static member TakeSuppressedDowns (): bool[] =
        let res = [| ldS; rdS; mdS; x1S; x2S |]
        ldS <- false; rdS <- false; mdS <- false; x1S <- false; x2S <- false
        res

    static member GetAndReset_SuppressedDown (up: MouseEvent) =
        match up with
        | LeftUp(_) -> getAndReset(&ldS)
        | RightUp(_) -> getAndReset(&rdS)
        | MiddleUp(_) -> getAndReset(&mdS)
        | X1Up(_) -> getAndReset(&x1S)
        | X2Up(_) -> getAndReset(&x2S)
        | _ -> raise (ArgumentException())

    static member GetAndReset_SuppressedDown (up: KeyboardEvent) =
        match up with
        | KeyUp(_) -> isKey up.VKCode && getAndReset(&kdS.[up.VKCode])
        | _ -> raise (ArgumentException())

    // The flags of the press of the button of an event (resent, suppressed, passed)
    static member ResetFor (me: MouseEvent) =
        match me with
        | LeftEvent(_) -> ldR <- false; ldS <- false; ldP <- false
        | RightEvent(_) -> rdR <- false; rdS <- false; rdP <- false
        | MiddleEvent(_) -> mdS <- false
        | X1Event(_) -> x1S <- false
        | X2Event(_) -> x2S <- false
        | _ -> ()

    (*
    static member Reset (down: KeyboardEvent) =
        match down with
        | KeyDown(_) -> kdS.[down.VKCode] <- false
        | _ -> ()
    *)

let getPollTimeout () =
    Volatile.Read(&pollTimeout.contents)

let isPassMode () =
    Pass.Mode

let setPassMode b =
    Pass.Mode <- b

type HookInfo = WinAPI.MSLLHOOKSTRUCT
type KHookInfo = WinAPI.KBDLLHOOKSTRUCT

let private createMenuItem (data: MenuData): ToolStripMenuItem =
    let item = new ToolStripMenuItem(convLang(data.engText))
    //item.Tag <- data
    item.Tag <- data.engText
    item

let private createMenuItem_NonID text: ToolStripMenuItem =
    createMenuItem {engText = text; id = None}

(*
let private createMenuItem (text: string): ToolStripMenuItem =
    __createMenuItem text text

let private createMenuItem_id (text: string) (id: string): ToolStripMenuItem =
    __createMenuItem text id
*)

(*
let private createMenuItem_event (text: String) (onClick: EventHandler): ToolStripMenuItem =
    //new ToolStripMenuItem(text, null, onClick)
    let item = createMenuItem text
    item.Click.AddHandler(onClick)
    item
*)

(*
let private tagToID (item: ToolStripMenuItem): string =
    match item.Tag with
    | :? MenuData as data ->
        match data.id with
        | Some id -> id
        | None -> raise (ArgumentException("id is None."))
    | _ -> raise (ArgumentException("Tag is not MenuTag."))
*)

let private getBooleanOfName (name: string): bool =
    match name with
    | DataID.realWheelMode -> RealWheel.Mode
    | DataID.cursorChange -> Scroll.CursorChange
    | DataID.horizontalScroll -> Scroll.Horizontal
    | DataID.reverseScroll -> Scroll.Reverse
    | DataID.quickFirst -> RealWheel.QuickFirst
    | DataID.quickTurn -> RealWheel.QuickTurn
    | DataID.accelTable -> Accel.Table
    | DataID.customAccelTable -> Accel.CustomTable
    | DataID.draggedLock -> Scroll.DraggedLock
    | DataID.swapScroll -> Scroll.Swap
    | DataID.sendMiddleClick -> Volatile.Read(&sendMiddleClick.contents)
    | DataID.keyboardHook -> Volatile.Read(&keyboardHook.contents)
    | DataID.vhAdjusterMode -> VHAdjuster.Mode
    | DataID.firstPreferVertical -> VHAdjuster.FirstPreferVertical
    | DataID.passMode -> Pass.Mode
    | DataID.excludeAvast -> Exclusion.Avast
    | e -> raise (ArgumentException(e))

let private setBooleanOfName (name:string) (b:bool) =
    Debug.WriteLine(sprintf "setBoolean: %s = %s" name (b.ToString()))
    match name with
    | DataID.realWheelMode -> RealWheel.Mode <- b
    | DataID.cursorChange -> Scroll.CursorChange <- b
    | DataID.horizontalScroll -> Scroll.Horizontal <- b
    | DataID.reverseScroll -> Scroll.Reverse <- b
    | DataID.quickFirst -> RealWheel.QuickFirst <- b
    | DataID.quickTurn -> RealWheel.QuickTurn <- b
    | DataID.accelTable -> Accel.Table <- b
    | DataID.customAccelTable -> Accel.CustomTable <- b
    | DataID.draggedLock -> Scroll.DraggedLock <- b
    | DataID.swapScroll -> Scroll.Swap <- b
    | DataID.sendMiddleClick -> Volatile.Write(sendMiddleClick, b)
    | DataID.keyboardHook -> Volatile.Write(keyboardHook, b)
    | DataID.vhAdjusterMode -> VHAdjuster.Mode <- b
    | DataID.firstPreferVertical -> VHAdjuster.FirstPreferVertical <- b
    | DataID.passMode -> Pass.Mode <- b
    | DataID.excludeAvast -> Exclusion.Avast <- b
    | e -> raise (ArgumentException(e))

(*
let private makeSetBooleanEvent (id: String): EventHandler =
    new EventHandler(
        fun (sender:obj) _ ->
            let item = sender :?> ToolStripMenuItem
            let b = item.Checked
            setBooleanOfName id b
    )
*)

let private makeSetBooleanEvent id =
    fun (sender: obj) (evt: EventArgs) ->
        let item = sender :?> ToolStripMenuItem
        let b = item.Checked
        setBooleanOfName id b

let private createBoolMenuItem (data: MenuData) enabled =
    let item = createMenuItem data
    item.Click.AddHandler(new EventHandler(makeSetBooleanEvent data.id.Value))
    item.CheckOnClick <- true
    item.Enabled <- enabled
    boolMenuDict.[data.id.Value] <- item
    item

let private createBoolMenuItemS name =
    createBoolMenuItem ({engText = name; id = Some(name)}) true

let private uncheckAllItems (dict: MenuDict) =
    for KeyValue(name, item) in dict do
        item.CheckState <- CheckState.Unchecked

let private setMenuEnabled (dict:MenuDict) key enabled =
    if dict.ContainsKey(key) then
        dict.[key].Enabled <- enabled

// Called with whether the trigger has changed (not on every properties load)
let mutable private changeTrigger: (bool -> unit) = (fun _ -> ())

let setChangeTrigger (f: bool -> unit) =
    changeTrigger <- f

let private setTrigger (text: string) =
    let res = Mouse.getTriggerOfStr text
    Debug.WriteLine("setTrigger: " + res.Name)
    let changed = res <> getFirstTrigger()
    Volatile.Write(firstTrigger, res)

    setMenuEnabled boolMenuDict DataID.sendMiddleClick res.IsSingle
    setMenuEnabled boolMenuDict DataID.draggedLock res.IsDrag

    changeTrigger changed

let private addClick (item: ToolStripMenuItem) f =
    item.Click.Add (fun e -> f (e))
    item

(*
let private addClickDictMenuItem (item: ToolStripMenuItem) dict f =
    Debug.WriteLine("addClickDictMenuItem")

    addClick item (fun e -> 
        if item.CheckState = CheckState.Unchecked then
            uncheckAllItems dict
            item.CheckState <- CheckState.Indeterminate
            f (e)
    )
*)

let private createDictMenuItem (data: MenuData) (dict: MenuDict) (setID: (string -> unit)): ToolStripMenuItem =
    let item = createMenuItem data
    let id = data.id.Value
    dict.[id] <- item

    addClick item (fun _ -> 
        if item.CheckState = CheckState.Unchecked then
            uncheckAllItems dict
            item.CheckState <- CheckState.Indeterminate
            setID(id)
    )

let private addSeparator (col: ToolStripItemCollection) =
    col.Add(new ToolStripSeparator()) |> ignore

let private createTriggerMenu () =
    let menu = createMenuItem_NonID "Trigger"
    let items = menu.DropDownItems
    let create = fun text ->
        let data = {engText = text; id = Some(getFirstWord text)}
        createDictMenuItem data triggerMenuDict setTrigger
    let add = fun text -> items.Add(create text) |> ignore

    add (DataID.LR + " (Left <<-->> Right)")
    add (DataID.Left + " (Left -->> Right)")
    add (DataID.Right + " (Right -->> Left)")
    addSeparator items

    add DataID.Middle
    add DataID.X1
    add DataID.X2
    addSeparator items

    add DataID.LeftDrag
    add DataID.RightDrag
    add DataID.MiddleDrag
    add DataID.X1Drag
    add DataID.X2Drag
    addSeparator items

    add DataID.None
    addSeparator items

    items.Add(createBoolMenuItem {engText = "Send MiddleClick"; id = Some(DataID.sendMiddleClick)} (isSingleTrigger())) |> ignore
    items.Add(createBoolMenuItem {engText = "Dragged Lock"; id = Some(DataID.draggedLock)} (isDragTrigger())) |> ignore

    menu

let private getOnOffText (b: bool) =
    //if b then "ON (-->> OFF)" else "OFF (-->> ON)"
    "ON / OFF"

let private createOnOffMenuItem (id:string) (action: bool -> unit) =
    //let item = new ToolStripMenuItem(getOnOffText(getBooleanOfName id))
    let item = createMenuItem {engText = getOnOffText(getBooleanOfName id); id = Some(id)}
    item.CheckOnClick <- true
    boolMenuDict.[id] <- item

    addClick item (fun _ ->
        let b  = item.Checked
        item.Text <- convLang(getOnOffText b)
        setBooleanOfName id b
        action(b)
    )

let private createOnOffMenuItemNA (id:string) =
    createOnOffMenuItem id (fun _ -> ())

let private setAccelMultiplier name =
    Debug.WriteLine("setAccelMultiplier " + name)
    Accel.Multiplier <- getAccelMultiplierOfName name

let private createAccelTableMenu () =
    let menu = createMenuItem_NonID "Accel Table"
    let items = menu.DropDownItems
    let create = fun text ->
        let data = {engText = text; id = Some(getFirstWord text)}
        createDictMenuItem data accelMenuDict setAccelMultiplier
    let add = fun text -> items.Add(create text) |> ignore

    items.Add(createOnOffMenuItemNA DataID.accelTable) |> ignore
    addSeparator items

    add (DataID.M5 + " (1.0 ... 4.8)")
    add (DataID.M6 + " (1.2 ... 5.8)")
    add (DataID.M7 + " (1.4 ... 6.7)")
    add (DataID.M8 + " (1.6 ... 7.7)")
    add (DataID.M9 + " (1.8 ... 8.7)")
    addSeparator items

    items.Add(createBoolMenuItem {engText = "Custom Table"; id = Some(DataID.customAccelTable)} (not Accel.CustomDisabled)) |> ignore
    
    menu

let private setPriority name =
    let p = ProcessPriority.getPriority name
    Debug.WriteLine("setPriority: " + p.Name)
    Volatile.Write(processPriority, p)
    ProcessPriority.setPriority p

let private createPriorityMenu () =
    let menu = createMenuItem_NonID "Priority"
    let create = fun name id ->
        let data = {engText = name; id = Some(id)}
        createDictMenuItem data priorityMenuDict setPriority
    let add = fun name id -> menu.DropDownItems.Add(create name id) |> ignore

    add "High" DataID.High
    add "Above Normal" DataID.AboveNormal
    add "Normal" DataID.Normal

    menu

let private getNumberOfName (name: string): int =
    match name with
    | DataID.pollTimeout -> Volatile.Read(&pollTimeout.contents)
    | DataID.scrollLocktime -> Scroll.LockTime
    | DataID.verticalThreshold -> Threshold.Vertical
    | DataID.horizontalThreshold -> Threshold.Horizontal
    | DataID.wheelDelta -> RealWheel.WheelDelta
    | DataID.vWheelMove -> RealWheel.VWheelMove
    | DataID.hWheelMove -> RealWheel.HWheelMove
    | DataID.firstMinThreshold -> VHAdjuster.FirstMinThreshold
    | DataID.switchingThreshold -> VHAdjuster.SwitchingThreshold
    | e -> raise (ArgumentException(e))

let private setNumberOfName (name: string) (n: int): unit =
    Debug.WriteLine(sprintf "setNumber: %s = %d" name n)
    match name with
    | DataID.pollTimeout -> Volatile.Write(pollTimeout, n)
    | DataID.scrollLocktime -> Scroll.LockTime <- n
    | DataID.verticalThreshold -> Threshold.Vertical <- n
    | DataID.horizontalThreshold -> Threshold.Horizontal <- n
    | DataID.wheelDelta -> RealWheel.WheelDelta <- n
    | DataID.vWheelMove -> RealWheel.VWheelMove <- n
    | DataID.hWheelMove -> RealWheel.HWheelMove <- n
    | DataID.firstMinThreshold -> VHAdjuster.FirstMinThreshold <- n
    | DataID.switchingThreshold -> VHAdjuster.SwitchingThreshold <- n
    | e -> raise (ArgumentException(e))

let private makeNumberText (name: string) (num: int) =
    sprintf "%s = %d" (convLang name) num

let private createNumberMenuItem name low up =
    let item = createMenuItem {engText = name; id = Some(name)}
    numberMenuDict.[name] <- item

    addClick item (fun _ ->
        let cur = getNumberOfName name
        let num = Dialog.openNumberInputBox (convLang name) (convLang "Set Number") low up cur
        match num with
        | Ok n ->
            setNumberOfName name n
            item.Text <- makeNumberText name n
        | Error s ->
            if s <> "" then
                Dialog.errorMessage (sprintf "%s: %s" (convLang "Invalid Number") s) (convLang "Error")
    )

let private createSetNumberMenu () =
    let menu = createMenuItem_NonID "Set Number"
    let add = fun name low up ->
        menu.DropDownItems.Add(createNumberMenuItem name low up) |> ignore

    add DataID.pollTimeout 50 500
    add DataID.scrollLocktime 150 500
    addSeparator menu.DropDownItems

    add DataID.verticalThreshold 0 500
    add DataID.horizontalThreshold 0 500

    menu

let private createRealWheelModeMenu () =
    let menu = createMenuItem_NonID "Real Wheel Mode"
    let items = menu.DropDownItems
    let addNum = fun name low up ->
        items.Add(createNumberMenuItem name low up) |> ignore
    let addBool = fun name ->
        items.Add(createBoolMenuItemS name) |> ignore

    items.Add(createOnOffMenuItemNA DataID.realWheelMode) |> ignore
    addSeparator items

    addNum DataID.wheelDelta 10 500
    addNum DataID.vWheelMove 10 500
    addNum DataID.hWheelMove 10 500
    addSeparator items
        
    addBool DataID.quickFirst
    addBool DataID.quickTurn
        
    menu

let private getVhAdjusterMethodOfName name =
    match name with
    | DataID.Fixed -> Fixed
    | DataID.Switching -> Switching
    | _ -> raise (ArgumentException())

let private setVhAdjusterMethod name =
    Debug.WriteLine("setVhAdjusterMethod: " + name)
    VHAdjuster.Method <- (getVhAdjusterMethodOfName name)

let private createVhAdjusterMenu () =
    let menu = createMenuItem_NonID "VH Adjuster"
    let items = menu.DropDownItems

    let create = fun text ->
        let data = {engText = text; id = Some(text)}
        createDictMenuItem data vhAdjusterMenuDict setVhAdjusterMethod
    let add = fun name -> items.Add(create name) |> ignore

    let addNum = fun name low up ->
        items.Add(createNumberMenuItem name low up) |> ignore
    let addBool = fun name ->
        items.Add(createBoolMenuItemS name) |> ignore

    items.Add(createOnOffMenuItemNA DataID.vhAdjusterMode) |> ignore
    boolMenuDict.[DataID.vhAdjusterMode].Enabled <- Scroll.Horizontal
    addSeparator items

    add DataID.Fixed
    add DataID.Switching
    addSeparator items

    addBool DataID.firstPreferVertical
    addNum DataID.firstMinThreshold 1 10
    addNum DataID.switchingThreshold 10 500

    menu

// Called with the old trigger key when the key changes (true), or when the keyboard hook is
// removed (false): see KEventHandler.changeTrigger
let mutable private changeKeyboardTrigger: int -> bool -> unit = (fun _ _ -> ())

let setChangeKeyboardTrigger f =
    changeKeyboardTrigger <- f

// Called when the keyboard hook is installed (again): see KEventHandler.hookInstalled
let mutable private keyboardHookInstalled: unit -> unit = (fun () -> ())

let setKeyboardHookInstalled f =
    keyboardHookInstalled <- f

let private setTargetVKCode name =
    Debug.WriteLine("setTargetVKCode: " + name)
    let old = getTargetVKCode()
    let code = Keyboard.getVKCode name
    Volatile.Write(targetVKCode, code)

    if code <> old then
        changeKeyboardTrigger old true

let private setKeyboardHookOf (b: bool) =
    match WinHook.setOrUnsetKeyboardHook b with
    | Some(err) ->
        // Not installed (the previous hook removed): off, as the menu shows
        Volatile.Write(keyboardHook, false)
        match boolMenuDict.TryGetValue DataID.keyboardHook with
        | true, item ->
            item.Checked <- false
            item.Text <- convLang(getOnOffText false)
        | _ -> ()

        changeKeyboardTrigger (getTargetVKCode()) false
        Dialog.errorMessage (sprintf "%s: %s" (convLang "Failed keyboard hook install") err) (convLang "Error")
    | None ->
        if b then
            // Installed again on every load (e.g. Reload after Windows removed it): ups may have
            // been missed
            keyboardHookInstalled ()
        else
            changeKeyboardTrigger (getTargetVKCode()) false



let private createKeyboardMenu () =
    let menu = createMenuItem_NonID "Keyboard"
    let items = menu.DropDownItems

    let create = fun text ->
        let data = {engText = text; id = Some(getFirstWord(text))}
        createDictMenuItem data keyboardMenuDict setTargetVKCode
    let add = fun text -> items.Add(create text) |> ignore

    items.Add(createOnOffMenuItem DataID.keyboardHook setKeyboardHookOf) |> ignore
    addSeparator items

    add (DataID.VK_TAB + " (Tab)")
    add (DataID.VK_PAUSE + " (Pause)")
    add (DataID.VK_CAPITAL + " (Caps Lock)")
    add (DataID.VK_CONVERT + " (Henkan)")
    add (DataID.VK_NONCONVERT + " (Muhenkan)")
    add (DataID.VK_PRIOR + " (Page Up)")
    add (DataID.VK_NEXT + " (Page Down)")
    add (DataID.VK_END + " (End)")
    add (DataID.VK_HOME + " (Home)")
    add (DataID.VK_SNAPSHOT + " (Print Screen)")
    add (DataID.VK_INSERT + " (Insert)")
    add (DataID.VK_DELETE + " (Delete)")
    add (DataID.VK_LWIN + " (Left Windows)")
    add (DataID.VK_RWIN + " (Right Windows)")
    add (DataID.VK_APPS + " (Application)")
    add (DataID.VK_NUMLOCK + " (Number Lock)")
    add (DataID.VK_SCROLL + " (Scroll Lock)")
    add (DataID.VK_LSHIFT + " (Left Shift)")
    add (DataID.VK_RSHIFT + " (Right Shift)")
    add (DataID.VK_LCONTROL + " (Left Ctrl)")
    add (DataID.VK_RCONTROL + " (Right Ctrl)")
    add (DataID.VK_LMENU + " (Left Alt)")
    add (DataID.VK_RMENU + " (Right Alt)")

    addSeparator items
    add DataID.None

    menu

let private createCursorChangeMenuItem () =
    createBoolMenuItem {engText = "Cursor Change"; id = Some(DataID.cursorChange)} true

let private createHorizontalScrollMenuItem () =
    let item = createBoolMenuItem {engText = "Horizontal Scroll"; id = Some(DataID.horizontalScroll)} true
    addClick item (fun _ ->
        boolMenuDict.[DataID.vhAdjusterMode].Enabled <- item.Checked
    )

let private createReverseScrollMenuItem () =
    createBoolMenuItem {engText = "Reverse Scroll (Flip)"; id = Some(DataID.reverseScroll)} true

let private createSwapScrollMenuItem () =
    createBoolMenuItem {engText = "Swap Scroll (V.H)"; id = Some(DataID.swapScroll)} true

let private createPassModeMenuItem () =
    let id = DataID.passMode
    let item = createMenuItem {engText = "Pass Mode"; id = Some(id)}
    item.Click.AddHandler(new EventHandler(makeSetBooleanEvent id))
    item.CheckOnClick <- true
    passModeMenuItem <- item
    item

let private createInfoMenuItem () =
    let item = createMenuItem_NonID "Info"

    addClick item (fun _ ->
        let msg = sprintf "%s: %s / %s: %s" (convLang("Name")) AppDef.PROGRAM_NAME_NET (convLang("Version")) AppDef.PROGRAM_VERSION
        MessageBox.Show(msg, convLang("Info"), MessageBoxButtons.OK, MessageBoxIcon.Information) |> ignore
    )

// Before exiting without the message loop (Environment.Exit): the icon would remain in the tray
let hideSystemTray () =
    notifyIcon.Visible <- false

let exitAction () =
    notifyIcon.Visible <- false
    Application.Exit()

let private createExitMenuItem (): ToolStripMenuItem =
    let item = createMenuItem_NonID "Exit"
    item.Click.AddHandler(new EventHandler(fun _ _ -> exitAction()))
    item

let private NumberNames: string[] =
    [|DataID.pollTimeout; DataID.scrollLocktime;
      DataID.verticalThreshold; DataID.horizontalThreshold;
      DataID.wheelDelta; DataID.vWheelMove; DataID.hWheelMove;
      DataID.firstMinThreshold; DataID.switchingThreshold|]

let private BooleanNames: string[] =
    [|DataID.realWheelMode; DataID.cursorChange;
     DataID.horizontalScroll; DataID.reverseScroll;
     DataID.quickFirst; DataID.quickTurn;
     DataID.accelTable; DataID.customAccelTable;
     DataID.draggedLock; DataID.swapScroll;
     DataID.sendMiddleClick; DataID.keyboardHook;
     DataID.vhAdjusterMode; DataID.firstPreferVertical;
     DataID.excludeAvast|]

let private OnOffNames: string[] =
    [|DataID.realWheelMode; DataID.accelTable; DataID.keyboardHook; DataID.vhAdjusterMode|]

let private resetDictMenuItems (dict: MenuDict) pred =
    Debug.WriteLine("resetDictMenuItems")
    for KeyValue(id, item) in dict do
        item.CheckState <-
            if pred id then
                CheckState.Indeterminate
            else
                CheckState.Unchecked

let private resetTriggerMenuItems () =
    resetDictMenuItems triggerMenuDict
        (fun id -> Mouse.getTriggerOfStr id = getFirstTrigger())

let private resetAccelMenuItems () =
    resetDictMenuItems accelMenuDict
        (fun id -> id = Accel.Multiplier.Name)

let private resetPriorityMenuItems () =
    resetDictMenuItems priorityMenuDict
        (fun id -> ProcessPriority.getPriority id = getProcessPriority())

let private resetLanguageMenuItems () =
    resetDictMenuItems languageMenuDict
        (fun id -> id = getUILanguage())

let private resetKeyboardMenuItems () =
    resetDictMenuItems keyboardMenuDict
        (fun id -> Keyboard.getVKCode id = getTargetVKCode())

let private resetVhAdjusterMenuItems () =
    boolMenuDict.[DataID.vhAdjusterMode].Enabled <- Scroll.Horizontal
    resetDictMenuItems vhAdjusterMenuDict
        (fun id -> getVhAdjusterMethodOfName id = getVhAdjusterMethod())

let private resetNumberMenuItems () =
    for KeyValue(id, item) in numberMenuDict do
        let num = getNumberOfName id
        item.Text <- makeNumberText id num

let private isCreatedMenuItems (): bool =
    notifyIcon.ContextMenuStrip <> null

let private setUILanguage lang =
    Debug.WriteLine("setUILanguage: " + lang)
    let reset = lang <> getUILanguage()
    Volatile.Write(uiLanguage, lang)

    if reset && isCreatedMenuItems() then
        resetMenuText()
        resetNumberMenuItems()
        notifyIcon.Text <- getTrayText Pass.Mode

let private createLanguageMenu () =
    let menu = createMenuItem_NonID "Language"
    let create = fun text id ->
        createDictMenuItem {engText = text; id = Some(id)} languageMenuDict setUILanguage
    let add = fun text id ->
        menu.DropDownItems.Add(create text id) |> ignore

    add "English" DataID.English
    add "Japanese" DataID.Japanese

    menu 

let private resetBoolNumberMenuItems () =
    for KeyValue(name, item) in boolMenuDict do
        item.Checked <- getBooleanOfName name

let private resetOnOffMenuItems () =
    OnOffNames |> Array.iter (fun name ->
        let item = boolMenuDict.[name]
        item.Text <- convLang(getOnOffText(getBooleanOfName name))
    )

// Reload or switching properties can add or remove the custom table
let private resetCustomAccelMenuItem () =
    match boolMenuDict.TryGetValue DataID.customAccelTable with
    | true, item -> item.Enabled <- not Accel.CustomDisabled
    | _ -> ()

let private resetAllMenuItems () =
    Debug.WriteLine("resetAllMenuItems")
    resetTriggerMenuItems()
    resetKeyboardMenuItems()
    resetAccelMenuItems()
    resetPriorityMenuItems()
    resetLanguageMenuItems()
    resetNumberMenuItems()
    resetBoolNumberMenuItems()
    resetCustomAccelMenuItem()
    resetVhAdjusterMenuItems()
    resetOnOffMenuItems()

let private prop = Properties.Properties()

// The settings before the first load (the default values), written as in a properties file
let private defaultProp = Properties.Properties()

// Write the settings to p (the custom accel table is not written: it is only read)
let private setSettings (p: Properties.Properties) =
    let set = fun key value -> p.[key] <- value

    set DataID.firstTrigger (getFirstTrigger().Name)
    set DataID.accelMultiplier (Accel.Multiplier.Name)
    set DataID.processPriority (getProcessPriority().Name)
    set DataID.targetVKCode (Keyboard.getName (getTargetVKCode()))
    set DataID.vhAdjusterMethod (getVhAdjusterMethod().Name)
    set DataID.uiLanguage (getUILanguage())

    BooleanNames |> Array.iter (fun n -> p.SetBool(n, (getBooleanOfName n)))
    NumberNames |> Array.iter (fun n -> p.SetInt(n, (getNumberOfName n)))

let private isInvalidValue (e: exn) =
    e :? KeyNotFoundException || e :? FormatException || e :? OverflowException || e :? ArgumentException

// Applies the value of name in prop. If it is missing or invalid, the default value is
// applied instead: otherwise the value of the previous file would be kept (Reload,
// switching properties).
let private setOrDefault (name: string) (apply: Properties.Properties -> unit) =
    try
        apply prop
    with
        | e when isInvalidValue e ->
            Debug.WriteLine(sprintf "Invalid value: %s (%s): use default" name e.Message)
            apply defaultProp

let private setStringOfProperty name setFunc =
    setOrDefault name (fun p -> setFunc(p.GetString(name)))

let private setTriggerOfProperty (): unit =
    setStringOfProperty DataID.firstTrigger setTrigger

let private setAccelOfProperty (): unit =
    setStringOfProperty DataID.accelMultiplier setAccelMultiplier

let private setCustomAccelOfProperty (): unit =
    // Without a valid custom table in this file, do not keep the table of the previous file
    Accel.CustomDisabled <- true

    try
        let cat = prop.GetIntArray(DataID.customAccelThreshold)
        let cam = prop.GetDoubleArray(DataID.customAccelMultiplier)

        // The thresholds are looked up in ascending order (Windows.getNearestIndex), and a
        // multiplier that is not a number (NaN, Infinity: parsed without error) would make the
        // delta invalid
        let isAscending = cat |> Array.pairwise |> Array.forall (fun (a, b) -> a < b)
        let isValidThreshold = cat |> Array.forall (fun n -> n >= 0) && isAscending
        let isValidMultiplier = cam |> Array.forall (fun d -> Double.IsFinite(d) && d >= 0.0)

        if cat.Length <> 0 && cat.Length = cam.Length && isValidThreshold && isValidMultiplier then
            Debug.WriteLine(sprintf "customAccelThreshold: %A" cat)
            Debug.WriteLine(sprintf "customAccelMultiplier: %A" cam)

            Accel.CustomThreshold <- cat
            Accel.CustomMultiplier <- cam
            Accel.CustomDisabled <- false
    with
        | :? KeyNotFoundException as e -> Debug.WriteLine("Not found: " + e.Message)
        | :? FormatException as e -> Debug.WriteLine("Parse error: " + e.Message)
        | :? OverflowException as e -> Debug.WriteLine("Overflow: " + e.Message)

let private setPriorityOfProperty (): unit =
    setStringOfProperty DataID.processPriority setPriority

let private setVKCodeOfProperty (): unit =
    setStringOfProperty DataID.targetVKCode setTargetVKCode

let private setVhAdjusterMethodOfProperty (): unit =
    setStringOfProperty DataID.vhAdjusterMethod setVhAdjusterMethod

let private setUILanguageOfProperty (): unit =
    setStringOfProperty DataID.uiLanguage (fun lang ->
        // Only the languages of the Language menu
        if lang <> DataID.English && lang <> DataID.Japanese then
            raise (ArgumentException("Unknown language: " + lang))

        setUILanguage lang)

let private setBooleanOfProperty (name: string): unit =
    setOrDefault name (fun p -> setBooleanOfName name (p.GetBool name))

let private setNumberOfProperty (name:string) (low:int) (up:int) =
    setOrDefault name (fun p ->
        let n = p.GetInt name
        Debug.WriteLine(sprintf "setNumberOfProperty: %s: %d" name n)
        if n < low || n > up then
            raise (ArgumentOutOfRangeException(name, "Number out of bounds"))

        setNumberOfName name n)

let private getSelectedPropertiesPath () =
    Properties.getPath (getSelectedProperties())

let mutable private loaded = false
// The default values are kept (defaultProp) once, before the first load: not after a load that
// failed (loaded is false again), the settings are not the default values then
let mutable private defaultSaved = false

let loadPropertiesFileOnly (): unit =
    try
        prop.Load(getSelectedPropertiesPath(), true)
    with
        | _ -> ()

let convLangWithProp (msg: string) =
    let lang = prop.GetPropertyOption(DataID.uiLanguage) |> Option.defaultValue (getUILanguage())
    Debug.WriteLine((sprintf "convLangWithProp: lang:[%s], msg:[%s]" lang msg))
    Locale.convLang lang msg

let loadProperties (update:bool): unit =
    if not defaultSaved then
        setSettings defaultProp
        defaultSaved <- true

    try
        // Not loaded if the file could not be read (e.g. locked): the settings are not stored
        // (storeProperties) until a load succeeds, or they would overwrite the file (with the
        // default values, or those of another properties file switched from)
        loaded <- false
        try
            prop.Load(getSelectedPropertiesPath(), update)
        with
            // All the settings have their default values (see setOrDefault)
            | :? FileNotFoundException -> Debug.WriteLine("Properties file not found")
        loaded <- true

        Debug.WriteLine("Start load")

        setTriggerOfProperty()
        setAccelOfProperty()
        setCustomAccelOfProperty()
        setPriorityOfProperty()
        setVKCodeOfProperty()
        setVhAdjusterMethodOfProperty()
        setUILanguageOfProperty()

        BooleanNames |> Array.iter (fun n -> setBooleanOfProperty n)
        setKeyboardHookOf (Volatile.Read(&keyboardHook.contents))

        Debug.WriteLine("setNumberOfProperties")
        let setNum = setNumberOfProperty
        setNum DataID.pollTimeout 50 500
        setNum DataID.scrollLocktime 150 500
        setNum DataID.verticalThreshold 0 500
        setNum DataID.horizontalThreshold 0 500

        setNum DataID.wheelDelta 10 500
        setNum DataID.vWheelMove 10 500
        setNum DataID.hWheelMove 10 500

        setNum DataID.firstMinThreshold 1 10
        setNum DataID.switchingThreshold 10 500
    with
        | e -> Debug.WriteLine("load: " + (e.ToString()))

// Whether the settings differ from the properties of the file (p)
let private isChangedProperties (p: Properties.Properties) =
    try
        let isChangedBoolean () =
            BooleanNames |>
            Array.map (fun n -> (p.GetBool n) <> getBooleanOfName n) |>
            Array.contains true
        let isChangedNumber () =
            NumberNames |>
            Array.map (fun n -> (p.GetInt n) <> getNumberOfName n) |>
            Array.contains true

        let check = fun n v -> p.GetString(n) <> v

        check DataID.firstTrigger (getFirstTrigger().Name) ||
        check DataID.accelMultiplier (Accel.Multiplier.Name) ||
        check DataID.processPriority (getProcessPriority().Name) ||
        check DataID.targetVKCode (Keyboard.getName(getTargetVKCode())) ||
        check DataID.vhAdjusterMethod (getVhAdjusterMethod().Name) ||
        check DataID.uiLanguage (getUILanguage()) ||
        isChangedBoolean() || isChangedNumber() 
    with
        | :? KeyNotFoundException as e -> Debug.WriteLine("Not found: " + e.Message); true
        | e -> Debug.WriteLine("isChanged: " + (e.ToString())); true

// The settings are stored in the file as it is now (read anew): its keys that are not settings
// (e.g. customAccelThreshold) are kept as they are, and those removed from it since it was
// loaded are not stored again. Not stored if it cannot be read (it could not be written either).
let storeProperties () =
    try
        if not loaded then
            Debug.WriteLine("Not loaded properties")
        else
            let path = getSelectedPropertiesPath()
            let file = Properties.Properties()
            let found =
                try
                    file.Load(path, true)
                    true
                with
                    | :? FileNotFoundException -> Debug.WriteLine("First write properties"); false

            if found && not (isChangedProperties file) then
                Debug.WriteLine("Not changed properties")
            else
                setSettings file
                file.Store(path)
    with
        | e -> Debug.WriteLine("store: " + (e.ToString()))

let reloadProperties () =
    prop.Clear()
    loadProperties true
    resetAllMenuItems()

let private createReloadPropertiesMenuItem () =
    let item = createMenuItem_NonID "Reload"
    addClick item (fun _ -> reloadProperties ())

let private createSavePropertiesMenuItem () =
    let item = createMenuItem_NonID "Save"
    addClick item (fun _ -> storeProperties())

let private createOpenDirMenuItem (dir: string) =
    let item = createMenuItem_NonID "Open Dir"
    addClick item (fun _ -> Process.Start(ProcessStartInfo(dir, UseShellExecute = true)) |> ignore)

let private DEFAULT_DEF = Properties.DEFAULT_DEF

// A part of a file name (see Properties.getPath), not Default whatever its case (a name only
// differing in case would be confused with it), nor a command (--)
let private isValidPropertiesName (name: string) =
    not (String.IsNullOrWhiteSpace(name)) && name.Trim() = name &&
    not (String.Equals(name, DEFAULT_DEF, StringComparison.OrdinalIgnoreCase)) &&
    not (name.StartsWith("--")) &&
    name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0

let private createAddPropertiesMenuItem () =
    let item = createMenuItem_NonID "Add"
    addClick item (fun _ ->
        let res = Dialog.openTextInputBox (convLang "Properties Name") (convLang "Add Properties")

        try
            res |> Option.iter (fun name ->
                if isValidPropertiesName name then
                    storeProperties()
                    Properties.copy (getSelectedProperties()) name
                    setSelectedProperties name
                else
                    Dialog.errorMessage (sprintf "%s: %s" (convLang "Invalid Name") name) (convLang "Name Error")
            )
        with
            | e -> Dialog.errorMessageE e 
    )

let private openYesNoMessage msg =
    let res = MessageBox.Show(msg, (convLang "Question"), MessageBoxButtons.YesNo, MessageBoxIcon.Information)
    res = DialogResult.Yes

let private setProperties name =
    if getSelectedProperties() <> name then
        Debug.WriteLine("setProperties: " + name)

        setSelectedProperties name
        // Do not keep the keys of the previous file (they would be stored in this file)
        prop.Clear()
        loadProperties true
        resetAllMenuItems()

let private createDeletePropertiesMenuItem () =
    let item = createMenuItem_NonID "Delete"
    let name = getSelectedProperties()
    item.Enabled <- (name <> DEFAULT_DEF)
    addClick item (fun _ ->
        try
            if openYesNoMessage (sprintf "%s: %s" (convLang "Delete properties") name) then
                Properties.delete name
                setProperties DEFAULT_DEF
        with
            | e -> Dialog.errorMessageE e
    )

let private createPropertiesMenuItem (name: string) =
    // Only Default is translated: the other names are the user's (e.g. "Middle" is not "中央")
    let item =
        if name = DEFAULT_DEF then createMenuItem_NonID name else new ToolStripMenuItem(name)
    item.CheckState <-
        if name = getSelectedProperties() then
            CheckState.Indeterminate
        else
            CheckState.Unchecked

    addClick item (fun _ ->
        storeProperties()
        setProperties name
    )

let private createPropertiesMenu () =
    let menu = createMenuItem_NonID "Properties"
    let items = menu.DropDownItems
    addSeparator items

    let addItem = fun (menuItem: ToolStripMenuItem) -> items.Add(menuItem) |> ignore
    let addDefault = fun () -> addItem (createPropertiesMenuItem DEFAULT_DEF)
    let add = fun path -> addItem (createPropertiesMenuItem (Properties.getUserDefName path))

    menu.DropDownOpening.Add(fun _ ->
        // The items made the last time are released (Clear does not dispose them)
        let old = Array.init items.Count (fun i -> items.[i])
        items.Clear()
        old |> Array.iter (fun item -> item.Dispose())

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
    let add = fun (item: ToolStripMenuItem) -> menu.Items.Add(item) |> ignore
    add (createTriggerMenu())
    add (createKeyboardMenu())
    addSeparator menu.Items

    add (createAccelTableMenu())
    add (createPriorityMenu())
    add (createSetNumberMenu())
    add (createRealWheelModeMenu())
    add (createVhAdjusterMenu())
    addSeparator menu.Items

    add (createPropertiesMenu())
    addSeparator menu.Items
    
    add (createCursorChangeMenuItem())
    add (createHorizontalScrollMenuItem())
    add (createReverseScrollMenuItem())
    add (createSwapScrollMenuItem())
    add (createPassModeMenuItem())
    addSeparator menu.Items

    add (createLanguageMenu())
    add (createInfoMenuItem())
    add (createExitMenuItem())

    resetAllMenuItems()

    menu

let mutable private contextMenu: ContextMenuStrip = null

let setSystemTray (): unit =
    let ni = notifyIcon
    ni.Icon <- getTrayIcon false
    ni.Text <- getTrayText false
    ni.Visible <- true
    ni.ContextMenuStrip <- createContextMenuStrip()
    ni.DoubleClick.Add (fun _ -> Pass.toggleMode())
    
let mutable private initStateMEH: unit -> unit = (fun () -> ())
let mutable private initStateKEH: unit -> unit = (fun () -> ())
let mutable private offerEW: MouseEvent -> bool = (fun me -> false)

let setInitStateMEH f = initStateMEH <- f
let setInitStateKEH f = initStateKEH <- f
let setOfferEW f = offerEW <- f

let initState () =
    // The physical ups now pass W10Wheel: the LR trigger neither resends nor starts for a down
    // received before, and the wheel events queued are not sent. A down resent before and
    // sent after its physical up is dropped by the hook (Windows.dropResent). First, as Pass Mode
    // does: a wait timing out while it is canceled below sees the new generation and does not
    // resend its down (its flag would be reset by Init, and its up not resent).
    newPassModeGeneration ()
    // Cancel the LR trigger wait before Init: its timeout could set the flags again after it
    offerEW(Cancel) |> ignore
    initStateMEH()
    initStateKEH()
    LastFlags.Init()
    exitScrollMode()

// Pass Mode bypasses the event handlers, so nothing they wait for completes while it is on:
// e.g. the LR trigger wait would resend a down after the timeout although the up has already
// passed. Cancel the wait and reset their state (scroll mode is left by Scroll.SetPassMode).
onPassModeOn <- (fun () ->
    // A down resent by W10Wheel (LR trigger) waits for W10Wheel to resend its up when the
    // physical up comes. In Pass Mode the physical up passes instead and releases it; if it
    // passes before the resent down (Windows input queue), the hook drops the down
    // (Windows.dropResent).
    offerEW(Cancel) |> ignore
    initStateMEH()
    initStateKEH()
    LastFlags.Init())
