module PreventMultiInstance

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.IO

let LOCK_FILE_DIR = Path.GetTempPath()
let LOCK_FILE_NAME = AppDef.PROGRAM_NAME + ".lock"

let LOCK_FILE_PATH = Path.Combine(LOCK_FILE_DIR, LOCK_FILE_NAME)

let mutable private lockStream: FileStream = null

let tryLock (): bool =
    if lockStream <> null then
        raise (InvalidOperationException())

    try 
        lockStream <- new FileStream(LOCK_FILE_PATH, FileMode.OpenOrCreate,
                                 FileAccess.ReadWrite, FileShare.None)
        true
    with
        | :? IOException -> false

let unlock () =
    if lockStream = null then
        raise (InvalidOperationException())

    lockStream.Close()
