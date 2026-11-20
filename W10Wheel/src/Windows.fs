module Windows

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
// (Pass Mode generation when queued, inputs): see sendUnlessPassMode
let private inputQueue = new BlockingCollection<int * WinAPI.MINPUT[]>()

// Interval between the inputs of one batch (the down and up of a resent click).
// If they are sent in a single SendInput call, they arrive at the same moment, and
// tools that remap injected input (e.g. X-Mouse Button Control) may miss the up,
// leaving the remapped button pressed.
let private BATCH_INPUT_INTERVAL_MS = 10

// Held while a batch is being sent, so that closeInput can wait for it at exit
let private sendLock = new Object()

// Set by closeInput at exit: no more inputs are sent
let private closing = ref false

// The rest of a batch (the up) whose down was sent but which could not be sent even with
// retries. It is tried again before the next batch, periodically while nothing is queued
// (see senderThread) and at exit, so that the button is not left pressed.
let private unsent: WinAPI.MINPUT[] ref = ref null

let private sendOne (input: WinAPI.MINPUT): bool =
    WinAPI.SendInput(1u, [| input |], MINPUT_SIZE) = 1u

let private isUp (input: WinAPI.MINPUT): bool =
    let ups =
        WinAPI.Event.MOUSEEVENTF_LEFTUP ||| WinAPI.Event.MOUSEEVENTF_RIGHTUP |||
        WinAPI.Event.MOUSEEVENTF_MIDDLEUP ||| WinAPI.Event.MOUSEEVENTF_XUP
    (input.mi.dwFlags &&& uint32 ups) <> 0u

// Inputs other than ups (downs and wheel events) are not sent in Pass Mode, nor if Pass Mode
// has been turned on since they were queued (generation changed): the physical up passes
// W10Wheel in Pass Mode and could arrive before a resent down, leaving the button pressed,
// and W10Wheel does not scroll in Pass Mode. Ups are always sent (or a button could stay
// pressed). Checked and sent under Ctx.passModeLock, under which Pass Mode is switched.
let private sendUnlessPassMode (generation: int) (input: WinAPI.MINPUT): bool =
    lock Ctx.passModeLock (fun () ->
        if Ctx.isPassMode() || Ctx.getPassModeGeneration() <> generation then
            Debug.WriteLine("sendBatch: dropped (Pass Mode)")
            false
        else
            sendOne input)

let private sendWithRetry (input: WinAPI.MINPUT): bool =
    let rec loop n =
        if sendOne input then
            true
        elif n > 0 then
            Thread.Sleep(BATCH_INPUT_INTERVAL_MS)
            loop (n - 1)
        else
            Debug.WriteLine("SendInput failed")
            false
    loop 3

// Sends the inputs in order (with retries if withRetry); returns the ones that could not be
// sent (null if all were sent)
let private sendInOrder (withRetry: bool) (inputs: WinAPI.MINPUT[]): WinAPI.MINPUT[] =
    let send = if withRetry then sendWithRetry else sendOne
    let rec loop i =
        if i >= inputs.Length then
            null
        else
            if i > 0 then Thread.Sleep(BATCH_INPUT_INTERVAL_MS)
            if send inputs.[i] then loop (i + 1) else inputs.[i..]
    loop 0

let private retryUnsent (withRetry: bool) =
    match unsent.Value with
    | null -> ()
    | rest -> unsent.Value <- sendInOrder withRetry rest

let private addUnsent (rest: WinAPI.MINPUT[]) =
    match rest with
    | null -> ()
    | _ ->
        unsent.Value <-
            match unsent.Value with
            | null -> rest
            | earlier -> Array.append earlier rest

