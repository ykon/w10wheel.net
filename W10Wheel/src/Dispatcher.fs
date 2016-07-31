module Dispatcher

open System
open System.Diagnostics
open System.Threading
open WinAPI.Message

type HookInfo = WinAPI.MSLLHOOKSTRUCT
type KHookInfo = WinAPI.KBDLLHOOKSTRUCT

let private mouseProc nCode wParam info: nativeint =
    let callNextHook = (fun _ -> WinHook.callNextMouseHook nCode wParam info)
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

let getMouseDispatcher () = new WinAPI.LowLevelMouseProc(mouseProc)

let private keyboardProc nCode wParam (info:KHookInfo): nativeint =
    let callNextHook = (fun _ -> WinHook.callNextKeyboardHook nCode wParam info)
    KEventHandler.setCallNextHook callNextHook
    if nCode < 0 || Ctx.isPassMode() then
        callNextHook()
    else
        match wParam.ToInt32() with
        | x when x = WM_KEYDOWN || x = WM_SYSKEYDOWN -> KEventHandler.keyDown info
        | x when x = WM_KEYUP || x = WM_SYSKEYUP -> KEventHandler.keyUp info
        | _ -> callNextHook()

let getKeyboardDispatcher () = new WinAPI.LowLevelKeyboardProc(keyboardProc)

