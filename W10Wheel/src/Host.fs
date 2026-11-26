module Host

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

// The calls to Windows of the triggers' handlers that act on the desktop (the input sent, the
// cursor changed, RawInput registered, the next hook called) or read its state (the keys down, the
// window under a point and its program). The program uses them as they are; the tests
// (W10Wheel.Tests) replace them, so that they neither send input nor change the cursor, and tell
// what the handlers did.

open System
open System.Runtime.InteropServices

let private MINPUT_SIZE = Marshal.SizeOf(typeof<WinAPI.MINPUT>)

// One input sent (SendInput): whether it was
let mutable sendInput: WinAPI.MINPUT -> bool =
    fun input -> WinAPI.SendInput(1u, [| input |], MINPUT_SIZE) = 1u

// The state of a key now (GetAsyncKeyState)
let mutable getAsyncKeyState: int -> int16 =
    fun vk -> WinAPI.GetAsyncKeyState(vk)

// A system cursor (id) set to a copy of hCur (SetSystemCursor destroys the copy only if it
// succeeds: destroyed here otherwise)
let mutable setSystemCursor: nativeint -> uint32 -> unit =
    fun hCur id ->
        let h = WinAPI.CopyIcon(hCur)
        if h <> IntPtr.Zero && not (WinAPI.SetSystemCursor(h, id)) then
            WinAPI.DestroyCursor(h) |> ignore

// The system cursors reloaded (SPI_SETCURSORS): whether it succeeded
let mutable restoreSystemCursors: unit -> bool =
    fun () -> WinAPI.SystemParametersInfo(uint32 WinAPI.SPI_SETCURSORS, 0u, IntPtr.Zero, 0u)

// RegisterRawInputDevices
let mutable registerRawInputDevices: WinAPI.RAWINPUTDEVICE[] -> bool =
    fun rid -> WinAPI.RegisterRawInputDevices(rid, uint32 rid.Length, uint32 (Marshal.SizeOf(typeof<WinAPI.RAWINPUTDEVICE>)))

// CallNextHookEx of the mouse and keyboard hooks
let mutable callNextMouseHook: nativeint -> int -> nativeint -> WinAPI.MSLLHOOKSTRUCT -> nativeint =
    fun hhk nCode wParam info -> WinAPI.CallNextHookExM(hhk, nCode, wParam, info)

let mutable callNextKeyboardHook: nativeint -> int -> nativeint -> WinAPI.KBDLLHOOKSTRUCT -> nativeint =
    fun hhk nCode wParam info -> WinAPI.CallNextHookExK(hhk, nCode, wParam, info)

// The cursor position (GetCursorPos): None if it cannot be read
let mutable getCursorPos: unit -> WinAPI.POINT option =
    fun () ->
        let mutable pt = WinAPI.POINT()
        if WinAPI.GetCursorPos(&pt) then Some(pt) else None

// The window at a point (WindowFromPoint), its top-level window (GetAncestor GA_ROOT), the process
// of a window (GetWindowThreadProcessId: 0 if none) and the full path of a process (None if it
// cannot be read)
let mutable windowFromPoint: WinAPI.POINT -> nativeint =
    fun pt -> WinAPI.WindowFromPoint(pt)

let mutable rootWindow: nativeint -> nativeint =
    fun hWnd -> WinAPI.GetAncestor(hWnd, 2u) // GA_ROOT

let mutable processIdOfWindow: nativeint -> uint32 =
    fun hWnd ->
        let mutable processId = 0u
        WinAPI.GetWindowThreadProcessId(hWnd, &processId) |> ignore
        processId

let mutable fullPathOfProcess: uint32 -> string option =
    fun processId ->
        // PROCESS_QUERY_LIMITED_INFORMATION is enough for QueryFullProcessImageName
        let hProcess = WinAPI.OpenProcess(0x1000u, false, processId)
        if hProcess = IntPtr.Zero then
            None
        else
            let buffer = System.Text.StringBuilder(1024)
            let mutable bufferSize = buffer.Capacity
            let success = WinAPI.QueryFullProcessImageName(hProcess, 0, buffer, &bufferSize)
            WinAPI.CloseHandle(hProcess) |> ignore
            if success then Some(buffer.ToString()) else None
