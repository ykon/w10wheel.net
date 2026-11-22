(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open System.Threading
open System.Windows.Forms
open Microsoft.Win32

let private convLang msg =
    Ctx.convLang msg

// Before loadProperties()
let private convLangP msg =
    Ctx.convLangWithProp msg

let private messageDoubleLaunch () =
    MessageBox.Show(convLangP "Double Launch?", convLangP "Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore

// Started by a restart as administrator (Ctx.RESTART_ADMIN_ARG): the old instance is exiting
let mutable private waitForLock = false

// The lock of the old instance is waited for after a restart (30 s at most): it is released when
// the old one has exited (after storing the properties, which this one then loads)
let private checkDoubleLaunch () =
    let rec tryLock (left: int) =
        PreventMultiInstance.tryLock() || (left > 0 && (Thread.Sleep(100); tryLock (left - 1)))
    if not (tryLock (if waitForLock then 300 else 0)) then
        messageDoubleLaunch()
        Environment.Exit(1)
    elif waitForLock then
        // Read before the old instance had stored them at its exit
        Ctx.loadPropertiesFileOnly ()

// Whether to restart as administrator. A task dialog, not a message box: its buttons say what they
// do, and the restart has the shield of UAC. The link opens the wiki (startup as administrator,
// etc.) and leaves the dialog open. Closed (Esc): continue.
let private askRestartAsAdmin (): bool =
    let page = TaskDialogPage(Caption = AppDef.PROGRAM_NAME_NET, Icon = TaskDialogIcon.Information,
                              Heading = convLangP "W10Wheel is not running as administrator",
                              Text = convLangP LocaleData.AdminMessage, AllowCancel = true, EnableLinks = true)
    let restart = TaskDialogCommandLinkButton(convLangP "Restart as administrator",
                                              convLangP "Windows asks for permission (User Account Control).",
                                              ShowShieldIcon = true)
    let continueWithout = TaskDialogCommandLinkButton(convLangP "Continue without administrator rights")
    page.Buttons.Add(restart)
    page.Buttons.Add(continueWithout)
    page.Footnote <- TaskDialogFootnote(sprintf "<a href=\"%s\">%s</a>" AppDef.WIKI_URL (convLangP "More info (Wiki)"))
    page.LinkClicked.Add(fun e -> Dialog.shellOpen e.LinkHref)
    // For those who run it without administrator rights on purpose: asked again by checking the
    // setting in the settings dialog (General)
    page.Verification <- TaskDialogVerificationCheckBox(convLangP "Do not ask again")

    let res = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen)
    // Only with Continue without administrator rights: the choice to run without them. Not if
    // closed (Esc) or restarted.
    if page.Verification.Checked && res = continueWithout then Ctx.storeNoAdminCheck ()
    res = restart

// Starts this program as administrator (UAC) with the selected properties (procArgv has checked
// the argument: Default if it does not exist, and the commands have exited). Before the hooks and
// the tray icon: this instance exits at once if it has been started, and the new one waits for its
// lock (see checkDoubleLaunch). Nothing to undo if not.
let private restartAsAdmin (): bool =
    match Ctx.startAsAdmin () with
    | Ctx.AdminStarted -> true
    | Ctx.AdminCanceled -> false
    | Ctx.AdminFailed e ->
        Dialog.errorMessageE e
        false

let private checkAdmin () =
    if not (Ctx.isAdmin ()) && Ctx.isAdminCheckWithProp () && askRestartAsAdmin () && restartAsAdmin () then
        Environment.Exit(0)

let private closeAll () =
    Debug.WriteLine("procExit")

    // No more downs are resent: the hooks are removed next (see Windows.beginClose)
    Windows.beginClose()
    WinHook.unhook()

    // Cancel an LR trigger wait: after its timeout it would resend a down whose up does not
    // come any more (the hook is removed)
    // Maybe on another thread (SessionEnded): an offer made by the hook at the same time throws
    // (EventWaiter), which must not stop the rest (storing the properties included)
    try
        EventWaiter.offer Mouse.Cancel |> ignore
    with
        | e -> Debug.WriteLine("procExit: " + e.Message)

    // Exiting while scrolling (e.g. --sendExit): restore the cursor and unregister RawInput.
    // This also waits for a scroll start in progress and refuses later ones.
    Ctx.closeScrollMode()

    // Wait for a resent click being sent (so that its up is sent), send the ups still queued
    // and stop sending
    Windows.closeInput()

    Ctx.storeProperties()
    PreventMultiInstance.unlock()

let private exitLock = new Object()
let mutable private exited = false

// Called when the message loop ends and when the session ends (SessionEnded, maybe on another
// thread): done once. The other call waits for it to be done, or the process could end while
// the properties are stored.
let private procExit () = lock exitLock (fun () ->
    if not exited then
        exited <- true
        closeAll ()
)

let private getBool (sl: string list): bool =
    try
        match sl with
        | s :: _ -> Boolean.Parse(s)
        | _ -> true
    with
        | :? FormatException as e ->
            Dialog.errorMessageE e
            Environment.Exit(1)
            false

// The errors of the arguments are shown in the language of the properties (Default: no other
// is selected then)
let private setSelectedProperties name =
    // Default is always valid (its file is not written until a setting is changed or W10Wheel
    // exits)
    if name = Properties.DEFAULT_DEF || Properties.exists(name) then
        Ctx.setSelectedProperties name
    else
        Ctx.loadPropertiesFileOnly ()
        Dialog.errorMessage (sprintf "%s: %s" (convLangP "Properties does not exist") name) (convLangP "Error")

let private unknownCommand name =
    Ctx.loadPropertiesFileOnly ()
    Dialog.errorMessage (sprintf "%s: %s" (convLangP "Unknown Command") name) (convLangP "Command Error")
    Environment.Exit(1)

let private procArgv (argv: string[]) =
    Debug.WriteLine("procArgv")

    // Some(false) if the command could not be sent (SendInput failed)
    let sent =
        match argv |> Array.toList with
        | "--sendExit" :: _ -> Some(W10Message.sendExit ())
        | "--sendPassMode" :: rest -> Some(W10Message.sendPassMode (getBool(rest)))
        | "--sendReloadProp" :: _ -> Some(W10Message.sendReloadProp ())
        | "--sendInitState" :: _ -> Some(W10Message.sendInitState ())
        // Not a command for the users: the old instance is exiting (see checkDoubleLaunch)
        | Ctx.RESTART_ADMIN_ARG :: rest ->
            waitForLock <- true
            rest |> List.tryHead |> Option.iter setSelectedProperties
            None
        | name :: _ when name.StartsWith("--") -> unknownCommand name; None
        | name :: _ -> setSelectedProperties name; None
        | _ -> None

    sent |> Option.iter (fun ok ->
        if ok then
            Thread.Sleep(1000)
            Environment.Exit(0)
        else
            Environment.Exit(1))


let private initSetFunctions () =
    Dispatcher.setMouseDispatcher ()
    Dispatcher.setKeyboardDispatcher ()
    EventHandler.setChangeTrigger ()
    Windows.setSendWheelRaw ()
    Windows.setInitScroll ()
    EventWaiter.setOfferEW ()
    EventHandler.setInitStateMEH ()
    KEventHandler.setInitStateKEH ()
    KEventHandler.setChangeTrigger ()

[<STAThread>]
[<EntryPoint>]
let main argv =
    // Before any window or message box (procArgv can show one): without it, they are drawn in the
    // classic style
    Application.EnableVisualStyles()
    // The texts of the controls drawn with GDI (TextRenderer), not GDI+ (true is still the default):
    // better on high DPI and with Japanese fonts, and measured as SettingsForm measures them
    Application.SetCompatibleTextRenderingDefault(false)
    // The controls drawn as in .NET 11 (rounded, larger check boxes), not Classic (the default).
    // Net11, not Latest: the look does not change with a newer .NET. SettingsForm adjusts the
    // widths for it (isModernStyles).
    Application.SetDefaultVisualStylesMode(VisualStylesMode.Net11)
    // Dark or light as the apps mode of Windows (the settings dialog, the tray menu, the input
    // box). SystemColors then return the colors of the mode, which SettingsForm uses. The message
    // boxes (MessageBox, drawn by Windows) stay light.
    Application.SetColorMode(SystemColorMode.System)
    // Also before any window: the windows of the modules (Ctx.notifyIcon, RawInput.messageWindow)
    // are created when the modules are first used, after this. Without it (no manifest either),
    // the menu and the dialogs are stretched and blurred on a scaled display. Scrolling does not
    // depend on it: the hook coordinates are physical and RawInput moves are relative.
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2) |> ignore
    procArgv argv

    Ctx.loadPropertiesFileOnly ()
    checkDoubleLaunch ()
    checkAdmin ()

    // Not at SessionEnding: the shutdown can still be canceled there (and W10Wheel kept running)
    SystemEvents.SessionEnded.Add (fun _ -> procExit())
    initSetFunctions ()

    Ctx.loadProperties false
    Ctx.setSystemTray ()
    match WinHook.setMouseHook () with
    | Some(err) ->
        Ctx.hideSystemTray ()
        Dialog.errorMessage (sprintf "%s: %s" (convLang "Failed mouse hook install") err) (convLang "Error")
        Environment.Exit(1)
    | None -> ()

    Application.Run()
    Debug.WriteLine("Exit message loop")
    procExit()
    0
