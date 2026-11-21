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

let private hasFlags (flags: int) (input: WinAPI.MINPUT): bool =
    (input.mi.dwFlags &&& uint32 flags) <> 0u

let private isUp =
    hasFlags (WinAPI.Event.MOUSEEVENTF_LEFTUP ||| WinAPI.Event.MOUSEEVENTF_RIGHTUP |||
              WinAPI.Event.MOUSEEVENTF_MIDDLEUP ||| WinAPI.Event.MOUSEEVENTF_XUP)

let private isDown =
    hasFlags (WinAPI.Event.MOUSEEVENTF_LEFTDOWN ||| WinAPI.Event.MOUSEEVENTF_RIGHTDOWN |||
              WinAPI.Event.MOUSEEVENTF_MIDDLEDOWN ||| WinAPI.Event.MOUSEEVENTF_XDOWN)

// Wheel events are not sent in Pass Mode, nor if Pass Mode has been turned on (or the state
// initialized) since they were queued (generation changed): W10Wheel does not scroll then.
// Not checked under a lock: SendInput waits for the low-level hooks (on the UI thread), which
// may wait for that lock (e.g. Pass Mode switched there). The resent downs and ups are checked
// by the hook instead, which sees them in the order the application gets them (see dropResent).
let private sendUnlessPassMode (generation: int) (input: WinAPI.MINPUT): bool =
    if Ctx.isPassMode() || Ctx.getPassModeGeneration() <> generation then
        Debug.WriteLine("sendBatch: dropped (Pass Mode)")
        false
    else
        sendOne input

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

// Sends ups after the unsent ones (in order, with retries); the rest stays unsent
let private sendUps (ups: WinAPI.MINPUT[]) =
    addUnsent ups
    retryUnsent true

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
            sendUps msgs
        | 1 when isDown msgs.[0] ->
            // A down resent alone (its up comes later). Not before unsent ups: they would be
            // sent after it, releasing it. Its up is then dropped by the hook (dropResent).
            if not (isNull unsent.Value) then
                Debug.WriteLine("sendBatch: dropped a down (unsent ups)")
            elif not (sendOne msgs.[0]) then
                Debug.WriteLine("sendBatch: SendInput failed (down)")
        | 1 -> sendUnlessPassMode generation msgs.[0] |> ignore
        | _ when not (isNull unsent.Value) ->
            // Ups are still not sent: do not press another button, it could not be released
            // either, and the old ups would be sent after the new click
            Debug.WriteLine("sendBatch: dropped a click (unsent ups)")
        | _ ->
            // A click (or a down and up pair) is sent even if Pass Mode has been turned on (or
            // the state initialized) since it was queued: it cannot leave the button pressed,
            // and the user clicked before
            if sendOne msgs.[0] then
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
        // Read without sendLock: written on this thread, and by closeInput at exit
        let retry =
            not (Volatile.Read(&closing.contents)) && not (isNull (Volatile.Read(&unsent.contents)))
        let timeout = if retry then UNSENT_RETRY_INTERVAL_MS else Timeout.Infinite
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

// The kind of a resent input and the ID of the physical press it is for, in the upper half of
// dwExtraInfo (resendTag in the lower half): see dropResent. In a 32-bit process dwExtraInfo
// has only the tag (all are KIND_PLAIN).
let private KIND_PLAIN = 0UL   // passed as is (e.g. a down and up pair)
let private KIND_DOWN = 1UL    // a down resent alone (its up comes later)
let private KIND_RELEASE = 2UL // the up resent for a down resent alone

let private PRESS_ID_MASK = 0xFFFFFF

let private resendExtra (kind: uint64) (pressId: int): uint64 =
    if IntPtr.Size = 8 then
        let upper = (kind <<< 24) ||| uint64 (pressId &&& PRESS_ID_MASK)
        (upper <<< 32) ||| uint64 resendTag
    else
        uint64 resendTag

// LLMHF_INJECTED, LLMHF_LOWER_IL_INJECTED
// https://msdn.microsoft.com/en-ca/library/windows/desktop/ms644970(v=vs.85).aspx
// Events injected from a lower integrity level process have both bits set (flags = 3),
// so test the bits instead of comparing the whole value.
let private isInjected (info: HookInfo) =
    (info.flags &&& 0x3u) <> 0u

let isInjectedEvent (me: MouseEvent) =
    isInjected me.Info

// dwExtraInfo is pointer-sized: compare as 64-bit (ToUInt32() throws on 64-bit values)
let private isResendInfo (info: HookInfo) =
    (info.dwExtraInfo.ToUInt64() &&& 0xFFFFFFFFUL) = uint64 resendTag

let isResendEvent (me: MouseEvent) =
    isResendInfo me.Info

let isResendClickEvent (me: MouseEvent) =
    me.Info.dwExtraInfo.ToUInt64() = uint64 resendClickTag

