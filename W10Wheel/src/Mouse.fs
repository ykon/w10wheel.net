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
        | LeftDown(_), LeftDown(_) -> true
        | LeftUp(_), LeftUp(_) -> true
        | RightDown(_), RightDown(_) -> true
        | RightUp(_), RightUp(_) -> true
        | MiddleDown(_), MiddleDown(_) -> true
        | MiddleUp(_), MiddleUp(_) -> true
        | X1Down(_), X1Down(_) -> true
        | X1Up(_), X1Up(_) -> true
        | X2Down(_), X2Down(_) -> true
        | X2Up(_), X2Up(_) -> true
        | _ -> false

    member self.SameButton me2 =
        match self, me2 with
        | LeftDown(_), LeftUp(_) -> true
        | LeftUp(_), LeftDown(_) -> true
        | RightDown(_), RightUp(_) -> true
        | RightUp(_), RightDown(_) -> true
        | MiddleDown(_), MiddleUp(_) -> true
        | MiddleUp(_), MiddleDown(_) -> true
        | X1Down(_), X1Up(_) -> true
        | X1Up(_), X1Down(_) -> true
        | X2Down(_), X2Up(_) -> true
        | X2Up(_), X2Down(_) -> true
        | _ -> false

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

    member self.Name = getUnionCaseName(self)

    member self.IsSingle =
        match self with
        | MiddleTrigger | X1Trigger | X2Trigger -> true
        | _ -> false
        
    member self.IsDrag =
        match self with
        | LeftDragTrigger | RightDragTrigger | MiddleDragTrigger | X1DragTrigger | X2DragTrigger -> true
        | _ -> false

let isXButton1 (mouseData: uint32) =
    (mouseData >>> 16) = (uint32 WinAPI.XBUTTON1)

let isXButton2 (mouseData: uint32) =
    not (isXButton1 mouseData)

let getTrigger = function
    | LeftDown(_) | LeftUp(_) -> LeftTrigger
    | RightDown(_) | RightUp(_) -> RightTrigger
    | MiddleDown(_) | MiddleUp(_) -> MiddleTrigger
    | X1Down(_) | X1Up(_) -> X1Trigger
    | X2Down(_) | X2Up(_) -> X2Trigger
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
    | e -> raise (ArgumentException(e))
