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

let private callNextHook () = Some(__callNextHook())
let private suppress () = Some(IntPtr(1))

let mutable private lastEvent: MouseEvent = NonEvent
let private lastResendLeftEvent: MouseEvent ref = ref NonEvent
let private lastResendRightEvent: MouseEvent ref = ref NonEvent

let mutable private resentDownUp = false
let mutable private secondTriggerUp = false
let mutable private dragged = false

let private initState () =
    lastEvent <- NonEvent
    lastResendLeftEvent := NonEvent
    lastResendRightEvent := NonEvent
    resentDownUp <- false
    secondTriggerUp <- false
    dragged <- false

let setInitStateMEH () =
    Ctx.setInitStateMEH initState

let private resetLastFlagsLR (me: MouseEvent): nativeint option =
    Ctx.LastFlags.ResetLR me
    None

let private getLastResendEvent me =
    match me with
    | LeftEvent(_) -> lastResendLeftEvent
    | RightEvent(_) -> lastResendRightEvent
    | _ -> raise (InvalidOperationException())

let private isCorrectOrder pre cur =
    match pre, cur with
    | NonEvent, LeftUp(_) | LeftUp(_), LeftUp(_) | NonEvent, RightUp(_) | RightUp(_), RightUp(_) ->
        false
    | _ -> true

let private checkCorrectOrder me =
    isCorrectOrder !(getLastResendEvent me) me

let private debug (msg: string) (me: MouseEvent) =
    Debug.WriteLine(msg + ": " + me.Name)

let private skipResendEventLR (me: MouseEvent): nativeint option =
    let pass () =
        debug "pass resend event" me
        (getLastResendEvent me) := me
        callNextHook()

    if not (Windows.isInjectedEvent me) then
        None
    elif Windows.isResendClickEvent me then
        debug "pass resendClick event" me
        callNextHook()
    elif Windows.isResendEvent me then
        if resentDownUp then
            debug "ResendEvent: resentDownUp" me
            resentDownUp <- false

            if checkCorrectOrder me then
                pass()
            else
                debug "Bad: resendUp retry" me
                Thread.Sleep(1)
                Windows.resendUp me
                suppress()
        else
            pass()

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

let private passPressedScrollMode (down: MouseEvent): nativeint option =
    if Ctx.isPressedScrollMode() then
        debug "pass scroll mode (Pressed)" down
        Ctx.LastFlags.SetPassed(down)
        callNextHook()
    else
        None

let private checkExitScrollUp (me: MouseEvent): nativeint option =
    if Ctx.isPressedScrollMode() then
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
    if Ctx.isPressedScrollMode() then
        if not secondTriggerUp then
            debug "ignore first up" up
        elif Ctx.checkExitScroll up.Info.time then
            debug "exit scroll mode (Pressed)" up
            Ctx.exitScrollMode()
        else
            debug "continue scroll mode (Released)" up
            Ctx.setReleasedScrollMode()

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
    if EventWaiter.offer me then
        debug "success to offer" me
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
        resentDownUp <- true
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

    drag <- dragStart
    dragged <- false

    suppress()

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
        resetLastFlagsLR
        checkExitScrollDown
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

let private procDownLR (d: MouseEvent): nativeint =
    Volatile.Read(__procDownLR)(d)

let private procUpLR (d: MouseEvent): nativeint =
    Volatile.Read(__procUpLR)(d)

let private procDownS (d: MouseEvent): nativeint =
    Volatile.Read(__procDownS)(d)

let private procUpS (d: MouseEvent): nativeint =
    Volatile.Read(__procUpS)(d)

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

let private changeTrigger (): unit =
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

let setChangeTrigger () =
    Ctx.setChangeTrigger changeTrigger




