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

let private waiting = ref false
let mutable private waitingEvent = NoneEvent

let private sync = new BlockingCollection<MouseEvent>(1)

let private setFlagsOffer me =
    match me with
    | Move(_) | LeftUp(_) | RightUp(_) ->
        Debug.WriteLine(sprintf "setFlagsOffer - setResent: %s" waitingEvent.Name)
        Ctx.LastFlags.SetResent waitingEvent
    | LeftDown(_) | RightDown(_) ->
        Debug.WriteLine(sprintf "setFlagsOffer - setSuppressed: %s" waitingEvent.Name)
        Ctx.LastFlags.SetSuppressed waitingEvent
        Ctx.LastFlags.SetSuppressed me
    | _ -> raise (InvalidOperationException())

let private isWaiting () = Volatile.Read(waiting)

let offer me: bool =
    if isWaiting() && sync.TryAdd(me) then
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
    | LeftDown(_), LeftUp(_)  -> resendC(LeftClick(down.Info))
    | LeftDown(_), RightUp(_) -> resendUD()
    | RightDown(_), RightUp(_) -> resendC(RightClick(down.Info))
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
    Thread.Sleep(0)
    let res: MouseEvent ref = ref NoneEvent
    if sync.TryTake(res) then
        dispatchEvent down res.Value
    else
        Ctx.LastFlags.SetResent down
        Debug.WriteLine(sprintf "wait Trigger (%s -->> Timeout): resend %s" down.Name down.Name)
        Windows.resendDown down

let private waiterQueue = new BlockingCollection<MouseEvent>(1)

let private waiter () =
    let res: MouseEvent ref = ref NoneEvent
    while true do
        let down = waiterQueue.Take()
            
        let ts = new TimeSpan(0, 0, 0, 0, Ctx.getPollTimeout())
        let timeout = not (sync.TryTake(res, ts))
        Volatile.Write(waiting, false)

        if timeout then
            fromTimeout down
        else
            dispatchEvent down res.Value
        
let private waiterThread = new Thread(waiter)
waiterThread.IsBackground <- true
waiterThread.Start()

let start (down: MouseEvent) =
    if not (down.IsDown) then
        raise (ArgumentException())

    Volatile.Write(waiting, true)
    waitingEvent <- down
    waiterQueue.Add(down)

