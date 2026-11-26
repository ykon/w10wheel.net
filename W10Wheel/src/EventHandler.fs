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
// by its up, or by the next down of its button (the up was missed, see checkSameLastEvent). An
// up of another button (e.g. the trigger was changed by Reload while pressed), or without it,
// has its down passed W10Wheel (e.g. pressed in Pass Mode), so the up passes.
let mutable private triggerStarted: MouseEvent = NonEvent

// The earlier down of the button of the down handled was suppressed, and no up of the button
// has come since (the flag before procDown resets it for this down): the up was missed, the
// buttons do not repeat (see checkSameLastEvent)
let mutable private earlierDownSuppressed = false

// The downs that started scroll mode of a trigger before it was changed (changeTrigger), by
// button (buttonIndex): changed again while pressed, there can be several. Their up is
// suppressed before the handlers of the current trigger, which would handle it as their own
// (e.g. the LR trigger's wait would resend it). Reset by the up or the next down of the button.
let private canceledDowns = Array.create 5 false

// The downs of the left and right buttons resent alone (LR trigger: the wait timed out or was
// ended by a move) whose up had not come when the trigger was changed (changeTrigger), by button:
// their up is resent (and the physical one suppressed) before the handlers of the current
// trigger, which do not resend it (e.g. the middle trigger's would pass it: if the resent down has
// not reached the hook yet, the up is dropped as alone, then the down as late, a lost click).
// Reset by the up or the next down of the button.
let private resentDowns = Array.create 2 false

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
    Array.fill resentDowns 0 resentDowns.Length false

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

// The earlier press of the button of a down held scroll mode, and its up was missed: the press
// of the single or drag trigger (triggerStarted), or one of the pair of the LR, left and right
// triggers (both downs suppressed, see EventWaiter.setFlagsOffer; earlierDownSuppressed). Not
// e.g. the middle trigger's for an X1 down, nor a down passed in the LR trigger's scroll mode
// after the first up (the other button still holds it). By button, whatever the other buttons
// have done since (lastEvent).
let private isHeldByEarlierPress (down: MouseEvent): bool =
    let isLR = match down with LeftDown(_) | RightDown(_) -> true | _ -> false
    triggerStarted.SameEvent down || (Ctx.isDoubleTrigger() && isLR && earlierDownSuppressed)

let private checkSameLastEvent (me: MouseEvent): nativeint option =
    let held = me.IsDown && isHeldByEarlierPress me
    if me.IsDown && (me.SameEvent lastEvent || held) then
        // A down again with no up of the button between (the buttons do not repeat): the up of
        // the earlier press was missed (e.g. released on the secure desktop or over a window of
        // a higher integrity level). A new press: passed, its up would be handled by the state of
        // the earlier one (e.g. suppressed as the trigger's), leaving the button pressed. The
        // scroll mode that press held could not end by its up any more: it is taken for locked,
        // so that this down ends it (checkExitScrollDown, its up suppressed too). Only the scroll
        // mode of this button (isHeldByEarlierPress), also if other buttons were pressed since.
        debug "down again (the up was missed)" me
        if held && Ctx.isPressedMouseScrollMode() then
            Ctx.setReleasedScrollMode()

        if triggerStarted.SameEvent me then
            triggerStarted <- NonEvent

        lastEvent <- me
        None
    elif me.SameEvent lastEvent then
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
// suppressed, and so is its up (checkSuppressedDown). For the LR, left and right triggers, see
// passPressedScrollMode.
let private checkExitOtherScroll (down: MouseEvent): nativeint option =
    if Ctx.isPressedKeyboardScrollMode() then
        debug "exit scroll mode (other trigger)" down
        Ctx.exitScrollMode()
        Ctx.LastFlags.SetSuppressed down
        suppress()
    else
        None

// While the keyboard trigger's scroll mode is held, the buttons of the LR, left and right
// triggers wait as when not scrolling: if the trigger is pressed (the second down, offered to
// the wait before, whichever button it is), it ends that scroll mode instead of starting one
// (EventWaiter.waiterThread), as another trigger does; otherwise the down is resent (or the
// click) as usual
let private passPressedScrollMode (down: MouseEvent): nativeint option =
    let isTriggerButton = Ctx.isLRTrigger() || Ctx.isTriggerEvent down
    if Ctx.isPressedScrollMode() && not (Ctx.isPressedKeyboardScrollMode() && isTriggerButton) then
        debug "pass scroll mode (Pressed)" down
        Ctx.LastFlags.SetPassed(down)
        callNextHook()
    else
        None