let private sendBatch (generation: int) (msgs: WinAPI.MINPUT[]) = lock sendLock (fun () ->
    if not (Volatile.Read(&closing.contents)) then
        // Without retries here: while SendInput keeps failing, retrying before every
        // batch (wheel events while scrolling) would make the queue fall behind
        retryUnsent false

        match msgs.Length with
        | 0 -> ()
        | 1 when isUp msgs.[0] ->
            // An up for a down resent earlier (e.g. the LR trigger resent the down and the
            // physical up was suppressed): it must be sent, or the button stays pressed
            addUnsent (sendInOrder true msgs)
        | 1 -> sendUnlessPassMode generation msgs.[0] |> ignore
        | _ when unsent.Value <> null ->
            // Ups are still not sent: do not press another button, it could not be released
            // either, and the old ups would be sent after the new click
            Debug.WriteLine("sendBatch: dropped a click (unsent ups)")
        | _ ->
            if sendUnlessPassMode generation msgs.[0] then
                // The down was sent: the rest (the up) must be sent, or the button stays pressed
                Thread.Sleep(BATCH_INPUT_INTERVAL_MS)
                addUnsent (sendInOrder true msgs.[1..])
)

// Called at exit: stops sending, waits for a batch being sent (its up included) and
// tries again an up that could not be sent.
let closeInput () =
    Volatile.Write(closing, true)
    if Monitor.TryEnter(sendLock, 1000) then
        try
            retryUnsent true
        finally
            Monitor.Exit(sendLock)
    else
        Debug.WriteLine("closeInput: timeout")

// While ups are unsent, they are tried again at this interval even if nothing is queued
// (e.g. SendInput fails while the secure desktop is shown)
let private UNSENT_RETRY_INTERVAL_MS = 100

let private retryUnsentIdle () = lock sendLock (fun () ->
    if not (Volatile.Read(&closing.contents)) then
        retryUnsent false
)

