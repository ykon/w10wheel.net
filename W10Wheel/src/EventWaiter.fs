module EventWaiter

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading

open Mouse

type private SynchronousQueue() =
    let sync = new BlockingCollection<MouseEvent>(1)

    [<VolatileField>]
    let mutable waiting = false

    member self.setWaiting () =
        waiting <- true

    member self.IsWaiting = waiting

    // handle is called with the result (None: timeout) before waiting is reset, so before an
    // offer made meanwhile returns: what it queues (resends) comes before what the hook queues
    // for the events after (e.g. the resent up for the physical up of a resent down)
    member self.poll (timeout: int) (handle: MouseEvent option -> unit): unit =
        let ts = new TimeSpan(0, 0, 0, 0, timeout)
        let mutable res = NonEvent

        try
            handle (if sync.TryTake(&res, ts) then Some(res) else None)
        finally
            waiting <- false

    member self.offer (e: MouseEvent): bool =
        let mutable res = NonEvent

        if waiting then
            if not (sync.TryAdd(e)) then
                raise (InvalidOperationException())

            let rec loop () =
                if waiting then
                    Thread.Sleep(0)
                    loop()
                else
                    not (sync.TryTake(&res))

            loop()
        else
            false


let private THREAD_PRIORITY = ThreadPriority.AboveNormal

//let private waiting = ref false
let mutable private waitingEvent = NonEvent

let private sync = new SynchronousQueue()

let private setFlagsOffer me =
    match me with
    | Move(_) ->
        Debug.WriteLine(sprintf "setFlagsOffer - setResent (Move): %s" waitingEvent.Name)
        Ctx.LastFlags.SetResent waitingEvent
        //Thread.Sleep(0)
    | LeftUp(_) | RightUp(_) ->
        // Only for the up of the other button: the waiting down is resent and its physical up
        // comes later. The up of the same button is resent with the down (the click is done).
        if not (waitingEvent.SameButton me) then
            Debug.WriteLine(sprintf "setFlagsOffer - setResent (Up): %s" waitingEvent.Name)
            Ctx.LastFlags.SetResent waitingEvent
    | LeftDown(_) | RightDown(_) ->
        Debug.WriteLine(sprintf "setFlagsOffer - setSuppressed: %s" waitingEvent.Name)
        Ctx.LastFlags.SetSuppressed waitingEvent
        Ctx.LastFlags.SetSuppressed me
        // The scroll is starting already (waiterThread)
    | Cancel -> Debug.WriteLine("setFlagsOffer: cancel")
    | _ -> raise (InvalidOperationException())

let offer me: bool =
    if sync.offer(me) then
        setFlagsOffer me
        true 
    else
        false

// Cancels the wait (on the hook thread, e.g. when the trigger changes): the down that was
// waiting, if the wait was canceled (the down is not resent)
let cancel (): MouseEvent option =
    let down = waitingEvent
    if offer Cancel then Some(down) else None

// Whether a wait is in progress for a down of another button than up's (on the hook thread)
let isWaitingOtherThan (up: MouseEvent): bool =
    sync.IsWaiting && not (waitingEvent.SameButton up)

// Whether a wait is in progress for a down at the point of me (on the hook thread): a move
// there has not moved the cursor
let isWaitingAt (me: MouseEvent): bool =
    sync.IsWaiting && Mouse.samePoint waitingEvent me

let setOfferEW () =
    Ctx.setOfferEW offer

// generation: the Pass Mode generation when the physical down was received (see start)
// pressId: the press of the physical down (Windows.pressIdOf), for the hook (Windows.dropResent)

let private fromMove (generation: int) (pressId: int) (down: MouseEvent) =
    //Ctx.LastFlags.SetResent down
    Debug.WriteLine(sprintf "wait Trigger (%s -->> Move): resend %s" down.Name down.Name)
    Windows.resendDown generation pressId down

let private fromUp (generation: int) (pressId: int) (down:MouseEvent) (up:MouseEvent) =
    //Ctx.LastFlags.SetResent down

    let resendC (mc: MouseClick) =
        Debug.WriteLine(sprintf "wait Trigger (%s -->> %s): resend %s" down.Name up.Name mc.Name)
        Windows.resendClickWith generation mc

    let resendUD () =
        let wn = down.Name
        let rn = up.Name
        Debug.WriteLine(sprintf "wait Trigger (%s -->> %s): resend %s, %s" wn rn wn rn)
        Windows.resendDownUp generation pressId down up

    match down, up with
    | LeftDown(_), LeftUp(_)  ->
        if Mouse.samePoint down up then
            resendC(LeftClick(down.Info))
        else
            resendUD()
    | LeftDown(_), RightUp(_) -> resendUD()
    | RightDown(_), RightUp(_) ->
        if Mouse.samePoint down up then
            resendC(RightClick(down.Info))
        else
            resendUD()
    | RightDown(_), LeftUp(_) -> resendUD()
    | _ -> raise (InvalidOperationException())

