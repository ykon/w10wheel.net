﻿module Windows

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.Runtime.InteropServices
open System.Threading
open Microsoft.FSharp.NativeInterop
open System.Windows.Forms

open Mouse

type HookInfo = WinAPI.MSLLHOOKSTRUCT
type KHookInfo = WinAPI.KBDLLHOOKSTRUCT

let private MINPUT_SIZE = Marshal.SizeOf(typeof<WinAPI.MINPUT>)
let private inputQueue = new BlockingCollection<WinAPI.MINPUT[]>()

let private senderThread = new Thread(fun () ->
    while true do
        let msgs = inputQueue.Take()
        WinAPI.SendInput(uint32 msgs.Length, msgs, MINPUT_SIZE) |> ignore
        //let res = WinAPI.SendInput(uint32 msgs.Length, msgs, MINPUT_SIZE)
        //Debug.WriteLine(sprintf "sendinput: %d" res)
)

//let private senderThread = new Thread(sender)
senderThread.IsBackground <- true
senderThread.Start()

let private rand = Random()

let private createRandomNumber (): uint32 =    
    let mutable res = 0u

    while res = 0u do
        res <- uint32 (rand.Next())

    res

let private resendTag = createRandomNumber()
let private resendClickTag = createRandomNumber()

// LLMHF_INJECTED, LLMHF_LOWER_IL_INJECTED
// https://msdn.microsoft.com/en-ca/library/windows/desktop/ms644970(v=vs.85).aspx
let isInjectedEvent (me: MouseEvent) =
    me.Info.flags = 1u || me.Info.flags = 2u
    
let isResendEvent (me: MouseEvent) =
    me.Info.dwExtraInfo.ToUInt32() = resendTag

let isResendClickEvent (me: MouseEvent) =
    me.Info.dwExtraInfo.ToUInt32() = resendClickTag

let private createInput (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint32): WinAPI.MINPUT =
    let mi = WinAPI.MOUSEINPUT(pt.x, pt.y, (uint32 data), uint32 flags, time, UIntPtr(extra))
    WinAPI.MINPUT(mi)

let sendInput (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint32) =
    let input = createInput pt data flags time extra
    //WinAPI.SendInput(1u, [|input|], MINPUT_SIZE)
    inputQueue.Add [|input|]

let sendInputDirect (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint32) =
    let input = createInput pt data flags time extra
    WinAPI.SendInput(1u, [|input|], MINPUT_SIZE)

let private sendInputArray (msgs: WinAPI.MINPUT[]) =
    //WinAPI.SendInput(uint32 msgs.Length, msgs, MINPUT_SIZE)
    inputQueue.Add msgs

open WinAPI.Event

let private passInt (d: int) = d

let mutable private addAccelIf = passInt

let private reverseIfFlip (d: int) = -d
let mutable private reverseIfV = passInt
let mutable private reverseIfH = reverseIfFlip
let mutable private reverseIfDelta = reverseIfFlip

let private swapIfOn (x: int) (y: int) = (y, x)
let private swapIfOff (x: int) (y: int) = (x, y)
let mutable private swapIf = swapIfOff

// d == Not Zero
let private getNearestIndex (d:int) (thr:int[]): int =
    let ad = Math.Abs(d)

    let rec loop i =
        match thr.[i] with
        | n when n = ad -> i
        | n when n > ad ->
            if n - ad < Math.Abs(thr.[i - 1] - ad) then i else i - 1
        | _ ->
            if i <> thr.Length - 1 then loop (i + 1) else i

    loop 0

let mutable private accelThreshold: int[] = null
let mutable private accelMultiplier: double[] = null

let private addAccel (d:int) =
    let i = getNearestIndex d (accelThreshold)
    int ((double d) * accelMultiplier.[i])

let mutable private vwCount = 0
let mutable private hwCount = 0

type MoveDirection =
    | Plus
    | Minus
    | Zero

let mutable private vLastMove: MoveDirection = Zero
let mutable private hLastMove: MoveDirection = Zero

let mutable private vWheelMove = 0
let mutable private hWheelMove = 0
let mutable private quickTurn = false

