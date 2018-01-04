module Keyboard

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System

type KHookInfo = WinAPI.KBDLLHOOKSTRUCT

type KeyboardEvent =
    | KeyDown of KHookInfo
    | KeyUp of KHookInfo
    | NonEvent

    member self.Name = Mouse.getUnionCaseName(self) + (sprintf " (%d)" self.VKCode)

    member self.IsNone =
        match self with
        | NonEvent -> true
        | _ -> false

    member self.Info =
        match self with
        | KeyDown(info) | KeyUp(info) -> info
        | _ -> raise (ArgumentException())

    member self.VKCode =
        (int self.Info.vkCode)

    member self.SameEvent ke2 =
        match self, ke2 with
        | KeyDown(_), KeyDown(_) -> true
        | KeyUp(_), KeyUp(_) -> true
        | _ -> false

    member self.SameKey (ke2: KeyboardEvent) =
        self.VKCode = ke2.VKCode

    member self.Same ke2 =
        (self.SameEvent ke2) && (self.SameKey ke2)

// https://msdn.microsoft.com/library/windows/desktop/dd375731
let private vkCodeMap =
    Map.ofList <| [
        (DataID.None, 0)
        (DataID.VK_TAB, 0x09)
        (DataID.VK_PAUSE, 0x13)
        (DataID.VK_CAPITAL, 0x14)
        (DataID.VK_CONVERT, 0x1C)
        (DataID.VK_NONCONVERT, 0x1D)
        (DataID.VK_PRIOR, 0x21)
        (DataID.VK_NEXT, 0x22)
        (DataID.VK_END, 0x23)
        (DataID.VK_HOME, 0x24)
        (DataID.VK_SNAPSHOT, 0x2C)
        (DataID.VK_INSERT, 0x2D)
        (DataID.VK_DELETE, 0x2E)
        (DataID.VK_LWIN, 0x5B)
        (DataID.VK_RWIN, 0x5C)
        (DataID.VK_APPS, 0x5D)
        (DataID.VK_NUMLOCK, 0x90)
        (DataID.VK_SCROLL, 0x91)
        (DataID.VK_LSHIFT, 0xA0)
        (DataID.VK_RSHIFT, 0xA1)
        (DataID.VK_LCONTROL, 0xA2)
        (DataID.VK_RCONTROL, 0xA3)
        (DataID.VK_LMENU, 0xA4)
        (DataID.VK_RMENU, 0xA5) 
    ]

// http://stackoverflow.com/questions/30258611/swap-key-and-value-in-a-map-in-fsharp
let private revVKCodeMap =
    let swap (x, y) = y, x
    let swapAll p = List.map swap p
    vkCodeMap |> Map.toList |> swapAll |> Map.ofList

let getVKCode name = vkCodeMap.[name]
let getName vkCode = revVKCodeMap.[vkCode]

