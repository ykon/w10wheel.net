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

// Whether the keyboard hook removed by the last install had been removed by Windows already
// (UnhookWindowsHookEx failed): the keys may have been pressed and released meanwhile, with no
// hook to see them (see KEventHandler.hookInstalled). Set by setKeyboardHook, on the UI thread.
let mutable private oldKeyboardHookLost = false
let isOldKeyboardHookLost () = oldKeyboardHookLost

// None if installed, else the error message
let setKeyboardHook (): string option =
    // setOrUnsetKeyboardHook true is called on every properties load (Reload, switching properties).
    // Remove the previous hook first: installing again would leave it installed (unhookKeyboard
    // only removes the last one). Installing anew also recovers a hook that Windows removed
    // silently because of a timeout (UnhookWindowsHookEx then just fails).
    let old = Volatile.Read(&keyboardHhk.contents)
    oldKeyboardHookLost <- old <> IntPtr.Zero && not (__unhook old)

    let hhk = __setKeyboardHook()
    let err = getHookError hhk
    Volatile.Write(keyboardHhk, hhk)
    err

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
        
// None if done, else the error message of the install (shown by Ctx, translated)
let setOrUnsetKeyboardHook (b: bool): string option =
    Debug.WriteLine(sprintf "setOrUnsetKeyboardHook: %b" b)
    if b then
        setKeyboardHook()
    else
        unhookKeyboard()
        None

let unhook () =
    Volatile.Write(closed, true)
    unhookMouse()
    unhookKeyboard()
