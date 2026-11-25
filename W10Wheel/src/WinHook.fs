module WinHook

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

#nowarn "9"

open System
open System.Diagnostics
open System.Threading
open FSharp.NativeInterop
open WinAPI.Message

type HookInfo = WinAPI.MSLLHOOKSTRUCT
type KHookInfo = WinAPI.KBDLLHOOKSTRUCT

let private mouseHhk: nativeint ref = ref IntPtr.Zero
let private keyboardHhk: nativeint ref = ref IntPtr.Zero

let mutable private mouseDispatcher: WinAPI.LowLevelMouseProc = null
let mutable private keyboardDispatcher: WinAPI.LowLevelKeyboardProc = null

let setMouseDispatcher disp =
    mouseDispatcher <- disp

let setKeyboardDispatcher disp =
    keyboardDispatcher <- disp

// Whether it was removed (false: e.g. Windows had removed it already, for a timeout)
let private __unhook (hhk: nativeint): bool =
    WinAPI.UnhookWindowsHookEx(hhk)

let private __setMouseHook () =
    let hMod = WinAPI.GetModuleHandle(null)
    WinAPI.SetWindowsHookExM(WinAPI.WH_MOUSE_LL, mouseDispatcher, hMod, 0u)

let private __setKeyboardHook () =
    let hMod = WinAPI.GetModuleHandle(null)
    WinAPI.SetWindowsHookExK(WinAPI.WH_KEYBOARD_LL, keyboardDispatcher, hMod, 0u)

let callNextMouseHook nCode wParam info =
    let hhk = Volatile.Read(&mouseHhk.contents)
    WinAPI.CallNextHookExM(hhk, nCode, wParam, info)

let callNextKeyboardHook nCode wParam info =
    let hhk = Volatile.Read(&keyboardHhk.contents)
    WinAPI.CallNextHookExK(hhk, nCode, wParam, info)

// The error message of a failure, read right after it (another call could change the last error)
let private getHookError (hhk: nativeint): string option =
    if hhk = IntPtr.Zero then Some(WinError.getLastErrorMessage()) else None

// The hooks have been removed for the exit (unhook): not installed again after it (e.g. by a
// timer of Ctx.reinstallHooks, still running if the session ends on another thread)
let private closed: bool ref = ref false
let isClosed () = Volatile.Read(&closed.contents)

// None if installed, else the error message
let setMouseHook (): string option =
    let hhk = __setMouseHook()
    let err = getHookError hhk
    Volatile.Write(mouseHhk, hhk)
    err

// Installs the mouse hook again, to be first in the chain again (the newest hook is called first):
// before the hooks installed after it (e.g. mouse software that installs its hook again after a
// resume, then sees the buttons first and passes on only those it injects, which W10Wheel ignores).
// The new hook is installed before the old one is removed (kept if that fails, None): no event is
// handled in between, as the hooks are called on this thread only when it takes its messages.
// Some(true): Windows had removed the old one already (e.g. for a timeout; UnhookWindowsHookEx
// failed), so events may have been missed before.
let reinstallMouseHook (): bool option =
    let old = Volatile.Read(&mouseHhk.contents)
    if Volatile.Read(&closed.contents) || old = IntPtr.Zero then
        None
    else
        let hhk = __setMouseHook()
        if hhk = IntPtr.Zero then
            Debug.WriteLine("reinstallMouseHook: " + WinError.getLastErrorMessage())
            None
        else
            Volatile.Write(mouseHhk, hhk)
            Some(not (__unhook old))

// What setOrUnsetKeyboardHook did
type KeyboardHookResult =
    // Nothing changed, also when a replacement could not be installed (the old hook stays)
    | Unchanged
    // Installed where there was none
    | Installed
    // Replaced by a new one, installed before the old one was removed: no event was missed.
    // oldHookLost: Windows had removed the old one already (e.g. for a timeout; its
    // UnhookWindowsHookEx failed), so keys may have been pressed and released before with no hook
    // to see them (see KEventHandler.hookLost).
    | Replaced of oldHookLost: bool
    // Removed: no keyboard events are seen any more
    | Removed
    // Could not be installed where there was none (the error message, shown by Ctx, translated)
    | Failed of string

let unhookMouse () =
    let hhk = Volatile.Read(&mouseHhk.contents)
    if hhk <> IntPtr.Zero then
        __unhook hhk |> ignore
        Volatile.Write(mouseHhk, IntPtr.Zero)

let unhookKeyboard () =
    let hhk = Volatile.Read(&keyboardHhk.contents)
    if hhk <> IntPtr.Zero then
        __unhook hhk |> ignore
        Volatile.Write(keyboardHhk, IntPtr.Zero)

// Installs (b) or removes the keyboard hook. Installed, it is replaced by a new one, first in the
// chain: on every properties load (Reload, switching properties), after a resume or an unlock
// (Ctx.reinstallHooks). This also recovers a hook that Windows removed silently because of a
// timeout. As the mouse hook (reinstallMouseHook): the new one is installed before the old one is
// removed, with no event handled in between, and the old one stays if that fails.
let setOrUnsetKeyboardHook (b: bool): KeyboardHookResult =
    Debug.WriteLine(sprintf "setOrUnsetKeyboardHook: %b" b)
    let old = Volatile.Read(&keyboardHhk.contents)
    if b then
        let hhk = __setKeyboardHook()
        match getHookError hhk with
        | Some err when old <> IntPtr.Zero ->
            Debug.WriteLine("setOrUnsetKeyboardHook: not replaced: " + err)
            Unchanged
        | Some err -> Failed err
        | None ->
            Volatile.Write(keyboardHhk, hhk)
            if old = IntPtr.Zero then Installed else Replaced(not (__unhook old))
    elif old <> IntPtr.Zero then
        unhookKeyboard()
        Removed
    else
        Unchanged

let unhook () =
    Volatile.Write(closed, true)
    unhookMouse()
    unhookKeyboard()
