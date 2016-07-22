module Properties

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.IO
open System.Collections.Generic
open System.Diagnostics

let USER_DIR = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

type Properties() =
    let sdict = SortedDictionary<string, string>()

    member private self.GetPath name =
        Path.Combine(USER_DIR, name)

    member self.Load (name: string): unit =
        let removeComment (l: string) =
            let si = l.IndexOf('#')
            if si = -1 then l else l.Substring(0, si)

        let splitEqual (l: string) =
            let pair: string array = l.Split('=')
            (pair.[0].Trim(), pair.[1].Trim())

        File.ReadAllLines(self.GetPath name) |>
        Array.map (fun l -> (removeComment l).Trim()) |>
        Array.filter (fun l -> l <> "") |>
        Array.map splitEqual |>
        Array.iter (fun (k, v) -> sdict.[k] <- v)

    member self.Store (name: string): unit =
        let lines =
            sdict.Keys |> Seq.map (fun k ->
                (sprintf "%s=%s" k sdict.[k]))
        File.WriteAllLines(self.GetPath name, lines)
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

    member self.SetBool (key: string, b: bool) =
        self.SetProperty(key, b.ToString())
        