module KeyboardTests

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

// The keyboard trigger (KEventHandler), as the Rust version tests it (w10wheel.rs, trigger.rs)

open Xunit
open Harness

let private eq (expected: Decision) (actual: Decision) = Assert.Equal(expected, actual)
let private scrolling () = Assert.True(isScrolling ())
let private notScrolling () = Assert.False(isScrolling ())

// The keyboard trigger on VK_LMENU, the mouse trigger on Middle (scrollLocktime 200 ms)
let private kb () =
    reset DataID.Middle DataID.VK_LMENU
    VK_LMENU

[<Fact>]
let ``other keys pass their down, repeats and up`` () =
    kb () |> ignore
    eq Pass (keyDown VK_A 10)
    eq Pass (keyDown VK_A 40)
    eq Pass (keyUp VK_A 60)
    // An up with no down seen passes too
    eq Pass (keyUp VK_A 70)
    // A code out of the table passes, untouched
    eq Pass (keyDown 0x1FF 80)
    eq Pass (keyUp 0x1FF 80)
    notScrolling ()

[<Fact>]
let ``the trigger key's up ends or releases the scroll by the locktime`` () =
    let vk = kb ()

    // Held longer than the locktime: its repeats are suppressed, and its up ends the scroll
    eq Suppress (keyDown vk 100)
    eq Suppress (keyDown vk 130)
    scrolling ()
    eq Suppress (keyUp vk 500)
    notScrolling ()

    // Released within the locktime: the scroll goes on, released
    eq Suppress (keyDown vk 600)
    eq Suppress (keyUp vk 700)
    scrolling ()

    // Another key ends the released scroll: its down, its repeats and its up are suppressed
    eq Suppress (keyDown VK_A 800)
    notScrolling ()
    eq Suppress (keyDown VK_A 830)
    eq Suppress (keyUp VK_A 850)
    // Its next press passes
    eq Pass (keyDown VK_A 900)
    eq Pass (keyUp VK_A 950)

    // The trigger key ends a released scroll of its own: suppressed until its up
    keyDown vk 1000 |> ignore
    keyUp vk 1050 |> ignore
    scrolling ()
    eq Suppress (keyDown vk 1100)
    notScrolling ()
    eq Suppress (keyDown vk 1130)
    notScrolling ()
    eq Suppress (keyUp vk 1500)
    notScrolling ()

[<Fact>]
let ``the trigger key ends a mouse scroll and is suppressed until its up`` () =
    let vk = kb ()
    down Middle 0 0 100 |> ignore
    scrolling ()

    eq Suppress (keyDown vk 200)
    notScrolling ()
    // Its repeats do not start a scroll of their own
    eq Suppress (keyDown vk 230)
    notScrolling ()
    eq Suppress (keyUp vk 300)
    notScrolling ()

    // Its next press is the trigger
    eq Suppress (keyDown vk 400)
    scrolling ()

[<Fact>]
let ``a mouse trigger ends the keyboard scroll, the key's up is suppressed`` () =
    let vk = kb ()
    eq Suppress (keyDown vk 100)
    scrolling ()
    eq Suppress (down Middle 0 0 200)
    notScrolling ()
    eq Suppress (up Middle 0 0 250)
    eq Suppress (keyUp vk 300)
    notScrolling ()

[<Fact>]
let ``a reset leaves the up of a suppressed key to be suppressed alone`` () =
    let vk = kb ()

    // The trigger key in its scroll, reset (--sendInitState): its up is suppressed alone, as its
    // down was, and only that one up
    keyDown vk 100 |> ignore
    Ctx.initState ()
    notScrolling ()
    eq Suppress (keyUp vk 200)
    notScrolling ()
    eq Pass (keyUp vk 250)
    eq Suppress (keyDown vk 300)
    scrolling ()
    keyUp vk 350 |> ignore

    // Pressed again before that up (its up missed): the new down is a press of its own, the
    // trigger, and the lone up is forgotten
    Ctx.initState ()
    keyDown vk 400 |> ignore
    Ctx.initState ()
    eq Suppress (keyDown vk 500)
    scrolling ()
    eq Suppress (keyUp vk 1000)
    notScrolling ()
    eq Pass (keyUp vk 1050)

    // Another key suppressed as it ended a released scroll: its up too
    keyDown vk 1100 |> ignore
    keyUp vk 1150 |> ignore
    eq Suppress (keyDown VK_A 1200)
    Ctx.initState ()
    eq Suppress (keyUp VK_A 1300)

    // A key passed keeps passing: its up passes after the reset
    eq Pass (keyDown VK_A 1400)
    Ctx.initState ()
    eq Pass (keyUp VK_A 1500)

    // The input lost (a lock, a suspend...): nothing is kept, the up passes
    keyDown vk 1600 |> ignore
    Ctx.inputLost ()
    eq Pass (keyUp vk 1700)

