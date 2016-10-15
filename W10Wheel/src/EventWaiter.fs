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
    let mres = new ManualResetEventSlim()

    [<VolatileField>]
    let mutable waiting = false

    member self.setWaiting () =
        waiting <- true

    member self.poll (timeout: int): MouseEvent option =
        let ts = new TimeSpan(0, 0, 0, 0, timeout)
        let mutable res = NoneEvent

        try
            if sync.TryTake(&res, ts) then Some(res) else None
        finally
            waiting <- false
            mres.Set()

    member self.offer (e: MouseEvent): bool =
        let mutable res = NoneEvent

        try
            if waiting then
                if not (sync.TryAdd(e)) then
                    raise (InvalidOperationException())
                mres.Wait()
                sync.TryTake(&res) = false
            else
                false
        finally
            mres.Reset()



let private THREAD_PRIORITY = ThreadPriority.AboveNormal

let private waiting = ref false
let mutable private waitingEvent = NoneEvent

let private sync = new SynchronousQueue()

let private setFlagsOffer me =
    match me with
    | Move(_) ->
        Debug.WriteLine(sprintf "setFlagsOffer - setResent (Move): %s" waitingEvent.Name)
        Ctx.LastFlags.SetResent waitingEvent
        //Thread.Sleep(1)
    | LeftUp(_) | RightUp(_) ->
        Debug.WriteLine(sprintf "setFlagsOffer - setResent (Up): %s" waitingEvent.Name)
        Ctx.LastFlags.SetResent waitingEvent
    | LeftDown(_) | RightDown(_) ->
        Debug.WriteLine(sprintf "setFlagsOffer - setSuppressed: %s" waitingEvent.Name)
        Ctx.LastFlags.SetSuppressed waitingEvent
        Ctx.LastFlags.SetSuppressed me
        Ctx.setStartingScrollMode()
    | _ -> raise (InvalidOperationException())

let offer me: bool =
    if sync.offer(me) then
        setFlagsOffer me
        true 
    else
        false

let private fromMove (down: MouseEvent) =
    //Ctx.LastFlags.SetResent down
    Debug.WriteLine(sprintf "wait Trigger (%s -->> Move): resend %s" down.Name down.Name)
    Windows.resendDown down

let private fromUp (down:MouseEvent) (up:MouseEvent) =
    //Ctx.LastFlags.SetResent down

    let resendC (mc: MouseClick) =
        Debug.WriteLine(sprintf "wait Trigger (%s -->> %s): resend %s" down.Name up.Name mc.Name)
        Windows.resendClick mc

    let resendUD () =
        let wn = down.Name
        let rn = up.Name
        Debug.WriteLine(sprintf "wait Trigger (%s -->> %s): resend %s, %s" wn rn wn rn)
        Windows.resendDown down
        Windows.resendUp up

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

let private fromDown (d1:MouseEvent) (d2:MouseEvent) =
    //Ctx.LastFlags.SetSuppressed d1
    //Ctx.LastFlags.SetSuppressed d2

    Debug.WriteLine(sprintf "wait Trigger (%s -->> %s): start scroll mode" d1.Name d2.Name)
    Ctx.startScrollMode d2.Info

let private dispatchEvent down res =
    match res with
    | Move(_) -> fromMove down
    | LeftUp(_) | RightUp(_) -> fromUp down res
    | LeftDown(_) | RightDown(_) -> fromDown down res
    | _ -> raise (InvalidOperationException())

let private fromTimeout down =
    Ctx.LastFlags.SetResent down
    Debug.WriteLine(sprintf "wait Trigger (%s -->> Timeout): resend %s" down.Name down.Name)
    Windows.resendDown down

let private waiterQueue = new BlockingCollection<MouseEvent>(1)

let private waiter () =
    while true do
        let down = waiterQueue.Take()

        match sync.poll(Ctx.getPollTimeout()) with
        | Some(res) -> dispatchEvent down res
        | None -> fromTimeout down

        
let private waiterThread = new Thread(waiter)
waiterThread.IsBackground <- true
waiterThread.Priority <- THREAD_PRIORITY
waiterThread.Start()

let start (down: MouseEvent) =
    if not (down.IsDown) then
        raise (ArgumentException())

    waitingEvent <- down
    waiterQueue.Add(down)
    sync.setWaiting()


