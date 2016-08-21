module W10Message

open System
open System.Diagnostics
open System.Windows.Forms

open WinAPI.Event

let private W10_MESSAGE_BASE = 264816059 &&& 0x0FFFFFFF
let W10_MESSAGE_EXIT = W10_MESSAGE_BASE + 1
let W10_MESSAGE_PASSMODE = W10_MESSAGE_BASE + 2

let private sendMessage (msg: int) =
    let pt = WinAPI.POINT(Cursor.Position.X, Cursor.Position.Y)
    let res = Windows.sendInputDirect pt 1 MOUSEEVENTF_HWHEEL 0u (uint32 msg)
    Debug.WriteLine(sprintf "SendInput: %d" res)

let sendExit () =
    Debug.WriteLine("send W10_MESSAGE_EXIT")
    sendMessage W10_MESSAGE_EXIT

let setBoolBit msg b =
    msg ||| if b then 0x10000000 else 0x00000000

let getBoolBit msg =
    (msg &&& 0xF0000000) <> 0

let getFlag msg =
    msg &&& 0x0FFFFFFF

let sendPassMode b =
    Debug.WriteLine("send W10_MESSAGE_PASSMODE")
    let msg = setBoolBit W10_MESSAGE_PASSMODE b
    sendMessage msg