let startWheelCount () =
    Debug.WriteLine("startWheelCount")
    vwCount <- if Ctx.isQuickFirst() then vWheelMove else vWheelMove / 2
    hwCount <- if Ctx.isQuickFirst() then hWheelMove else hWheelMove / 2
    vLastMove <- Zero
    hLastMove <- Zero

let mutable private wheelDelta = 0

let private getVWheelDelta input =
    let delta = wheelDelta
    let res = if input > 0 then -delta else delta

    reverseIfDelta res

let private getHWheelDelta input =
    -(getVWheelDelta input)

let private isTurnMove last d =
    match last with
    | Zero -> false
    | Plus -> d < 0
    | Minus -> d > 0

let private sendRealVWheel pt d =
    let send () = sendInput pt (getVWheelDelta d) MOUSEEVENTF_WHEEL 0u 0u
    vwCount <- vwCount + Math.Abs(d)

    if quickTurn && isTurnMove vLastMove d then
        send()
    elif vwCount >= vWheelMove then
        send()
        vwCount <- vwCount - vWheelMove

    vLastMove <- if d > 0 then Plus else Minus

let private sendDirectVWheel (pt:WinAPI.POINT) (d:int) =
    sendInput pt (reverseIfV (addAccelIf d)) MOUSEEVENTF_WHEEL 0u 0u

let private sendRealHWheel pt d =
    let send () = sendInput pt (getHWheelDelta d) MOUSEEVENTF_HWHEEL 0u 0u
    hwCount <- hwCount + Math.Abs(d)

    if quickTurn && isTurnMove hLastMove d then
        send()
    elif hwCount >= hWheelMove then
        send()
        hwCount <- hwCount - hWheelMove

    hLastMove <- if d > 0 then Plus else Minus

let private sendDirectHWheel (pt:WinAPI.POINT) (d:int) =
    sendInput pt (reverseIfH (addAccelIf d)) MOUSEEVENTF_HWHEEL 0u 0u

let mutable private sendVWheel = sendDirectVWheel
let mutable private sendHWheel = sendDirectHWheel

type VHDirection =
    | Vertical
    | Horizontal
    | NonDirection

//let mutable private vhDirection: VHDirection = NonDirection
let mutable private fixedVHD: VHDirection = NonDirection
let mutable private latestVHD: VHDirection = NonDirection

(*
let private setVHA vha =
    match vha with
    | Vertical ->
        vhDirection <- Vertical
        if Ctx.isCursorChange() then WinCursor.changeV()
    | Horizontal ->
        vhDirection <- Horizontal
        if Ctx.isCursorChange() then WinCursor.changeH()
    | NonDirection -> ()
*)

let private changeCursorVHD vhd =
    match vhd with
    | Vertical ->
        if Ctx.isCursorChange() then WinCursor.changeV()
    | Horizontal ->
        if Ctx.isCursorChange() then WinCursor.changeH()
    | NonDirection -> ()

(*
let private setVerticalVHA () =
    vhDirection <- Vertical
    if Ctx.isCursorChange() then WinCursor.changeV()

let private setHorizontalVHA () =
    vhDirection <- Horizontal
    if Ctx.isCursorChange() then WinCursor.changeH()
*)

let private getFirstVHD (adx: int) (ady: int): VHDirection =
    let mthr = Ctx.getFirstMinThreshold()
    if adx > mthr || ady > mthr then
        let y = if Ctx.isFirstPreferVertical() then ady * 2 else ady
        if y >= adx then Vertical else Horizontal
    else
        NonDirection

let mutable private switchingThreshold = 0

let private switchVHD adx ady =
    let sthr = switchingThreshold
    if ady > sthr then
        Vertical
    elif adx > sthr then
        Horizontal
    else
        NonDirection

let private switchVHDifNone adx ady = fixedVHD
let mutable private switchVHDif = switchVHD