// (kind, press ID) of a resent input
let private resendKind (info: HookInfo): (uint64 * int) option =
    if isInjected info && isResendInfo info then
        let upper = info.dwExtraInfo.ToUInt64() >>> 32
        Some(upper >>> 24, int (upper &&& uint64 PRESS_ID_MASK))
    else
        None

// The physical presses and the downs resent alone (LR trigger) by button (0: left, 1: right),
// as the hook sees them: in the order the target application gets the inputs, whenever
// SendInput returns. Used on the hook thread only.
// pressId: the ID of the last physical down (see handlingButton); the waits and resends for
// it carry it (pressIdOf).
// upPassed: the physical up of that press has passed W10Wheel (e.g. in Pass Mode, or after
// initState reset the flags).
// downPassedId: the press whose down resent alone has reached the application, with no up
// since (NO_PRESS if none).
let private NO_PRESS = 0 // the IDs are 1 to PRESS_ID_MASK - 1
let private pressId = Array.create 2 NO_PRESS
let private upPassed = Array.create 2 false
let private downPassedId = Array.create 2 NO_PRESS
// downSuppressed: the physical down of that press did not pass (W10Wheel suppressed it)
let private downSuppressed = Array.create 2 false
let mutable private lastPressId = 0

// Called by the mouse hook for a left or right down or up before it is handled, also in
// Pass Mode: true to drop it.
// - A down resent alone for a press whose physical up has passed (or for an earlier press)
//   would leave the button pressed.
// - The up resent for a down resent alone that did not reach the application (dropped above,
//   or not sent) would be an up without a down (e.g. a right up opens a context menu).
let dropResent (button: int) (isDown: bool) (info: HookInfo): bool =
    match resendKind info with
    | Some(kind, id) when kind = KIND_DOWN && isDown &&
                          (id <> pressId.[button] || upPassed.[button]) ->
        Debug.WriteLine("dropResent: a down after its physical up")
        true
    | Some(kind, id) when kind = KIND_RELEASE && not isDown &&
                          id <> downPassedId.[button] ->
        Debug.WriteLine("dropResent: an up for a down not passed")
        true
    | _ ->
        false

// Called by the mouse hook before handling a left or right down or up that is not dropped:
// a physical down starts a new press
let handlingButton (button: int) (isDown: bool) (info: HookInfo) =
    if isDown && not (isInjected info) then
        lastPressId <- lastPressId % (PRESS_ID_MASK - 1) + 1
        pressId.[button] <- lastPressId
        upPassed.[button] <- false
        downSuppressed.[button] <- false

// Called by the mouse hook after handling a left or right down or up (passed: passed on)
let handledButton (button: int) (isDown: bool) (info: HookInfo) (passed: bool) =
    if isDown && not passed && not (isInjected info) then
        downSuppressed.[button] <- true

    if passed then
        if isDown then
            match resendKind info with
            | Some(kind, id) when kind = KIND_DOWN -> downPassedId.[button] <- id
            | _ -> ()
        else
            downPassedId.[button] <- NO_PRESS
            if not (isInjected info) then
                upPassed.[button] <- true

// Called by the mouse hook when a left or right up is about to be passed on (by the handlers,
// or in Pass Mode): true to suppress it instead. If the physical down of its press was
// suppressed and no down resent alone for it reached the application, the application has the
// button released (or got a whole click instead): the up alone would be an up without a down
// (e.g. a right up opens a context menu). The press is ended then: a down resent late for it
// is dropped (dropResent).
let dropLoneUp (button: int) (info: HookInfo): bool =
    if not (isInjected info) && downSuppressed.[button] && downPassedId.[button] <> pressId.[button] then
        Debug.WriteLine("dropLoneUp: an up whose down did not reach the application")
        upPassed.[button] <- true
        true
    else
        false

// The ID of the current press of the button of a left or right event (on the hook thread)
let pressIdOf (me: MouseEvent): int =
    match me with
    | LeftEvent(_) -> pressId.[0]
    | RightEvent(_) -> pressId.[1]
    | _ -> raise (ArgumentException())

let private createInput (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint64): WinAPI.MINPUT =
    let mi = WinAPI.MOUSEINPUT(pt.x, pt.y, (uint32 data), uint32 flags, time, UIntPtr(extra))
    WinAPI.MINPUT(mi)

// generation: the Pass Mode generation to check an input against (see sendUnlessPassMode)
let private enqueue (generation: int) (msgs: WinAPI.MINPUT[]) =
    inputQueue.Add((generation, msgs))

let sendInput (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint32) =
    let input = createInput pt data flags time (uint64 extra)
    //WinAPI.SendInput(1u, [|input|], MINPUT_SIZE)
    enqueue (Ctx.getPassModeGeneration()) [|input|]

