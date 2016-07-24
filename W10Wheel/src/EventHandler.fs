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

let private __callNextHook: (unit -> nativeint) ref = ref (fun () -> IntPtr(0))
let setCallNextHook (f: unit -> nativeint): unit = __callNextHook := f

let private callNextHook () = Some(__callNextHook.Value())
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

let private skipResendEvent (me: MouseEvent): nativeint option =
    if Windows.isResendEvent me then
        Debug.WriteLine(sprintf "skip resend event: %s" me.Name)
        lastResendEvent <- me
        callNextHook()
    else
        None

let private skipFirstUpOrSingle (me: MouseEvent): nativeint option =
    if lastEvent = NoneEvent || lastEvent.IsSingle then
        Debug.WriteLine(sprintf "skip first up event: %s" me.Name)
        callNextHook()
    else
        None

let private skipFirstUpOrLR (me: MouseEvent): nativeint option =
    if lastEvent = NoneEvent || not (lastEvent.IsSingle) then
        Debug.WriteLine(sprintf "skip first single event: %s" me.Name)
        callNextHook()
    else
        None

let private checkSameLastEvent (me: MouseEvent): nativeint option =
    if me.Same lastEvent then
        Debug.WriteLine(sprintf "same last event: %s" me.Name)
        callNextHook()
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

let private retryOffer (me: MouseEvent): bool =
    let rec loop b i =
        if b then
            Debug.WriteLine(sprintf "retryOffer: %d" i)
            true
        elif i = 0 then
            Debug.WriteLine("retryOffer failed")
            false
        else
            loop (EventWaiter.offer me) (i - 1)

    loop false 3


let private offerEventWaiter (me: MouseEvent): nativeint option =
    if EventWaiter.isWaiting() then
        if retryOffer me then
            Debug.WriteLine(sprintf "success to offer: %s" me.Name)
            suppress()
        else
            Debug.WriteLine(sprintf "fail to offer: %s" me.Name)
            None
    else
        None

let private checkDownSuppressed (up: MouseEvent): nativeint option =
    let suppressed = Ctx.LastFlags.IsDownSuppressed up

    if suppressed then
        Debug.WriteLine(sprintf "after suppressed down event: %s" up.Name)
        suppress()
    else
        None

let private checkDownResent (up: MouseEvent): nativeint option =
    let resent = Ctx.LastFlags.IsDownResent up

    if resent then
        if up.SameButton lastResendEvent then
            Debug.WriteLine(sprintf "pass (checkDownResent): %s" up.Name)
            callNextHook()
        else
            Debug.WriteLine(sprintf "resend (checkDownResent): %s" up.Name)
            Windows.resendUp up
            suppress()
    else
        None

let private checkTriggerWaitStart (me: MouseEvent): nativeint option =
    if Ctx.isLRTrigger() || Ctx.isTriggerEvent me then
        Debug.WriteLine(sprintf "start wait trigger: %s" me.Name)
        //Async.Start (EventWaiter.start me)
        EventWaiter.start me
        suppress()
    else
        None

let private checkKeySendMiddle (me: MouseEvent): nativeint option =
    if Windows.getAsyncShiftState() || Windows.getAsyncCtrlState() || Windows.getAsyncAltState() then
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

let private endCallNextHook (me:MouseEvent) (msg:string option): nativeint option =
    Option.iter (fun s -> Debug.WriteLine(s)) msg
    callNextHook()

let private endNotTrigger (me: MouseEvent): nativeint option =
    endCallNextHook me (Some(sprintf "pass not trigger: %s" me.Name))

let private endUnknownEvent (me: MouseEvent): nativeint option =
    endCallNextHook me (Some(sprintf "unknown event: %s" me.Name))

let private endAfterTimeoutOrMove (me: MouseEvent): nativeint option =
    endCallNextHook me (Some(sprintf "pass after timeout or move?: %s" me.Name))

let private endSuppress (me:MouseEvent) (msg:string option): nativeint option =
    Option.iter (fun s -> Debug.WriteLine(s)) msg
    suppress()

let private endIllegalState (me: MouseEvent): nativeint option =
    Debug.WriteLine(sprintf "illegal state: %s" me.Name)
    suppress()

