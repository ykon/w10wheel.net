module WinError

open System.Runtime.InteropServices
open System.ComponentModel

let getLastErrorCode (): int =
    Marshal.GetLastWin32Error()

let getLastErrorMessage (): string =
    (new Win32Exception(getLastErrorCode())).Message

