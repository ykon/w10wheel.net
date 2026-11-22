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

let getSelectedProperties () =
    Volatile.Read(&selectedProperties.contents)

let private firstTrigger: Trigger ref = ref LRTrigger
let private pollTimeout = ref 200
let private processPriority = ref ProcessPriority.AboveNormal
let private sendMiddleClick = ref false

let private keyboardHook = ref false
let private targetVKCode = ref (Keyboard.getVKCode(DataID.VK_NONCONVERT))
let private uiLanguage = ref (Locale.getLanguage())

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

type MenuData = { engText: string }

(*
let private tagToEngText (item: ToolStripMenuItem): string =
    match item.Tag with
    | :? MenuData as tag -> tag.engText
    | _ -> raise (ArgumentException("Tag is not MenuTag."))
*)

let private resetMenuText (): unit =        
    let toList = fun (mc: ToolStripItemCollection) ->
        if mc.Count = 0 then
            List.empty
        else
            let array = Array.zeroCreate mc.Count
            mc.CopyTo(array, 0)
            List.ofArray array
            |> List.filter (not << isSeparator)
            |> List.map (fun item -> item :?> ToolStripMenuItem)

    let rec loop (list: ToolStripMenuItem list): unit =
        match list with
        | [] -> ()
        | item :: rest ->
            // Only the items with an English text (Tag) are translated
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
    // A wheel event has been sent since scroll mode started (Windows.sendInput): the press of
    // the drag trigger is a drag then, even if the cursor would not have moved
    [<VolatileField>] static let mutable scrolled = false

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
                scrolled <- false
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
                scrolled <- false
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
    static member Scrolled
        with get() = scrolled
        and set b = scrolled <- b
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
let isScrolled () = Scroll.Scrolled
let setScrolled () = Scroll.Scrolled <- true
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

    // The down of the button of an event was suppressed and its up has not come yet
    static member IsSuppressedDown (me: MouseEvent): bool =
        match me with
        | LeftEvent(_) -> ldS
        | RightEvent(_) -> rdS
        | MiddleEvent(_) -> mdS
        | X1Event(_) -> x1S
        | X2Event(_) -> x2S
        | _ -> false

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
    createMenuItem {engText = text}

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

// Set when the settings dialog is made (see createSettingsDialog): it shows the settings and the
// list of the properties again when they have been loaded (or a file added or deleted), and its
// texts when the language has changed
let mutable private refreshSettingsDialog: unit -> unit = (fun () -> ())
let mutable private retextSettingsDialog: unit -> unit = (fun () -> ())

// Called with whether the trigger has changed (not on every properties load)
let mutable private changeTrigger: (bool -> unit) = (fun _ -> ())

let setChangeTrigger (f: bool -> unit) =
    changeTrigger <- f

// The last mouse trigger other than None (in this session, or LR): the settings dialog shows it
// while the trigger is None (Mouse unchecked) and sets it again when Mouse is checked
let mutable private lastMouseTrigger = LRTrigger

let private setTrigger (text: string) =
    let res = Mouse.getTriggerOfStr text
    Debug.WriteLine("setTrigger: " + res.Name)
    let changed = res <> getFirstTrigger()
    Volatile.Write(firstTrigger, res)
    if not res.IsNone then lastMouseTrigger <- res

    changeTrigger changed

let private addClick (item: ToolStripMenuItem) f =
    item.Click.Add (fun e -> f (e))
    item

let private addSeparator (col: ToolStripItemCollection) =
    col.Add(new ToolStripSeparator()) |> ignore

let private setAccelMultiplier name =
    Debug.WriteLine("setAccelMultiplier " + name)
    Accel.Multiplier <- getAccelMultiplierOfName name

let private setPriority name =
    let p = ProcessPriority.getPriority name
    Debug.WriteLine("setPriority: " + p.Name)
    Volatile.Write(processPriority, p)
    ProcessPriority.setPriority p

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

let private getVhAdjusterMethodOfName name =
    match name with
    | DataID.Fixed -> Fixed
    | DataID.Switching -> Switching
    | _ -> raise (ArgumentException())

