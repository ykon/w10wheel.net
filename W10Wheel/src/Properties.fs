module Properties

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.IO
open System.Collections.Generic
open System.Diagnostics
open System.Text.RegularExpressions

type Properties() =
    let sdict = SortedDictionary<string, string>()

    member self.Load (path: string): unit =
        let removeComment (l: string) =
            let si = l.IndexOf('#')
            if si = -1 then l else l.Substring(0, si)

        let splitEqual (l: string) =
            let pair: string array = l.Split('=')
            (pair.[0].Trim(), pair.[1].Trim())

        File.ReadAllLines(path) |>
        Array.map (fun l -> (removeComment l).Trim()) |>
        Array.filter (fun l -> l <> "") |>
        Array.map splitEqual |>
        Array.iter (fun (k, v) -> sdict.[k] <- v)

    member self.Store (path: string): unit =
        let lines =
            sdict.Keys |> Seq.map (fun k ->
                (sprintf "%s=%s" k sdict.[k]))
        File.WriteAllLines(path, lines)
        ()

    member self.GetProperty (key: string): string =
        try
            sdict.[key]
        with
            | :? KeyNotFoundException as e -> raise (KeyNotFoundException(key))

    member self.SetProperty (key: string, value: string) =
        sdict.[key] <- value

    member self.Item
        with get key = self.GetProperty key
        and set key value = self.SetProperty(key, value)

    member self.GetString (key: string) =
        self.GetProperty(key)

    member self.GetInt (key: string): int =
        Int32.Parse(self.GetString(key))

    member self.GetDouble (key: string): double =
        Double.Parse(self.GetString(key))

    member self.GetBool (key: string): bool =
        Boolean.Parse(self.GetString(key))

    member self.GetArray (key: string): string array =
        self.GetString(key).Split(',') |>
        Array.map (fun s -> s.Trim()) |>
        Array.filter (fun s -> s <> "")

    member self.GetIntArray (key: string): int array =
        self.GetArray(key) |> Array.map (fun s -> Int32.Parse(s))

    member self.GetDoubleArray (key: string): double array =
        self.GetArray(key) |> Array.map (fun s -> Double.Parse(s))

    member self.SetInt (key: string, n: int) =
        self.SetProperty(key, n.ToString())

    member self.SetDouble (key: string, d: double) =
        self.SetProperty(key, d.ToString("F"))

    member self.SetBool (key: string, b: bool) =
        self.SetProperty(key, b.ToString())
        
let USER_DIR = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
let PROGRAM_NAME = AppDef.PROGRAM_NAME
let PROP_NAME = (sprintf ".%s" PROGRAM_NAME)
let PROP_EXT = "properties"
let DEFAULT_PROP_NAME = (sprintf "%s.%s" PROP_NAME PROP_EXT)
let DEFAULT_DEF = "Default"

let private BAD_DEFAULT_NAME = (sprintf "%s.%s.%s" PROP_NAME DEFAULT_DEF PROP_EXT)

let private userDefPat = new Regex(sprintf "^\.%s\.(.+)\.%s$" PROGRAM_NAME PROP_EXT)

let private isPropFile (path: String): bool =
    let name = Path.GetFileName(path)
    name <> BAD_DEFAULT_NAME && userDefPat.Match(name).Success

let getUserDefName (path: String) =
    let name = Path.GetFileName(path)
    userDefPat.Match(name).Groups.[1].Value

let getPropFiles () =
    Directory.GetFiles(USER_DIR) |> Array.filter isPropFile 

let getDefaultPath () =
    Path.Combine(USER_DIR, DEFAULT_PROP_NAME)

let getPath name =
    if name = "Default" then
        getDefaultPath()
    else
        Path.Combine(USER_DIR, (sprintf "%s.%s.%s" PROP_NAME name PROP_EXT))

let exists name =
    File.Exists(getPath name)

let copy (srcName: string) (destName: string) =
    let srcPath = getPath(srcName)
    let destPath = getPath(destName)

    File.Copy(srcPath, destPath)

let delete (name: string) =
    File.Delete(getPath name)

