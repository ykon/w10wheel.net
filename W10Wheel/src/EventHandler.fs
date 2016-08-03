module EventHandler

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop

open Mouse

let mutable private __callNextHook: (unit -> nativeint) = fun _ -> IntPtr(0)
let setCallNextHook (f: unit -> nativeint): unit = __callNextHook <- f

let private callNextHook () = Some(__callNextHook())
let private suppress () = Some(IntPtr(1))

let mutable private lastEvent: MouseEvent = NoneEvent
let mutable private lastResendEvent: MouseEvent = NoneEvent
let mutable private dragged = false

(*
let checkSkip (me: MouseEvent): nativeint option =
    if Ctx.checkSkip me then
        Debug.WriteLine(sprintf "skip resend event: %s" me.name)
        callNextHook()
    else
        None
*)

let private resetLastFlags (me: MouseEvent): nativeint option =
    Ctx.LastFlags.Reset me
    None

let private skipResendEventLR (me: MouseEvent): nativeint option =
    if Windows.isResendEvent me then
        match lastResendEvent, me with
        | LeftUp(_), LeftUp(_) | RightUp(_), RightUp(_) ->
            Debug.WriteLine(sprintf "re-resend event: %s" me.Name)
            Windows.resendUp(me)
            suppress()
        | _ ->
            Debug.WriteLine(sprintf "skip resend event: %s" me.Name)
            lastResendEvent <- me
            callNextHook()
    else
        None

let private skipResendEventSingle (me: MouseEvent): nativeint option =
    if Windows.isResendEvent me then
        Debug.WriteLine(sprintf "skip resend event: %s" me.Name)
        callNextHook()
    else
        None

let private skipFirstUp (me: MouseEvent): nativeint option =
    if lastEvent.IsNone then
        Debug.WriteLine(sprintf "skip first Up: %s" me.Name)
        callNextHook()
    else
        None

(*
let private skipFirstUpOrSingle (me: MouseEvent): nativeint option =
    if lastEvent.IsNone || lastEvent.IsSingle then
        Debug.WriteLine(sprintf "skip first Up or Single: %s" me.Name)
        callNextHook()
    else
        None

let private skipFirstUpOrLR (me: MouseEvent): nativeint option =
    if lastEvent.IsNone || lastEvent.IsLR then
        Debug.WriteLine(sprintf "skip first Up or LR: %s" me.Name)
        callNextHook()
    else
        None
*)

let private checkSameLastEvent (me: MouseEvent): nativeint option =
    if me.SameEvent lastEvent then
        Debug.WriteLine(sprintf "same last event: %s" me.Name)
        callNextHook()
        //suppress()
    else
        lastEvent <- me
        None

(*
let checkCancelUp (me: MouseEvent): nativeint option =
    if Ctx.checkCancelUp me then
        Debug.WriteLine(sprintf "cancel up: %s: " me.Name)
        suppress()
    else
        None
*)

let private checkExitScrollDown (me: MouseEvent): nativeint option =
    if Ctx.isScrollMode() then
        Debug.WriteLine(sprintf "exit scroll mode %s: " me.Name)
        Ctx.exitScrollMode()
        Ctx.LastFlags.SetSuppressed me
        suppress()
    else
        None

let private checkExitScrollUp (me: MouseEvent): nativeint option =
    if Ctx.isScrollMode() then
        if Ctx.checkExitScroll me.Info.time then
            Debug.WriteLine(sprintf "exit scroll mode: %s" me.Name)
            Ctx.exitScrollMode()
        else
            Debug.WriteLine(sprintf "continue scroll mode: %s" me.Name)

        suppress()
    else
        None

let private passSingleTrigger (me: MouseEvent): nativeint option =
    if Ctx.isSingleTrigger() then
        Debug.WriteLine(sprintf "pass: single trigger: %s" me.Name)
        callNextHook()
    else
        None

let private offerEventWaiter (me: MouseEvent): nativeint option =
    if EventWaiter.offer me then
        Debug.WriteLine(sprintf "success to offer: %s" me.Name)
        suppress()
    else
        None

let private checkDownSuppressed (up: MouseEvent): nativeint option =
    let suppressed = Ctx.LastFlags.IsDownSuppressed up

    if suppressed then
        Debug.WriteLine(sprintf "suppress (checkDownSuppressed): %s" up.Name)
        suppress()
    else
        None

let private checkDownResent (up: MouseEvent): nativeint option =
    let resent = Ctx.LastFlags.IsDownResent up

    if resent then
        Debug.WriteLine(sprintf "resendUp and suppress (checkDownResent): %s" up.Name)
        Windows.resendUp up
        suppress()
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
    if Ctx.isSendMiddleClick() && (Windows.getAsyncShiftState() || Windows.getAsyncCtrlState() || Windows.getAsyncAltState()) then
        Debug.WriteLine(sprintf "send middle click: %s" me.Name)
        Windows.resendClick(MiddleClick(me.Info))
        Ctx.LastFlags.SetSuppressed(me)
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
    if Ctx.isCursorChange() then
        WinCursor.change()

    drag <- dragDefault
    dragged <- true

