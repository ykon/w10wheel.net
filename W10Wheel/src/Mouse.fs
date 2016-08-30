module Mouse

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open Microsoft.FSharp.Reflection

// http://stackoverflow.com/questions/1259039/what-is-the-enum-getname-equivalent-for-f-union-member
let getUnionCaseName (x:'a) = 
    match FSharpValue.GetUnionFields(x, typeof<'a>) with
    | case, _ -> case.Name

type HookInfo = WinAPI.MSLLHOOKSTRUCT

type MouseEvent =
    | LeftDown of HookInfo
    | LeftUp of HookInfo
    | RightDown of HookInfo
    | RightUp of HookInfo
    | MiddleDown of HookInfo
    | MiddleUp of HookInfo
    | X1Down of HookInfo
    | X1Up of HookInfo
    | X2Down of HookInfo
    | X2Up of HookInfo
    | Move of HookInfo
    | NoneEvent

    member self.Name = getUnionCaseName(self)

    member self.Info =
        match self with
        | LeftDown(info) | LeftUp(info) -> info
        | RightDown(info) | RightUp(info) -> info
        | MiddleDown(info) | MiddleUp(info) -> info
        | X1Down(info) | X1Up(info) -> info
        | X2Down(info) | X2Up(info) -> info
        | Move(info) -> info
        | _ -> raise (ArgumentException())

    member self.IsDown =
        match self with
        | LeftDown(_) | RightDown(_) | MiddleDown(_) | X1Down(_) | X2Down(_) -> true
        | _ -> false

    member self.IsUp =
        match self with
        | LeftUp(_) | RightUp(_) | MiddleUp(_) | X1Up(_) | X2Up(_) -> true
        | _ -> false

    member self.IsSingle =
        match self with
        | MiddleDown(_) | MiddleUp(_) -> true
        | X1Down(_) | X1Up(_) -> true
        | X2Down(_) | X2Up(_) -> true
        | _ -> false

    member self.IsLR =
        match self with 
        | LeftDown(_) | LeftUp(_) | RightDown(_) | RightUp(_) -> true
        | _ -> false

    member self.IsNone =
        match self with
        | NoneEvent -> true
        | _ -> false

    member self.SameEvent me2 =
        match self, me2 with
        | LeftDown(_), LeftDown(_) | LeftUp(_), LeftUp(_) -> true
        | RightDown(_), RightDown(_) | RightUp(_), RightUp(_) -> true
        | MiddleDown(_), MiddleDown(_) | MiddleUp(_), MiddleUp(_) -> true
        | X1Down(_), X1Down(_) | X1Up(_), X1Up(_) -> true
        | X2Down(_), X2Down(_) | X2Up(_), X2Up(_)  -> true
        | _ -> false

    member self.SameButton me2 =
        match self, me2 with
        | LeftDown(_), LeftUp(_) | LeftUp(_), LeftDown(_) -> true
        | RightDown(_), RightUp(_) | RightUp(_), RightDown(_) -> true
        | MiddleDown(_), MiddleUp(_) | MiddleUp(_), MiddleDown(_) -> true
        | X1Down(_), X1Up(_) | X1Up(_), X1Down(_) -> true
        | X2Down(_), X2Up(_) | X2Up(_), X2Down(_) -> true
        | _ -> false

let (|LeftEvent|_|) (me: MouseEvent) =
    match me with
    | LeftDown(_) | LeftUp(_) -> Some(me.Info)
    | _ -> None

let (|RightEvent|_|) (me: MouseEvent) =
    match me with
    | RightDown(_) | RightUp(_) -> Some(me.Info)
    | _ -> None

let (|MiddleEvent|_|) (me: MouseEvent) =
    match me with
    | MiddleDown(_) | MiddleUp(_) -> Some(me.Info)
    | _ -> None

let (|X1Event|_|) (me: MouseEvent) =
    match me with
    | X1Down(_) | X1Up(_) -> Some(me.Info)
    | _ -> None

let (|X2Event|_|) (me: MouseEvent) =
    match me with
    | X2Down(_) | X2Up(_) -> Some(me.Info)
    | _ -> None

type MouseClick =
    | LeftClick of HookInfo
    | RightClick of HookInfo
    | MiddleClick of HookInfo
    | X1Click of HookInfo
    | X2Click of HookInfo

    member self.Name = getUnionCaseName(self)

    member self.Info =
        match self with
        | LeftClick(info) | RightClick(info) -> info
        | MiddleClick(info) -> info
        | X1Click(info) | X2Click(info) -> info

type Trigger =
    | LRTrigger
    | LeftTrigger
    | RightTrigger
    | MiddleTrigger
    | X1Trigger
    | X2Trigger
    | LeftDragTrigger
    | RightDragTrigger
    | MiddleDragTrigger
    | X1DragTrigger
    | X2DragTrigger
    | NoneTrigger

    member self.Name = getUnionCaseName(self)

    member self.IsSingle =
        match self with
        | MiddleTrigger | X1Trigger | X2Trigger -> true
        | _ -> false

    member self.IsDouble =
        match self with
        | LRTrigger | LeftTrigger | RightTrigger -> true
        | _ -> false
        
    member self.IsDrag =
        match self with
        | LeftDragTrigger | RightDragTrigger | MiddleDragTrigger | X1DragTrigger | X2DragTrigger -> true
        | _ -> false

    member self.IsNone = self = NoneTrigger

let isXButton1 (mouseData: uint32) =
    (mouseData >>> 16) = (uint32 WinAPI.XBUTTON1)

let isXButton2 (mouseData: uint32) =
    not (isXButton1 mouseData)

let getTrigger = function
    | LeftEvent(_) -> LeftTrigger
    | RightEvent(_) -> RightTrigger
    | MiddleEvent(_) -> MiddleTrigger
    | X1Event(_) -> X1Trigger
    | X2Event(_) -> X2Trigger
    | _ -> raise (ArgumentException())

let getTriggerOfStr = function
    | "LR" | "LRTrigger" -> LRTrigger
    | "Left" | "LeftTrigger"  -> LeftTrigger
    | "Right" | "RightTrigger" -> RightTrigger
    | "Middle" | "MiddleTrigger" -> MiddleTrigger
    | "X1" | "X1Trigger" -> X1Trigger
    | "X2" | "X2Trigger" -> X2Trigger
    | "LeftDrag" | "LeftDragTrigger" -> LeftDragTrigger
    | "RightDrag" | "RightDragTrigger" -> RightDragTrigger
    | "MiddleDrag" | "MiddleDragTrigger" -> MiddleDragTrigger
    | "X1Drag" | "X1DragTrigger" -> X1DragTrigger
    | "X2Drag" | "X2DragTrigger" -> X2DragTrigger
    | "None" | "NoneTrigger" -> NoneTrigger
    | e -> raise (ArgumentException(e))

let samePoint (me1:MouseEvent) (me2:MouseEvent) =
    (me1.Info.pt.x = me2.Info.pt.x) && (me1.Info.pt.y = me2.Info.pt.y)
