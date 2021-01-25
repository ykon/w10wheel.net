(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open System.Threading
open System.Windows.Forms
open Microsoft.Win32
open System.Security.Principal

[<Literal>]
let WikiUrl = "https://github.com/ykon/w10wheel.net/wiki"

let private convLang msg =
    Ctx.convLang msg

// Before loadProperties()
let private convLangP msg =
    Ctx.convLangWithProp msg

let private messageDoubleLaunch () =
    MessageBox.Show(convLangP "Double Launch?", convLangP "Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore

let private checkDoubleLaunch () =
    if not (PreventMultiInstance.tryLock()) then
        messageDoubleLaunch()
        Environment.Exit(1)

let private isAdmin () =
    let myDomain = Thread.GetDomain();
    myDomain.SetPrincipalPolicy(PrincipalPolicy.WindowsPrincipal);
    let myPrincipal = Thread.CurrentPrincipal :?> WindowsPrincipal
    myPrincipal.IsInRole(WindowsBuiltInRole.Administrator)

let private showAdminMesssage (): DialogResult =
    MessageBox.Show(convLangP LocaleData.AdminMessage, convLangP "Info", MessageBoxButtons.OKCancel, MessageBoxIcon.Information)

let private checkAdmin () =
    if not (isAdmin ()) then
        if (showAdminMesssage ()) = DialogResult.OK then
            Process.Start(WikiUrl) |> ignore

let private procExit () =
    Debug.WriteLine("procExit")

    WinHook.unhook()
    Ctx.storeProperties()
    PreventMultiInstance.unlock()

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

let private setSelectedProperties name =
    if Properties.exists(name) then
        Ctx.setSelectedProperties name
    else
        Dialog.errorMessage (sprintf "%s: %s" (convLang "Properties does not exist") name) (convLang "Error")

let private unknownCommand name =
    Dialog.errorMessage (sprintf "%s: %s" (convLang "Unknown Command") name) (convLang "Command Error")
    Environment.Exit(1)

let private procArgv (argv: string[]) =
    Debug.WriteLine("procArgv")

    match argv |> Array.toList with
    | "--sendExit" :: _ -> W10Message.sendExit ()
    | "--sendPassMode" :: rest -> W10Message.sendPassMode (getBool(rest))
    | "--sendReloadProp" :: _ -> W10Message.sendReloadProp ()
    | "--sendInitState" :: _ -> W10Message.sendInitState ()
    | name :: _ when name.StartsWith("--") -> unknownCommand name
    | name :: _ -> setSelectedProperties name
    | _ -> ()

    if argv.Length > 0 && argv.[0].StartsWith("--send") then
        Thread.Sleep(1000)
        Environment.Exit(0)


let private initSetFunctions () =
    Dispatcher.setMouseDispatcher ()
    Dispatcher.setKeyboardDispatcher ()
    EventHandler.setChangeTrigger ()
    Windows.setSendWheelRaw ()
    Windows.setInitScroll ()
    EventWaiter.setOfferEW ()
    EventHandler.setInitStateMEH ()
    KEventHandler.setInitStateKEH ()

[<STAThread>]
[<EntryPoint>]
let main argv =
    procArgv argv

    Ctx.loadPropertiesFileOnly ()
    checkDoubleLaunch ()
    checkAdmin ()

    SystemEvents.SessionEnding.Add (fun _ -> procExit())
    initSetFunctions ()

    Ctx.loadProperties false
    Ctx.setSystemTray ()
    if not (WinHook.setMouseHook ()) then
        Dialog.errorMessage (sprintf "%s: %s" (convLang "Failed mouse hook install") (WinError.getLastErrorMessage())) (convLang "Error")
        Environment.Exit(1)

    Application.Run()
    Debug.WriteLine("Exit message loop")
    procExit()
    0
