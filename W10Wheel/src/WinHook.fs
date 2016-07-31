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

let private __unhook hhk =
    WinAPI.UnhookWindowsHookEx(hhk) |> ignore

let private __setMouseHook () =
    let hMod = WinAPI.GetModuleHandle(null)
    WinAPI.SetWindowsHookExM(WinAPI.WH_MOUSE_LL, mouseDispatcher, hMod, 0u)

let private __setKeyboardHook () =
    let hMod = WinAPI.GetModuleHandle(null)
    WinAPI.SetWindowsHookExK(WinAPI.WH_KEYBOARD_LL, keyboardDispatcher, hMod, 0u)

let callNextMouseHook nCode wParam info =
    let hhk = Volatile.Read(mouseHhk)
    WinAPI.CallNextHookExM(hhk, nCode, wParam, info)

let callNextKeyboardHook nCode wParam info =
    let hhk = Volatile.Read(keyboardHhk)
    WinAPI.CallNextHookExK(hhk, nCode, wParam, info)

let setMouseHook () =
    let hhk = __setMouseHook()
    Volatile.Write(mouseHhk, hhk)

let setKeyboardHook () =
    let hhk = __setKeyboardHook()
    Volatile.Write(keyboardHhk, hhk)

let unhookMouse () =
    let hhk = Volatile.Read(mouseHhk)
    if hhk <> IntPtr.Zero then
        __unhook hhk
        Volatile.Write(mouseHhk, IntPtr.Zero)

let unhookKeyboard () =
    let hhk = Volatile.Read(keyboardHhk)
    if hhk <> IntPtr.Zero then
        __unhook hhk
        Volatile.Write(keyboardHhk, IntPtr.Zero)
        
let setOrUnsetKeyboardHook (b: bool): unit =
    Debug.WriteLine(sprintf "setOrUnsetKeyboardHook: %b" b)
    if b then setKeyboardHook() else unhookKeyboard()

let unhook () =
    unhookMouse()
    unhookKeyboard()
