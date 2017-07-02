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

let mutable private lastEvent: MouseEvent = NoneEvent
let private lastResendLeftEvent: MouseEvent ref = ref NoneEvent
let private lastResendRightEvent: MouseEvent ref = ref NoneEvent

let mutable private resentDownUp = false
let mutable private secondTriggerUp = false
let mutable private dragged = false

let private initState () =
    lastEvent <- NoneEvent
    lastResendLeftEvent := NoneEvent
    lastResendRightEvent := NoneEvent
    resentDownUp <- false
    secondTriggerUp <- false
    dragged <- false

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
    | NoneEvent, LeftUp(_) | LeftUp(_), LeftUp(_) | NoneEvent, RightUp(_) | RightUp(_), RightUp(_) ->
        false
    | _ -> true

let private checkCorrectOrder me =
    isCorrectOrder !(getLastResendEvent me) me

let private skipResendEventLR (me: MouseEvent): nativeint option =
    let pass () =
        Debug.WriteLine(sprintf "pass resend event: %s" me.Name)
        (getLastResendEvent me) := me
        callNextHook()

    if not (Windows.isInjectedEvent me) then
        None
    elif Windows.isResendClickEvent me then
        Debug.WriteLine(sprintf "pass resendClick event: %s" me.Name)
        callNextHook()
    elif Windows.isResendEvent me then
        if resentDownUp then
            Debug.WriteLine(sprintf "ResendEvent: resentDownUp: %s" me.Name)
            resentDownUp <- false

            if checkCorrectOrder me then
                pass()
            else
                Debug.WriteLine(sprintf "Bad: resendUp retry: %s" me.Name)
                Thread.Sleep(1)
                Windows.resendUp me
                suppress()
        else
            pass()

    else
        Debug.WriteLine(sprintf "pass other software event: %s" me.Name)
        callNextHook()

let private skipResendEventSingle (me: MouseEvent): nativeint option =
    if not (Windows.isInjectedEvent me) then
        None
    elif Windows.isResendClickEvent me then
        Debug.WriteLine(sprintf "pass resendClick event: %s" me.Name)
        callNextHook()
    else
        Debug.WriteLine(sprintf "pass other software event: %s" me.Name)
        callNextHook()

let private checkEscape (me: MouseEvent): nativeint option =
    if Windows.checkEscState() then
        Debug.WriteLine(sprintf "init state and exit scroll: %s" me.Name)
        initState()
        Ctx.LastFlags.Init()
        Ctx.exitScrollMode()
        EventWaiter.offer(Cancel) |> ignore
        callNextHook()
    else
        None

let private skipFirstUp (me: MouseEvent): nativeint option =
    if lastEvent.IsNone then
        Debug.WriteLine(sprintf "skip first Up: %s" me.Name)
        callNextHook()
    else
        None

let private checkSameLastEvent (me: MouseEvent): nativeint option =
    if me.SameEvent lastEvent then
        Debug.WriteLine(sprintf "same last event: %s" me.Name)
        callNextHook()
        //suppress()
    else
        lastEvent <- me
        None

let private checkExitScrollDown (me: MouseEvent): nativeint option =
    if Ctx.isReleasedScrollMode() then
        Debug.WriteLine(sprintf "exit scroll mode (Released): %s" me.Name)
        Ctx.exitScrollMode()
        Ctx.LastFlags.SetSuppressed me
        suppress()
    else
        None

let private passPressedScrollMode (down: MouseEvent): nativeint option =
    if Ctx.isPressedScrollMode() then
        Debug.WriteLine(sprintf "pass scroll mode (Pressed): %s" down.Name)
        Ctx.LastFlags.SetPassed(down)
        callNextHook()
    else
        None

let private checkExitScrollUp (me: MouseEvent): nativeint option =
    if Ctx.isPressedScrollMode() then
        if Ctx.checkExitScroll me.Info.time then
            Debug.WriteLine(sprintf "exit scroll mode (Pressed): %s" me.Name)
            Ctx.exitScrollMode()
        else
            Debug.WriteLine(sprintf "continue scroll mode (Released): %s" me.Name)
            Ctx.setReleasedScrollMode()

        suppress()
    else
        None

let private checkExitScrollUpLR (up: MouseEvent): nativeint option =
    if Ctx.isPressedScrollMode() then
        if not secondTriggerUp then
            Debug.WriteLine(sprintf "ignore first up: %s" up.Name)
        elif Ctx.checkExitScroll up.Info.time then
            Debug.WriteLine(sprintf "exit scroll mode (Pressed): %s" up.Name)
            Ctx.exitScrollMode()
        else
            Debug.WriteLine(sprintf "continue scroll mode (Released): %s" up.Name)
            Ctx.setReleasedScrollMode()

        secondTriggerUp <- not secondTriggerUp
        suppress()
    else
        None

