﻿(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics
open System.Runtime.InteropServices
open System.Windows.Forms
open Microsoft.FSharp.NativeInterop
open Microsoft.Win32

let private messageDoubleLaunch () =
    MessageBox.Show("Double Launch?", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore

let private procExit () =
    WinHook.unhook()
    Ctx.storeProperties()
    PreventMultiInstance.unlock()

let private procArgv (argv: string array) =
    if argv.Length = 1 then
        let name = argv.[0]
        if Properties.exists(name) then
            Ctx.setSelectedProperties name
        else
            Dialog.errorMessage (sprintf "'%s' properties does not exist." name) "Error"

[<STAThread>]
[<EntryPoint>]
let main argv =
    if not (PreventMultiInstance.tryLock()) then
        messageDoubleLaunch()
        Environment.Exit(0)

    SystemEvents.SessionEnding.Add (fun _ -> procExit())

    WinHook.setMouseDispatcher(Dispatcher.getMouseDispatcher())
    WinHook.setKeyboardDispatcher(Dispatcher.getKeyboardDispatcher())
    EventHandler.setChangeTrigger()
    Windows.setInitScroll()

    procArgv argv
    Ctx.loadProperties()
    Ctx.setSystemTray()
    
    WinHook.setMouseHook()
    //Hook.setKeyboardHook()
    Application.Run()

    Debug.WriteLine("exit message loop")
    procExit()
    0
        
