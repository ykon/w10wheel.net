module WinHook

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop

type HookInfo = WinAPI.MSLLHOOKSTRUCT

let mutable private hhook: nativeint = IntPtr.Zero

let unhook () =
    if hhook <> IntPtr.Zero then
        WinAPI.UnhookWindowsHookEx(hhook) |> ignore
        hhook <- IntPtr.Zero

let setHook (proc: WinAPI.LowLevelMouseProc) =
    let handle = WinAPI.GetModuleHandle(null)
    hhook <- WinAPI.SetWindowsHookEx(WinAPI.WH_MOUSE_LL, proc, handle, 0u)

let callNextHook (nCode:int) (wParam:nativeint) (info:HookInfo): nativeint =
    WinAPI.CallNextHookEx(hhook, nCode, wParam, info)
