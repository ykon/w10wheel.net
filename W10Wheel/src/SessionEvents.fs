module SessionEvents

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open System.Windows.Forms

let private WM_POWERBROADCAST = 0x0218
let private PBT_APMSUSPEND = 0x0004
let private PBT_APMRESUMESUSPEND = 0x0007
let private PBT_APMRESUMEAUTOMATIC = 0x0012
let private WM_WTSSESSION_CHANGE = 0x02B1
let private WTS_SESSION_LOCK = 0x7
let private WTS_SESSION_UNLOCK = 0x8

// Posted by the window to itself for an event (see EventWindow), with its wParam: of
// WM_POWERBROADCAST (PBT_*) and of WM_WTSSESSION_CHANGE (WTS_SESSION_*)
let private WM_APP_POWER = 0x8000 + 1
let private WM_APP_SESSION = 0x8000 + 2

// After a resume or an unlock, the hooks are installed again (Ctx.reinstallHooks): some time after
// the event, after the other software's, and once more later in case that software was late (2 s
// and 10 s after it)
let private REINSTALL_DELAYS_MS = [| 2000; 8000 |]
let private reinstallTimer = new Timer()
// The next of REINSTALL_DELAYS_MS after the one running
let mutable private reinstallStep = 0

let private scheduleReinstall () =
    reinstallStep <- 1
    reinstallTimer.Stop()
    reinstallTimer.Interval <- REINSTALL_DELAYS_MS.[0]
    reinstallTimer.Start()

reinstallTimer.Tick.Add (fun _ ->
    reinstallTimer.Stop()
    Ctx.reinstallHooks ()
    if reinstallStep < REINSTALL_DELAYS_MS.Length then
        reinstallTimer.Interval <- REINSTALL_DELAYS_MS.[reinstallStep]
        reinstallStep <- reinstallStep + 1
        reinstallTimer.Start())

// A resume is notified once or twice: PBT_APMRESUMEAUTOMATIC first, always, and
// PBT_APMRESUMESUSPEND after it when the user wakes the system, maybe much later: at the first input
// after an automatic resume. The state is reset at PBT_APMRESUMEAUTOMATIC, always (also if the
// suspend was not notified), and at PBT_APMRESUMESUSPEND only if it came alone (none since the
// suspend): the hooks have seen the input since, and a reset would forget what they saw (a trigger
// key passed meanwhile would be taken as a new press: its repeats would start scroll mode, and its
// up be suppressed, the key left pressed in the application). The hooks are installed again after
// both (scheduleReinstall: from the last).
// Set at a resume, cleared at the suspend.
let mutable private resumeSeen = false

let private suspended () =
    resumeSeen <- false
    Ctx.inputLost ()

let private resumed (automatic: bool) =
    if not automatic && resumeSeen then
        Debug.WriteLine("SessionEvents: resumed by the user, the state kept")
    else
        resumeSeen <- true
        Ctx.inputLost ()
    scheduleReinstall ()

let private locked () =
    Ctx.inputLost ()

let private unlocked () =
    Ctx.inputLost ()
    scheduleReinstall ()

let private powerEvent (pbt: int) =
    if pbt = PBT_APMSUSPEND then suspended ()
    elif pbt = PBT_APMRESUMEAUTOMATIC then resumed true
    elif pbt = PBT_APMRESUMESUSPEND then resumed false

let private sessionEvent (wts: int) =
    if wts = WTS_SESSION_LOCK then locked ()
    elif wts = WTS_SESSION_UNLOCK then unlocked ()

// A hidden top-level window: the suspend and resume (WM_POWERBROADCAST) and the lock and unlock
// (WM_WTSSESSION_CHANGE) are not sent to a message-only window (as RawInput's). They are sent
// messages, which this thread also handles while it waits for a lock in a hook call (an STA thread
// pumps them): what they do is posted, and done from the message loop, not in the middle of an
// event being handled.
// The hooks see none of the input while the session is locked (it goes to the secure desktop) or
// the system asleep: the state is reset at each of them (Ctx.inputLost), and once for a resume
// (resumed). After an unlock or a resume, the hooks are installed again (scheduleReinstall).
type private EventWindow() =
    inherit NativeWindow()

    member self.Create () =
        self.CreateHandle(CreateParams())

    member private self.Post (msg: int) (wParam: nativeint) =
        WinAPI.PostMessage(self.Handle, uint32 msg, wParam, IntPtr.Zero) |> ignore

    override self.WndProc(m: Message byref): unit =
        if m.Msg = WM_POWERBROADCAST then
            Debug.WriteLine(sprintf "SessionEvents: power (%d)" m.WParam)
            self.Post WM_APP_POWER m.WParam
        elif m.Msg = WM_WTSSESSION_CHANGE then
            Debug.WriteLine(sprintf "SessionEvents: session (%d)" m.WParam)
            self.Post WM_APP_SESSION m.WParam
        elif m.Msg = WM_APP_POWER then
            powerEvent (int m.WParam)
        elif m.Msg = WM_APP_SESSION then
            sessionEvent (int m.WParam)

        base.WndProc(&m)

let private window = new EventWindow()

// The registration for the suspend and resume: also with modern standby (S0 Low Power Idle), where
// WM_POWERBROADCAST is sent only to the windows registered for it (IntPtr.Zero if it failed: the
// unlock still installs the hooks again)
let mutable private suspendResumeNotification = IntPtr.Zero

// The registration for the lock and unlock. Started at the start of the session (e.g. at the
// sign-in), WTSRegisterSessionNotification can fail until the services it needs have started:
// retried 3 times, 10 s apart. Started later as usual, they have started already.
let mutable private sessionNotification = false
let private SESSION_RETRY_MS = 10000
let private SESSION_RETRIES = 3
let mutable private sessionRetries = 0
let private sessionRetryTimer = new Timer(Interval = SESSION_RETRY_MS)

let private registerSession () =
    sessionRetryTimer.Stop()
    if WinAPI.WTSRegisterSessionNotification(window.Handle, WinAPI.NOTIFY_FOR_THIS_SESSION) then
        sessionNotification <- true
    else
        Debug.WriteLine("WTSRegisterSessionNotification: " + WinError.getLastErrorMessage())
        if sessionRetries < SESSION_RETRIES then
            sessionRetries <- sessionRetries + 1
            sessionRetryTimer.Start()

sessionRetryTimer.Tick.Add (fun _ -> registerSession ())

// Started once the mouse hook is installed (Program.main), on the thread of the message loop
let start () =
    window.Create()
    suspendResumeNotification <-
        WinAPI.RegisterSuspendResumeNotification(window.Handle, WinAPI.DEVICE_NOTIFY_WINDOW_HANDLE)
    if suspendResumeNotification = IntPtr.Zero then
        Debug.WriteLine("RegisterSuspendResumeNotification: " + WinError.getLastErrorMessage())
    registerSession ()

// When the message loop has ended (Program.main)
let stop () =
    reinstallTimer.Stop()
    sessionRetryTimer.Stop()
    if suspendResumeNotification <> IntPtr.Zero then
        WinAPI.UnregisterSuspendResumeNotification(suspendResumeNotification) |> ignore
        suspendResumeNotification <- IntPtr.Zero
    if sessionNotification then
        WinAPI.WTSUnRegisterSessionNotification(window.Handle) |> ignore
        sessionNotification <- false
    window.DestroyHandle()
