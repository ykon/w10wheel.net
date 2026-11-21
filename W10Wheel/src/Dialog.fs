module Dialog

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Windows.Forms
open Microsoft.VisualBasic

let errorMessage msg title =
    MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore

let errorMessageE (e: Exception) =
    errorMessage e.Message (e.GetType().Name)

let openTextInputBox msg title: string option =
    let input = Interaction.InputBox(msg, title)
    if input <> "" then Some(input) else None

