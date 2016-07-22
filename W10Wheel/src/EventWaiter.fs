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
let private sync = new BlockingCollection<MouseEvent>(1)

let offer e =
    if Volatile.Read(waiting) then sync.TryAdd e else false

let private fromTimeout we =
    Ctx.LastFlags.SetResent we
    Debug.WriteLine(sprintf "wait Trigger (%s -->> Timeout): resend %s" we.Name we.Name)
    Windows.resendDown we

let private fromMove we =
    Ctx.LastFlags.SetResent we
    Debug.WriteLine(sprintf "wait Trigger (%s -->> Move): resend %s" we.Name we.Name)
    Windows.resendDown we

let private fromUp (we:MouseEvent) (res:MouseEvent) =
    Ctx.LastFlags.SetSuppressed we

    let resendC (mc: MouseClick) =
        Debug.WriteLine(sprintf "wait Trigger (%s -->> %s): resend %s" we.Name res.Name mc.Name)
        Windows.resendClick mc

    let resendUD () =
        let wn = we.Name
        let rn = res.Name
        Debug.WriteLine(sprintf "wait Trigger (%s -->> %s): resend %s, %s" wn rn wn rn)
        Windows.resendDown we
        Windows.resendUp res

    match we with
    | LeftDown(_) ->
        match res with
        | LeftUp(_)  -> resendC(LeftClick(we.Info))
        | RightUp(_) -> resendUD()
        | _ -> raise (InvalidOperationException())
    | RightDown(_) ->
        match res with
        | RightUp(_) -> resendC(RightClick(we.Info))
        | LeftUp(_) -> resendUD()
        | _ -> raise (InvalidOperationException())
    | _ -> raise (InvalidOperationException())

let private fromDown (we:MouseEvent) (res:MouseEvent) =
    Ctx.LastFlags.SetSuppressed we
    Debug.WriteLine(sprintf "wait Trigger (%s -->> %s): start scroll mode" we.Name res.Name)
    Ctx.startScrollMode res.Info

let start (we: MouseEvent) = async {
    if not (we.IsDown) then
        raise (ArgumentException())

    Volatile.Write(waiting, true)
    let res: MouseEvent ref = ref NoneEvent
    let timeout = not (sync.TryTake(res, new TimeSpan(0, 0, 0, 0, 300)))
    Volatile.Write(waiting, false)

    if timeout then
        fromTimeout we
    else
        match res.Value with
        | Move(_) -> fromMove(we)
        | LeftUp(_) | RightUp(_) -> fromUp we res.Value
        | LeftDown(_) | RightDown(_) -> fromDown we res.Value
        | _ -> raise (InvalidOperationException())
}
