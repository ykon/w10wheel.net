module Dispatcher

open System
open System.Diagnostics
open System.Threading
open System.Windows.Forms
open WinAPI.Message

type HookInfo = WinAPI.MSLLHOOKSTRUCT
type KHookInfo = WinAPI.KBDLLHOOKSTRUCT

let private procCommand (info: HookInfo): bool =
    if (info.mouseData >>> 16) <> 1u then
        false
    else
        Debug.WriteLine("receive (mouseData >>> 16) = 1")
        // Commands are sent with 32-bit values: anything larger is a normal wheel event
        // (ToUInt32() would throw on it)
        let extra = info.dwExtraInfo.ToUInt64()
        if extra > 0xFFFFFFFFUL then
            false
        else
            W10Message.procMessage (int (uint32 extra))

// The button (0: left, 1: right) and whether it is a down, for Windows.dropResent
let private lrButton (msg: int): (int * bool) option =
    if msg = WM_LBUTTONDOWN then Some(0, true)
    elif msg = WM_LBUTTONUP then Some(0, false)
    elif msg = WM_RBUTTONDOWN then Some(1, true)
    elif msg = WM_RBUTTONUP then Some(1, false)
    else None

let private procMouseEvent (wParam: nativeint) (info: HookInfo) (callNextHook: unit -> nativeint): nativeint =
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
        | _ -> callNextHook()

let private procMouse nCode (wParam: nativeint) (info: HookInfo): nativeint =
    //Debug.WriteLine("mouseProc")
    let lr = if nCode < 0 then None else lrButton (wParam.ToInt32())
    let passed = ref false

    // Passes the event on, unless it is an up without its down (Windows.dropLoneUp)
    let callNextHook = (fun () ->
        match lr with
        | Some(button, false) when Windows.dropLoneUp button info -> IntPtr(1)
        | _ ->
            passed.Value <- true
            WinHook.callNextMouseHook nCode wParam info)

    if nCode < 0 then
        callNextHook()
    else
        // The handlers use the function of the event being handled. Restored after: waiting for
        // a lock in the handlers, the thread can handle a hook call within this one (an STA
        // thread pumps sent messages while it waits), which sets it for its own event.
        let outer = EventHandler.getCallNextHook()
        EventHandler.setCallNextHook callNextHook
        try
            // Before Pass Mode and the triggers: the resent downs and ups are checked whatever
            // handles them, in the order the application gets them
            match lr with
            | Some(button, isDown) when Windows.dropResent button isDown info -> IntPtr(1)
            | _ ->
                lr |> Option.iter (fun (button, isDown) -> Windows.handlingButton button isDown info)
                let res = procMouseEvent wParam info callNextHook
                lr |> Option.iter (fun (button, isDown) ->
                    Windows.handledButton button isDown info passed.Value)
                res
        finally
            EventHandler.setCallNextHook outer

// An exception escaping a low-level hook procedure terminates the process on .NET,
// so pass the event on to the next hook instead.
let private mouseProc nCode wParam info: nativeint =
    try
        procMouse nCode wParam info
    with e ->
        Debug.WriteLine("mouseProc: " + e.ToString())
        WinHook.callNextMouseHook nCode wParam info

let private getMouseDispatcher () = new WinAPI.LowLevelMouseProc(mouseProc)

let setMouseDispatcher () =
    WinHook.setMouseDispatcher (getMouseDispatcher ())

let private procKeyboardEvent nCode (wParam: nativeint) (info:KHookInfo) (callNextHook: unit -> nativeint): nativeint =
    if nCode < 0 then
        callNextHook()
    elif Ctx.isPassMode() then
        // The keys pressed in Pass Mode pass: so do their repeats and up after it is turned off,
        // even if they have become the trigger (see KEventHandler.keyDown)
        match wParam.ToInt32() with
        | WM_KEYDOWN | WM_SYSKEYDOWN ->
            Ctx.LastFlags.NewKeyDown (int info.vkCode)
            Ctx.LastFlags.SetPassedKey (int info.vkCode)
        | WM_KEYUP | WM_SYSKEYUP -> Ctx.LastFlags.GetAndReset_PassedKey (int info.vkCode) |> ignore
        | _ -> ()

        callNextHook()
    else
        match wParam.ToInt32() with
        | WM_KEYDOWN | WM_SYSKEYDOWN -> KEventHandler.keyDown info
        | WM_KEYUP | WM_SYSKEYUP -> KEventHandler.keyUp info
        | _ -> callNextHook()

let private procKeyboard nCode wParam (info:KHookInfo): nativeint =
    let callNextHook = (fun () -> WinHook.callNextKeyboardHook nCode wParam info)
    // Restored after, as for the mouse (see procMouse)
    let outer = KEventHandler.getCallNextHook()
    KEventHandler.setCallNextHook callNextHook
    try
        procKeyboardEvent nCode wParam info callNextHook
    finally
        KEventHandler.setCallNextHook outer

let private keyboardProc nCode wParam (info:KHookInfo): nativeint =
    try
        procKeyboard nCode wParam info
    with e ->
        Debug.WriteLine("keyboardProc: " + e.ToString())
        WinHook.callNextKeyboardHook nCode wParam info

let private getKeyboardDispatcher () = new WinAPI.LowLevelKeyboardProc(keyboardProc)

let setKeyboardDispatcher () =
    WinHook.setKeyboardDispatcher (getKeyboardDispatcher ())

