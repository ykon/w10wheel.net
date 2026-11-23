module Properties

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.IO
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.Text
open System.Text.RegularExpressions
open System.Threading

// key=value: the value can have spaces (e.g. "1, 2, 3" as in AccelTable.txt), the key cannot
// be empty or have "="
let private propLineRegex = new Regex("^\s*[^\s=]+\s*=\s*\S.*$")

// The file of path written with lines (UTF-8 without a BOM, CRLF: as File.WriteAllLines) through a
// temporary file in its folder, flushed to the disk, which then replaces it: a write that fails
// (e.g. a full disk, W10Wheel or Windows ended meanwhile) leaves the file as it was, not half
// written or empty. File.Replace keeps the attributes of the file (e.g. hidden, which the direct
// write cannot open) and its permissions; if it cannot merge them (e.g. a file system without them,
// a network share), the file is still replaced: not written directly at each change for this.
// Written directly as before:
// - a read-only file: not opened, the error says so with its path (nothing written), as before
// - a symbolic link (a reparse point): it stays a link, its target is written
// - the replacement failed (e.g. the file open in another program that does not share its
//   deletion, the temporary file being scanned): tried 3 times first, 10 ms apart (about 16 ms
//   each with the timer of Windows: about 31 ms in all), not longer as this thread also runs the
//   low-level hooks (the mouse stops meanwhile). The temporary file has been written, so the disk
//   had room, which its deletion gives back first (the new lines may need more than the old).
let private writeLines (path: string) (lines: string[]): unit =
    let attrs = if File.Exists(path) then Some(File.GetAttributes(path)) else None
    let direct () = File.WriteAllLines(path, lines)
    match attrs with
    | Some a when a.HasFlag(FileAttributes.ReadOnly) || a.HasFlag(FileAttributes.ReparsePoint) ->
        direct ()
    | _ ->
        // A name of its own (as the Java version, which shares the file): not another's being written
        let dir = Path.GetDirectoryName(Path.GetFullPath(path))
        let tmp = Path.Combine(dir, sprintf "%s-%s.tmp" (Path.GetFileName(path)) (Guid.NewGuid().ToString("N").Substring(0, 8)))
        let replaced =
            try
                do
                    use fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                    use w = new StreamWriter(fs, UTF8Encoding(false))
                    lines |> Array.iter w.WriteLine
                    w.Flush()
                    fs.Flush(true)
                let replace () =
                    if attrs.IsSome then File.Replace(tmp, path, null, true) else File.Move(tmp, path)
                let rec tryReplace n =
                    try
                        replace ()
                        true
                    with
                        | :? IOException | :? UnauthorizedAccessException when n > 1 ->
                            Thread.Sleep(10)
                            tryReplace (n - 1)
                        | :? IOException | :? UnauthorizedAccessException -> false
                tryReplace 3
            finally
                // Not over the error of the write
                try File.Delete(tmp) with _ -> ()
        if not replaced then
            Debug.WriteLine("writeLines: not replaced, written directly: " + path)
            direct ()

type Properties() =
    let sdict = SortedDictionary<string, string>()
    let trims (ss:string[]) = ss |> Array.map (fun s -> s.Trim())

    let removeComment (l:string) =
        match l.IndexOf('#') with
        | -1 -> l
        | n -> l.Substring(0, n)

    let isPropLine l = propLineRegex.Match(l).Success

    let getKeyValue (l:string) =
        // At the first "=": the value can have "=" (a line with spaces is read too)
        match l.Split([|'='|], 2) |> trims with
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
        let lines = Seq.map makePropLine sdict.Keys |> Array.ofSeq
        writeLines path lines

    // Writes the values of keys to the file (path) as it is now: the lines of these keys get their
    // values (a comment after a value is kept), the other lines stay as they are (the other keys,
    // the comments, the empty lines, their order), and the keys not in it are added at the end
    member self.Update (path:string, keys:string list): unit =
        let toWrite = HashSet<string>(keys)
        let written = HashSet<string>()
        let keyOf (l:string) =
            let p = (removeComment l).Trim()
            if isPropLine p then Some(fst (getKeyValue p)) else None
        let lines =
            File.ReadAllLines(path) |> Array.map (fun l ->
                match keyOf l with
                | Some k when toWrite.Contains k ->
                    written.Add k |> ignore
                    let comment =
                        match l.IndexOf('#') with
                        | -1 -> ""
                        | n -> " " + l.Substring(n)
                    sprintf "%s=%s%s" k sdict.[k] comment
                | _ -> l)
        let added =
            keys |> List.filter (fun k -> not (written.Contains k)) |>
            List.map (fun k -> sprintf "%s=%s" k sdict.[k])
        writeLines path (Array.append lines (Array.ofList added))

    member self.Keys: string list =
        List.ofSeq sdict.Keys

    // The keys and the values of p instead of these
    member self.CopyFrom (p:Properties): unit =
        sdict.Clear()
        for k in p.Keys do sdict.[k] <- p.[k]

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
        Int32.Parse(self.GetString(key), CultureInfo.InvariantCulture)

    member self.GetDouble (key:string): double =
        Double.Parse(self.GetString(key), CultureInfo.InvariantCulture)

    member self.GetBool (key:string): bool =
        Boolean.Parse(self.GetString(key))

    member self.GetArray (key:string): string[] =
        self.GetString(key).Split(',') |> trims |>
        Array.filter (fun s -> s <> "")

    member self.GetIntArray (key:string): int[] =
        self.GetArray(key) |> Array.map (fun s -> Int32.Parse(s, CultureInfo.InvariantCulture))

    member self.GetDoubleArray (key:string): double[] =
        self.GetArray(key) |> Array.map (fun s -> Double.Parse(s, CultureInfo.InvariantCulture))

    member self.SetInt (key:string, n:int): unit =
        self.SetProperty(key, n.ToString(CultureInfo.InvariantCulture))

    member self.SetDouble (key:string, d:double): unit =
        self.SetProperty(key, d.ToString("F", CultureInfo.InvariantCulture))

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

// The name of existing properties as in their file name (None if they do not exist, as the file
// system tells): name may differ in case (e.g. typed as an argument), which Windows ignores, but
// not the list of the settings dialog, which compares the names (the selected properties would not
// be shown). The same name if it is listed (only a folder made case-sensitive can have both cases).
let findName (name:string): string option =
    if not (exists name) then
        None
    else
        let names = try getPropFiles() |> Array.map getUserDefName with _ -> [||]
        let same (comparison: StringComparison) = names |> Array.tryFind (fun n -> String.Equals(n, name, comparison))
        same StringComparison.Ordinal
        |> Option.orElseWith (fun () -> same StringComparison.OrdinalIgnoreCase)
        |> Option.defaultValue name
        |> Some

let copy (srcName:string) (destName:string): unit =
    let srcPath = getPath(srcName)
    let destPath = getPath(destName)

    File.Copy(srcPath, destPath)
    // Not read-only as the source may be (File.Copy copies its attributes): the new properties are
    // written when a setting is changed
    let attrs = File.GetAttributes(destPath)
    if attrs.HasFlag(FileAttributes.ReadOnly) then
        File.SetAttributes(destPath, attrs &&& ~~~FileAttributes.ReadOnly)

let delete (name:string): unit =
    File.Delete(getPath name)

