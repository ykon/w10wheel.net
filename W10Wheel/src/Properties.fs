﻿module Properties

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.IO
open System.Collections.Generic
open System.Diagnostics
open System.Text.RegularExpressions

let private propLineRegex = new Regex("^\s*\S+\s*=\s*\S+\s*$")

type Properties() =
    let sdict = SortedDictionary<string, string>()
    let trims (ss:string[]) = ss |> Array.map (fun s -> s.Trim())

    let removeComment (l:string) =
        match l.IndexOf('#') with
        | -1 -> l
        | n -> l.Substring(0, n)

    let isPropLine l = propLineRegex.Match(l).Success

    let getKeyValue (l:string) =
        match l.Split('=') |> trims with
        | [|k; v|] -> (k, v)
        | _ -> invalidArg "l" "Invalid property line"

    let setDict (k, v) =
        Debug.WriteLine(sprintf "Load property: %s = %s" k v)
        sdict.[k] <- v

    member self.Clear (): unit =
        sdict.Clear()

    member self.Load (path:string, update:bool): unit =
        if update || sdict.Count = 0 then
            File.ReadAllLines(path) |>
            Array.map removeComment |> trims |>
            Array.filter isPropLine |> Array.map getKeyValue |>
            Array.iter setDict

    member self.Store (path:string): unit =
        let makePropLine k = (sprintf "%s=%s" k sdict.[k])
        let lines = Seq.map makePropLine sdict.Keys
        File.WriteAllLines(path, lines)

    member self.GetProperty (key:string): string =
        try
            sdict.[key]
        with
            | :? KeyNotFoundException as e -> raise (KeyNotFoundException(key))

    member self.GetPropertyOption (key:string): string option =
        match sdict.TryGetValue key with
        | true, value -> Some(value)
        | _ -> None

    member self.SetProperty (key:string, value:string): unit =
        sdict.[key] <- value

    member self.Item
        with get key = self.GetProperty key
        and set key value = self.SetProperty(key, value)

    member self.GetString (key:string): string =
        self.GetProperty(key)

    member self.GetInt (key:string): int =
        Int32.Parse(self.GetString(key))

    member self.GetDouble (key:string): double =
        Double.Parse(self.GetString(key))

    member self.GetBool (key:string): bool =
        Boolean.Parse(self.GetString(key))

    member self.GetArray (key:string): string[] =
        self.GetString(key).Split(',') |> trims |>
        Array.filter (fun s -> s <> "")

    member self.GetIntArray (key:string): int[] =
        self.GetArray(key) |> Array.map (fun s -> Int32.Parse(s))

    member self.GetDoubleArray (key:string): double[] =
        self.GetArray(key) |> Array.map (fun s -> Double.Parse(s))

    member self.SetInt (key:string, n:int): unit =
        self.SetProperty(key, n.ToString())

    member self.SetDouble (key:string, d:double): unit =
        self.SetProperty(key, d.ToString("F"))

    member self.SetBool (key:string, b:bool): unit =
        self.SetProperty(key, b.ToString())

        
let USER_DIR = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
let PROGRAM_NAME = AppDef.PROGRAM_NAME
let PROP_NAME = (sprintf ".%s" PROGRAM_NAME)
let PROP_EXT = "properties"
let DEFAULT_PROP_NAME = (sprintf "%s.%s" PROP_NAME PROP_EXT)
let DEFAULT_DEF = "Default"

let private BAD_DEFAULT_NAME = (sprintf "%s.%s.%s" PROP_NAME DEFAULT_DEF PROP_EXT)

let private userDefRegex = new Regex(sprintf "^\.%s\.(?!--)(.+)\.%s$" PROGRAM_NAME PROP_EXT)

let private isPropFile (path:String): bool =
    let name = Path.GetFileName(path)
    name <> BAD_DEFAULT_NAME && userDefRegex.Match(name).Success

let getUserDefName (path:String): string =
    let name = Path.GetFileName(path)
    userDefRegex.Match(name).Groups.[1].Value

let getPropFiles (): string[] =
    Directory.GetFiles(USER_DIR) |> Array.filter isPropFile 

let getDefaultPath (): string =
    Path.Combine(USER_DIR, DEFAULT_PROP_NAME)

let getPath (name:string): string =
    if name = "Default" then
        getDefaultPath()
    else
        Path.Combine(USER_DIR, (sprintf "%s.%s.%s" PROP_NAME name PROP_EXT))

let exists (name:string): bool =
    File.Exists(getPath name)

let copy (srcName:string) (destName:string): unit =
    let srcPath = getPath(srcName)
    let destPath = getPath(destName)

    File.Copy(srcPath, destPath)

let delete (name:string): unit =
    File.Delete(getPath name)

