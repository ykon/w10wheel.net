module EventHandler

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open System.Threading
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop

open Mouse

let mutable private __callNextHook: (unit -> nativeint) = fun _ -> IntPtr(0)
let setCallNextHook (f: unit -> nativeint): unit = __callNextHook <- f
let getCallNextHook (): unit -> nativeint = __callNextHook

let private callNextHook () = Some(__callNextHook())
let private suppress () = Some(IntPtr(1))

let mutable private lastEvent: MouseEvent = NonEvent
let mutable private secondTriggerUp = false
let mutable private dragged = false

// The down of the single or drag trigger that started scroll mode (and was suppressed). Reset
// by its up. An up of another button (e.g. the trigger was changed by Reload while pressed), or
// without it, has its down passed W10Wheel (e.g. pressed in Pass Mode), so the up passes.
let mutable private triggerStarted: MouseEvent = NonEvent

// The downs that started scroll mode of a trigger before it was changed (changeTrigger), by
// button (buttonIndex): changed again while pressed, there can be several. Their up is
// suppressed before the handlers of the current trigger, which would handle it as their own
// (e.g. the LR trigger's wait would resend it). Reset by the up or the next down of the button.
let private canceledDowns = Array.create 5 false

let private buttonIndex (me: MouseEvent): int =
    match me with
    | LeftEvent(_) -> 0
    | RightEvent(_) -> 1
    | MiddleEvent(_) -> 2
    | X1Event(_) -> 3
    | X2Event(_) -> 4
    | _ -> raise (ArgumentException())

let private initState () =
    lastEvent <- NonEvent
    secondTriggerUp <- false
    dragged <- false
    triggerStarted <- NonEvent
    Array.fill canceledDowns 0 canceledDowns.Length false

let setInitStateMEH () =
    Ctx.setInitStateMEH initState

let private debug (msg: string) (me: MouseEvent) =
    Debug.WriteLine(msg + ": " + me.Name)

// The injected events pass: W10Wheel's own resends (checked by the hook before, see
// Windows.dropResent) and those of other software. A resent up cannot come before its down:
// the down is queued first and the sender sends in order, so they are not reordered here.
let private skipResendEventLR (me: MouseEvent): nativeint option =
    if not (Windows.isInjectedEvent me) then
        None
    elif Windows.isResendClickEvent me then
        debug "pass resendClick event" me
        callNextHook()
    elif Windows.isResendEvent me then
        debug "pass resend event" me
        callNextHook()
    else
        debug "pass other software event" me
        callNextHook()

let private skipResendEventSingle (me: MouseEvent): nativeint option =
    if not (Windows.isInjectedEvent me) then
        None
    elif Windows.isResendClickEvent me then
        debug "pass resendClick event" me
        callNextHook()
    else
        debug "pass other software event" me
        callNextHook()

let private checkEscape (me: MouseEvent): nativeint option =
    if Windows.checkEscState() then
        debug "init state and exit scroll" me
        Ctx.initState ()
        callNextHook()
    else
        None

let private skipFirstUp (me: MouseEvent): nativeint option =
    if lastEvent.IsNone then
        debug "skip first Up" me
        callNextHook()
    else
        None

let private checkSameLastEvent (me: MouseEvent): nativeint option =
    if me.SameEvent lastEvent then
        debug "same last event" me
        callNextHook()
        //suppress()
    else
        lastEvent <- me
        None

let private checkExitScrollDown (me: MouseEvent): nativeint option =
    if Ctx.isReleasedScrollMode() then
        debug "exit scroll mode (Released)" me
        Ctx.exitScrollMode()
        Ctx.LastFlags.SetSuppressed me
        suppress()
    else
        None

// The scroll mode of the keyboard trigger is held: pressing a mouse trigger ends it. Its down is
// suppressed, and so is its up (checkSuppressedDown).
let private checkExitOtherScroll (down: MouseEvent): nativeint option =
    if Ctx.isPressedKeyboardScrollMode() then
        debug "exit scroll mode (other trigger)" down
        Ctx.exitScrollMode()
        Ctx.LastFlags.SetSuppressed down
        suppress()
    else
        None

// For the LR, left and right triggers: their buttons only (another button passes as before)
let private checkExitOtherScrollLR (down: MouseEvent): nativeint option =
    if Ctx.isLRTrigger() || Ctx.isTriggerEvent down then
        checkExitOtherScroll down
    else
        None