let private checkStartingScroll (up: MouseEvent): nativeint option =
    if Ctx.isStartingScrollMode() then
        Debug.WriteLine("check starting scroll")

        if not secondTriggerUp then
            Debug.WriteLine(sprintf "ignore first up (starting): %s" up.Name)
            Thread.Sleep(1)
        else
            Debug.WriteLine(sprintf "exit scroll mode (starting): %s" up.Name)
            Thread.Sleep(1)
            Ctx.exitScrollMode()

        secondTriggerUp <- not secondTriggerUp
        suppress()
    else
        None

let private passSingleEvent (me: MouseEvent): nativeint option =
    if me.IsSingle then
        Debug.WriteLine(sprintf "pass single event: %s" me.Name)
        callNextHook()
    else
        None

let private offerEventWaiter (me: MouseEvent): nativeint option =
    if EventWaiter.offer me then
        Debug.WriteLine(sprintf "success to offer: %s" me.Name)
        suppress()
    else
        None

let private checkSuppressedDown (up: MouseEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_SuppressedDown up then
        Debug.WriteLine(sprintf "suppress (checkSuppressedDown): %s" up.Name)
        suppress()
    else
        None

let private checkResentDown (up: MouseEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_ResentDown up then
        Debug.WriteLine(sprintf "resendUp and suppress (checkResentDown): %s" up.Name)
        resentDownUp <- true
        Windows.resendUp up
        suppress()
    else
        None

let private checkPassedDown (up: MouseEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_PassedDown up then
        Debug.WriteLine(sprintf "pass (checkPassedDown): %s" up.Name)
        callNextHook()
    else
        None

let private checkTriggerWaitStart (me: MouseEvent): nativeint option =
    if Ctx.isLRTrigger() || Ctx.isTriggerEvent me then
        Debug.WriteLine(sprintf "start wait trigger: %s" me.Name)
        EventWaiter.start me
        suppress()
    else
        None

let private checkKeySendMiddle (me: MouseEvent): nativeint option =
    if Ctx.isSendMiddleClick() && (Windows.checkShiftState() || Windows.checkCtrlState() || Windows.checkAltState()) then
        Debug.WriteLine(sprintf "send middle click: %s" me.Name)
        Windows.resendClick(MiddleClick(me.Info))
        Ctx.LastFlags.SetSuppressed me
        suppress()
    else
        None

let private checkTriggerScrollStart (me: MouseEvent): nativeint option =
    if Ctx.isTriggerEvent me then
        Debug.WriteLine(sprintf "start scroll mode: %s" me.Name)
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
    Debug.WriteLine(sprintf "start scroll mode (Drag): %s" me.Name)
    Ctx.startScrollMode me.Info

    drag <- dragStart
    dragged <- false

    suppress()

let private continueScrollDrag (me: MouseEvent): nativeint option =
    if Ctx.isDraggedLock() && dragged then
        Debug.WriteLine(sprintf "continueScrollDrag (Released): %s" me.Name)
        Ctx.setReleasedScrollMode()
        suppress()
    else
        None

let private exitAndResendDrag (me: MouseEvent): nativeint option =
    Debug.WriteLine(sprintf "exit scroll mode (Drag): %s" me.Name)
    Ctx.exitScrollMode()

    if not dragged then
        Debug.WriteLine(sprintf "resend click: %s" me.Name)

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
        Debug.WriteLine(sprintf "pass not trigger: %s" me.Name)
        callNextHook()
    else
        None

let private passNotTriggerLR (me: MouseEvent): nativeint option =
    if not (Ctx.isLRTrigger()) && not (Ctx.isTriggerEvent me) then
        Debug.WriteLine(sprintf "pass not trigger: %s" me.Name)
        callNextHook()
    else
        None

let private passNotDragTrigger (me: MouseEvent): nativeint option =
    if not (Ctx.isDragTriggerEvent me) then
       Debug.WriteLine(sprintf "pass not trigger: %s" me.Name)
       callNextHook()
    else
        None 

let private endCallNextHook (me:MouseEvent) (msg:string): nativeint option =
    Debug.WriteLine(msg)
    callNextHook()

let private endNotTrigger (me: MouseEvent): nativeint option =
    endCallNextHook me (sprintf "endNotTrigger: %s" me.Name)

let private endPass (me: MouseEvent): nativeint option =
    endCallNextHook me (sprintf "endPass: %s" me.Name)

let private endUnknownEvent (me: MouseEvent): nativeint option =
    endCallNextHook me (sprintf "unknown event: %s" me.Name)

let private endAfterTimeoutOrMove (me: MouseEvent): nativeint option =
    endCallNextHook me (sprintf "pass after timeout or move?: %s" me.Name)

let private endSuppress (me:MouseEvent) (msg:string option): nativeint option =
    Option.iter (fun s -> Debug.WriteLine(s)) msg
    suppress()

let private endIllegalState (me: MouseEvent): nativeint option =
    Debug.WriteLine(sprintf "illegal state: %s" me.Name)
    suppress()

type Checkers = (MouseEvent -> nativeint option) list

let rec private getResult (cs:Checkers) (me:MouseEvent) =
    match cs with
    | f :: fs ->
        let res = f me
        if res.IsSome then res.Value else getResult fs me
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