[<Fact>]
let ``a repeat passed with no record passes its up too`` () =
    let vk = kb ()

    // Suppressed in its scroll, reset (its up left to be suppressed alone), then down in the
    // system (its up missed, pressed again with the down reaching the application): its repeat
    // and its up pass
    eq Suppress (keyDown vk 100)
    Ctx.initState ()
    setKeyDown vk true
    eq Pass (keyDown vk 500)
    eq Pass (keyUp vk 600)

[<Fact>]
let ``the trigger key passes over an excluded window, its repeats and its up too`` () =
    let vk = kb ()
    exclude ()
    addExcludedPoint 100 100

    setCursor 100 100
    eq Pass (keyDown vk 10)
    // Moved out: its repeats still pass, no scroll starts
    setCursor 20 20
    eq Pass (keyDown vk 40)
    notScrolling ()
    eq Pass (keyUp vk 100)

    // The next press, out of it, is the trigger
    eq Suppress (keyDown vk 200)
    scrolling ()
    eq Suppress (keyUp vk 600)
    notScrolling ()

[<Fact>]
let ``a lost keyboard hook forgets the passed keys`` () =
    let vk = kb ()
    exclude ()
    addExcludedPoint 100 100

    // The trigger key passed over an excluded window, released with no hook: after the
    // recovery, its first press starts the scroll
    setCursor 100 100
    eq Pass (keyDown vk 10)
    Ctx.ForTests.keyboardHookLost ()
    setCursor 20 20
    eq Suppress (keyDown vk 500)
    scrolling ()

    // Passed over an excluded window and still held at the recovery (moved out meanwhile): its
    // repeats and its up pass, no scroll starts
    Ctx.initState ()
    keyUp vk 600 |> ignore
    setCursor 100 100
    eq Pass (keyDown vk 700)
    setCursor 20 20
    setKeyDown vk true
    Ctx.ForTests.keyboardHookLost ()
    eq Pass (keyDown vk 730)
    notScrolling ()
    eq Pass (keyUp vk 800)
    setKeyDown vk false

    // The trigger key in its scroll, released and pressed again with no hook (the new down
    // reached the application), down at the recovery: the scroll is ended, and the repeats and
    // the up of that new press pass (not suppressed as the old one's). The next press is the
    // trigger.
    eq Suppress (keyDown vk 900)
    scrolling ()
    setKeyDown vk true
    Ctx.ForTests.keyboardHookLost ()
    notScrolling ()
    eq Pass (keyDown vk 930)
    notScrolling ()
    eq Pass (keyUp vk 2000)
    setKeyDown vk false
    eq Suppress (keyDown vk 2100)
    scrolling ()
    keyUp vk 2200 |> ignore

    // First pressed with no hook (no record of it), down at the recovery: its repeats and its
    // up pass, no scroll starts
    Ctx.initState ()
    setKeyDown vk true
    Ctx.ForTests.keyboardHookLost ()
    eq Pass (keyDown vk 3000)
    notScrolling ()
    eq Pass (keyUp vk 3100)

[<Fact>]
let ``the lost input forgets the keys`` () =
    let vk = kb ()
    exclude ()
    addExcludedPoint 100 100

    // The trigger key passed over an excluded window, released while the session was locked:
    // after the unlock, its first press starts the scroll
    setCursor 100 100
    eq Pass (keyDown vk 10)
    Ctx.inputLost ()
    setCursor 20 20
    eq Suppress (keyDown vk 500)
    scrolling ()

[<Fact>]
let ``the lost input after a passed press keeps passing it`` () =
    let vk = kb ()
    exclude ()
    addExcludedPoint 100 100

    // After the unlock, the trigger key passed over an excluded window before the reset posted at
    // the unlock was handled (the hook's events come first): its repeats and its up pass, no
    // scroll starts. The next press is the trigger.
    setCursor 100 100
    eq Pass (keyDown vk 10)
    setKeyDown vk true
    Ctx.inputLost ()
    setCursor 20 20
    eq Pass (keyDown vk 500)
    notScrolling ()
    eq Pass (keyDown vk 530)
    notScrolling ()
    eq Pass (keyUp vk 600)
    setKeyDown vk false
    eq Suppress (keyDown vk 700)
    scrolling ()
    keyUp vk 800 |> ignore

    // Another key passed before the reset was handled, then a released scroll: the key's repeat
    // does not end the scroll, and is not suppressed (its up would be too)
    Ctx.initState ()
    eq Pass (keyDown VK_A 900)
    setKeyDown VK_A true
    Ctx.inputLost ()
    keyDown vk 1000 |> ignore
    keyUp vk 1050 |> ignore
    scrolling ()
    eq Pass (keyDown VK_A 1100)
    scrolling ()
    eq Pass (keyUp VK_A 1200)

