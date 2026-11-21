module ProcessPriority

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Diagnostics

type Priority =
    | Normal
    | AboveNormal
    | High

    member self.Name = Mouse.getUnionCaseName(self)

let getPriority = function
    | DataID.High -> High
    | DataID.AboveNormal | "Above Normal" -> AboveNormal
    | DataID.Normal -> Normal
    | e -> raise (ArgumentException(e))

// A failure is ignored (the priority is not changed): thrown, it would stop loading the other
// settings, which would then be stored with their default values
let setPriority p =
    use cp = Process.GetCurrentProcess()
    try
        match p with
        | Normal -> cp.PriorityClass <- ProcessPriorityClass.Normal
        | AboveNormal -> cp.PriorityClass <- ProcessPriorityClass.AboveNormal
        | High -> cp.PriorityClass <- ProcessPriorityClass.High
    with
        | :? ComponentModel.Win32Exception as e -> Debug.WriteLine("setPriority: " + e.Message)

