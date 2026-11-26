module Harness

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

// The handlers called as the hooks call them (Dispatcher.mouseProc, keyboardProc), with the calls
// to Windows replaced (Host): no input is sent, no cursor changed. The state of the handlers is
// in modules, one of each: the tests run one at a time (DisableTestParallelization), each from a
// reset (Harness.reset).

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Threading
open Xunit

open WinAPI.Message

[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do ()

type Decision =
    | Pass
    | Suppress

// What the fake Host returns for a passed event (CallNextHookEx): the handlers suppress with 1
let private PASSED = nativeint 0x5A5A
let private decisionOf (res: nativeint) =
    if res = PASSED then Pass
    elif res = nativeint 1 then Suppress
    else failwithf "unexpected hook result: %d" res

// The fake state of the desktop
let private keysDown = HashSet<int>()
let mutable private cursor = WinAPI.POINT(500, 400)
let private excludedPoints = HashSet<int * int>()
let mutable rawInputFails = false
let private sent = ConcurrentQueue<WinAPI.MINPUT>()

let EXCLUDED_PROGRAM = @"C:\Games\game.exe"
let private OTHER_PROGRAM = @"C:\Apps\app.exe"
let private EXCLUDED_WINDOW = nativeint 2
let private OTHER_WINDOW = nativeint 1

let private installHost () =
    Host.sendInput <- (fun input -> sent.Enqueue input; true)
    Host.getAsyncKeyState <- (fun vk -> lock keysDown (fun () -> if keysDown.Contains vk then 0x8000s else 0s))
    Host.setSystemCursor <- (fun _ _ -> ())
    Host.restoreSystemCursors <- (fun () -> true)
    Host.registerRawInputDevices <- (fun rid ->
        // RIDEV_REMOVE (unregister) always succeeds
        not rawInputFails || rid.[0].dwFlags = WinAPI.RawInput.RIDEV_REMOVE)
    Host.callNextMouseHook <- (fun _ _ _ _ -> PASSED)
    Host.callNextKeyboardHook <- (fun _ _ _ _ -> PASSED)
    Host.getCursorPos <- (fun () -> Some(cursor))
    Host.windowFromPoint <- (fun pt -> if excludedPoints.Contains((pt.x, pt.y)) then EXCLUDED_WINDOW else OTHER_WINDOW)
    Host.rootWindow <- id
    Host.processIdOfWindow <- (fun hWnd -> uint32 hWnd)
    Host.fullPathOfProcess <- (fun pid -> Some(if nativeint pid = EXCLUDED_WINDOW then EXCLUDED_PROGRAM else OTHER_PROGRAM))

// The folder of the properties files: one of the tests, not the user's profile
let USER_DIR = Path.Combine(Path.GetTempPath(), "W10Wheel.Tests")

let private initialized = lazy (
    Directory.CreateDirectory(USER_DIR) |> ignore
    Properties.USER_DIR <- USER_DIR
    // Before any test changes a setting: the defaults (Reset to defaults) are those of a start
    Ctx.ForTests.saveDefaults ()
    installHost ()
    Program.initSetFunctions ())

let setKeyDown (vk: int) (down: bool) =
    lock keysDown (fun () -> if down then keysDown.Add vk |> ignore else keysDown.Remove vk |> ignore)

let setCursor (x: int) (y: int) = cursor <- WinAPI.POINT(x, y)

// A point in the window of an excluded program (EXCLUDED_PROGRAM, excluded by exclude)
let addExcludedPoint (x: int) (y: int) = excludedPoints.Add((x, y)) |> ignore
let exclude () = Ctx.setExcludedPrograms [| EXCLUDED_PROGRAM |]

// The inputs sent since the last call, waiting up to timeoutMs for count of them (the sender
// thread sends them)
let takeSent (count: int) (timeoutMs: int): WinAPI.MINPUT list =
    let deadline = DateTime.UtcNow.AddMilliseconds(float timeoutMs)
    while sent.Count < count && DateTime.UtcNow < deadline do
        Thread.Sleep(5)
    let res = List<WinAPI.MINPUT>()
    let mutable input = Unchecked.defaultof<WinAPI.MINPUT>
    while sent.TryDequeue(&input) do
        res.Add input
    List.ofSeq res

// None sent within timeoutMs
let noneSent (timeoutMs: int) = (takeSent Int32.MaxValue timeoutMs).IsEmpty

let isScrolling () = Ctx.isScrollMode ()

// The settings and the state as at a start (the given mouse trigger, keyboard trigger and
// locktime), the desktop as the fake one is at first. Waits for the sender to send what an earlier
// test queued.
let reset (trigger: string) (targetVK: string) =
    initialized.Force()
    Ctx.inputLost ()
    takeSent Int32.MaxValue 50 |> ignore
    lock keysDown (fun () -> keysDown.Clear())
    excludedPoints.Clear()
    cursor <- WinAPI.POINT(500, 400)
    rawInputFails <- false
    Ctx.setExcludedPrograms [||]
    Ctx.ForTests.setSendMiddleClick false
    Ctx.ForTests.setDraggedLock false
    Ctx.ForTests.setNumber DataID.scrollLocktime 200
    Ctx.ForTests.setNumber DataID.pollTimeout 200
    Ctx.ForTests.setTrigger trigger
    Ctx.ForTests.setTargetVKCode targetVK
    // Nothing kept of the trigger before (changeTrigger), nor of the presses
    Ctx.inputLost ()

// The hook structures, as Windows gives them (made from their bytes: they have no constructor)
let private structOf<'T> (fill: nativeint -> unit): 'T =
    let size = Marshal.SizeOf(typeof<'T>)
    let p = Marshal.AllocHGlobal(size)
    try
        for i in 0 .. size - 1 do Marshal.WriteByte(p, i, 0uy)
        fill p
        let res = Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof<'T>)
        Marshal.PtrToStructure(p, res)
        res :?> 'T
    finally
        Marshal.FreeHGlobal(p)

// The offset of a field (val name: its field may be named name@)
let private offset<'T> (field: string) =
    let flags = Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.Public ||| Reflection.BindingFlags.NonPublic
    let f = typeof<'T>.GetFields(flags) |> Array.find (fun f -> f.Name = field || f.Name = field + "@")
    int (Marshal.OffsetOf(typeof<'T>, f.Name))

let mouseInfo (x: int) (y: int) (mouseData: uint32) (flags: uint32) (time: uint32) (extra: uint64): WinAPI.MSLLHOOKSTRUCT =
    structOf<WinAPI.MSLLHOOKSTRUCT> (fun p ->
        let o = offset<WinAPI.MSLLHOOKSTRUCT>
        Marshal.WriteInt32(p, o "pt", x)
        Marshal.WriteInt32(p, o "pt" + 4, y)
        Marshal.WriteInt32(p, o "mouseData", int mouseData)
        Marshal.WriteInt32(p, o "flags", int flags)
        Marshal.WriteInt32(p, o "time", int time)
        Marshal.WriteInt64(p, o "dwExtraInfo", int64 extra))

let private keyInfo (vk: int) (time: uint32): WinAPI.KBDLLHOOKSTRUCT =
    structOf<WinAPI.KBDLLHOOKSTRUCT> (fun p ->
        let o = offset<WinAPI.KBDLLHOOKSTRUCT>
        Marshal.WriteInt32(p, o "vkCode", vk)
        Marshal.WriteInt32(p, o "time", int time))

let private mouse (msg: int) (info: WinAPI.MSLLHOOKSTRUCT) =
    decisionOf (Dispatcher.mouseProc 0 (nativeint msg) info)

type Button =
    | Left
    | Right
    | Middle
    | X1
    | X2

let private downMsg = function
    | Left -> WM_LBUTTONDOWN | Right -> WM_RBUTTONDOWN | Middle -> WM_MBUTTONDOWN
    | X1 | X2 -> WM_XBUTTONDOWN
let private upMsg = function
    | Left -> WM_LBUTTONUP | Right -> WM_RBUTTONUP | Middle -> WM_MBUTTONUP
    | X1 | X2 -> WM_XBUTTONUP
let private xData = function
    | X1 -> uint32 WinAPI.XBUTTON1 <<< 16
    | X2 -> uint32 WinAPI.XBUTTON2 <<< 16
    | _ -> 0u

// Physical events (not injected) at a point and a time
let down (b: Button) (x: int) (y: int) (time: int) = mouse (downMsg b) (mouseInfo x y (xData b) 0u (uint32 time) 0UL)
let up (b: Button) (x: int) (y: int) (time: int) = mouse (upMsg b) (mouseInfo x y (xData b) 0u (uint32 time) 0UL)
let move (x: int) (y: int) (time: int) = mouse WM_MOUSEMOVE (mouseInfo x y 0u 0u (uint32 time) 0UL)
// A move of other software (injected)
let moveInjected (x: int) (y: int) (time: int) = mouse WM_MOUSEMOVE (mouseInfo x y 0u 1u (uint32 time) 0UL)

// What a sent input is (its button and whether a down), and the input as the hook gets it back
// (injected, with its dwExtraInfo)
let private inputEvents = [
    (WinAPI.Event.MOUSEEVENTF_LEFTDOWN, (Left, true)); (WinAPI.Event.MOUSEEVENTF_LEFTUP, (Left, false))
    (WinAPI.Event.MOUSEEVENTF_RIGHTDOWN, (Right, true)); (WinAPI.Event.MOUSEEVENTF_RIGHTUP, (Right, false))
    (WinAPI.Event.MOUSEEVENTF_MIDDLEDOWN, (Middle, true)); (WinAPI.Event.MOUSEEVENTF_MIDDLEUP, (Middle, false)) ]

let buttonOfInput (input: WinAPI.MINPUT): (Button * bool) option =
    let flags = int input.mi.dwFlags
    let x () = if int input.mi.mouseData = WinAPI.XBUTTON1 then X1 else X2
    if flags &&& WinAPI.Event.MOUSEEVENTF_XDOWN <> 0 then Some(x (), true)
    elif flags &&& WinAPI.Event.MOUSEEVENTF_XUP <> 0 then Some(x (), false)
    else inputEvents |> List.tryPick (fun (flag, e) -> if flags &&& flag <> 0 then Some e else None)

// The sent input coming back to the hook: its decision (the hook may drop it)
let receive (input: WinAPI.MINPUT) =
    match buttonOfInput input with
    | Some(b, isDown) ->
        // The X button in the high word of mouseData, as the hook gets it; LLMHF_INJECTED
        let info = mouseInfo input.mi.dx input.mi.dy (xData b) 1u 0u (input.mi.dwExtraInfo.ToUInt64())
        mouse (if isDown then downMsg b else upMsg b) info
    | None -> failwith "not a button input"

let keyDown (vk: int) (time: int) = decisionOf (Dispatcher.keyboardProc 0 (nativeint WM_KEYDOWN) (keyInfo vk (uint32 time)))
let keyUp (vk: int) (time: int) = decisionOf (Dispatcher.keyboardProc 0 (nativeint WM_KEYUP) (keyInfo vk (uint32 time)))

let VK_PAUSE = 0x13
let VK_LMENU = 0xA4
let VK_A = 0x41
