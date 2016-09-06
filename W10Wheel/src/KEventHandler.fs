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

let mutable private lastEvent: KeyboardEvent = NoneEvent

let private skipFirstUp (ke: KeyboardEvent): nativeint option =
    if lastEvent.IsNone then
        Debug.WriteLine(sprintf "skip first Up: %s" ke.Name)
        callNextHook()
    else
        None

let private checkSameLastEvent (ke: KeyboardEvent): nativeint option =
    if ke.Same lastEvent && Ctx.isScrollMode() then
        Debug.WriteLine(sprintf "same last event: %s" ke.Name)
        suppress()
    else
        lastEvent <- ke
        None

let private passNotTrigger (ke: KeyboardEvent): nativeint option =
    if not (Ctx.isTriggerKey ke) then
        Debug.WriteLine(sprintf "pass not trigger: %s" ke.Name)
        callNextHook()
    else
        None

let private checkTriggerScrollStart (ke: KeyboardEvent): nativeint option =
    if Ctx.isTriggerKey ke then
        Debug.WriteLine(sprintf "start scroll mode: %s" ke.Name)
        Ctx.startScrollModeK ke.Info
        suppress()
    else
        None

let private checkExitScrollDown (ke: KeyboardEvent): nativeint option =
    if Ctx.isReleasedScrollMode() then
        Debug.WriteLine(sprintf "exit scroll mode (Released): %s" ke.Name)
        Ctx.exitScrollMode()
        Ctx.LastFlags.SetSuppressed ke
        suppress()
    else
        None

let private checkExitScrollUp (ke: KeyboardEvent): nativeint option =
    if Ctx.isPressedScrollMode() then
        if Ctx.checkExitScroll ke.Info.time then
            Debug.WriteLine(sprintf "exit scroll mode (Pressed): %s" ke.Name)
            Ctx.exitScrollMode()
        else
            Debug.WriteLine(sprintf "continue scroll mode (Released): %s" ke.Name)
            Ctx.setReleasedScrollMode()

        suppress()
    else
        None

let private checkSuppressedDown (up: KeyboardEvent): nativeint option =
    if Ctx.LastFlags.GetAndReset_SuppressedDown up then
        Debug.WriteLine(sprintf "suppress (checkSuppressedDown): %s" up.Name)
        suppress()
    else
        None

let private endCallNextHook (ke:KeyboardEvent) (msg:string): nativeint option =
    Debug.WriteLine(msg)
    callNextHook()

let private endPass (ke: KeyboardEvent): nativeint option =
    endCallNextHook ke (sprintf "endPass: %s" ke.Name)

let private endIllegalState (ke: KeyboardEvent): nativeint option =
    Debug.WriteLine(sprintf "illegal state: %s" ke.Name)
    suppress()

type Checkers = (KeyboardEvent -> nativeint option) list

let rec private getResult (cs:Checkers) (ke:KeyboardEvent) =
    match cs with
    | f :: fs ->
        let res = f ke
        if res.IsSome then res.Value else getResult fs ke
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


