module RawInput

open System
open System.Diagnostics
open System.Runtime.InteropServices
open System.Windows.Forms
open System.Threading
open System.ComponentModel

open WinAPI.RawInput
open Dialog

let mutable private sendWheelRaw: int -> int -> unit = (fun x y -> ())

let setSendWheelRaw f =
    sendWheelRaw <- f

let private procRawInput (lParam:nativeint): unit =
    let mutable pcbSize = 0u
    let cbSizeHeader = uint32 <| Marshal.SizeOf(typeof<WinAPI.RAWINPUTHEADER>)

    let getRawInputData data =
        WinAPI.GetRawInputData(lParam, RID_INPUT, data, &pcbSize, cbSizeHeader)

    let isMouseMoveRelative (ri:WinAPI.RAWINPUT) =
        // usFlags can have other bits with a relative move (e.g. MOUSE_MOVE_NOCOALESCE)
        (ri.header.dwType = RIM_TYPEMOUSE) && ((ri.mouse.usFlags &&& MOUSE_MOVE_ABSOLUTE) = 0us)

    // Not smaller than RAWINPUT (a mouse input, the only device registered): it is read whole
    // from the buffer, which would be read past otherwise
    let minSize = uint32 <| Marshal.SizeOf(typeof<WinAPI.RAWINPUT>)

    if (getRawInputData IntPtr.Zero) = 0u && pcbSize >= minSize then
        let buf = Marshal.AllocHGlobal(int pcbSize)
        try
            if (getRawInputData buf) = pcbSize then
                let ri = Marshal.PtrToStructure(buf, typeof<WinAPI.RAWINPUT>) :?> WinAPI.RAWINPUT
                if isMouseMoveRelative ri then
                    let rm = ri.mouse
                    sendWheelRaw rm.lLastX rm.lLastY
        finally
            Marshal.FreeHGlobal(buf)

type MessageWindow() =
    inherit NativeWindow()
    do
        let cp = CreateParams()
        cp.Parent <- HWND_MESSAGE
        base.CreateHandle(cp)

    override self.WndProc(m:Message byref): unit =
        if m.Msg = WM_INPUT then
            procRawInput(m.LParam)
        
        base.WndProc(&m)

let private messageWindow = new MessageWindow()

let private registerMouseRawInputDevice (dwFlags:uint32) (hwnd:nativeint) =
    let rid = [| WinAPI.RAWINPUTDEVICE(HID_USAGE_PAGE_GENERIC, HID_USAGE_GENERIC_MOUSE, dwFlags, hwnd) |]
    Host.registerRawInputDevices rid

// Called by Ctx.Scroll under its lock, maybe on the EventWaiter thread (LR trigger): no dialog,
// it would block the lock that the hook waits for (no mouse input, the dialog could not be
// closed). Returns whether it succeeded: scroll mode does not start if it failed.
let register (): bool =
    let res = registerMouseRawInputDevice RIDEV_INPUTSINK messageWindow.Handle
    if not res then
        Debug.WriteLine("Failed register RawInput: " + (WinError.getLastErrorMessage()))
    res
         
let unregister (): bool =
    let res = registerMouseRawInputDevice RIDEV_REMOVE IntPtr.Zero
    if not res then
        Debug.WriteLine("Failed unregister RawInput: " + (WinError.getLastErrorMessage()))
    res
