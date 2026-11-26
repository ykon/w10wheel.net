module MouseTests

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

// The mouse triggers (EventHandler, EventWaiter, the checks of Windows), as the Rust version tests
// them (w10wheel.rs, trigger.rs). The LR trigger's wait runs on its own thread (EventWaiter): its
// scroll starts, and its timeout resends, some time after the hook returns.

open System
open System.Threading
open Xunit
open Harness

let private eq (expected: Decision) (actual: Decision) = Assert.Equal(expected, actual)
let private notScrolling () = Assert.False(isScrolling ())

// Waits up to 1 s for cond
let private waitUntil (cond: unit -> bool) =
    let deadline = DateTime.UtcNow.AddSeconds(1.0)
    while not (cond ()) && DateTime.UtcNow < deadline do
        Thread.Sleep(5)
    cond ()

// The LR trigger's scroll starts on the waiter thread
let private scrolling () = Assert.True(waitUntil isScrolling)

// The sent inputs (button, down) within 1 s, count of them
let private sentButtons (count: int) =
    takeSent count 1000 |> List.map (fun i -> (buttonOfInput i).Value)

// What sentButtons gives, the inputs themselves
let private sentInputs (count: int) = takeSent count 1000

let private isClick (inputs: WinAPI.MINPUT list) (b: Button) =
    (inputs |> List.choose buttonOfInput) = [ (b, true); (b, false) ]

[<Fact>]
let ``a single or drag trigger pressed again with its up missed ends the scroll`` () =
    // The second down ends the scroll, suppressed with its up; the next press is the trigger again
    for trigger, b in [ (DataID.Middle, Middle); (DataID.RightDrag, Right) ] do
        reset trigger DataID.None
        eq Suppress (down b 0 0 100)
        scrolling ()
        eq Suppress (down b 0 0 300)
        notScrolling ()
        eq Suppress (up b 0 0 400)
        Assert.True(noneSent 50)
        eq Suppress (down b 0 0 500)
        scrolling ()

[<Fact>]
let ``a button of the LR pair pressed again with its up missed ends the scroll`` () =
    reset DataID.LR DataID.None
    eq Suppress (down Left 0 0 100)
    eq Suppress (down Right 0 0 150)
    scrolling ()
    eq Suppress (down Left 0 0 300)
    notScrolling ()
    // The other button's up is still suppressed
    eq Suppress (up Left 0 0 350)
    eq Suppress (up Right 0 0 400)
    Assert.True(noneSent 50)
    // The next press waits for its pair again
    eq Suppress (down Left 0 0 500)
    eq Suppress (down Right 0 0 520)
    scrolling ()

[<Fact>]
let ``the LR pair ends or releases at its second up by the locktime`` () =
    reset DataID.LR DataID.None

    // The first up keeps the scroll; the second, later than the locktime after the start, ends it
    down Left 0 0 100 |> ignore
    down Right 0 0 150 |> ignore
    scrolling ()
    eq Suppress (up Left 0 0 200)
    scrolling ()
    eq Suppress (up Right 0 0 500)
    notScrolling ()

    // Within the locktime: released, the next down ends it
    down Left 0 0 1000 |> ignore
    down Right 0 0 1050 |> ignore
    scrolling ()
    up Right 0 0 1100 |> ignore
    up Left 0 0 1200 |> ignore
    scrolling ()
    eq Suppress (down Middle 0 0 1500)
    notScrolling ()
    eq Suppress (up Middle 0 0 1550)

    // A button other than the pair's up does not count as the pair's: the ups of the pair, later
    // than the locktime, are the first (the scroll goes on) and the second (it ends)
    down Left 0 0 2000 |> ignore
    down Right 0 0 2050 |> ignore
    scrolling ()
    eq Pass (down X1 0 0 2060)
    eq Pass (up X1 0 0 2070)
    up Left 0 0 2400 |> ignore
    scrolling ()
    up Right 0 0 2450 |> ignore
    notScrolling ()

[<Fact>]
let ``the LR wait timed out resends its down, and the up for it`` () =
    reset DataID.LR DataID.None
    eq Suppress (down Left 50 60 100)
    let downs = sentInputs 1
    Assert.Equal<(Button * bool) list>([ (Left, true) ], downs |> List.choose buttonOfInput)

    // The physical up before the resent down reaches the hook: the up is resent (and the physical
    // one suppressed), both pass the hook after
    eq Suppress (up Left 50 60 310)
    let ups = sentInputs 1
    Assert.Equal<(Button * bool) list>([ (Left, false) ], ups |> List.choose buttonOfInput)
    eq Pass (receive downs.Head)
    eq Pass (receive ups.Head)
    notScrolling ()

[<Fact>]
let ``a waiting down resent by a move passes, and the up for it`` () =
    reset DataID.LR DataID.None
    down Left 10 10 100 |> ignore
    eq Suppress (move 12 10 120)
    let downs = sentInputs 1
    Assert.Equal<(Button * bool) list>([ (Left, true) ], downs |> List.choose buttonOfInput)
    eq Pass (receive downs.Head)
    // Its up is resent for it
    eq Suppress (up Left 12 10 200)
    let ups = sentInputs 1
    Assert.Equal<(Button * bool) list>([ (Left, false) ], ups |> List.choose buttonOfInput)
    eq Pass (receive ups.Head)
    notScrolling ()