let private passPressedScrollMode (down: MouseEvent): nativeint option =
    if Ctx.isPressedScrollMode() then
        debug "pass scroll mode (Pressed)" down
        Ctx.LastFlags.SetPassed(down)
        callNextHook()
    else
        None

let private matchAvastUI (pathOpt: string option): bool =
    match pathOpt with
    | Some (path) -> path.EndsWith("\\AvastUI.exe")
    //| Some (path) -> path.EndsWith("\\chrome.exe") // for Debug
    | None -> false

let private skipExcludeWindow (down: MouseEvent): nativeint option =
    if Ctx.isExcludeAvast() then
        //let pt_match = matchAvastUI(Windows.getFullPathFromCursorPos()) // for DPI scaling
        let fw_match = matchAvastUI(Windows.getFullPathFromForegroundWindow())
        if fw_match then
            debug "pass avast" down
            Ctx.LastFlags.SetPassed(down)
            callNextHook()
        else
            None
    else
        None

let private checkExitScrollUp (me: MouseEvent): nativeint option =
    if Ctx.isPressedMouseScrollMode() then
        if Ctx.checkExitScroll me.Info.time then
            debug "exit scroll mode (Pressed)" me
            Ctx.exitScrollMode()
        else
            debug "continue scroll mode (Released)" me
            Ctx.setReleasedScrollMode()

        suppress()
    else
        None

let private checkExitScrollUpLR (up: MouseEvent): nativeint option =
    if Ctx.isPressedMouseScrollMode() then
        if not secondTriggerUp then
            debug "ignore first up" up
        elif Ctx.checkExitScroll up.Info.time then
            debug "exit scroll mode (Pressed)" up
            Ctx.exitScrollMode()
        else
            debug "continue scroll mode (Released)" up
            Ctx.setReleasedScrollMode()

        // Its down was suppressed (setFlagsOffer): handled
        Ctx.LastFlags.GetAndReset_SuppressedDown up |> ignore
        secondTriggerUp <- not secondTriggerUp
        suppress()
    else
        None

let private checkStartingScroll (up: MouseEvent): nativeint option =
    if Ctx.isStartingScrollMode() then
        Debug.WriteLine("check starting scroll")
        Thread.Sleep(1)

        if not secondTriggerUp then
            debug "ignore first up (starting)" up
        else
            debug "exit scroll mode (starting)" up
            Ctx.exitScrollMode()

        Ctx.LastFlags.GetAndReset_SuppressedDown up |> ignore
        secondTriggerUp <- not secondTriggerUp
        suppress()
    else
        None

let private passSingleEvent (me: MouseEvent): nativeint option =
    if me.IsSingle then
        debug "pass single event" me
        callNextHook()
    else
        None

let private offerEventWaiter (me: MouseEvent): nativeint option =
    // The up of the other button than the waiting down, whose own down did not reach the
    // application (e.g. suppressed by a wait canceled when Pass Mode was turned on): resent
    // with the waiting down, it would be an up without a down. Suppress it, the wait goes on.
    if me.IsUp && EventWaiter.isWaitingOtherThan me && Windows.dropLoneUpOf me then
        debug "suppress (up without its down, other button)" me
        suppress()
    elif EventWaiter.offer me then
        debug "success to offer" me
        // The second down starts LR scroll mode: no up of the pair counted yet (the count
        // of an earlier one may remain, e.g. when the keyboard trigger took over its scroll)
        if me.IsDown then secondTriggerUp <- false
        suppress()
    else
        None