let private senderThread = new Thread(fun () ->
    while true do
        let timeout = if isNull unsent.Value then Timeout.Infinite else UNSENT_RETRY_INTERVAL_MS
        let mutable item = Unchecked.defaultof<int * WinAPI.MINPUT[]>

        if inputQueue.TryTake(&item, timeout) then
            let generation, msgs = item
            sendBatch generation msgs
        else
            retryUnsentIdle ()
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
// Events injected from a lower integrity level process have both bits set (flags = 3),
// so test the bits instead of comparing the whole value.
let isInjectedEvent (me: MouseEvent) =
    (me.Info.flags &&& 0x3u) <> 0u
    
// dwExtraInfo is pointer-sized: compare as 64-bit (ToUInt32() throws on 64-bit values)
let isResendEvent (me: MouseEvent) =
    me.Info.dwExtraInfo.ToUInt64() = uint64 resendTag

let isResendClickEvent (me: MouseEvent) =
    me.Info.dwExtraInfo.ToUInt64() = uint64 resendClickTag

let private createInput (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint32): WinAPI.MINPUT =
    let mi = WinAPI.MOUSEINPUT(pt.x, pt.y, (uint32 data), uint32 flags, time, UIntPtr(extra))
    WinAPI.MINPUT(mi)

// generation: the Pass Mode generation to check an input against (see sendUnlessPassMode)
let private enqueue (generation: int) (msgs: WinAPI.MINPUT[]) =
    inputQueue.Add((generation, msgs))

let sendInput (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint32) =
    let input = createInput pt data flags time extra
    //WinAPI.SendInput(1u, [|input|], MINPUT_SIZE)
    enqueue (Ctx.getPassModeGeneration()) [|input|]

let sendInputDirect (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint32) =
    let input = createInput pt data flags time extra
    WinAPI.SendInput(1u, [|input|], MINPUT_SIZE)

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
            // i = 0: smaller than the first threshold (custom tables may not start with 1)
            if i = 0 then i
            elif n - ad < Math.Abs(thr.[i - 1] - ad) then i else i - 1
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

// On a turn, the move left from the other direction does not count: start counting again
// as at the scroll start (quickTurn sends a notch at once, like quickFirst).
// A move larger than wheelMove sends a notch for each wheelMove.
let private sendRealVWheel pt d =
    let send () = sendInput pt (getVWheelDelta d) MOUSEEVENTF_WHEEL 0u 0u

    if isTurnMove vLastMove d then
        vwCount <- if quickTurn then vWheelMove else vWheelMove / 2

    vwCount <- vwCount + Math.Abs(d)

    while vWheelMove > 0 && vwCount >= vWheelMove do
        send()
        vwCount <- vwCount - vWheelMove

    vLastMove <- if d > 0 then Plus else Minus

let private sendDirectVWheel (pt:WinAPI.POINT) (d:int) =
    sendInput pt (reverseIfV (addAccelIf d)) MOUSEEVENTF_WHEEL 0u 0u

let private sendRealHWheel pt d =
    let send () = sendInput pt (getHWheelDelta d) MOUSEEVENTF_HWHEEL 0u 0u

    if isTurnMove hLastMove d then
        hwCount <- if quickTurn then hWheelMove else hWheelMove / 2

    hwCount <- hwCount + Math.Abs(d)

    while hWheelMove > 0 && hwCount >= hWheelMove do
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
    // x and y are relative moves. WM_INPUT already queued is still handled after scroll mode
    // has ended (RawInput unregistered).
    if Ctx.isScrollMode() && (x <> 0 || y <> 0) then
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

// The functions taking a generation are for EventWaiter, which resends on its own thread
// some time after the physical down: it passes the Pass Mode generation recorded when the
// physical down was received (EventWaiter.start), so that a down is dropped if Pass Mode was
// turned on since then, even before it is queued.
let resendClickWith (generation: int) (mc: MouseClick) =
    enqueue generation (createClick mc)

let resendClick (mc: MouseClick) =
    resendClickWith (Ctx.getPassModeGeneration()) mc

let resendClickDU (down:MouseEvent) (up:MouseEvent) =
    match down, up with
    | LeftDown(_), LeftUp(_) -> resendClick(LeftClick(down.Info))
    | RightDown(_), RightUp(_) -> resendClick(RightClick(down.Info))
    | _ -> raise (ArgumentException())

let resendDown (generation: int) (me: MouseEvent) =
    let send (info: HookInfo) flags = enqueue generation [| createInput info.pt 0 flags 0u resendTag |]
    match me with
    | LeftDown(info) -> send info MOUSEEVENTF_LEFTDOWN
    | RightDown(info) -> send info MOUSEEVENTF_RIGHTDOWN
    | _ -> raise (ArgumentException())

let private __resendUp (me: MouseEvent) (extra: uint32) =
    match me with
    | LeftUp(info) -> sendInput info.pt 0 MOUSEEVENTF_LEFTUP 0u extra
    | RightUp(info) -> sendInput info.pt 0 MOUSEEVENTF_RIGHTUP 0u extra
    | _ -> raise (ArgumentException())

let resendUp (me: MouseEvent) =
    __resendUp me resendTag

// Resends a down and an up (at different points). The same button's down and up are sent
// as one batch, like resendClick: 10 ms apart, the up with retries and waited for at exit.
let resendDownUp (generation: int) (down: MouseEvent) (up: MouseEvent) =
    let input (info: HookInfo) flags = createInput info.pt 0 flags 0u resendTag
    match down, up with
    | LeftDown(d), LeftUp(u) ->
        enqueue generation [| input d MOUSEEVENTF_LEFTDOWN; input u MOUSEEVENTF_LEFTUP |]
    | RightDown(d), RightUp(u) ->
        enqueue generation [| input d MOUSEEVENTF_RIGHTDOWN; input u MOUSEEVENTF_RIGHTUP |]
    | _ ->
        // Different buttons: not a pair (the other button's up comes later)
        resendDown generation down
        resendUp up

// Ups for downs resent earlier whose physical up W10Wheel will not handle (Pass Mode turned
// on, see Ctx). Queued like the downs, so they are sent after them.
let private releaseResent (left: bool) (right: bool) =
    let pt = WinAPI.POINT(Cursor.Position.X, Cursor.Position.Y)
    if left then sendInput pt 0 MOUSEEVENTF_LEFTUP 0u resendTag
    if right then sendInput pt 0 MOUSEEVENTF_RIGHTUP 0u resendTag

let setReleaseResent () =
    Ctx.setReleaseResent releaseResent

open WinAPI.VKey

let private checkAsyncKeyState (vKey:int) =
    (WinAPI.GetAsyncKeyState(vKey) &&& 0x8000s) <> 0s  // most significant bit: key is down

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
        let hProcess = WinAPI.OpenProcess(0x1000u, false, processId)  // PROCESS_QUERY_LIMITED_INFORMATION is enough for QueryFullProcessImageName
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
