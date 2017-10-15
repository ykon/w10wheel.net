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
        (ri.header.dwType = RIM_TYPEMOUSE) && (ri.mouse.usFlags = MOUSE_MOVE_RELATIVE)

    if (getRawInputData IntPtr.Zero) = 0u then
        let buf = Marshal.AllocHGlobal(int pcbSize)
        if (getRawInputData buf) = pcbSize then
            let ri = Marshal.PtrToStructure(buf, typeof<WinAPI.RAWINPUT>) :?> WinAPI.RAWINPUT
            if isMouseMoveRelative ri then
                let rm = ri.mouse
                sendWheelRaw rm.lLastX rm.lLastY

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
    let ridSize = uint32 <| Marshal.SizeOf(typeof<WinAPI.RAWINPUTDEVICE>)
    WinAPI.RegisterRawInputDevices(rid, 1u, ridSize)

let register () =
    if not (registerMouseRawInputDevice RIDEV_INPUTSINK messageWindow.Handle) then
        Dialog.errorMessage ("Failed register RawInput: " + (WinError.getLastErrorMessage())) "Error"
         
let unregister () =
    if not (registerMouseRawInputDevice RIDEV_REMOVE IntPtr.Zero) then
        Dialog.errorMessage ("Failed unregister RawInput: " + (WinError.getLastErrorMessage())) "Error"
