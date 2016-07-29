(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

#nowarn "9"

open System
open System.Diagnostics
open System.Runtime.InteropServices
open System.Windows.Forms
open Microsoft.FSharp.NativeInterop
open Microsoft.Win32

open WinAPI.Message

let private mouseProc (nCode:int) (wParam:nativeint) (info:Windows.HookInfo): nativeint =
    let callNextHook = (fun () -> WinHook.callNextHook nCode wParam info)
    EventHandler.setCallNextHook callNextHook
    if nCode < 0 || Ctx.isPassMode() then
        callNextHook()
    else
        match wParam.ToInt32() with
        | x when x = WM_MOUSEMOVE -> EventHandler.move info
        | x when x = WM_LBUTTONDOWN -> EventHandler.leftDown info
        | x when x = WM_LBUTTONUP -> EventHandler.leftUp info
        | x when x = WM_RBUTTONDOWN -> EventHandler.rightDown info
        | x when x = WM_RBUTTONUP -> EventHandler.rightUp info
        | x when x = WM_MBUTTONDOWN -> EventHandler.middleDown info
        | x when x = WM_MBUTTONUP -> EventHandler.middleUp info
        | x when x = WM_XBUTTONDOWN -> EventHandler.xDown info
        | x when x = WM_XBUTTONUP -> EventHandler.xUp info
        | x when x = WM_MOUSEWHEEL || x = WM_MOUSEHWHEEL -> callNextHook()
        | _ -> raise (InvalidOperationException())

let private eventDispatcher = new WinAPI.LowLevelMouseProc(mouseProc)

let private messageDoubleLaunch () =
    MessageBox.Show("Double Launch?", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore

let private procExit () =
    WinHook.unhook()
    Ctx.storeProperties()
    PreventMultiInstance.unlock()

[<STAThread>]
[<EntryPoint>]
let main argv =
    if not (PreventMultiInstance.tryLock()) then
        messageDoubleLaunch()
        Environment.Exit(0)

    SystemEvents.SessionEnding.Add (fun _ -> procExit())

    Ctx.loadProperties()
    Ctx.setSystemTray()
    
    WinHook.setHook(eventDispatcher)
    Application.Run()

    Debug.WriteLine("exit message loop")
    procExit()
    0
        