let private sendWheelVHA (wspt:WinAPI.POINT) (dx:int) (dy:int) =
    let adx = Math.Abs(dx)
    let ady = Math.Abs(dy)

    let curVHD =
        match fixedVHD with
        | NonDirection ->
            fixedVHD <- getFirstVHD adx ady
            fixedVHD
        | _ -> switchVHDif adx ady

    if curVHD <> NonDirection && curVHD <> latestVHD then
        changeCursorVHD curVHD
        latestVHD <- curVHD

    match latestVHD with
    | Vertical -> if dy <> 0 then sendVWheel wspt dy
    | Horizontal -> if dx <> 0 then sendHWheel wspt dx
    | _ -> ()

let mutable private verticalThreshold = 0
let mutable private horizontalThreshold = 0

let private sendWheelStdHorizontal (wspt:WinAPI.POINT) (dx:int) (dy:int) =
    if Math.Abs(dx) > horizontalThreshold then
        sendHWheel wspt dx

let private sendWheelStdNone (wspt:WinAPI.POINT) (dx:int) (dy:int) = ()

let mutable private sendWheelStdIfHorizontal = sendWheelStdHorizontal

let private sendWheelStd (wspt:WinAPI.POINT) (dx:int) (dy:int) =
    if Math.Abs(dy) > verticalThreshold then
        sendVWheel wspt dy

    sendWheelStdIfHorizontal wspt dx dy

let mutable private sendWheelIf = sendWheelStd

let mutable private scrollStartPoint: (int * int) = 0, 0

let sendWheel (movePt: WinAPI.POINT) =
    let sx, sy = scrollStartPoint
    let dx, dy = swapIf (movePt.x - sx) (movePt.y - sy)
    let wspt = WinAPI.POINT(sx, sy)
    sendWheelIf wspt dx dy

let private sendWheelRaw (x: int) (y: int): unit =
    let sx, sy = scrollStartPoint
    if (sx <> x) && (sy <> y) then
        let dx, dy = swapIf x y
        let wspt = WinAPI.POINT(sx, sy)
        sendWheelIf wspt dx dy

let setSendWheelRaw (): unit =
    RawInput.setSendWheelRaw sendWheelRaw

let private createClick (mc:MouseClick) =
    let extra = resendClickTag
    let create mouseData es = Array.map (fun e -> createInput mc.Info.pt mouseData e 0u extra) es
    match mc with
    | LeftClick(_) -> create 0 [|MOUSEEVENTF_LEFTDOWN; MOUSEEVENTF_LEFTUP|]
    | RightClick(_) -> create 0 [|MOUSEEVENTF_RIGHTDOWN; MOUSEEVENTF_RIGHTUP|]
    | MiddleClick(_) -> create 0 [|MOUSEEVENTF_MIDDLEDOWN; MOUSEEVENTF_MIDDLEUP|]
    | X1Click(_) -> create WinAPI.XBUTTON1 [|MOUSEEVENTF_XDOWN; MOUSEEVENTF_XUP|]
    | X2Click(_) -> create WinAPI.XBUTTON2 [|MOUSEEVENTF_XDOWN; MOUSEEVENTF_XUP|]

let resendClick (mc: MouseClick) =
    sendInputArray (createClick mc)

let resendClickDU (down:MouseEvent) (up:MouseEvent) =
    match down, up with
    | LeftDown(_), LeftUp(_) -> resendClick(LeftClick(down.Info))
    | RightDown(_), RightUp(_) -> resendClick(RightClick(down.Info))
    | _ -> raise (ArgumentException())

let resendDown (me: MouseEvent) =
    match me with
    | LeftDown(info) -> sendInput info.pt 0 MOUSEEVENTF_LEFTDOWN 0u resendTag
    | RightDown(info) -> sendInput info.pt 0 MOUSEEVENTF_RIGHTDOWN 0u resendTag
    | _ -> raise (ArgumentException())

let private __resendUp (me: MouseEvent) (extra: uint32) =
    match me with
    | LeftUp(info) -> sendInput info.pt 0 MOUSEEVENTF_LEFTUP 0u extra
    | RightUp(info) -> sendInput info.pt 0 MOUSEEVENTF_RIGHTUP 0u extra
    | _ -> raise (ArgumentException())

