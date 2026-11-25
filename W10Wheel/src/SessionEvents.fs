module SessionEvents

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open System.Windows.Forms

let private WM_POWERBROADCAST = 0x0218
let private PBT_APMRESUMESUSPEND = 0x0007
let private PBT_APMRESUMEAUTOMATIC = 0x0012
let private WM_WTSSESSION_CHANGE = 0x02B1
let private WTS_SESSION_UNLOCK = 0x8

// Posted by the window to itself for an event (see EventWindow)
let private WM_APP_RESUMED = 0x8000 + 1

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

let private isWParam (m: Message) (n: int) =
    m.WParam = nativeint n

// A hidden top-level window: the resume (WM_POWERBROADCAST) and the unlock (WM_WTSSESSION_CHANGE)
// are not sent to a message-only window (as RawInput's). They are sent messages, which this thread
// also handles while it waits for a lock in a hook call (an STA thread pumps them): what they do is
// posted, and done from the message loop, not in the middle of an event being handled.
type private EventWindow() =
    inherit NativeWindow()

    member self.Create () =
        self.CreateHandle(CreateParams())

    override self.WndProc(m: Message byref): unit =
        if (m.Msg = WM_POWERBROADCAST && (isWParam m PBT_APMRESUMEAUTOMATIC || isWParam m PBT_APMRESUMESUSPEND))
           || (m.Msg = WM_WTSSESSION_CHANGE && isWParam m WTS_SESSION_UNLOCK) then
            Debug.WriteLine(sprintf "SessionEvents: resumed (0x%X, %d)" m.Msg m.WParam)
            WinAPI.PostMessage(self.Handle, uint32 WM_APP_RESUMED, IntPtr.Zero, IntPtr.Zero) |> ignore
        elif m.Msg = WM_APP_RESUMED then
            scheduleReinstall ()

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
