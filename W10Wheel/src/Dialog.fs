module Dialog

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Windows.Forms

let errorMessage msg title =
    MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore

let errorMessageE (e: Exception) =
    errorMessage e.Message (e.GetType().Name)

// Opens a folder or a URL as Explorer would. An error (e.g. no default browser) is shown, not
// thrown: it would end the program from an event handler.
let shellOpen (target: string) =
    try
        use p = System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo(target, UseShellExecute = true))
        ()
    with
        | e -> errorMessageE e
