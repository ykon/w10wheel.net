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

// The trigger key pressed (its down suppressed, as the scroll it started) is suppressed like the
// other keys whose down was (LastFlags.SuppressedKeysToLoneUp): its up alone is suppressed, or it
// would pass (e.g. the Alt key would activate the menu bar)
let private initState () =
    match lastEvent with
    | KeyDown(_) -> Ctx.LastFlags.SetSuppressedKey lastEvent.VKCode
    | _ -> ()

    lastEvent <- NonEvent

let setInitStateKEH () =
    Ctx.setInitStateKEH initState

let private debug (msg: string) (ke: KeyboardEvent) =
    Debug.WriteLine(msg + ": " + ke.Name)

let private isKeyDown (ke: KeyboardEvent) =
    match ke with
    | KeyDown(_) -> true
    | _ -> false

// An up of the trigger key whose down W10Wheel did not handle (the last event is not its down):
// the first one, or one whose down was not seen (e.g. pressed over a window of a higher
// integrity level). The down reached the application (or Windows): the up passes, or the key
// would be left pressed. (The ups of the downs suppressed otherwise are handled before, see
// keyUp.)
let private skipFirstUp (ke: KeyboardEvent): nativeint option =
    if not (isKeyDown lastEvent) then
        debug "skip Up (its down not handled)" ke
        callNextHook()
    else
        None

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

let private endCallNextHook (ke:KeyboardEvent) (msg:string): nativeint option =
    Debug.WriteLine(msg)
    callNextHook()

let private endPass (ke: KeyboardEvent): nativeint option =
    endCallNextHook ke ("endPass: " + ke.Name)

// The down of a key passed: its repeats and up pass too (see keyDown)
let private endPassDown (ke: KeyboardEvent): nativeint option =
    Ctx.LastFlags.SetPassedKey ke.VKCode
    endPass ke

// Scroll mode could not start (RawInput could not be registered, see Ctx.Scroll.Start, or W10Wheel
// is exiting): the down passes, as if the key were not the trigger (as the mouse triggers do,
// EventHandler.passNotStartedScroll). Its repeats and its up pass too (a passed key: keyDown,
// keyUp), and its next down is not taken for a repeat (lastEvent). Suppressed, the key would be
// lost, and its up taken for an illegal state. As the Java version (2be43f7).
let private checkTriggerScrollStart (ke: KeyboardEvent): nativeint option =
    if Ctx.isTriggerKey ke then
        debug "start scroll mode" ke
        Ctx.startScrollModeK ke.Info
        if not (Ctx.isScrollMode()) then
            debug "pass (scroll mode not started)" ke
            lastEvent <- NonEvent
            endPassDown ke
        else
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

// The trigger key pressed with the cursor on the window of an excluded program (Windows.
// isExcludedWindowAtCursor): its down passes, and its repeats and its up (keyDown, keyUp), as a key
// that is not the trigger. Not the last event: its next down would be taken for a repeat
// (checkSameLastEvent), and suppressed.
let private passExcludedWindow (ke: KeyboardEvent): nativeint option =
    if Windows.isExcludedWindowAtCursor () then
        debug "pass (excluded program)" ke
        lastEvent <- NonEvent
        Ctx.LastFlags.SetPassedKey ke.VKCode
        callNextHook()
    else
        None