[<Fact>]
let ``the installed keyboard hook passes the trigger key held`` () =
    let vk = kb ()

    // Pressed while paused (no hook: its down reached the application), held at the end of the
    // pause: its repeats and its up pass, no scroll starts. The next press is the trigger.
    Ctx.ForTests.keyboardHookRemoved ()
    setKeyDown vk true
    Ctx.ForTests.keyboardHookInstalled ()
    eq Pass (keyDown vk 30)
    notScrolling ()
    eq Pass (keyUp vk 100)
    setKeyDown vk false
    eq Suppress (keyDown vk 200)
    scrolling ()
    keyUp vk 300 |> ignore

    // Not held at the end of the pause: its first press is the trigger
    Ctx.initState ()
    Ctx.ForTests.keyboardHookRemoved ()
    Ctx.ForTests.keyboardHookInstalled ()
    eq Suppress (keyDown vk 400)
    scrolling ()

[<Fact>]
let ``the lost keyboard hook ends the key's scroll, its up passes`` () =
    let vk = kb ()

    // Windows had removed the old hook: the key may have been released and pressed again
    // meanwhile, so its records are forgotten and its up passes
    keyDown vk 10 |> ignore
    Ctx.ForTests.keyboardHookLost ()
    notScrolling ()
    eq Pass (keyUp vk 50)
    eq Suppress (keyDown vk 90)
    scrolling ()

[<Fact>]
let ``the lost mouse hook keeps the keys`` () =
    let vk = kb ()
    eq Suppress (keyDown vk 100)
    Ctx.ForTests.mouseHookLost ()
    scrolling ()
    // Its up is still the trigger's: suppressed, it ends the scroll (past the locktime)
    eq Suppress (keyUp vk 500)
    notScrolling ()

[<Fact>]
let ``the trigger key changed while held: its repeats and its up are suppressed`` () =
    let vk = kb ()
    eq Suppress (keyDown vk 100)
    scrolling ()
    Ctx.ForTests.setTargetVKCode DataID.VK_PAUSE
    notScrolling ()
    eq Suppress (keyDown vk 130)
    notScrolling ()
    eq Suppress (keyUp vk 500)
    // A key as the others now
    eq Pass (keyDown vk 600)
    eq Pass (keyUp vk 650)

[<Fact>]
let ``the Pause key sending its down and up at once: a released scroll, ended by the next press`` () =
    reset DataID.Middle DataID.VK_PAUSE
    // A press of VK_PAUSE (its down and up at once, as a PS/2 keyboard sends them) starts a
    // released keyboard scroll; the next press ends it
    eq Suppress (keyDown VK_PAUSE 100)
    eq Suppress (keyUp VK_PAUSE 100)
    scrolling ()
    eq Suppress (keyDown VK_PAUSE 400)
    notScrolling ()
    eq Suppress (keyUp VK_PAUSE 400)
    notScrolling ()

    // Passed over an excluded window: its up too
    exclude ()
    addExcludedPoint 100 100
    setCursor 100 100
    eq Pass (keyDown VK_PAUSE 500)
    notScrolling ()
    eq Pass (keyUp VK_PAUSE 550)
    setCursor 20 20
    eq Suppress (keyDown VK_PAUSE 600)
    scrolling ()

[<Fact>]
let ``the Pause key repeats as the other keys`` () =
    reset DataID.Middle DataID.VK_PAUSE
    exclude ()
    addExcludedPoint 100 100

    // Held (a USB keyboard repeats it): its repeats are suppressed and the scroll goes on (not
    // ended and started again at each repeat, the cursor flickering); its up ends it
    eq Suppress (keyDown VK_PAUSE 100)
    for time in [ 600; 633; 666; 700 ] do
        eq Suppress (keyDown VK_PAUSE time)
        scrolling ()
    eq Suppress (keyUp VK_PAUSE 1000)
    notScrolling ()

    // Passed over an excluded window: its repeats pass after moving out, no scroll starts
    setCursor 100 100
    eq Pass (keyDown VK_PAUSE 2000)
    setCursor 20 20
    eq Pass (keyDown VK_PAUSE 2500)
    notScrolling ()
    eq Pass (keyUp VK_PAUSE 2600)

    // Held when the keyboard hook is installed: its repeats and its up pass
    setKeyDown VK_PAUSE true
    Ctx.ForTests.keyboardHookRemoved ()
    Ctx.ForTests.keyboardHookInstalled ()
    eq Pass (keyDown VK_PAUSE 3000)
    notScrolling ()
    eq Pass (keyUp VK_PAUSE 3100)
    setKeyDown VK_PAUSE false
    eq Suppress (keyDown VK_PAUSE 3200)
    scrolling ()
