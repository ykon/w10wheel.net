(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open System.Threading
open System.Windows.Forms
open Microsoft.Win32

let private messageDoubleLaunch () =
    MessageBox.Show("Double Launch?", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore

let private procExit () =
    Debug.WriteLine("procExit")

    WinHook.unhook()
    Ctx.storeProperties()
    PreventMultiInstance.unlock()

let private getBool (argv: string array) i =
    try
        if argv.Length = 1 then true else Boolean.Parse(argv.[i])
    with
        | :? FormatException as e ->
            Dialog.errorMessageE e
            Environment.Exit(0)
            false

let private setSelectedProperties name =
    if Properties.exists(name) then
        Ctx.setSelectedProperties name
    else
        Dialog.errorMessage (sprintf "'%s' properties does not exist." name) "Error"

let private unknownCommand name =
    Dialog.errorMessage ("Unknown Command: " + name) "Command Error"
    Environment.Exit(0)

let private procArgv (argv: string array) =
    Debug.WriteLine("procArgv")

    if argv.Length > 0 then
        match argv.[0] with
        | "--sendExit" -> W10Message.sendExit ()
        | "--sendPassMode" -> W10Message.sendPassMode (getBool argv 1)
        | "--sendReloadProp" -> W10Message.sendReloadProp ()
        | "--sendInitState" -> W10Message.sendInitState ()
        | name when name.StartsWith("--") -> unknownCommand name
        | name -> setSelectedProperties name

        if argv.[0].StartsWith("--send") then
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

    if not (PreventMultiInstance.tryLock()) then
        messageDoubleLaunch()
        Environment.Exit(0)

    SystemEvents.SessionEnding.Add (fun _ -> procExit())
    initSetFunctions ()

    Ctx.loadProperties ()
    Ctx.setSystemTray ()
    WinHook.setMouseHook ()

    Application.Run()
    Debug.WriteLine("Exit message loop")
    procExit()
    0
        