let private singleDown (ke: KeyboardEvent): nativeint =
    let checkers = [
        checkSameLastEvent
        checkExitScrollDown
        checkExitOtherScroll
        passExcludedWindow
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

    // The Pause key is a key as the others: held, it repeats too (a USB keyboard), and its up
    // comes (with its down at once, a PS/2 one)
    let kd = KeyDown(info)
    Ctx.LastFlags.ResetLoneUpKey kd.VKCode

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
    // Already down in the system: in the hook, GetAsyncKeyState still tells the state from before
    // this event, so this is a repeat of a press whose down reached the application with no record
    // of it here. E.g. passed just before the reset posted at an unlock or a resume
    // (SessionEvents) was handled: the hook's calls are handled before the posted messages, and
    // the reset forgot it. It passes as that press did, its up too: taken as a new press, it would
    // start scroll mode (or end one) and be suppressed, its up too, the key left pressed in the
    // application. A suppressed down leaves the key up, so the trigger works as before.
    elif Windows.checkKeyState kd.VKCode then
        debug "pass (repeat of a down with no record)" kd
        Ctx.LastFlags.SetPassedKey kd.VKCode
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
    // The up alone of a key whose down was suppressed before the state was initialized
    elif Ctx.LastFlags.GetAndReset_LoneUpKey ku.VKCode then
        debug "suppress (up alone of a suppressed down)" ku
        if Ctx.isTriggerKey ku then lastEvent <- ku
        IntPtr(1)
    elif Ctx.LastFlags.GetAndReset_PassedKey ku.VKCode then
        debug "pass (up of a passed down)" ku
        // As above: the last event may be the down of an earlier press of the trigger key whose up
        // was missed (this press passed with no record of it, keyDown), and the next press would be
        // taken for its repeat, suppressed with no scroll
        if Ctx.isTriggerKey ku then lastEvent <- ku
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
        Debug.WriteLine("changeTrigger (keyboard): the hook removed")
        Ctx.keyboardHookGone ()
    else
        if pressed then
            Ctx.LastFlags.SetSuppressedKey oldVK

        lastEvent <- NonEvent

        if Ctx.isKeyboardScrollMode() then
            Debug.WriteLine("changeTrigger (keyboard): exit scroll mode")
            Ctx.exitScrollMode()

// The keyboard hook has been replaced (on every load, after a resume or an unlock), and Windows had
// removed the old one (e.g. for a timeout): keys may have been released, and pressed again, with no
// hook to see it (the new down reached the application). So nothing of before is kept: the scroll
// mode of the trigger key (which would not end) is ended, and all the keys' records are forgotten,
// also those of the passed keys (one passed over an excluded program and released meanwhile would
// pass its next press). (A key held all along whose down was suppressed has its up passed alone:
// not as bad as a key left pressed in the application.) If the trigger key is down now, it is taken
// as passed: its repeats and its up pass (they would start scroll mode, or the up of a down that
// reached the application be suppressed); its next press is the trigger (passHeldTriggerKey). The
// other keys pass anyway.
// A hook replaced while the old one was still installed missed nothing: this is not called then
// (Ctx.setKeyboardHookOf).
// The trigger key, if it is down now (GetAsyncKeyState), is taken as passed: its down reached the
// application with no hook to see it, so its repeats and its up pass, and its next press is the
// trigger. 0 also when the state cannot be read: a key taken as up is a new press.
let private passHeldTriggerKey (caller: string) =
    let vk = Ctx.getTargetVKCode()
    if vk <> 0 && Windows.checkKeyState vk then
        Debug.WriteLine(sprintf "%s: the trigger key down, taken as passed: %d" caller vk)
        Ctx.LastFlags.SetPassedKey vk

let private hookLost () =
    Debug.WriteLine("hookLost (keyboard)")
    Ctx.keyboardHookGone ()
    passHeldTriggerKey "hookLost"

// The keyboard hook has been installed where there was none (at the end of a pause, at startup, or
// turned on): the records of the keys were reset when it was removed (changeTrigger), and the keys
// pressed meanwhile reached the application. The trigger key held now is taken as passed (see
// passHeldTriggerKey): its repeats would start scroll mode, and its up be suppressed, the key
// left pressed in the application.
let private hookInstalled () =
    passHeldTriggerKey "hookInstalled"

let setChangeTrigger () =
    Ctx.setChangeKeyboardTrigger changeTrigger
    Ctx.setKeyboardHookLost hookLost
    Ctx.setKeyboardHookInstalled hookInstalled