let resendUp (me: MouseEvent) =
    __resendUp me resendTag

open WinAPI.VKey

let private checkAsyncKeyState (vKey:int) =
    (WinAPI.GetAsyncKeyState(vKey) &&& 0xf000s) <> 0s

let checkShiftState () = checkAsyncKeyState(VK_SHIFT)
let checkCtrlState () = checkAsyncKeyState(VK_CONTROL)
let checkAltState () = checkAsyncKeyState(VK_MENU)
let checkEscState () = checkAsyncKeyState(VK_ESCAPE)

let private initFuncs () =
    addAccelIf <- if Ctx.isAccelTable() then addAccel else passInt
    swapIf <- if Ctx.isSwapScroll() then swapIfOn else swapIfOff

    reverseIfV <- if Ctx.isReverseScroll() then passInt else reverseIfFlip
    reverseIfH <- if Ctx.isReverseScroll() then reverseIfFlip else passInt

    sendVWheel <- if Ctx.isRealWheelMode() then sendRealVWheel else sendDirectVWheel
    sendHWheel <- if Ctx.isRealWheelMode() then sendRealHWheel else sendDirectHWheel

    sendWheelIf <- if Ctx.isHorizontalScroll() && Ctx.isVhAdjusterMode() then sendWheelVHA else sendWheelStd

let private initAccelTable () =
    accelThreshold <- Ctx.getAccelThreshold()
    accelMultiplier <- Ctx.getAccelMultiplier()

let private initRealWheelMode () =
    vWheelMove <- Ctx.getVWheelMove()
    hWheelMove <- Ctx.getHWheelMove()
    quickTurn <- Ctx.isQuickTurn()
    wheelDelta <- Ctx.getWheelDelta()
    reverseIfDelta <- if Ctx.isReverseScroll() then reverseIfFlip else passInt

    startWheelCount()

let private initVhAdjusterMode () =
    fixedVHD <- NonDirection
    latestVHD <- NonDirection
    switchingThreshold <- Ctx.getSwitchingThreshold()
    switchVHDif <- if Ctx.isVhAdjusterSwitching() then switchVHD else switchVHDifNone

let private initStdMode () =
    verticalThreshold <- Ctx.getVerticalThreshold()
    horizontalThreshold <- Ctx.getHorizontalThreshold()
    sendWheelStdIfHorizontal <- if Ctx.isHorizontalScroll() then sendWheelStdHorizontal else sendWheelStdNone

let initScroll () =
    scrollStartPoint <- Ctx.getScrollStartPoint()
    initFuncs()

    if Ctx.isAccelTable() then
        initAccelTable()
    if Ctx.isRealWheelMode() then
        initRealWheelMode()

    if Ctx.isVhAdjusterMode() then
        initVhAdjusterMode()
    else
        initStdMode()

let setInitScroll () =
    Ctx.setInitScroll initScroll

let private getFullPathFromWindow (hWnd: nativeint): string option =
    if hWnd = IntPtr.Zero then
        None
    else
        let mutable processId = 0u
        let _ = WinAPI.GetWindowThreadProcessId(hWnd, &processId)
        let hProcess = WinAPI.OpenProcess(0x0400u, false, processId)
        if hProcess = IntPtr.Zero then
            None
        else
            let buffer = System.Text.StringBuilder(1024);
            let mutable bufferSize = buffer.Capacity;
            let success = WinAPI.QueryFullProcessImageName(hProcess, 0, buffer, &bufferSize)
            WinAPI.CloseHandle(hProcess) |> ignore
            if success then Some(buffer.ToString()) else None

let getFullPathFromForegroundWindow (): string option =
    getFullPathFromWindow (WinAPI.GetForegroundWindow())

let getFullPathFromPoint (pt: WinAPI.POINT): string option =
    getFullPathFromWindow (WinAPI.WindowFromPoint(pt))

let getFullPathFromCursorPos (): string option =
    let mutable pt = WinAPI.POINT()
    if WinAPI.GetCursorPos(&pt) then getFullPathFromPoint(pt) else None
