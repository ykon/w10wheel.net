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

let private isValidNumber input low up =
    match Int32.TryParse(input)  with
    | (true, res) -> res >= low && res <= up 
    | _ -> false

let openNumberInputBox name low up cur: int option =
    let msg = sprintf "%s (%d - %d)" name low up
    let dvalue = cur.ToString()
    let input = Interaction.InputBox(msg, "Set Number", dvalue)
    if isValidNumber input low up then
        Some(Int32.Parse(input))
    else
        None

let openTextInputBox msg title: string option =
    let input = Interaction.InputBox(msg, title)
    if input <> "" then Some(input) else None

