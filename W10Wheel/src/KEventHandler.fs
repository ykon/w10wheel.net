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
let getCallNextHook (): unit -> nativeint = __callNextHook

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

let private isKeyDown (ke: KeyboardEvent) =
    match ke with
    | KeyDown(_) -> true
    | _ -> false

let private checkSameLastEvent (ke: KeyboardEvent): nativeint option =
    // A key repeat of the trigger key is always suppressed, like its first down: otherwise
    // holding the key after leaving scroll mode with it would start scroll mode again
    if ke.Same lastEvent && (Ctx.isScrollMode() || isKeyDown ke) then
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
    // Only the scroll mode it started: a mouse trigger can start scroll mode while it is pressed
    if Ctx.isPressedKeyboardScrollMode() then
        if Ctx.checkExitScroll ke.Info.time then
            debug "exit scroll mode (Pressed)" ke
            Ctx.exitScrollMode()
        else
            debug "continue scroll mode (Released)" ke
            Ctx.setReleasedScrollMode()

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

// The scroll mode of a mouse trigger is held (or its start pending): pressing the keyboard
// trigger ends it. Its down is suppressed, and so are its repeats and its up (keyUp).
let private checkExitOtherScroll (ke: KeyboardEvent): nativeint option =
    if Ctx.isPressedMouseScrollMode() || Ctx.isStartingScrollMode() then
        debug "exit scroll mode (other trigger)" ke
        Ctx.exitScrollMode()
        Ctx.LastFlags.SetSuppressed ke
        suppress()
    else
        None

let private singleDown (ke: KeyboardEvent): nativeint =
    let checkers = [
        checkSameLastEvent
        checkExitScrollDown
        checkExitOtherScroll
        checkTriggerScrollStart
        endIllegalState
    ]

    getResult checkers ke

let private singleUp (ke: KeyboardEvent) =
    let checkers = [
        skipFirstUp
        checkSameLastEvent
        checkExitScrollUp
        endIllegalState
    ]

    getResult checkers ke

// The down of a key passed: its repeats and up pass too (see keyDown)
let private endPassDown (ke: KeyboardEvent): nativeint option =
    Ctx.LastFlags.SetPassedKey ke.VKCode
    endPass ke

let private noneDown (ke: KeyboardEvent): nativeint =
    let checkers = [
        checkExitScrollDown
        endPassDown
    ]

    getResult checkers ke

let private noneUp (ke: KeyboardEvent): nativeint =
    // (the ups of suppressed downs are handled in keyUp)
    let checkers = [
        endPass
    ]

    getResult checkers ke

let keyDown (info: KHookInfo) =
    //Debug.WriteLine(sprintf "keyDown: %d" info.vkCode)

    let kd = KeyDown(info)
    Ctx.LastFlags.NewKeyDown kd.VKCode

    // The trigger key that does not repeat (Pause) may send no up: a down after its down was
    // released, quickly. As its up would, lock the scroll it started (this down then ends it),
    // and do not take the down for a repeat.
    if Keyboard.isNoRepeatKey kd.VKCode && Ctx.isTriggerKey kd && kd.Same lastEvent then
        debug "no up before (no repeat key)" kd
        if Ctx.isPressedKeyboardScrollMode() then
            Ctx.setReleasedScrollMode()

        lastEvent <- KeyUp(info)

    // A key whose down was suppressed (e.g. to leave scroll mode, or the old trigger key when
    // the trigger changed): its repeats are suppressed too, whatever the trigger is now
    if Ctx.LastFlags.IsSuppressedKey kd.VKCode then
        debug "suppress (repeat of a suppressed down)" kd
        IntPtr(1)
    // A key whose down passed (to the application): its repeats pass too, even if it has become
    // the trigger since (they would start scroll mode, and its up would be suppressed)
    elif Ctx.LastFlags.IsPassedKey kd.VKCode then
        debug "pass (repeat of a passed down)" kd
        __callNextHook()
    elif Ctx.isTriggerKey kd then singleDown kd else noneDown kd

let keyUp (info: KHookInfo) =
    //Debug.WriteLine(sprintf "keyUp: %d" info.vkCode)

    let ku = KeyUp(info)
    // The up of a suppressed down, whatever the trigger is now (it may have changed since)
    if Ctx.LastFlags.GetAndReset_SuppressedDown ku then
        debug "suppress (up of a suppressed down)" ku
        // As the trigger's handlers do, or its next down would be taken for a repeat
        if Ctx.isTriggerKey ku then lastEvent <- ku
        IntPtr(1)
    elif Ctx.LastFlags.GetAndReset_PassedKey ku.VKCode then
        debug "pass (up of a passed down)" ku
        __callNextHook()
    elif Ctx.isTriggerKey ku then singleUp ku else noneUp ku

// The trigger key has changed (keyChanged), or the keyboard hook is removed: the scroll mode
// the old key started would not end (its up is not the trigger's any more, or not seen). End
// it. If the old key is pressed after a change (its down suppressed), suppress it until its up
// (keyUp). Without the hook, the ups will not be seen: forget the suppressed downs.
let private changeTrigger (oldVK: int) (keyChanged: bool) =
    let pressed =
        match lastEvent with
        | KeyDown(_) -> lastEvent.VKCode = oldVK
        | _ -> false

    if not keyChanged then
        Ctx.LastFlags.ResetKeys()
    elif pressed then
        Ctx.LastFlags.SetSuppressedKey oldVK

    lastEvent <- NonEvent

    if Ctx.isKeyboardScrollMode() then
        Debug.WriteLine("changeTrigger (keyboard): exit scroll mode")
        Ctx.exitScrollMode()

// The keyboard hook is installed again (on every load): ups may have been missed while it was
// removed (e.g. by Windows for a timeout). The scroll mode the trigger key started would not
// end, and its next down would be taken for a repeat: end it and forget the last event. The
// suppressed and passed keys are kept until their up: whether a key is still pressed cannot
// be told for sure.
let private hookInstalled () =
    // The trigger key pressed (its down suppressed, as the scroll it started): suppress its
    // repeats and its up until the up (keyUp), or the up alone would pass, or the repeats start
    // scroll mode again. If its up was missed, its next press is suppressed once.
    match lastEvent with
    | KeyDown(_) -> Ctx.LastFlags.SetSuppressedKey lastEvent.VKCode
    | _ -> ()

    lastEvent <- NonEvent

    if Ctx.isKeyboardScrollMode() then
        Debug.WriteLine("hookInstalled: exit keyboard scroll mode")
        Ctx.exitScrollMode()

let setChangeTrigger () =
    Ctx.setChangeKeyboardTrigger changeTrigger
    Ctx.setKeyboardHookInstalled hookInstalled