let private fromDown (generation: int) (d1:MouseEvent) (d2:MouseEvent) =
    //Ctx.LastFlags.SetSuppressed d1
    //Ctx.LastFlags.SetSuppressed d2

    Debug.WriteLine(sprintf "wait Trigger (%s -->> %s): start scroll mode" d1.Name d2.Name)
    Ctx.startScrollModeFor generation d2.Info

let private dispatchEvent generation pressId down res =
    match res with
    | Move(_) -> fromMove generation pressId down
    | LeftUp(_) | RightUp(_) -> fromUp generation pressId down res
    | LeftDown(_) | RightDown(_) -> fromDown generation down res
    | Cancel -> Debug.WriteLine("dispatchEvent: cancel")
    | _ -> raise (InvalidOperationException())

let private fromTimeout (generation: int) (pressId: int) (down: MouseEvent) =
    // The resent flag is also seen by the up of a later press: if Pass Mode has been turned
    // on since the physical down (and maybe off again, with a new press received), neither
    // set it nor resend. Checked under Ctx.passModeLock, under which Pass Mode is switched.
    lock Ctx.passModeLock (fun () ->
        if Ctx.isPassMode() || Ctx.getPassModeGeneration() <> generation then
            Debug.WriteLine(sprintf "wait Trigger (%s -->> Timeout): dropped (Pass Mode)" down.Name)
        else
            Debug.WriteLine(sprintf "wait Trigger (%s -->> Timeout): resend %s" down.Name down.Name)
            // Queue the down first: the up of a hook seeing the flag is queued after it
            Windows.resendDown generation pressId down
            Ctx.LastFlags.SetResent down)

// (physical down, Pass Mode generation when it was received, its press ID, whether the keyboard
// trigger's scroll mode was on then)
let private waiterQueue = new BlockingCollection<MouseEvent * int * int * bool>(1)

let private waiterThread = new Thread(fun () ->
    while true do
        let down, generation, pressId, keyboardScroll = waiterQueue.Take()

        // The scroll start (second down) takes longer: it is done after the offer returns,
        // the hook handles the ups meanwhile as starting (set before the offer returns)
        let second = ref NonEvent
        sync.poll (Ctx.getPollTimeout()) (fun res ->
            match res with
            | Some(LeftDown(_) | RightDown(_) as d2) ->
                // The keyboard trigger's scroll mode is on (see EventHandler.
                // passPressedScrollMode): the LR trigger ends it instead of starting one, as
                // another trigger does. Decided here, before the offer returns: the hook waits
                // for it, so the keyboard trigger cannot end the scroll mode meanwhile (its up),
                // which would let the LR trigger start one after (not as starting either).
                // Neither started if it was on when the wait started and has ended since (the
                // keyboard trigger released in between): the trigger was pressed to end it.
                if keyboardScroll || Ctx.isKeyboardScrollMode() then
                    Debug.WriteLine(sprintf "wait Trigger (%s -->> %s): exit scroll mode (keyboard)" down.Name d2.Name)
                    if Ctx.isKeyboardScrollMode() then
                        Ctx.exitScrollMode()
                else
                    Ctx.setStartingScrollModeFor generation
                    second.Value <- d2
            | Some(res) -> dispatchEvent generation pressId down res
            | None -> fromTimeout generation pressId down)

        if second.Value.IsDown then
            dispatchEvent generation pressId down second.Value
)
        
//let private waiterThread = new Thread(waiter)
waiterThread.IsBackground <- true
waiterThread.Priority <- THREAD_PRIORITY
waiterThread.Start()

let start (down: MouseEvent) =
    if not (down.IsDown) then
        raise (ArgumentException())

    waitingEvent <- down
    // Before queuing the wait: set after it, the wait could already have timed out (and reset
    // waiting), leaving waiting set without a wait, so that the next offer would never return
    sync.setWaiting()
    // Called by the mouse hook when the physical down is received: record the Pass Mode
    // generation and the press now, the resend happens later on the waiter thread
    waiterQueue.Add((down, Ctx.getPassModeGeneration(), Windows.pressIdOf down, Ctx.isKeyboardScrollMode()))