let private checkSuppressedDown (up: MouseEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_SuppressedDown up then
        debug "suppress (checkSuppressedDown)" up
        suppress()
    else
        None

let private checkResentDown (up: MouseEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_ResentDown up then
        debug "resendUp and suppress (checkResentDown)" up
        Windows.resendUp up
        suppress()
    else
        None

let private checkPassedDown (up: MouseEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_PassedDown up then
        debug "pass (checkPassedDown)" up
        callNextHook()
    else
        None

let private checkTriggerWaitStart (me: MouseEvent): nativeint option =
    if Ctx.isLRTrigger() || Ctx.isTriggerEvent me then
        debug "start wait trigger" me
        EventWaiter.start me
        suppress()
    else
        None

let private checkKeySendMiddle (me: MouseEvent): nativeint option =
    if Ctx.isSendMiddleClick() && (Windows.checkShiftState() || Windows.checkCtrlState() || Windows.checkAltState()) then
        debug "send middle click" me
        Windows.resendClick(MiddleClick(me.Info))
        Ctx.LastFlags.SetSuppressed me
        suppress()
    else
        None

let private checkTriggerScrollStart (me: MouseEvent): nativeint option =
    if Ctx.isTriggerEvent me then
        debug "start scroll mode" me
        Ctx.startScrollMode me.Info
        triggerStarted <- me
        suppress()
    else
        None

let private dragDefault info = ()
let mutable private drag: HookInfo -> unit = dragDefault

let private dragStart info =
    if Ctx.isCursorChange() && not (Ctx.isVhAdjusterMode()) then
        WinCursor.changeV()

    drag <- dragDefault
    dragged <- true

let private startScrollDrag (me: MouseEvent): nativeint option =
    debug "start scroll mode (Drag)" me
    Ctx.startScrollMode me.Info
    triggerStarted <- me

    drag <- dragStart
    dragged <- false

    suppress()

let private passNotStartedTrigger (up: MouseEvent): nativeint option =
    if triggerStarted.SameButton up then
        triggerStarted <- NonEvent
        None
    else
        debug "pass (the down passed)" up
        callNextHook()

// The drag is not the pressed scroll mode any more: the keyboard trigger has started scroll mode
// since (maybe ended already). Its up neither continues nor ends it (nor resends a click),
// and its down started the drag (suppressed), so the up is suppressed.
let private suppressNotDragScroll (up: MouseEvent): nativeint option =
    if not (Ctx.isPressedMouseScrollMode()) then
        debug "suppress (not drag scroll mode)" up
        suppress()
    else
        None

let private continueScrollDrag (me: MouseEvent): nativeint option =
    if Ctx.isDraggedLock() && dragged then
        debug "continueScrollDrag (Released)" me
        Ctx.setReleasedScrollMode()
        suppress()
    else
        None

let private exitAndResendDrag (me: MouseEvent): nativeint option =
    debug "exit scroll mode (Drag)" me
    Ctx.exitScrollMode()

    if not dragged then
        debug "resend click" me

        match me with
        | LeftUp(info) -> Windows.resendClick(LeftClick(info))
        | RightUp(info) -> Windows.resendClick(RightClick(info))
        | MiddleUp(info) -> Windows.resendClick(MiddleClick(info))
        | X1Up(info) -> Windows.resendClick(X1Click(info))
        | X2Up(info) -> Windows.resendClick(X2Click(info))
        | _ -> raise (InvalidOperationException())

    suppress()

let private passNotTrigger (me: MouseEvent): nativeint option =
    if not (Ctx.isTriggerEvent me) then
        debug "pass not trigger" me
        callNextHook()
    else
        None

let private passNotTriggerLR (me: MouseEvent): nativeint option =
    if not (Ctx.isLRTrigger()) && not (Ctx.isTriggerEvent me) then
        debug "pass not trigger" me
        callNextHook()
    else
        None

let private passNotDragTrigger (me: MouseEvent): nativeint option =
    if not (Ctx.isDragTriggerEvent me) then
       debug "pass not trigger" me
       callNextHook()
    else
        None 

let private endCallNextHook (me:MouseEvent) (msg:string): nativeint option =
    Debug.WriteLine(msg)
    callNextHook()

let private endNotTrigger (me: MouseEvent): nativeint option =
    endCallNextHook me ("endNotTrigger: " + me.Name)

let private endPass (me: MouseEvent): nativeint option =
    endCallNextHook me ("endPass: " + me.Name)

let private endUnknownEvent (me: MouseEvent): nativeint option =
    endCallNextHook me ("unknown event: " + me.Name)

let private endAfterTimeoutOrMove (me: MouseEvent): nativeint option =
    endCallNextHook me ("pass after timeout or move?: " + me.Name)

let private endSuppress (me:MouseEvent) (msg:string option): nativeint option =
    Option.iter (fun s -> Debug.WriteLine(s)) msg
    suppress()

let private endIllegalState (me: MouseEvent): nativeint option =
    debug "illegal state" me
    suppress()

type Checkers = (MouseEvent -> nativeint option) list

let rec private getResult (cs:Checkers) (me:MouseEvent): nativeint =
    match cs with
    | f :: fs ->
        match f me with
        | Some(res) -> res
        | None -> getResult fs me
    | _ -> raise (ArgumentException())

let private lrDown (me: MouseEvent): nativeint =
    //Debug.WriteLine("lrDown")
    let checkers = [
        skipResendEventLR
        checkSameLastEvent
        skipExcludeWindow
        checkExitScrollDown
        checkExitOtherScrollLR
        passPressedScrollMode
        //passSingleEvent
        offerEventWaiter
        checkTriggerWaitStart
        endNotTrigger
    ]

    getResult checkers me

let private lrUp (me: MouseEvent): nativeint =
    //Debug.WriteLine("lrUp")
    let checkers = [
        skipResendEventLR
        checkEscape
        skipFirstUp
        checkSameLastEvent
        //checkSingleSuppressed
        checkPassedDown
        checkResentDown
        checkExitScrollUpLR
        checkStartingScroll
        offerEventWaiter
        // The wait timed out during the checks above: its down has been resent
        checkResentDown
        checkSuppressedDown
        endNotTrigger
    ]

    getResult checkers me

let private singleDown (me: MouseEvent): nativeint =
    //Debug.WriteLine("singleDown")
    let checkers = [
        skipResendEventSingle
        checkSameLastEvent
        //resetLastFlags
        checkExitScrollDown
        passNotTrigger
        checkExitOtherScroll
        checkKeySendMiddle
        checkTriggerScrollStart
        endIllegalState
    ]

    getResult checkers me

let private singleUp (me: MouseEvent): nativeint =
    //Debug.WriteLine("singleUp")
    let checkers = [
        skipResendEventSingle
        checkEscape
        skipFirstUp
        checkSameLastEvent
        checkSuppressedDown
        passNotTrigger
        passNotStartedTrigger
        checkExitScrollUp
        endIllegalState
    ]

    getResult checkers me

let private dragDown (me: MouseEvent): nativeint =
    //Debug.WriteLine("dragDown")
    let checkers = [
        skipResendEventSingle
        checkSameLastEvent
        //resetLastFlags
        checkExitScrollDown
        passNotDragTrigger
        checkExitOtherScroll
        startScrollDrag
    ]

    getResult checkers me

let private dragUp (me: MouseEvent): nativeint =
    //Debug.WriteLine("dragUp")
    let checkers = [
        skipResendEventSingle
        checkEscape
        skipFirstUp
        checkSameLastEvent
        checkSuppressedDown
        passNotDragTrigger
        passNotStartedTrigger
        suppressNotDragScroll
        continueScrollDrag
        exitAndResendDrag
    ]

    getResult checkers me

let private noneDown (me: MouseEvent): nativeint =
    //Debug.WriteLine("noneDown")
    let checkers = [
        //resetLastFlags
        checkExitScrollDown
        endPass
    ]

    getResult checkers me

let private noneUp (me: MouseEvent): nativeint =
    //Debug.WriteLine("noneUp")
    let checkers = [
        checkEscape
        checkSuppressedDown
        endPass
    ]

    getResult checkers me

let private __procDownLR: (MouseEvent -> nativeint) ref = ref lrDown
let private __procUpLR: (MouseEvent -> nativeint) ref = ref lrUp
let private __procDownS: (MouseEvent -> nativeint) ref = ref noneDown
let private __procUpS: (MouseEvent -> nativeint) ref = ref noneUp

// Before the handlers of the trigger (see canceledDowns)
let private procDown (proc: (MouseEvent -> nativeint) ref) (d: MouseEvent): nativeint =
    if not (Windows.isInjectedEvent d) then
        canceledDowns.[buttonIndex d] <- false

        // A new press: the flags of the earlier one (resent, suppressed, passed) are reset
        // whatever the trigger, for all the buttons (e.g. the up of a suppressed middle down
        // that was missed would make the up of the next middle click suppressed)
        Ctx.LastFlags.ResetFor d

    Volatile.Read(&proc.contents)(d)

let private procUpBody (proc: (MouseEvent -> nativeint) ref) (u: MouseEvent): nativeint =
    let i = buttonIndex u
    if not (Windows.isInjectedEvent u) && canceledDowns.[i] then
        canceledDowns.[i] <- false

        // As the handlers do for an up: Esc initializes the state (and passes the up), and the
        // up is the last event (or the next down of the button would be taken as the same)
        match checkEscape u with
        | Some(res) -> res
        | None ->
            debug "suppress (the down was canceled by changing the trigger)" u
            lastEvent <- u
            suppress().Value
    else
        Volatile.Read(&proc.contents)(u)

let private procUp (proc: (MouseEvent -> nativeint) ref) (u: MouseEvent): nativeint =
    let res = procUpBody proc u

    // The press is over: its flags (resent, suppressed, passed) are not needed any more, reset
    // them whatever handled the up (e.g. after changing the trigger, the handlers of the new one
    // do not use them, and they would be taken for those of a later up whose down was not seen)
    if not (Windows.isInjectedEvent u) then
        Ctx.LastFlags.ResetFor u

    res

let private procDownLR (d: MouseEvent): nativeint = procDown __procDownLR d
let private procUpLR (u: MouseEvent): nativeint = procUp __procUpLR u
let private procDownS (d: MouseEvent): nativeint = procDown __procDownS d
let private procUpS (u: MouseEvent): nativeint = procUp __procUpS u

let leftDown (info: HookInfo) =
    //Debug.WriteLine("LeftDown")
    let ld = LeftDown(info)
    procDownLR ld

let leftUp (info: HookInfo) =
    //Debug.WriteLine("LeftUp")
    let lu = LeftUp(info)
    procUpLR lu
    
let rightDown(info: HookInfo) =
    //Debug.WriteLine("RightDown")
    let rd = RightDown(info)
    procDownLR rd

let rightUp (info: HookInfo) =
    //Debug.WriteLine("RightUp")
    let ru = RightUp(info)
    procUpLR ru

let middleDown (info: HookInfo) =
    let md = MiddleDown(info)
    procDownS md

let middleUp (info: HookInfo) =
    let mu = MiddleUp(info)
    procUpS mu

let xDown (info: HookInfo) =
    let xd = if Mouse.isXButton1(info.mouseData) then X1Down(info) else X2Down(info)
    procDownS xd

let xUp (info: HookInfo) =
    let xu = if Mouse.isXButton1(info.mouseData) then X1Up(info) else X2Up(info)
    procUpS xu

let move (info: HookInfo) =
    //Debug.WriteLine "Move: test"
    if Ctx.isScrollMode() then
        drag info
        //Windows.sendWheel info.pt
        suppress().Value
    elif EventWaiter.offer (Move(info)) then
        Debug.WriteLine("success to offer: Move")
        suppress().Value
    else
        callNextHook().Value

let private changeTrigger (changed: bool): unit =
    Debug.WriteLine("changeTrigger: EventHandler")

    let downLR, upLR, downS, upS =
        if Ctx.isDoubleTrigger() then
            Debug.WriteLine("set double down/up")
            lrDown, lrUp, noneDown, noneUp
        elif Ctx.isSingleTrigger() then
            Debug.WriteLine("set single down/up")
            noneDown, noneUp, singleDown, singleUp
        elif Ctx.isDragTrigger() then
            Debug.WriteLine("set drag down/up")
            dragDown, dragUp, dragDown, dragUp
        else
            Debug.WriteLine("set none down/up")
            noneDown, noneUp, noneDown, noneUp

    Volatile.Write(__procDownLR, downLR)
    Volatile.Write(__procUpLR, upLR)
    Volatile.Write(__procDownS, downS)
    Volatile.Write(__procUpS, upS)

    // The handlers of the old trigger are not used any more, so its scroll mode would not end
    // (e.g. the up of the drag trigger is not handled as such). End it (not the keyboard
    // trigger's), and suppress the up of its down before the new handlers (see canceledDowns).
    if changed then
        // A pending LR trigger wait would still resend its down: cancel it, the down was
        // suppressed and so is its up
        match EventWaiter.cancel () with
        | Some(down) when down.IsDown -> canceledDowns.[buttonIndex down] <- true
        | _ -> ()

        if (Ctx.isScrollMode() || Ctx.isStartingScrollMode()) && not (Ctx.isKeyboardScrollMode()) then
            Debug.WriteLine("changeTrigger: exit scroll mode")
            Ctx.exitScrollMode()

        if triggerStarted.IsDown then
            canceledDowns.[buttonIndex triggerStarted] <- true

        // The downs suppressed by the old trigger (e.g. the LR trigger's pair) too: the new
        // handlers could take their up first (e.g. a new LR trigger wait)
        Ctx.LastFlags.TakeSuppressedDowns() |> Array.iteri (fun i b ->
            if b then canceledDowns.[i] <- true)

        triggerStarted <- NonEvent
        drag <- dragDefault
        dragged <- false
        secondTriggerUp <- false

        // Some handlers do not record the last event (none trigger): the event recorded by the
        // old ones could stay a down after its up, so that the next down was taken as the same
        lastEvent <- NonEvent

let setChangeTrigger () =
    Ctx.setChangeTrigger changeTrigger




