module Windows

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

#nowarn "9"

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop

open Mouse

type HookInfo = WinAPI.MSLLHOOKSTRUCT

let private MINPUT_SIZE = Marshal.SizeOf(typedefof<WinAPI.MINPUT>)
let private inputQueue = new BlockingCollection<WinAPI.MINPUT array>(128)

let private inputSender = async {
    while true do
        let msgs = inputQueue.Take()
        WinAPI.SendInput(uint32 msgs.Length, msgs, MINPUT_SIZE) |> ignore
}
        
Async.Start inputSender

let private rand = Random()

let private createRandomNumber (): uint32 =    
    let mutable res = 0u

    while res = 0u do
        res <- uint32 (rand.Next())

    res

let private resendTag = createRandomNumber()
    
let isResendEvent (me: MouseEvent) =
    me.Info.dwExtraInfo.ToUInt32() = resendTag

let private createInput (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint32): WinAPI.MINPUT =
    let mi = WinAPI.MOUSEINPUT(pt.x, pt.y, uint32 data, uint32 flags, time, UIntPtr(extra))
    WinAPI.MINPUT(mi)

let private sendInput (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint32) =
    let input = createInput pt data flags time extra
    //WinAPI.SendInput(1u, [|input|], MINPUT_SIZE)
    inputQueue.Add [|input|]

let private sendInputArray (msgs: WinAPI.MINPUT array) =
    //WinAPI.SendInput(uint32 msgs.Length, msgs, MINPUT_SIZE)
    inputQueue.Add msgs

// d == Not Zero
let private getNearestIndex (d:int) (thr:int array): int =
    let ad = Math.Abs(d)

    let rec loop i =
        let n = thr.[i]
        if n = ad then
            i
        elif n > ad then
            if n - ad < Math.Abs(thr.[i - 1] - ad) then i else i - 1
        else
            if i <> thr.Length - 1 then loop (i + 1) else i

    loop 0

let private addAccel (d:int) =
    if not (Ctx.isAccelTable()) then
        d
    else
        let i = getNearestIndex d (Ctx.getAccelThreshold())
        int ((double d) * Ctx.getAccelMultiplier().[i])

open WinAPI.Event

let mutable private vwCount = 0
let mutable private hwCount = 0

type MoveDirection =
    | Plus
    | Minus
    | Zero

let mutable private vLastMove: MoveDirection = Zero
let mutable private hLastMove: MoveDirection = Zero

let startWheelCount () =
    vwCount <- if Ctx.isQuickFirst() then Ctx.getVWheelMove() else Ctx.getVWheelMove() / 2
    hwCount <- if Ctx.isQuickFirst() then Ctx.getHWheelMove() else Ctx.getHWheelMove() / 2
    vLastMove <- Zero
    hLastMove <- Zero

let private getVWheelDelta input =
    let delta = Ctx.getWheelDelta()

    if Ctx.isReverseScroll() then
        if input >= 0 then delta else -delta
    else
        if input >= 0 then -delta else delta

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

    if Ctx.isQuickTurn() && isTurnMove vLastMove d then
        send()
    elif vwCount >= Ctx.getVWheelMove() then
        send()
        vwCount <- vwCount - Ctx.getVWheelMove()

    vLastMove <- if d > 0 then Plus else Minus

let private setVDirection d =
    if Ctx.isReverseScroll() then d else -d

let private sendVerticalWheel (pt:WinAPI.POINT) (d:int) =
    if Ctx.isRealWheelMode() then
        sendRealVWheel pt d
    else
        sendInput pt (setVDirection (addAccel d)) MOUSEEVENTF_WHEEL 0u 0u

let private sendRealHWheel pt d =
    let send () = sendInput pt (getHWheelDelta d) MOUSEEVENTF_HWHEEL 0u 0u
    hwCount <- hwCount + Math.Abs(d)

    if Ctx.isQuickTurn() && isTurnMove hLastMove d then
        send()
    if hwCount >= Ctx.getHWheelMove() then
        send()
        hwCount <- hwCount - Ctx.getHWheelMove()

    hLastMove <- if d > 0 then Plus else Minus

let private setHDirection d =
    if Ctx.isReverseScroll() then -d else d

let private sendHorizontalWheel (pt:WinAPI.POINT) (d:int) =
    if Ctx.isRealWheelMode() then
        sendRealHWheel pt d
    else
        sendInput pt (setHDirection (addAccel d)) MOUSEEVENTF_HWHEEL 0u 0u

let sendWheel (pt: WinAPI.POINT) =
    let sx, sy = Ctx.getScrollStartPoint()
    let dx = pt.x - sx
    let dy = pt.y - sy

    let spt = WinAPI.POINT(sx, sy)

    if Math.Abs(dy) > Ctx.getVerticalThreshold() then
        sendVerticalWheel spt dy |> ignore

    if Ctx.isHorizontalScroll() then
        if Math.Abs(dx) > Ctx.getHorizontalThreshold() then
            sendHorizontalWheel spt dx |> ignore

let createClick (mc:MouseClick) (extra:uint32) =
    let create mouseData es = Array.map (fun e -> createInput mc.Info.pt mouseData e 0u extra) es
    match mc with
    | LeftClick(_) -> create 0 [|MOUSEEVENTF_LEFTDOWN; MOUSEEVENTF_LEFTUP|]
    | RightClick(_) -> create 0 [|MOUSEEVENTF_RIGHTDOWN; MOUSEEVENTF_RIGHTUP|]
    | MiddleClick(_) -> create 0 [|MOUSEEVENTF_MIDDLEDOWN; MOUSEEVENTF_MIDDLEUP|]
    | X1Click(_) -> create WinAPI.XBUTTON1 [|MOUSEEVENTF_XDOWN; MOUSEEVENTF_XUP|]
    | X2Click(_) -> create WinAPI.XBUTTON2 [|MOUSEEVENTF_XDOWN; MOUSEEVENTF_XUP|]

let resendClick (mc: MouseClick) =
    //Ctx.setSkipMC mc true
    sendInputArray (createClick mc resendTag)


let resendDown (me: MouseEvent) =
    //Ctx.setSkip me true
    match me with
    | LeftDown(info) -> sendInput info.pt 0 MOUSEEVENTF_LEFTDOWN 0u resendTag
    | RightDown(info) -> sendInput info.pt 0 MOUSEEVENTF_RIGHTDOWN 0u resendTag
    | _ -> raise (ArgumentException())

let resendUp (me: MouseEvent) =
    //Ctx.setSkip me true
    match me with
    | LeftUp(info) -> sendInput info.pt 0 MOUSEEVENTF_LEFTUP 0u resendTag
    | RightUp(info) -> sendInput info.pt 0 MOUSEEVENTF_RIGHTUP 0u resendTag
    | _ -> raise (ArgumentException())

open WinAPI.VKey

let getAsyncShiftState () =
    (WinAPI.GetAsyncKeyState(VK_SHIFT) &&& 0xf000s) <> 0s

let getAsyncCtrlState () =
    (WinAPI.GetAsyncKeyState(VK_CONTROL) &&& 0xf000s) <> 0s

let getAsyncAltState () =
    (WinAPI.GetAsyncKeyState(VK_MENU) &&& 0xf000s) <> 0s