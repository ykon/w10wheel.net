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
    | NoneEvent

    member self.Name = Mouse.getUnionCaseName(self) + (sprintf " (%d)" self.VKCode)

    member self.IsNone =
        match self with
        | NoneEvent -> true
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

let private vkCodeMap =
    Map.ofList <| [
        ("VK_PAUSE", 0x13)
        ("VK_CAPITAL", 0x14)
        ("VK_CONVERT", 0x1C)
        ("VK_NONCONVERT", 0x1D)
        ("VK_SNAPSHOT", 0x2C)
        ("VK_LWIN", 0x5B)
        ("VK_RWIN", 0x5C)
        ("VK_APPS", 0x5D)
        ("VK_NUMLOCK", 0x90)
        ("VK_SCROLL", 0x91)
        ("VK_LSHIFT", 0xA0)
        ("VK_RSHIFT", 0xA1)
        ("VK_LCONTROL", 0xA2)
        ("VK_RCONTROL", 0xA3)
        ("VK_LMENU", 0xA4)
        ("VK_RMENU", 0xA5) 
    ]

// http://stackoverflow.com/questions/30258611/swap-key-and-value-in-a-map-in-fsharp
let private revVKCodeMap =
    let swap (x, y) = y, x
    let swapAll p = List.map swap p
    vkCodeMap |> Map.toList |> swapAll |> Map.ofList

let getVKCode name = vkCodeMap.[name]
let getName vkCode = revVKCodeMap.[vkCode]

