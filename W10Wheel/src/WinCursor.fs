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

// Whether the system cursors have been changed since they were restored: restoring reloads them
// all (SPI_SETCURSORS), so only when they have been changed (e.g. not for a click of the drag
// trigger), and whatever the setting is now (it may have been changed while scrolling)
let private changed = ref false

// To a copy of hCur (Host.setSystemCursor)
let private setSystemCursor (hCur: nativeint) (id: int) =
    Host.setSystemCursor hCur (uint32 id)

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
        let res = Host.restoreSystemCursors ()
        if res then Volatile.Write(changed, false)
        res
    else
        true
