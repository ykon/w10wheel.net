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

let setPriority p =
    let cp = Process.GetCurrentProcess()
    match p with
    | Normal -> cp.PriorityClass <- ProcessPriorityClass.Normal
    | AboveNormal -> cp.PriorityClass <- ProcessPriorityClass.AboveNormal
    | High -> cp.PriorityClass <- ProcessPriorityClass.High

