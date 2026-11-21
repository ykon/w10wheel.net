module WinCursor

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Runtime.InteropServices
open System.Threading
open Microsoft.FSharp.NativeInterop

open WinAPI.CursorID

//let private CURSOR_ID = OCR_SIZENS

let private load (id: int) =
    //WinAPI.LoadCursor(IntPtr.Zero, IntPtr(id))
    WinAPI.LoadImage(IntPtr.Zero, IntPtr(id), uint32 WinAPI.IMAGE_CURSOR, 0, 0,
                     uint32 (WinAPI.LR_DEFAULTSIZE ||| WinAPI.LR_SHARED))

let CURSOR_V = load OCR_SIZENS
let CURSOR_H = load OCR_SIZEWE

let private copy (hCur:nativeint): nativeint =
    WinAPI.CopyIcon(hCur)

// Whether the system cursors have been changed since they were restored: restoring reloads them
// all (SPI_SETCURSORS), so only when they have been changed (e.g. not for a click of the drag
// trigger), and whatever the setting is now (it may have been changed while scrolling)
let private changed = ref false

// SetSystemCursor destroys the copy only if it succeeds: destroyed here otherwise
let private setSystemCursor (hCur: nativeint) (id: int) =
    let h = copy hCur
    if h <> IntPtr.Zero && not (WinAPI.SetSystemCursor(h, uint32 id)) then
        WinAPI.DestroyCursor(h) |> ignore

let change (hCur: nativeint) =
    Volatile.Write(changed, true)
    setSystemCursor hCur OCR_NORMAL
    setSystemCursor hCur OCR_IBEAM
    setSystemCursor hCur OCR_HAND

let changeV () =
    change(CURSOR_V)

let changeH () =
    change(CURSOR_H)

let restore () =
    if Volatile.Read(&changed.contents) then
        let res = WinAPI.SystemParametersInfo(uint32 WinAPI.SPI_SETCURSORS, 0u, IntPtr.Zero, 0u)
        if res then Volatile.Write(changed, false)
        res
    else
        true
