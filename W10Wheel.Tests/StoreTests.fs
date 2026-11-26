module StoreTests

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

// What Ctx knows of the properties files (loaded, the errors of a load and of a store), as the
// Rust version tests it (w10wheel.rs, properties.rs). In a folder of the tests (Harness.USER_DIR),
// not the user's.

open System
open System.IO
open Xunit
open Harness

let private propertiesPath () = Properties.getPath Properties.DEFAULT_DEF
let private exclusionsPath () = Properties.getExclusionsPath Properties.DEFAULT_DEF

// The folder emptied, the Default properties loaded from it (none: the default values)
let private loadEmpty () =
    reset DataID.RightDrag DataID.None
    for f in Directory.GetFiles(Properties.USER_DIR) do
        File.SetAttributes(f, FileAttributes.Normal)
        File.Delete(f)
    Ctx.setSelectedProperties Properties.DEFAULT_DEF
    Ctx.loadProperties true

// The file opened by another program that shares nothing (e.g. an editor writing it): it can be
// neither read nor written
let private locked (path: string) =
    new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)

let private readOnly (path: string) (b: bool) =
    File.SetAttributes(path, if b then FileAttributes.ReadOnly else FileAttributes.Normal)

let private status () = Ctx.ForTests.storeStatus ()

let private NOT_SAVED = Some "The changes could not be saved to the properties file"
let private NOT_READ = Some "The properties file could not be read"
let private EXCLUSIONS_NOT_SAVED = Some "The changes could not be saved to the exclusions file"
let private EXCLUSIONS_NOT_READ = Some "The exclusions file could not be read: changes are not saved"

[<Fact>]
let ``a Reload that fails keeps the properties loaded, and the stores after it`` () =
    loadEmpty ()
    Assert.Equal(None, status ())
    Ctx.ForTests.setNumber DataID.pollTimeout 300
    Ctx.storeProperties ()
    Assert.True(File.Exists(propertiesPath ()))
    Assert.Equal(None, status ())

    // The file cannot be read: the settings stay, still stored, with why
    do
        use _f = locked (propertiesPath ())
        Assert.False(Ctx.ForTests.reloadProperties ())
        Assert.Equal(NOT_READ, status ())

        // A change while it cannot be written: not stored, told before the error of the load
        Ctx.ForTests.setNumber DataID.pollTimeout 350
        Ctx.storeProperties ()
        Assert.Equal(NOT_SAVED, status ())

    // Stored when it can be written again; the error of the load is kept until a load succeeds
    Ctx.storeProperties ()
    Assert.Equal(NOT_READ, status ())
    Assert.Contains("pollTimeout=350", File.ReadAllText(propertiesPath ()))
    Assert.True(Ctx.ForTests.reloadProperties ())
    Assert.Equal(None, status ())

[<Fact>]
let ``the properties not read at startup are not stored`` () =
    loadEmpty ()
    Ctx.ForTests.setNumber DataID.pollTimeout 300
    Ctx.storeProperties ()
    let before = File.ReadAllText(propertiesPath ())
    do
        use _f = locked (propertiesPath ())
        Ctx.loadProperties true
        Assert.Equal(Some "The properties file could not be read: changes are not saved", status ())
    Ctx.ForTests.setNumber DataID.pollTimeout 400
    Ctx.storeProperties ()
    Assert.Equal(before, File.ReadAllText(propertiesPath ()))

[<Fact>]
let ``the exclusions' store error is kept through a Reload that cannot read them`` () =
    loadEmpty ()
    Ctx.setExcludedPrograms [| EXCLUDED_PROGRAM |]
    Ctx.storeProperties ()
    Assert.True(File.Exists(exclusionsPath ()))

    // Read-only: their change is not stored
    readOnly (exclusionsPath ()) true
    try
        Ctx.setExcludedPrograms [| EXCLUDED_PROGRAM; @"C:\Other\other.exe" |]
        Ctx.storeProperties ()
        Assert.Equal(EXCLUSIONS_NOT_SAVED, status ())

        // A Reload that cannot read them keeps the list running and the error of its store
        do
            use _f = locked (exclusionsPath ())
            Assert.True(Ctx.ForTests.reloadProperties ())
            Assert.Equal(EXCLUSIONS_NOT_READ, status ())
            Assert.Equal<string[]>([| EXCLUDED_PROGRAM; @"C:\Other\other.exe" |], Ctx.getExcludedPrograms ())
            match Ctx.ForTests.unstoredReason false with
            | Some(:? UnauthorizedAccessException) -> ()
            | other -> failwithf "not the error of the store: %A" other
    finally
        readOnly (exclusionsPath ()) false

    // Read again: the list of the file, nothing left not stored
    Assert.True(Ctx.ForTests.reloadProperties ())
    Assert.Equal(None, status ())
    Assert.Equal<string[]>([| EXCLUDED_PROGRAM |], Ctx.getExcludedPrograms ())

[<Fact>]
let ``the reason of the changes not stored, of both files`` () =
    loadEmpty ()
    Ctx.ForTests.setNumber DataID.pollTimeout 300
    Ctx.setExcludedPrograms [| EXCLUDED_PROGRAM |]
    Ctx.storeProperties ()

    readOnly (propertiesPath ()) true
    readOnly (exclusionsPath ()) true
    try
        Ctx.ForTests.setNumber DataID.pollTimeout 350
        Ctx.setExcludedPrograms [| EXCLUDED_PROGRAM; @"C:\Other\other.exe" |]
        Ctx.storeProperties ()
        let reason settings =
            match Ctx.ForTests.unstoredReason settings with
            | Some e -> e.Message
            | None -> ""
        // Of the settings if some are left, else of the excluded programs
        Assert.Contains(Path.GetFileName(propertiesPath ()), reason true)
        Assert.Contains(Path.GetFileName(exclusionsPath ()), reason false)
    finally
        readOnly (propertiesPath ()) false
        readOnly (exclusionsPath ()) false
