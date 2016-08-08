module WinCursor

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop

open WinAPI.CursorID

//let private CURSOR_ID = OCR_SIZENS

let private load (id: int) =
    //WinAPI.LoadCursor(IntPtr.Zero, IntPtr(id))
    WinAPI.LoadImage(IntPtr.Zero, IntPtr(id), uint32 WinAPI.IMAGE_CURSOR, 0, 0,
                     uint32 (WinAPI.LR_DEFAULTSIZE ^^^ WinAPI.LR_SHARED))

let CURSOR_V = load OCR_SIZENS
let CURSOR_H = load OCR_SIZEWE

let private copy (hCur:nativeint): nativeint =
    WinAPI.CopyIcon(hCur)

let change (hCur: nativeint) =
    WinAPI.SetSystemCursor(copy(hCur), uint32 OCR_NORMAL) |> ignore
    WinAPI.SetSystemCursor(copy(hCur), uint32 OCR_IBEAM) |> ignore
    WinAPI.SetSystemCursor(copy(hCur), uint32 OCR_HAND) |> ignore

let changeV () =
    change(CURSOR_V)

let changeH () =
    change(CURSOR_H)

let restore () =
    WinAPI.SystemParametersInfo(uint32 WinAPI.SPI_SETCURSORS, 0u, IntPtr.Zero, 0u)