let private setVhAdjusterMethod name =
    Debug.WriteLine("setVhAdjusterMethod: " + name)
    VHAdjuster.Method <- (getVhAdjusterMethodOfName name)

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
        // Not installed (the previous hook removed): off, as the settings dialog shows (refreshed
        // after the change or the load)
        Volatile.Write(keyboardHook, false)

        changeKeyboardTrigger (getTargetVKCode()) false
        Dialog.errorMessage (sprintf "%s: %s" (convLang "Failed keyboard hook install") err) (convLang "Error")
    | None ->
        if b then
            // Installed again on every load (e.g. Reload after Windows removed it): ups may have
            // been missed
            keyboardHookInstalled ()
        else
            changeKeyboardTrigger (getTargetVKCode()) false



let private createPassModeMenuItem () =
    let id = DataID.passMode
    // Pass Mode (its name in the code and the command --sendPassMode)
    let item = createMenuItem_NonID "Pause"
    item.Click.AddHandler(new EventHandler(makeSetBooleanEvent id))
    item.CheckOnClick <- true
    passModeMenuItem <- item
    item

let private createInfoMenuItem () =
    let item = createMenuItem_NonID "About"

    addClick item (fun _ ->
        let msg = sprintf "%s: %s / %s: %s" (convLang("Name")) AppDef.PROGRAM_NAME_NET (convLang("Version")) AppDef.PROGRAM_VERSION
        MessageBox.Show(msg, convLang("About"), MessageBoxButtons.OK, MessageBoxIcon.Information) |> ignore
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

let private isCreatedMenuItems (): bool =
    notifyIcon.ContextMenuStrip <> null

let private setUILanguage lang =
    Debug.WriteLine("setUILanguage: " + lang)
    let reset = lang <> getUILanguage()
    Volatile.Write(uiLanguage, lang)

    if reset && isCreatedMenuItems() then
        resetMenuText()
        notifyIcon.Text <- getTrayText Pass.Mode
        retextSettingsDialog()

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

// Applies the settings in prop (a load, or a reset to the default values)
let private applyProperties () =
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

    // The scroll settings are read when scroll mode starts (Windows.initScroll): a scroll in
    // progress (Reload, switching properties) ends once they are set, so that the new ones
    // apply from the next (also one started meanwhile, e.g. during the error dialog of the
    // keyboard hook). Not if the file could not be read: the settings have not changed. The
    // keyboard trigger's has ended (setKeyboardHookOf), and the mouse triggers' if the
    // trigger changed (changeTrigger). Their trigger's up is still suppressed.
    exitScrollMode()

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
        applyProperties ()
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
    refreshSettingsDialog()

let private openPropertiesDir () =
    Dialog.shellOpen Properties.USER_DIR

let private DEFAULT_DEF = Properties.DEFAULT_DEF

// A part of a file name (see Properties.getPath), not Default whatever its case (a name only
// differing in case would be confused with it), nor a command (--)
let private isValidPropertiesName (name: string) =
    not (String.IsNullOrWhiteSpace(name)) && name.Trim() = name &&
    not (String.Equals(name, DEFAULT_DEF, StringComparison.OrdinalIgnoreCase)) &&
    not (name.StartsWith("--")) &&
    name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0

// The new properties are a copy of the selected ones, and selected (the settings do not change)
let private addProperties () =
    let res = Dialog.openTextInputBox (convLang "Properties Name") (convLang "Add Properties")

    try
        res |> Option.iter (fun name ->
            if isValidPropertiesName name then
                storeProperties()
                Properties.copy (getSelectedProperties()) name
                setSelectedProperties name
                refreshSettingsDialog()
            else
                Dialog.errorMessage (sprintf "%s: %s" (convLang "Invalid Name") name) (convLang "Name Error")
        )
    with
        | e -> Dialog.errorMessageE e

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
        refreshSettingsDialog()

// The selected properties (not Default), then Default is selected
let private deleteProperties () =
    let name = getSelectedProperties()
    try
        if openYesNoMessage (sprintf "%s: %s" (convLang "Delete properties") name) then
            Properties.delete name
            setProperties DEFAULT_DEF
    with
        | e -> Dialog.errorMessageE e

// The settings of the selected properties back to their default values (defaultProp), except the
// language: written into prop (the keys that are not settings stay, e.g. the custom accel table),
// applied as a load does, and stored
let private resetSettings () =
    let name = getSelectedProperties()
    let shownName = if name = DEFAULT_DEF then convLang name else name
    let msg = sprintf "%s\n%s: %s" (convLang "Reset all the settings to their defaults? The language is kept.")
                                   (convLang "Properties") shownName
    if openYesNoMessage msg then
        // prop has the values of the last load (storeProperties writes a copy of the file): the
        // current settings first, or the language changed since would be set back
        setSettings prop
        let keys =
            [DataID.firstTrigger; DataID.accelMultiplier; DataID.processPriority;
             DataID.targetVKCode; DataID.vhAdjusterMethod]
            @ List.ofArray BooleanNames @ List.ofArray NumberNames
        keys |> List.iter (fun key -> prop.[key] <- defaultProp.GetProperty key)

        try
            applyProperties ()
        with
            | e -> Debug.WriteLine("reset: " + (e.ToString()))
        storeProperties()
        refreshSettingsDialog()

// The settings of the selected properties are stored first
let private switchProperties name =
    storeProperties()
    setProperties name

// Not None: it is Mouse unchecked in the settings dialog
let private triggerItems =
    [DataID.LR, DataID.LR + " (Left <<-->> Right)";
     DataID.Left, DataID.Left + " (Left -->> Right)";
     DataID.Right, DataID.Right + " (Right -->> Left)";
     DataID.Middle, DataID.Middle; DataID.X1, DataID.X1; DataID.X2, DataID.X2;
     DataID.LeftDrag, DataID.LeftDrag; DataID.RightDrag, DataID.RightDrag;
     DataID.MiddleDrag, DataID.MiddleDrag; DataID.X1Drag, DataID.X1Drag; DataID.X2Drag, DataID.X2Drag]

// The mouse trigger shown in the settings dialog: the last one while it is None
let private getShownMouseTrigger () =
    let t = getFirstTrigger()
    if t.IsNone then lastMouseTrigger else t

// Not None: Keyboard unchecked turns the keyboard trigger off. (None, the hook without a trigger
// key, is still read from the properties: the dialog then shows no key.)
let private keyboardItems =
    [DataID.VK_TAB, " (Tab)"; DataID.VK_PAUSE, " (Pause)"; DataID.VK_CAPITAL, " (Caps Lock)";
     DataID.VK_CONVERT, " (Henkan)"; DataID.VK_NONCONVERT, " (Muhenkan)";
     DataID.VK_PRIOR, " (Page Up)"; DataID.VK_NEXT, " (Page Down)"; DataID.VK_END, " (End)";
     DataID.VK_HOME, " (Home)"; DataID.VK_SNAPSHOT, " (Print Screen)"; DataID.VK_INSERT, " (Insert)";
     DataID.VK_DELETE, " (Delete)"; DataID.VK_LWIN, " (Left Windows)"; DataID.VK_RWIN, " (Right Windows)";
     DataID.VK_APPS, " (Application)"; DataID.VK_NUMLOCK, " (Number Lock)"; DataID.VK_SCROLL, " (Scroll Lock)";
     DataID.VK_LSHIFT, " (Left Shift)"; DataID.VK_RSHIFT, " (Right Shift)";
     DataID.VK_LCONTROL, " (Left Ctrl)"; DataID.VK_RCONTROL, " (Right Ctrl)";
     DataID.VK_LMENU, " (Left Alt)"; DataID.VK_RMENU, " (Right Alt)"]
    |> List.map (fun (id, name) -> id, id + name)

let private accelItems =
    [DataID.M5, DataID.M5 + " (1.0 ... 4.8)"; DataID.M6, DataID.M6 + " (1.2 ... 5.8)";
     DataID.M7, DataID.M7 + " (1.4 ... 6.7)"; DataID.M8, DataID.M8 + " (1.6 ... 7.7)";
     DataID.M9, DataID.M9 + " (1.8 ... 8.7)"]

// The settings dialog. A change is applied and stored at once. Not for Pass Mode (in the tray
// menu): it is not stored, and storing would overwrite the file edited outside W10Wheel and not
// reloaded yet.
let private createSettingsDialog () =
    let dialog = SettingsForm.SettingsDialog(convLang)
    dialog.Changed <- (fun () ->
        storeProperties()
        // The settings depending on it (e.g. Dragged Lock is for a drag trigger only, the keyboard
        // hook is off again if it could not be installed)
        dialog.RefreshValues())

    let addBool (page: SettingsForm.SettingsPage) engText id =
        page.AddCheck(engText, (fun () -> getBooleanOfName id), setBooleanOfName id)
    // enabled: whether the setting is used (see Windows.initScroll), else grayed
    let addBoolIf (page: SettingsForm.SettingsPage) engText id enabled =
        page.AddCheck(engText, (fun () -> getBooleanOfName id), setBooleanOfName id, enabled = enabled)
    // The ranges of loadProperties
    let addNumber (page: SettingsForm.SettingsPage) id low up =
        page.AddNumber(id, low, up, (fun () -> getNumberOfName id), setNumberOfName id)
    let addNumberIf (page: SettingsForm.SettingsPage) id low up enabled =
        page.AddNumber(id, low, up, (fun () -> getNumberOfName id), setNumberOfName id, enabled = enabled)

    dialog.AddPropertiesLabel "Properties"
    dialog.AddPropertiesComboBox(
        (fun () ->
            // Only Default is translated: the other names are the user's
            (DEFAULT_DEF, convLang DEFAULT_DEF) ::
            (Properties.getPropFiles() |> Array.map Properties.getUserDefName
             |> Array.map (fun name -> name, name) |> List.ofArray)),
        (fun name -> name = getSelectedProperties()),
        switchProperties)
    dialog.AddPropertiesButton("Add", addProperties)
    dialog.AddPropertiesButton("Delete", deleteProperties, enabled = (fun () -> getSelectedProperties() <> DEFAULT_DEF))
    dialog.AddPropertiesButton("Open Dir", openPropertiesDir)
    dialog.AddPropertiesButton("Reload", reloadProperties)
    dialog.AddBottomButton("Reset to defaults", resetSettings)

    let general = dialog.AddPage "General"
    general.AddComboBox("Language", [DataID.English, "English"; DataID.Japanese, "Japanese"],
                        (fun id -> id = getUILanguage()), setUILanguage)
    general.AddComboBox("Priority", [DataID.High, "High"; DataID.AboveNormal, "Above Normal"; DataID.Normal, "Normal"],
                        (fun id -> ProcessPriority.getPriority id = getProcessPriority()), setPriority)

    let trigger = dialog.AddPage "Trigger"
    // Mouse unchecked is the trigger None, as Keyboard unchecked is the keyboard hook off
    trigger.AddCheckComboBox("Mouse", (fun () -> not (isNoneTrigger())),
                             (fun b -> setTrigger (if b then lastMouseTrigger.Name else DataID.None)),
                             triggerItems, (fun id -> Mouse.getTriggerOfStr id = getShownMouseTrigger()), setTrigger)
    trigger.AddCheck("Send MiddleClick", (fun () -> getBooleanOfName DataID.sendMiddleClick),
                     setBooleanOfName DataID.sendMiddleClick, enabled = isSingleTrigger, indent = true)
    trigger.AddCheck("Dragged Lock", (fun () -> getBooleanOfName DataID.draggedLock),
                     setBooleanOfName DataID.draggedLock, enabled = isDragTrigger, indent = true)
    trigger.AddCheckComboBox("Keyboard", (fun () -> getBooleanOfName DataID.keyboardHook), (fun b ->
                                 // Without a key (None, from the properties of 2.8.x and before:
                                 // not in the list), the default key: Keyboard checked would do nothing
                                 if b && isNoneTriggerKey() then setTargetVKCode DataID.VK_NONCONVERT
                                 setBooleanOfName DataID.keyboardHook b
                                 setKeyboardHookOf b),
                             keyboardItems, (fun id -> Keyboard.getVKCode id = getTargetVKCode()), setTargetVKCode)
    trigger.AddGap()
    addNumber trigger DataID.pollTimeout 50 500
    addNumber trigger DataID.scrollLocktime 150 500

    let scroll = dialog.AddPage "Scroll"
    addBool scroll "Cursor Change" DataID.cursorChange
    addBool scroll "Horizontal Scroll" DataID.horizontalScroll
    addBool scroll "Reverse Scroll (Flip)" DataID.reverseScroll
    addBool scroll "Swap Scroll (V.H)" DataID.swapScroll
    scroll.AddGap()
    // The thresholds of the standard scroll: not with VH Adjuster (Windows.initStdMode)
    addNumberIf scroll DataID.verticalThreshold 0 500 (fun () -> not (isVhAdjusterMode()))
    addNumberIf scroll DataID.horizontalThreshold 0 500
        (fun () -> isHorizontalScroll() && not (isVhAdjusterMode()))

    // Not with Real Wheel Mode: its wheel events are not accelerated (Windows.sendRealVWheel)
    let accel = dialog.AddPage "Accel Table"
    let accelUsed () = not (isRealWheelMode())
    let customUsed () = not Accel.CustomDisabled && Accel.CustomTable
    addBoolIf accel "Enabled" DataID.accelTable accelUsed
    // The multipliers are not used with the custom table (getAccelMultiplier)
    accel.AddRadioButtons(accelItems, (fun id -> id = Accel.Multiplier.Name), setAccelMultiplier,
                          enabled = (fun () -> accelUsed() && Accel.Table && not (customUsed())))
    accel.AddCheck("Custom Table", (fun () -> getBooleanOfName DataID.customAccelTable),
                   setBooleanOfName DataID.customAccelTable,
                   enabled = (fun () -> accelUsed() && Accel.Table && not Accel.CustomDisabled))
    accel.AddNote "Not used while Real Wheel Mode is enabled."

    let realWheel = dialog.AddPage "Real Wheel Mode"
    addBool realWheel "Enabled" DataID.realWheelMode
    realWheel.AddGap()
    addNumberIf realWheel DataID.wheelDelta 10 500 isRealWheelMode
    addNumberIf realWheel DataID.vWheelMove 10 500 isRealWheelMode
    addNumberIf realWheel DataID.hWheelMove 10 500 isRealWheelMode
    realWheel.AddGap()
    addBoolIf realWheel DataID.quickFirst DataID.quickFirst isRealWheelMode
    addBoolIf realWheel DataID.quickTurn DataID.quickTurn isRealWheelMode

    // Used with Horizontal Scroll only (isVhAdjusterMode)
    let vhAdjuster = dialog.AddPage "VH Adjuster"
    vhAdjuster.AddCheck("Enabled", (fun () -> getBooleanOfName DataID.vhAdjusterMode),
                        setBooleanOfName DataID.vhAdjusterMode, enabled = (fun () -> Scroll.Horizontal))
    vhAdjuster.AddRadioButtons([DataID.Fixed, "Fixed"; DataID.Switching, "Switching"],
                               (fun id -> getVhAdjusterMethodOfName id = getVhAdjusterMethod()), setVhAdjusterMethod,
                               enabled = isVhAdjusterMode)
    vhAdjuster.AddGap()
    addBoolIf vhAdjuster DataID.firstPreferVertical DataID.firstPreferVertical isVhAdjusterMode
    addNumberIf vhAdjuster DataID.firstMinThreshold 1 10 isVhAdjusterMode
    // For Switching only (Windows.switchVHD)
    addNumberIf vhAdjuster DataID.switchingThreshold 10 500
        (fun () -> isVhAdjusterMode() && isVhAdjusterSwitching())
    vhAdjuster.AddNote "Used when Horizontal Scroll (Scroll) is enabled."

    refreshSettingsDialog <- dialog.RefreshAllLater
    retextSettingsDialog <- dialog.RetextLater
    dialog

// Made when it is opened first (not at startup), then kept: closing hides it
let private settingsDialog = lazy (createSettingsDialog())

let private createSettingsMenuItem () =
    let item = createMenuItem_NonID "Settings..."
    addClick item (fun _ -> settingsDialog.Value.Show())

let private createContextMenuStrip (): ContextMenuStrip =
    let menu = new ContextMenuStrip()
    let add = fun (item: ToolStripMenuItem) -> menu.Items.Add(item) |> ignore
    // The settings are in the dialog. Pass Mode is also switched by a double click on the icon.
    add (createSettingsMenuItem())
    add (createPassModeMenuItem())
    addSeparator menu.Items

    add (createInfoMenuItem())
    add (createExitMenuItem())

    menu

let setSystemTray (): unit =
    let ni = notifyIcon
    ni.Icon <- getTrayIcon false
    ni.Text <- getTrayText false
    ni.Visible <- true
    ni.ContextMenuStrip <- createContextMenuStrip()
    // A double click of the left button only: DoubleClick is also raised for the other buttons
    // (e.g. two right clicks to open the menu)
    ni.MouseDoubleClick.Add (fun e -> if e.Button = MouseButtons.Left then Pass.toggleMode())
    
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
