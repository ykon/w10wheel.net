module W10Message

open System
open System.Diagnostics
open System.Windows.Forms

open WinAPI.Event

let private W10_MESSAGE_BASE = 264816059 &&& 0x0FFFFFFF
let W10_MESSAGE_EXIT = W10_MESSAGE_BASE + 1
let W10_MESSAGE_PASSMODE = W10_MESSAGE_BASE + 2
let W10_MESSAGE_RELOAD_PROP = W10_MESSAGE_BASE + 3
let W10_MESSAGE_INIT_STATE = W10_MESSAGE_BASE + 4

let private sendMessage (msg: int) =
    let pt = WinAPI.POINT(Cursor.Position.X, Cursor.Position.Y)
    let res = Windows.sendInputDirect pt 1 MOUSEEVENTF_HWHEEL 0u (uint32 msg)
    Debug.WriteLine(sprintf "SendInput: %d" res)

let sendExit () =
    Debug.WriteLine("send W10_MESSAGE_EXIT")
    sendMessage W10_MESSAGE_EXIT

let private recvExit () =
    Debug.WriteLine("recv W10_MESSAGE_EXIT")
    Ctx.exitAction()
    true

let private setBoolBit msg b =
    msg ||| if b then 0x10000000 else 0x00000000

let private getBoolBit msg =
    (msg &&& 0xF0000000) <> 0

let private getFlag msg =
    msg &&& 0x0FFFFFFF

let sendPassMode b =
    Debug.WriteLine("send W10_MESSAGE_PASSMODE")
    let msg = setBoolBit W10_MESSAGE_PASSMODE b
    sendMessage msg

let private recvPassMode msg =
    Debug.WriteLine("recv W10_MESSAGE_PASSMODE")
    Ctx.setPassMode (getBoolBit msg)
    true

let sendReloadProp () =
    Debug.WriteLine("send W10_MESSAGE_RELOAD_PROP")
    sendMessage W10_MESSAGE_RELOAD_PROP

let private recvReloadProp () =
    Debug.WriteLine("recv W10_MESSAGE_RELOAD_PROP")
    Ctx.reloadProperties ()
    true

let sendInitState () =
    Debug.WriteLine("send W10_MESSAGE_INIT_STATE")
    sendMessage W10_MESSAGE_INIT_STATE

let private recvInitState () =
    Debug.WriteLine("recv W10_MESSAGE_INIT_STATE")
    Ctx.initState ()
    true

let procMessage msg =
    match getFlag(msg) with
    | n when n = W10_MESSAGE_EXIT ->
        recvExit ()
    | n when n = W10_MESSAGE_PASSMODE ->
        recvPassMode msg
    | n when n = W10_MESSAGE_RELOAD_PROP ->
        recvReloadProp ()
    | n when n = W10_MESSAGE_INIT_STATE ->
        recvInitState ()
    | _ -> false