[<Fact>]
let ``a move at the point of the waiting down does not end the wait`` () =
    reset DataID.LR DataID.None
    down Left 10 10 100 |> ignore
    eq Pass (move 10 10 110)
    eq Suppress (down Right 10 10 150)
    scrolling ()

[<Fact>]
let ``the waiting down released at its point: a click resent`` () =
    reset DataID.LR DataID.None
    down Left 10 10 100 |> ignore
    eq Suppress (up Left 10 10 150)
    Assert.True(isClick (sentInputs 2) Left)
    notScrolling ()

[<Fact>]
let ``a reset ends the LR wait, and the lost input forgets its press`` () =
    // initState: the wait is ended (its down not resent), and the up of that press, whose down
    // reached no application, is suppressed alone
    reset DataID.LR DataID.None
    down Left 10 10 100 |> ignore
    Ctx.initState ()
    Assert.True(noneSent 400)
    eq Suppress (up Left 10 10 200)

    // inputLost: the press is forgotten too (its up may have been missed, and the up of a press
    // made meanwhile reached the application with its down): the up passes
    reset DataID.LR DataID.None
    down Left 10 10 100 |> ignore
    Ctx.inputLost ()
    Assert.True(noneSent 400)
    eq Pass (up Left 10 10 200)

[<Fact>]
let ``the keyboard hook's changes keep the mouse triggers`` () =
    reset DataID.LR DataID.VK_LMENU

    // The wait of the LR trigger goes on: the other button makes the pair
    down Left 10 10 100 |> ignore
    Ctx.ForTests.keyboardHookLost ()
    Ctx.ForTests.keyboardHookRemoved ()
    eq Suppress (down Right 10 10 150)
    scrolling ()

    // The mouse scroll goes on
    Ctx.ForTests.keyboardHookLost ()
    Ctx.ForTests.keyboardHookRemoved ()
    Assert.True(isScrolling ())
    Assert.True(noneSent 50)

[<Fact>]
let ``the lost mouse hook ends the mouse scroll, the trigger's up passes`` () =
    reset DataID.RightDrag DataID.None
    down Right 10 10 100 |> ignore
    scrolling ()
    Ctx.ForTests.mouseHookLost ()
    notScrolling ()
    eq Pass (up Right 10 10 800)
    Assert.True(noneSent 50)

[<Fact>]
let ``the lost input ends a drag scroll, its up passes`` () =
    // A drag scroll whose trigger was released and pressed again while locked: ended, and the up
    // after the unlock passes
    reset DataID.RightDrag DataID.None
    down Right 10 10 100 |> ignore
    scrolling ()
    Ctx.inputLost ()
    notScrolling ()
    eq Pass (up Right 10 10 800)

[<Fact>]
let ``a physical up ends what the triggers kept of its press`` () =
    // LR: a released scroll ended by Right's down (its up the trigger's), Left waits, then Right's
    // up comes: dropped as a lone up, the wait goes on, and nothing of Right's press is kept after
    // its up
    reset DataID.LR DataID.None
    down Left 0 0 100 |> ignore
    down Right 0 0 150 |> ignore
    scrolling ()
    up Left 0 0 200 |> ignore
    up Right 0 0 250 |> ignore
    scrolling ()
    eq Suppress (down Right 0 0 1000)
    notScrolling ()
    down Left 0 0 1050 |> ignore
    eq Suppress (up Right 0 0 1100)
    Assert.True((takeSent 0 0).IsEmpty)
    Assert.False(Ctx.LastFlags.IsSuppressedDown(Mouse.RightUp(mouseInfo 0 0 0u 0u 0u 0UL)))
    // The wait goes on: Right again makes the pair
    eq Suppress (down Right 0 0 1150)
    scrolling ()

[<Fact>]
let ``a new press forgets a resent down whose up was missed`` () =
    // LR: the wait of Left timed out (resent), its up missed, another button between; Left pressed
    // again over an excluded window (passed), then Right waits: Left's up passes as that of the
    // passed press, not resent as the up of the old resent down
    reset DataID.LR DataID.None
    exclude ()
    addExcludedPoint 100 100
    down Left 10 10 100 |> ignore
    let downs = sentInputs 1
    eq Pass (receive downs.Head)
    eq Pass (down Middle 10 10 400)
    eq Pass (up Middle 10 10 450)
    eq Pass (down Left 100 100 500)
    eq Suppress (down Right 20 20 600)
    eq Pass (up Left 20 20 650)
    Assert.True((takeSent 0 0).IsEmpty)