type Checkers = (MouseEvent -> nativeint option) list

let rec private getResultL (cs:Checkers) (me:MouseEvent) =
    match cs with
    | f :: fs ->
        let res = f me
        if res.IsSome then res else getResultL fs me
    | _ -> raise (ArgumentException())

(*
let getResult (cs:Checkers) (me:MouseEvent) =
    List.toSeq cs |> Seq.choose (fun f -> f me) |> Seq.head

let getResultBranch (cs:Checkers) (me:MouseEvent) =
    List.toSeq cs |> Seq.map (fun f -> f me) |> Seq.find (fun o -> o.IsSome)
*)

let private branchDragDown (me: MouseEvent): nativeint option =
    if Ctx.isDragTrigger() then
        Debug.WriteLine(sprintf "branch LR only down: %s" me.Name)
        let cs = [passNotDragTrigger
                  startScrollDrag]
        
        getResultL cs me
    else
        None

let private branchDragUp (me: MouseEvent): nativeint option =
    if Ctx.isDragTrigger() then
        Debug.WriteLine(sprintf "branch LR only up: %s" me.Name)
        let cs = [checkDownSuppressed
                  passNotDragTrigger
                  continueScrollDrag
                  exitAndResendDrag]

        getResultL cs me
    else
        None

let private lrDown (me: MouseEvent): nativeint =
    let checkers = [
        skipResendEvent
        checkSameLastEvent
        resetLastFlags
        checkExitScrollDown
        branchDragDown
        passSingleTrigger
        offerEventWaiter
        checkTriggerWaitStart
        endNotTrigger
    ]

    (getResultL checkers me).Value

let private lrUp (me: MouseEvent): nativeint =
    let checkers = [
        skipResendEvent
        skipFirstUpOrSingle
        checkSameLastEvent
        branchDragUp
        passSingleTrigger
        checkExitScrollUp
        passNotTriggerLR
        checkDownResent
        offerEventWaiter
        checkDownSuppressed
        endUnknownEvent
    ]

    (getResultL checkers me).Value

let leftDown (info: HookInfo) =
    //Debug.WriteLine("LeftDown")
    lrDown(LeftDown(info))

let rightDown(info: HookInfo) =
    //Debug.WriteLine("RightDown")
    lrDown(RightDown(info))

let leftUp (info: HookInfo) =
    //Debug.WriteLine("LeftUp")
    lrUp(LeftUp(info))

let rightUp (info: HookInfo) =
    //Debug.WriteLine("RightUp")
    lrUp(RightUp(info))

let private singleDown (me: MouseEvent): nativeint =
    let checkers = [
        skipResendEvent
        checkSameLastEvent
        resetLastFlags
        checkExitScrollDown
        branchDragDown
        passNotTrigger
        checkKeySendMiddle
        checkTriggerScrollStart
        endIllegalState
    ]

    (getResultL checkers me).Value

let private singleUp (me: MouseEvent): nativeint =
    let checkers = [
        skipResendEvent
        skipFirstUpOrLR
        checkSameLastEvent
        branchDragUp
        passNotTrigger
        checkExitScrollUp
        checkDownSuppressed
        endIllegalState
    ]

    (getResultL checkers me).Value

let middleDown (info: HookInfo) =
    singleDown (MiddleDown(info))

let middleUp (info: HookInfo) =
    singleUp (MiddleUp(info))

let xDown (info: HookInfo) =
    let me = if Mouse.isXButton1(info.mouseData) then X1Down(info) else X2Down(info)
    singleDown(me)

let xUp (info: HookInfo) =
    let me = if Mouse.isXButton1(info.mouseData) then X1Down(info) else X2Down(info)
    singleUp(me)

let move (info: HookInfo) =
    //Debug.WriteLine "Move: test"
    if Ctx.isScrollMode() then
        drag info
        Windows.sendWheel info.pt
        suppress().Value
    elif EventWaiter.isWaiting() && EventWaiter.offer (Move(info)) then
        Debug.WriteLine("success to offer: Move")
        suppress().Value
    else
        callNextHook().Value