// The trigger pressed on the window of an excluded program (under the cursor, see
// Windows.isExcludedWindowAt): its down passes, and its up (checkPassedDown), as when it is not
// the trigger. Only where a wait would start: a scroll mode (and a wait) in progress gets the down
// as before (e.g. the locked scroll mode ends). Not looked up for the other button of the Left and
// Right triggers, which passes anyway (endNotTrigger). Nor while the keyboard trigger holds scroll
// mode: its wait ends it (passPressedScrollMode), as the other triggers end it on an excluded
// window (checkExitOtherScroll before passExcludedWindow).
let private passExcludedWindowLR (down: MouseEvent): nativeint option =
    if (Ctx.isLRTrigger() || Ctx.isTriggerEvent down) && not (Ctx.isPressedKeyboardScrollMode())
       && Windows.isExcludedWindowAt down.Info.pt then
        debug "pass (excluded program)" down
        Ctx.LastFlags.SetPassed(down)
        callNextHook()
    else
        None

// As passExcludedWindowLR for the single and drag triggers, where scroll mode would start: their up
// passes as the up of a down that did not start it (passNotStartedTrigger)
let private passExcludedWindow (down: MouseEvent): nativeint option =
    if Windows.isExcludedWindowAt down.Info.pt then
        debug "pass (excluded program)" down
        callNextHook()
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

// The wait of the earlier press of the button of a down may have timed out after procDown reset
// the flags of this down: its resent flag (EventWaiter.fromTimeout) is not of this press, whose
// up would be resent for a down the hook drops (its press is not the current one), with the
// wait of this press not offered that up (its down resent later, left pressed). Reset it when no
// wait is in progress any more: a timeout is handled before the wait ends (EventWaiter.
// SynchronousQueue.poll). No flag of this press has been set yet (lrDown).
let private resetRacedResentDown (down: MouseEvent) =
    Ctx.LastFlags.ResetFor down

let private offerEventWaiter (me: MouseEvent): nativeint option =
    // The up of the other button than the waiting down, whose own down did not reach the
    // application (e.g. suppressed by a wait canceled when Pass Mode was turned on): resent
    // with the waiting down, it would be an up without a down. Suppress it, the wait goes on.
    if me.IsUp && EventWaiter.isWaitingOtherThan me && Windows.dropLoneUpOf me then
        debug "suppress (up without its down, other button)" me
        suppress()
    // The up of the other button than the waiting down, whose down passed with no record of it
    // (the other button of the Left or Right trigger, not the trigger's; or pressed in Pass Mode):
    // the wait ends, the waiting down resent at once, and the up passes as it is (as
    // checkPassedDown, endNotTrigger). Not resent after that down: pressed again at once (passed,
    // not the trigger's), the button would have its down before that up (down, down, up).
    elif me.IsUp && EventWaiter.isWaitingOtherThan me then
        if EventWaiter.endByResend me then
            debug "the LR wait ended (its down resent)" me
        None
    // A down of the button whose down is waiting (the up was missed, or of another mouse): not
    // the second down of the trigger (the same button would start scroll mode). A new press, as
    // for checkSameLastEvent: the wait is canceled (its down is not resent: the hook would drop
    // it, its press is not the current one, see Windows.dropResent), this down is handled after.
    elif me.IsDown && EventWaiter.isWaitingFor me then
        debug "cancel the wait (down of the waiting button)" me
        EventWaiter.cancel () |> ignore
        resetRacedResentDown me
        None
    elif EventWaiter.offer me then
        debug "success to offer" me
        // The second down starts LR scroll mode: no up of the pair counted yet (the count
        // of an earlier one may remain, e.g. when the keyboard trigger took over its scroll)
        if me.IsDown then secondTriggerUp <- false
        suppress()
    else
        if me.IsDown then resetRacedResentDown me
        None