let sendInputDirect (pt:WinAPI.POINT) (data:int) (flags:int) (time:uint32) (extra:uint32) =
    let input = createInput pt data flags time (uint64 extra)
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
    let extra = uint64 resendClickTag
    let create mouseData es = Array.map (fun e -> createInput mc.Info.pt mouseData e 0u extra) es
    match mc with
    | LeftClick(_) -> create 0 [|MOUSEEVENTF_LEFTDOWN; MOUSEEVENTF_LEFTUP|]
    | RightClick(_) -> create 0 [|MOUSEEVENTF_RIGHTDOWN; MOUSEEVENTF_RIGHTUP|]
    | MiddleClick(_) -> create 0 [|MOUSEEVENTF_MIDDLEDOWN; MOUSEEVENTF_MIDDLEUP|]
    | X1Click(_) -> create WinAPI.XBUTTON1 [|MOUSEEVENTF_XDOWN; MOUSEEVENTF_XUP|]
    | X2Click(_) -> create WinAPI.XBUTTON2 [|MOUSEEVENTF_XDOWN; MOUSEEVENTF_XUP|]

// The functions taking a generation are for EventWaiter, which resends on its own thread
// some time after the physical down: it passes the Pass Mode generation recorded when the
// physical down was received (EventWaiter.start). The resent downs and ups are checked by the
// hook (dropResent), the generation is checked for wheel events only (sendUnlessPassMode).
let resendClickWith (generation: int) (mc: MouseClick) =
    enqueue generation (createClick mc)

let resendClick (mc: MouseClick) =
    resendClickWith (Ctx.getPassModeGeneration()) mc

let resendClickDU (down:MouseEvent) (up:MouseEvent) =
    match down, up with
    | LeftDown(_), LeftUp(_) -> resendClick(LeftClick(down.Info))
    | RightDown(_), RightUp(_) -> resendClick(RightClick(down.Info))
    | _ -> raise (ArgumentException())

// pressId: the press of the physical down (pressIdOf, recorded when the wait started)
let resendDown (generation: int) (pressId: int) (me: MouseEvent) =
    let send (info: HookInfo) flags =
        enqueue generation [| createInput info.pt 0 flags 0u (resendExtra KIND_DOWN pressId) |]
    match me with
    | LeftDown(info) -> send info MOUSEEVENTF_LEFTDOWN
    | RightDown(info) -> send info MOUSEEVENTF_RIGHTDOWN
    | _ -> raise (ArgumentException())

let private __resendUp (me: MouseEvent) (kind: uint64) (pressId: int) =
    let send (info: HookInfo) flags =
        enqueue (Ctx.getPassModeGeneration()) [| createInput info.pt 0 flags 0u (resendExtra kind pressId) |]
    match me with
    | LeftUp(info) -> send info MOUSEEVENTF_LEFTUP
    | RightUp(info) -> send info MOUSEEVENTF_RIGHTUP
    | _ -> raise (ArgumentException())

// The up of a down resent alone, for the physical up (suppressed) of the current press: called
// on the hook thread
let resendUp (me: MouseEvent) =
    __resendUp me KIND_RELEASE (pressIdOf me)

// Sends a resent up again as it came, with its kind and press (a later press may have started
// since): see EventHandler.skipResendEventLR
let resendUpAgain (me: MouseEvent) =
    let send (info: HookInfo) flags =
        enqueue (Ctx.getPassModeGeneration()) [| createInput info.pt 0 flags 0u (info.dwExtraInfo.ToUInt64()) |]
    match me with
    | LeftUp(info) -> send info MOUSEEVENTF_LEFTUP
    | RightUp(info) -> send info MOUSEEVENTF_RIGHTUP
    | _ -> raise (ArgumentException())

// The up of the other button than a waiting down (resendDownUp). Its down was not resent
// alone (the up of such a down is resent by checkResentDown before the wait is offered): it
// passed W10Wheel (e.g. pressed in Pass Mode), so its up is always passed.
let private resendOtherUp (me: MouseEvent) =
    __resendUp me KIND_PLAIN NO_PRESS

// Resends a down and an up (at different points). The same button's down and up are sent
// as one batch, like resendClick: 10 ms apart, the up with retries and waited for at exit.
let resendDownUp (generation: int) (pressId: int) (down: MouseEvent) (up: MouseEvent) =
    let input (info: HookInfo) flags = createInput info.pt 0 flags 0u (resendExtra KIND_PLAIN NO_PRESS)
    match down, up with
    | LeftDown(d), LeftUp(u) ->
        enqueue generation [| input d MOUSEEVENTF_LEFTDOWN; input u MOUSEEVENTF_LEFTUP |]
    | RightDown(d), RightUp(u) ->
        enqueue generation [| input d MOUSEEVENTF_RIGHTDOWN; input u MOUSEEVENTF_RIGHTUP |]
    | _ ->
        // Different buttons: not a pair (the other button's up comes later)
        resendDown generation pressId down
        resendOtherUp up

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