[<Fact>]
let ``the drag trigger: a click is resent unless it was dragged`` () =
    // Released at the point of its down: the scroll ends and the click is resent
    reset DataID.RightDrag DataID.None
    Ctx.ForTests.setDraggedLock true
    eq Suppress (down Right 10 10 100)
    scrolling ()
    eq Suppress (up Right 10 10 150)
    notScrolling ()
    Assert.True(isClick (sentInputs 2) Right)

    // Moved: dragged, released (draggedLock), no click
    eq Suppress (down Right 10 10 200)
    eq Suppress (move 11 10 210)
    eq Suppress (up Right 11 10 250)
    scrolling ()
    Assert.True(noneSent 50)

    // Released, the moves are suppressed and the next down ends it, with no click
    eq Suppress (move 40 40 300)
    eq Suppress (down Right 40 40 400)
    notScrolling ()
    eq Suppress (up Right 40 40 450)
    Assert.True(noneSent 50)

[<Fact>]
let ``the drag trigger takes a scroll or a real move as dragged`` () =
    // Scrolled with no move seen by the hook: dragged, released (draggedLock)
    reset DataID.RightDrag DataID.None
    Ctx.ForTests.setDraggedLock true
    down Right 10 10 100 |> ignore
    scrolling ()
    Ctx.setScrolled ()
    eq Suppress (up Right 10 10 200)
    scrolling ()
    Assert.True(noneSent 50)

    // Dragged with draggedLock off: its up ends the scroll, with no click
    reset DataID.RightDrag DataID.None
    down Right 10 10 100 |> ignore
    eq Suppress (move 12 10 120)
    eq Suppress (up Right 12 10 200)
    notScrolling ()
    Assert.True(noneSent 50)

    // A move to the same point and an injected move are not a drag: the up ends the scroll and the
    // click is resent
    reset DataID.RightDrag DataID.None
    Ctx.ForTests.setDraggedLock true
    down Right 10 10 1000 |> ignore
    eq Suppress (move 10 10 1010)
    eq Suppress (moveInjected 30 30 1020)
    eq Suppress (up Right 10 10 1100)
    notScrolling ()
    Assert.True(isClick (sentInputs 2) Right)

[<Fact>]
let ``the Left trigger passes the right button alone`` () =
    reset DataID.Left DataID.None
    eq Pass (down Right 10 10 100)
    eq Pass (up Right 10 10 110)
    notScrolling ()
    eq Suppress (down Left 10 10 200)
    eq Suppress (down Right 10 10 220)
    scrolling ()


[<Fact>]
let ``the trigger changed while the LR pair holds its scroll: the pair's ups are suppressed`` () =
    reset DataID.LR DataID.None
    down Left 0 0 100 |> ignore
    down Right 0 0 150 |> ignore
    scrolling ()
    Ctx.ForTests.setTrigger DataID.Middle
    notScrolling ()
    eq Suppress (up Left 0 0 200)
    eq Suppress (up Right 0 0 250)
    Assert.True(noneSent 50)
    // The buttons as the new trigger takes them
    eq Pass (down Left 0 0 300)
    eq Pass (up Left 0 0 350)

[<Fact>]
let ``the trigger changed after the LR wait resent its down: its up is resent`` () =
    reset DataID.LR DataID.None
    down Left 10 10 100 |> ignore
    let downs = sentInputs 1
    eq Pass (receive downs.Head)
    Ctx.ForTests.setTrigger DataID.Middle
    eq Suppress (up Left 10 10 400)
    let ups = sentInputs 1
    Assert.Equal<(Button * bool) list>([ (Left, false) ], ups |> List.choose buttonOfInput)
    eq Pass (receive ups.Head)

[<Fact>]
let ``the trigger changed while the LR wait is pending: the wait is canceled, the up suppressed`` () =
    reset DataID.LR DataID.None
    down Left 10 10 100 |> ignore
    Ctx.ForTests.setTrigger DataID.Middle
    Assert.True(noneSent 400)
    eq Suppress (up Left 10 10 500)

[<Fact>]
let ``a down resent for a press forgotten is dropped, one of an unknown kind passes`` () =
    reset DataID.LR DataID.None
    down Left 10 10 100 |> ignore
    let d = (sentInputs 1).Head
    // The press forgotten (e.g. the session locked) before the resent down reaches the hook: the
    // down would leave the button pressed
    Ctx.inputLost ()
    eq Suppress (receive d)

    // The kind bits of none (3): not W10Wheel's, passed as the input of other software
    let extra = d.mi.dwExtraInfo.ToUInt64() ||| (3UL <<< 56)
    let mi = WinAPI.MOUSEINPUT(d.mi.dx, d.mi.dy, d.mi.mouseData, d.mi.dwFlags, d.mi.time, UIntPtr(extra))
    eq Pass (receive (WinAPI.MINPUT(mi)))

[<Fact>]
let ``Pass Mode turned on ends the LR wait, the key's up is suppressed alone`` () =
    reset DataID.LR DataID.VK_LMENU
    down Left 10 10 100 |> ignore
    eq Suppress (keyDown VK_LMENU 110)
    Ctx.ForTests.passModeOn ()
    // The wait's down is not resent
    Assert.True(noneSent 400)
    // The up of the key suppressed in its scroll is suppressed alone, as its down was
    eq Suppress (keyUp VK_LMENU 600)
    eq Pass (keyUp VK_LMENU 650)
    Ctx.exitScrollMode ()
