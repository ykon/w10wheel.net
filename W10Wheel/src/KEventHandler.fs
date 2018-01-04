module KEventHandler

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open Keyboard

let mutable private __callNextHook: (unit -> nativeint) = fun _ -> IntPtr(0)
let setCallNextHook (f: unit -> nativeint): unit = __callNextHook <- f

let private callNextHook () = Some(__callNextHook())
let private suppress () = Some(IntPtr(1))

let mutable private lastEvent: KeyboardEvent = NonEvent

let private initState () =
    lastEvent <- NonEvent

let setInitStateKEH () =
    Ctx.setInitStateKEH initState

let private debug (msg: string) (ke: KeyboardEvent) =
    Debug.WriteLine(msg + ": " + ke.Name)

let private skipFirstUp (ke: KeyboardEvent): nativeint option =
    if lastEvent.IsNone then
        debug "skip first Up" ke
        callNextHook()
    else
        None

let private checkSameLastEvent (ke: KeyboardEvent): nativeint option =
    if ke.Same lastEvent && Ctx.isScrollMode() then
        debug "same last event" ke
        suppress()
    else
        lastEvent <- ke
        None

let private passNotTrigger (ke: KeyboardEvent): nativeint option =
    if not (Ctx.isTriggerKey ke) then
        debug "pass not trigger" ke
        callNextHook()
    else
        None

let private checkTriggerScrollStart (ke: KeyboardEvent): nativeint option =
    if Ctx.isTriggerKey ke then
        debug "start scroll mode" ke
        Ctx.startScrollModeK ke.Info
        suppress()
    else
        None

let private checkExitScrollDown (ke: KeyboardEvent): nativeint option =
    if Ctx.isReleasedScrollMode() then
        debug "exit scroll mode (Released)" ke
        Ctx.exitScrollMode()
        Ctx.LastFlags.SetSuppressed ke
        suppress()
    else
        None

let private checkExitScrollUp (ke: KeyboardEvent): nativeint option =
    if Ctx.isPressedScrollMode() then
        if Ctx.checkExitScroll ke.Info.time then
            debug "exit scroll mode (Pressed)" ke
            Ctx.exitScrollMode()
        else
            debug "continue scroll mode (Released)" ke
            Ctx.setReleasedScrollMode()

        suppress()
    else
        None

let private checkSuppressedDown (up: KeyboardEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_SuppressedDown up then
        debug "suppress (checkSuppressedDown)" up
        suppress()
    else
        None

let private endCallNextHook (ke:KeyboardEvent) (msg:string): nativeint option =
    Debug.WriteLine(msg)
    callNextHook()

let private endPass (ke: KeyboardEvent): nativeint option =
    endCallNextHook ke ("endPass: " + ke.Name)

let private endIllegalState (ke: KeyboardEvent): nativeint option =
    debug "illegal state" ke
    suppress()

type Checkers = (KeyboardEvent -> nativeint option) list

let rec private getResult (cs:Checkers) (ke:KeyboardEvent) =
    match cs with
    | f :: fs ->
        match f ke with
        | Some(res) -> res
        | None -> getResult fs ke
    | _ -> raise (ArgumentException())

let private singleDown (ke: KeyboardEvent): nativeint =
    let checkers = [
        checkSameLastEvent
        checkExitScrollDown
        checkTriggerScrollStart
        endIllegalState
    ]

    getResult checkers ke

let private singleUp (ke: KeyboardEvent) =
    let checkers = [
        skipFirstUp
        checkSameLastEvent
        checkSuppressedDown
        checkExitScrollUp
        endIllegalState
    ]

    getResult checkers ke

let private noneDown (ke: KeyboardEvent): nativeint =
    let checkers = [
        checkExitScrollDown
        endPass
    ]

    getResult checkers ke

let private noneUp (ke: KeyboardEvent): nativeint =
    let checkers = [
        checkSuppressedDown
        endPass
    ]

    getResult checkers ke

let keyDown (info: KHookInfo) =
    //Debug.WriteLine(sprintf "keyDown: %d" info.vkCode)

    let kd = KeyDown(info)
    if Ctx.isTriggerKey kd then singleDown kd else noneDown kd

let keyUp (info: KHookInfo) =
    //Debug.WriteLine(sprintf "keyUp: %d" info.vkCode)

    let ku = KeyUp(info)
    if Ctx.isTriggerKey ku then singleUp ku else noneUp ku


