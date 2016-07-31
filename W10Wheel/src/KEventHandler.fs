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

let private resetLastFlags (ke: KeyboardEvent): nativeint option =
    Ctx.LastFlags.Reset ke
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
    if Ctx.isScrollMode() then
        Debug.WriteLine(sprintf "exit scroll mode %s: " ke.Name)
        Ctx.exitScrollMode()
        Ctx.LastFlags.SetSuppressed ke
        suppress()
    else
        None

let private checkExitScrollUp (ke: KeyboardEvent): nativeint option =
    if Ctx.isScrollMode() then
        if Ctx.checkExitScroll ke.Info.time then
            Debug.WriteLine(sprintf "exit scroll mode: %s" ke.Name)
            Ctx.exitScrollMode()
        else
            Debug.WriteLine(sprintf "continue scroll mode: %s" ke.Name)

        suppress()
    else
        None

let private checkDownSuppressed (up: KeyboardEvent): nativeint option =
    let suppressed = Ctx.LastFlags.IsDownSuppressed up

    if suppressed then
        Debug.WriteLine(sprintf "after suppressed down event: %s" up.Name)
        suppress()
    else
        None

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

let keyDown (info: KHookInfo) =
    Debug.WriteLine(sprintf "keyDown: %d" info.vkCode)

    let checkers = [
        checkSameLastEvent
        resetLastFlags
        checkExitScrollDown
        passNotTrigger
        checkTriggerScrollStart
        endIllegalState
    ]

    getResult checkers (KeyDown(info))

let keyUp (info: KHookInfo) =
    Debug.WriteLine(sprintf "keyUp: %d" info.vkCode)

    let checkers = [
        skipFirstUp
        checkSameLastEvent
        checkDownSuppressed
        passNotTrigger
        checkExitScrollUp
        endIllegalState
    ]

    getResult checkers (KeyUp(info))