let private startScrollDrag (me: MouseEvent): nativeint option =
    Debug.WriteLine(sprintf "start scroll mode: %s" me.Name)
    Ctx.startScrollMode me.Info

    drag <- dragStart
    dragged <- false

    suppress()

let private continueScrollDrag (me: MouseEvent): nativeint option =
    if Ctx.isDraggedLock() && dragged then
        Debug.WriteLine(sprintf "continueScrollDrag: %s" me.Name)
        suppress()
    else
        None

let private exitAndResendDrag (me: MouseEvent): nativeint option =
    Debug.WriteLine(sprintf "exit scroll mode: %s" me.Name)
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

let private doubleDown (me: MouseEvent): nativeint =
    let checkers = [
        skipResendEventLR
        checkSameLastEvent
        resetLastFlags
        checkExitScrollDown
        offerEventWaiter
        checkTriggerWaitStart
        endNotTrigger
    ]

    getResult checkers me

let private doubleUp (me: MouseEvent): nativeint =
    let checkers = [
        skipResendEventLR
        skipFirstUp
        checkSameLastEvent
        checkExitScrollUp
        checkDownResent
        offerEventWaiter
        checkDownSuppressed
        endNotTrigger
    ]

    getResult checkers me

let private singleDown (me: MouseEvent): nativeint =
    let checkers = [
        skipResendEventSingle
        checkSameLastEvent
        resetLastFlags
        checkExitScrollDown
        passNotTrigger
        checkKeySendMiddle
        checkTriggerScrollStart
        endIllegalState
    ]

    getResult checkers me

let private singleUp (me: MouseEvent): nativeint =
    let checkers = [
        skipResendEventSingle
        skipFirstUp
        checkSameLastEvent
        checkDownSuppressed
        passNotTrigger
        checkExitScrollUp
        endIllegalState
    ]

    getResult checkers me

let private dragDown (me: MouseEvent): nativeint =
    let checkers = [
        skipResendEventSingle
        checkSameLastEvent
        resetLastFlags
        checkExitScrollDown
        passNotDragTrigger
        startScrollDrag
    ]

    getResult checkers me

let private dragUp (me: MouseEvent): nativeint =
    let checkers = [
        skipResendEventSingle
        skipFirstUp
        checkSameLastEvent
        checkDownSuppressed
        passNotDragTrigger
        continueScrollDrag
        exitAndResendDrag
    ]

    getResult checkers me

let private noneDown (me: MouseEvent): nativeint =
    let checkers = [
        resetLastFlags
        checkExitScrollDown
        endPass
    ]

    getResult checkers me

let private noneUp (me: MouseEvent): nativeint =
    let checkers = [
        checkDownSuppressed
        endPass
    ]

    getResult checkers me

let private dispatchDown (d: MouseEvent): nativeint = 
    if Ctx.isDoubleTrigger() then doubleDown d
    elif Ctx.isSingleTrigger() then singleDown d
    elif Ctx.isDragTrigger() then dragDown d
    else noneDown d

let private dispatchUp (u: MouseEvent): nativeint =
    if Ctx.isDoubleTrigger() then doubleUp u
    elif Ctx.isSingleTrigger() then singleUp u
    elif Ctx.isDragTrigger() then dragUp u
    else noneUp u

let private dispatchDownS (d: MouseEvent): nativeint = 
    if Ctx.isSingleTrigger() then singleDown d
    elif Ctx.isDragTrigger() then dragDown d
    else noneDown d

let private dispatchUpS (u: MouseEvent): nativeint =
    if Ctx.isSingleTrigger() then singleUp u
    elif Ctx.isDragTrigger() then dragUp u
    else noneUp u

let leftDown (info: HookInfo) =
    //Debug.WriteLine("LeftDown")
    let ld = LeftDown(info)
    dispatchDown ld

let leftUp (info: HookInfo) =
    //Debug.WriteLine("LeftUp")
    let lu = LeftUp(info)
    dispatchUp lu
    
let rightDown(info: HookInfo) =
    //Debug.WriteLine("RightDown")
    let rd = RightDown(info)
    dispatchDown rd

let rightUp (info: HookInfo) =
    //Debug.WriteLine("RightUp")
    let ru = RightUp(info)
    dispatchUp ru

let middleDown (info: HookInfo) =
    let md = MiddleDown(info)
    dispatchDownS md

let middleUp (info: HookInfo) =
    let mu = MiddleUp(info)
    dispatchUpS mu

let xDown (info: HookInfo) =
    let xd = if Mouse.isXButton1(info.mouseData) then X1Down(info) else X2Down(info)
    dispatchDownS xd

let xUp (info: HookInfo) =
    let xu = if Mouse.isXButton1(info.mouseData) then X1Up(info) else X2Up(info)
    dispatchUpS xu

let move (info: HookInfo) =
    //Debug.WriteLine "Move: test"
    if Ctx.isScrollMode() then
        drag info
        Windows.sendWheel info.pt
        suppress().Value
    elif EventWaiter.offer (Move(info)) then
        Debug.WriteLine("success to offer: Move")
        suppress().Value
    else
        callNextHook().Value



