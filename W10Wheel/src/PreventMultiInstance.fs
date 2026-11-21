module PreventMultiInstance

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.IO
open System.Diagnostics
open System.Threading

let LOCK_FILE_DIR = Path.GetTempPath()
let LOCK_FILE_NAME = AppDef.PROGRAM_NAME + ".lock"

let LOCK_FILE_PATH = Path.Combine(LOCK_FILE_DIR, LOCK_FILE_NAME)

let private lock: FileStream ref = ref null

let isLocked (): bool =
    Volatile.Read(&lock.contents) <> null

let tryLock (): bool =
    if isLocked() then
        raise (InvalidOperationException())

    try
        Volatile.Write(lock, new FileStream(LOCK_FILE_PATH, FileMode.OpenOrCreate,
                                 FileAccess.ReadWrite, FileShare.None))
        true
    with
        | :? IOException -> false

let unlock () =
    // Once (isLocked is false after)
    let stream = Interlocked.Exchange(&lock.contents, null)
    if not (isNull stream) then
        stream.Close()
