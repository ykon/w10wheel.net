module Dispatcher

open System
open System.Diagnostics
open System.Threading
open System.Windows.Forms
open WinAPI.Message

type HookInfo = WinAPI.MSLLHOOKSTRUCT
type KHookInfo = WinAPI.KBDLLHOOKSTRUCT

let private procCommand (info: HookInfo): bool =
    Debug.WriteLine("procCommand")
    if (info.mouseData >>> 16) <> 1u then
        false
    else
        Debug.WriteLine("receive (mouseData >>> 16) = 1")
        let msg = int (info.dwExtraInfo.ToUInt32())
        match W10Message.getFlag(msg) with
        | x when x = W10Message.W10_MESSAGE_EXIT ->
            Debug.WriteLine("receive W10_MESSAGE_EXIT")
            Ctx.exitAction()
            true
        | x when x = W10Message.W10_MESSAGE_PASSMODE ->
            Debug.WriteLine("receive W10_MESSAGE_PASSMODE")
            Ctx.setPassMode (W10Message.getBoolBit msg)
            true
        | _ -> false

let private mouseProc nCode wParam info: nativeint =
    //Debug.WriteLine("mouseProc")
    let callNextHook = (fun _ -> WinHook.callNextMouseHook nCode wParam info)
    EventHandler.setCallNextHook callNextHook
    if nCode < 0 then
        callNextHook()
    else
        if Ctx.isPassMode() then
            if wParam.ToInt32() = WM_MOUSEHWHEEL && procCommand info then
                IntPtr(1)
            else
                callNextHook()
        else
            match wParam.ToInt32() with
            | WM_MOUSEMOVE -> EventHandler.move info
            | WM_LBUTTONDOWN -> EventHandler.leftDown info
            | WM_LBUTTONUP -> EventHandler.leftUp info
            | WM_RBUTTONDOWN -> EventHandler.rightDown info
            | WM_RBUTTONUP -> EventHandler.rightUp info
            | WM_MBUTTONDOWN -> EventHandler.middleDown info
            | WM_MBUTTONUP -> EventHandler.middleUp info
            | WM_XBUTTONDOWN -> EventHandler.xDown info
            | WM_XBUTTONUP -> EventHandler.xUp info
            | WM_MOUSEWHEEL -> callNextHook()
            | WM_MOUSEHWHEEL ->
                if procCommand info then IntPtr(1) else callNextHook()
            | _ -> raise (InvalidOperationException())

let getMouseDispatcher () = new WinAPI.LowLevelMouseProc(mouseProc)

let private keyboardProc nCode wParam (info:KHookInfo): nativeint =
    let callNextHook = (fun _ -> WinHook.callNextKeyboardHook nCode wParam info)
    KEventHandler.setCallNextHook callNextHook
    if nCode < 0 || Ctx.isPassMode() then
        callNextHook()
    else
        match wParam.ToInt32() with
        | WM_KEYDOWN | WM_SYSKEYDOWN -> KEventHandler.keyDown info
        | WM_KEYUP | WM_SYSKEYUP -> KEventHandler.keyUp info
        | _ -> callNextHook()

let getKeyboardDispatcher () = new WinAPI.LowLevelKeyboardProc(keyboardProc)