let private checkSuppressedDown (up: MouseEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_SuppressedDown up then
        debug "suppress (checkSuppressedDown)" up
        suppress()
    else
        None

// Also before skipFirstUp (lrUp): the flag is of this press (reset at its down), e.g. the last
// event was reset by a trigger change after the down was resent. The last event is this up then,
// as checkSameLastEvent would set it.
let private checkResentDown (up: MouseEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_ResentDown up then
        debug "resendUp and suppress (checkResentDown)" up
        lastEvent <- up
        Windows.resendUp up
        suppress()
    else
        None

// The other button of the pair waiting (a down passed, e.g. over an excluded window, then the other
// pressed out of it): its wait ends, the waiting down resent at once, as the Rust version does (it
// would wait for the timeout, and the button pressed again within it make the pair). The up passes
// as it is, not resent after that down (pressed again, passed over the excluded window, the button
// would have its down before that up).
let private checkPassedDown (up: MouseEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_PassedDown up then
        if EventWaiter.isWaitingOtherThan up && EventWaiter.endByResend up then
            debug "the LR wait ended (its down resent)" up
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

// Scroll mode could not start (RawInput could not be registered, see Ctx.Scroll.Start, or W10Wheel
// is exiting): the down of the trigger passes, as if its button were not a trigger, and so does its
// up (not the started trigger: passNotStartedTrigger). Suppressed, the click would be lost (no
// scroll, and no click resent at the up). As the Java version (9e2de36).
let private passNotStartedScroll (me: MouseEvent): nativeint option =
    if not (Ctx.isScrollMode()) then
        debug "pass (scroll mode not started)" me
        callNextHook()
    else
        None

let private checkTriggerScrollStart (me: MouseEvent): nativeint option =
    if Ctx.isTriggerEvent me then
        debug "start scroll mode" me
        Ctx.startScrollMode me.Info
        passNotStartedScroll me |> Option.orElseWith (fun () ->
            triggerStarted <- me
            suppress())
    else
        None

let private dragDefault info = ()
let mutable private drag: HookInfo -> unit = dragDefault

// The press of the drag trigger is a drag (its click is not resent at the up) if the cursor
// would have moved from the start point (scroll mode suppresses the moves), or if it has
// scrolled (Ctx.isScrolled: the scroll uses RawInput, a slight move with enhanced pointer
// precision can scroll without moving the cursor, and so can a move toward the edge of the
// screen from it). Not for a move of other software (injected), nor for one that neither moved
// the cursor nor scrolled: the click is resent then, the scroll and the click never both.
let private isDragged () =
    dragged || Ctx.isScrolled()

let private dragStart (info: HookInfo) =
    let sx, sy = Ctx.getScrollStartPoint()
    if not (Windows.isInjectedEvent (Move(info))) && (info.pt.x <> sx || info.pt.y <> sy) then
        if Ctx.isCursorChange() && not (Ctx.isVhAdjusterMode()) then
            WinCursor.changeV()

        drag <- dragDefault
        dragged <- true

let private startScrollDrag (me: MouseEvent): nativeint option =
    debug "start scroll mode (Drag)" me
    Ctx.startScrollMode me.Info
    passNotStartedScroll me |> Option.orElseWith (fun () ->
        triggerStarted <- me

        drag <- dragStart
        dragged <- false

        suppress())

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
    if Ctx.isDraggedLock() && isDragged() then
        debug "continueScrollDrag (Released)" me
        Ctx.setReleasedScrollMode()
        suppress()
    else
        None

let private exitAndResendDrag (me: MouseEvent): nativeint option =
    debug "exit scroll mode (Drag)" me
    let dragged = isDragged()
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
        // A wait in progress gets the down first, whatever the scroll mode: started during the
        // keyboard trigger's scroll mode, the second down ends it (see EventWaiter.waiterThread),
        // also if it is locked now (the keyboard trigger released quickly in between); it would
        // not be offered after checkExitScrollDown, and the wait would resend its down
        offerEventWaiter
        checkExitScrollDown
        passPressedScrollMode
        //passSingleEvent
        // Only to start a wait: scroll mode is left, and a wait in progress gets the down,
        // also on the excluded window (or the wait would resend its down after this one)
        passExcludedWindowLR
        checkTriggerWaitStart
        endNotTrigger
    ]

    getResult checkers me

let private lrUp (me: MouseEvent): nativeint =
    //Debug.WriteLine("lrUp")
    let checkers = [
        skipResendEventLR
        checkEscape
        // The down was resent: its up must be resent (and the down it follows), even without a
        // last event (skipFirstUp would pass this up, and the hook drop the down not sent yet)
        checkResentDown
        skipFirstUp
        checkSameLastEvent
        //checkSingleSuppressed
        checkPassedDown
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
        // Also before the middle click it would send (Send MiddleClick): the button as it is
        passExcludedWindow
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
        passExcludedWindow
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

// While the LR trigger waits (a down of the pair), another button (middle, X1, X2) ends the wait, as
// the Rust version does: the waiting down is resent at once (EventWaiter), not after the timeout
// nor after the other button's whole click, and its up is resent at its physical up
// (checkResentDown). The other button's down and up pass as they are (as the hook returns: the
// application gets them just before the resent down). Not resent after the waiting down: the
// button pressed again before the sender has sent its up would have its down before that up
// (X1 down, down, up). Not an up whose down reached no application (Windows.isLoneUp): dropped
// (Dispatcher), and the wait goes on, as for the other button of the pair (offerEventWaiter).
// Then the event is handled as when no wait is in progress: a down before the end of a released
// scroll (it ends the wait too, as in the Rust version, and the scroll, suppressed).
let private endWaitByOtherButton (me: MouseEvent): nativeint option =
    let isOther =
        match me with
        | MiddleEvent(_) | X1Event(_) | X2Event(_) -> true
        | _ -> false
    if isOther && EventWaiter.isWaitingOtherThan me && not (me.IsUp && Windows.isLoneUp me)
       && EventWaiter.endByResend me then
        debug "the LR wait ended (its down resent)" me
    None

// As the other handlers, the injected events (of other software, or resent by W10Wheel) pass
// first: they neither end the locked scroll mode nor initialize the state (Escape)
let private noneDown (me: MouseEvent): nativeint =
    //Debug.WriteLine("noneDown")
    let checkers = [
        skipResendEventSingle
        endWaitByOtherButton
        //resetLastFlags
        checkExitScrollDown
        endPass
    ]

    getResult checkers me

let private noneUp (me: MouseEvent): nativeint =
    //Debug.WriteLine("noneUp")
    let checkers = [
        skipResendEventSingle
        checkEscape
        checkSuppressedDown
        endWaitByOtherButton
        endPass
    ]

    getResult checkers me

// The handlers of the trigger: of the left and right buttons, of the other buttons
let private handlersOfTrigger () =
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

// Those of the default trigger at first: also used if the properties cannot be read at startup
// (nothing is applied then, see Ctx.loadProperties), then set by changeTrigger
let private __procDownLR, __procUpLR, __procDownS, __procUpS =
    let downLR, upLR, downS, upS = handlersOfTrigger ()
    ref downLR, ref upLR, ref downS, ref upS

// Before the handlers of the trigger (see canceledDowns)
let private procDown (proc: (MouseEvent -> nativeint) ref) (d: MouseEvent): nativeint =
    earlierDownSuppressed <- false
    if not (Windows.isInjectedEvent d) then
        let i = buttonIndex d
        canceledDowns.[i] <- false
        if i < resentDowns.Length then resentDowns.[i] <- false

        // Before the reset (see checkSameLastEvent)
        earlierDownSuppressed <- Ctx.LastFlags.IsSuppressedDown d

        // A new press: the flags of the earlier one (resent, suppressed, passed) are reset
        // whatever the trigger, for all the buttons (e.g. the up of a suppressed middle down
        // that was missed would make the up of the next middle click suppressed)
        Ctx.LastFlags.ResetFor d

    Volatile.Read(&proc.contents)(d)

let private procUpBody (proc: (MouseEvent -> nativeint) ref) (u: MouseEvent): nativeint =
    let i = buttonIndex u
    let physical = not (Windows.isInjectedEvent u)
    if physical && canceledDowns.[i] then
        canceledDowns.[i] <- false

        // As the handlers do for an up: Esc initializes the state (and passes the up), and the
        // up is the last event (or the next down of the button would be taken as the same)
        match checkEscape u with
        | Some(res) -> res
        | None ->
            debug "suppress (the down was canceled by changing the trigger)" u
            lastEvent <- u
            suppress().Value
    elif physical && i < resentDowns.Length && resentDowns.[i] then
        resentDowns.[i] <- false

        // As checkResentDown does (after Esc, as lrUp)
        match checkEscape u with
        | Some(res) -> res
        | None ->
            debug "resendUp and suppress (the down was resent before changing the trigger)" u
            lastEvent <- u
            Windows.resendUp u
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
    // The moves of other software (injected) and those at the point of the waiting down (the
    // cursor has not moved) do not end the LR trigger wait (its down would be resent as for a
    // drag): they pass
    elif Windows.isInjectedEvent (Move(info)) || EventWaiter.isWaitingAt (Move(info)) then
        callNextHook().Value
    elif EventWaiter.offer (Move(info)) then
        Debug.WriteLine("success to offer: Move")
        suppress().Value
    else
        callNextHook().Value

let private changeTrigger (changed: bool): unit =
    Debug.WriteLine("changeTrigger: EventHandler")

    let downLR, upLR, downS, upS = handlersOfTrigger ()

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

        // The downs resent alone (the wait timed out or was ended by a move): their up is resent
        // before the new handlers, which would not resend it (see resentDowns)
        Ctx.LastFlags.TakeResentDowns() |> Array.iteri (fun i b ->
            if b then resentDowns.[i] <- true)

        triggerStarted <- NonEvent
        drag <- dragDefault
        dragged <- false
        secondTriggerUp <- false

        // Some handlers do not record the last event (none trigger): the event recorded by the
        // old ones could stay a down after its up, so that the next down was taken as the same
        lastEvent <- NonEvent

let setChangeTrigger () =
    Ctx.setChangeTrigger changeTrigger




