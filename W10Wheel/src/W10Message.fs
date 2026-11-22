module W10Message

open System
open System.Diagnostics
open System.Threading
open System.Windows.Forms

open WinAPI.Event

let private W10_MESSAGE_BASE = 264816059 &&& 0x0FFFFFFF
let W10_MESSAGE_EXIT = W10_MESSAGE_BASE + 1
let W10_MESSAGE_PASSMODE = W10_MESSAGE_BASE + 2
let W10_MESSAGE_RELOAD_PROP = W10_MESSAGE_BASE + 3
let W10_MESSAGE_INIT_STATE = W10_MESSAGE_BASE + 4

// false if SendInput failed
let private sendMessage (msg: int): bool =
    let pt = WinAPI.POINT(Cursor.Position.X, Cursor.Position.Y)
    let res = Windows.sendInputDirect pt 1 MOUSEEVENTF_HWHEEL 0u (uint32 msg)
    Debug.WriteLine(sprintf "SendInput: %d" res)
    res = 1u

let sendExit () =
    Debug.WriteLine("send W10_MESSAGE_EXIT")
    sendMessage W10_MESSAGE_EXIT

let private recvExit () =
    Debug.WriteLine("recv W10_MESSAGE_EXIT")
    Ctx.exitAction()

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

let sendReloadProp () =
    Debug.WriteLine("send W10_MESSAGE_RELOAD_PROP")
    sendMessage W10_MESSAGE_RELOAD_PROP

let private recvReloadProp () =
    Debug.WriteLine("recv W10_MESSAGE_RELOAD_PROP")
    Ctx.reloadProperties ()

let sendInitState () =
    Debug.WriteLine("send W10_MESSAGE_INIT_STATE")
    sendMessage W10_MESSAGE_INIT_STATE

let private recvInitState () =
    Debug.WriteLine("recv W10_MESSAGE_INIT_STATE")
    Ctx.initState ()

// Run after the mouse hook has returned, from the message loop (the hook runs on its thread): a
// hook must return quickly, or Windows removes it (W10Wheel would stop working, saying nothing).
// A load reads the file and installs the keyboard hook again (an error shown in a message box if
// it fails), and the exit closes the settings dialog (the settings stored). No context (no message
// loop yet): at once. After the message loop (Application.Run has returned: W10Wheel is exiting),
// the context posts to the thread pool: the command is dropped, not run on another thread. An
// exception is written to the debug output, as the hook did (Dispatcher.mouseProc), not shown as
// unhandled.
let private afterHook (f: unit -> unit) =
    let run () =
        try
            f ()
        with e ->
            Debug.WriteLine("command: " + e.ToString())
    match SynchronizationContext.Current with
    | :? WindowsFormsSynchronizationContext as ui -> ui.Post((fun _ -> run ()), null)
    | null -> run ()
    | _ -> Debug.WriteLine("command dropped: the message loop has ended")

// Called by the mouse hook (Dispatcher.procCommand): true if msg is a command (its event is not
// passed). All run after the hook, in the order they have been received (e.g. --sendPassMode
// after --sendReloadProp sent just before).
let procMessage msg =
    match getFlag(msg) with
    | n when n = W10_MESSAGE_EXIT ->
        afterHook recvExit; true
    | n when n = W10_MESSAGE_PASSMODE ->
        afterHook (fun () -> recvPassMode msg); true
    | n when n = W10_MESSAGE_RELOAD_PROP ->
        afterHook recvReloadProp; true
    | n when n = W10_MESSAGE_INIT_STATE ->
        afterHook recvInitState; true
    | _ -> false
