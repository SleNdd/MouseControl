# MouseControl Leonardo firmware

Author: **SleNdd**.

This sketch receives relative cursor commands from MouseControl over the Leonardo's USB
serial port and submits them through Arduino's standard `Mouse` library. The default build uses
the board's normal USB identity. It sends no clicks, wheel input, or keyboard input. Movement occurs only
when a valid MOVE command arrives; it does not continue movement on its own.

## Install with Arduino IDE

Uploading replaces the existing sketch. Preserve an existing firmware image before replacing
it if it is needed. This sketch does not operate a USB Host Shield or forward a physical mouse.

1. Install [Arduino IDE](https://www.arduino.cc/en/software).
2. In Boards Manager, install **Arduino AVR Boards**. Select **Arduino Leonardo** under
   **Tools → Board → Arduino AVR Boards**. Use a Leonardo with ATmega32U4 and a USB data cable.
3. Open `MouseControlLeonardo.ino` from this folder. Keep `Protocol.h` beside the sketch.
4. Select the Leonardo's port under **Tools → Port**. Confirm the selected port belongs to
   your board; disconnect/reconnect the board to identify it if necessary.
5. Click **Verify**. The sketch uses the standard Arduino **Mouse** library. If the IDE reports
   that `Mouse.h` is missing, install **Mouse by Arduino** with Library Manager.
6. Close MouseControl and Serial Monitor so the upload tool can use the port. Click **Upload**.
   Wait for the IDE's upload-complete message. The port can change while the bootloader runs.
7. Reconnect the board after uploading. Open MouseControl's **Settings**, set **Input
   method** to **Arduino Leonardo (USB HID)**, choose the board's current COM port, and click **Save**.
   Wait about two seconds for the Arduino indicator to become online. The app checks the
   firmware identity before enabling this backend. Enable motion and use a fresh activation
   hold outside the app window.

To update firmware, close the app, reopen this sketch, Verify/Upload again, then reconnect the
board and select its current port. Firmware upload is separate from publishing the Windows app.

## Connection behavior

The host uses 115200 baud, 8 data bits, no parity, one stop bit, and DTR. Closing the serial
connection clears the firmware connection state. An identification exchange is required for
each new connection. Missing or disconnected host commands produce no new movement.

The host waits for each acknowledgement before sending another command. MOVE components are
signed −127 to +127; larger app deltas are split into at most two commands. STOP is an ordered
barrier with no movement. A failed checksum, stale sequence, wrong connection nonce, unsupported
command, or invalid component produces no movement. The host closes an uncertain connection
without resending a MOVE whose outcome is unknown.

## Protocol version 1

Frames have 16 bytes. Multibyte fields use little endian.

| Bytes | Field |
| --- | --- |
| 0–1 | `A5 5A` prefix |
| 2 | Version `01` |
| 3 | Command |
| 4–7 | Nonzero connection nonce |
| 8–11 | Sequence number |
| 12–13 | Signed X/Y components, or response data |
| 14–15 | CRC16-CCITT-FALSE of bytes 0–13; initial `FFFF`, polynomial `1021` |

Commands are HELLO `01`, PING `02`, MOVE `03`, and STOP `04`. HELLO uses sequence zero and
zero components. Its response is HELLO_ACK `81`, echoing nonce/sequence with X=`M`, Y=`C`.
The exact HELLO frame for nonce `01020304` and sequence zero is
`A5 5A 01 01 04 03 02 01 00 00 00 00 00 00 6D 44`.

Subsequent command sequences must increase by exactly one, without wrapping. Successful
commands receive ACK `82` with zero response components. Rejected valid frames receive NACK
`83`, with error code in X: 1 identification/state, 2 sequence, 3 component, 4 command, or
5 connection nonce/state. Invalid CRC/version frames are discarded without a reply.

The sketch accepts HELLO only once while a serial connection remains open. A new connection
must reset DTR state and identify again. PING has no movement effect; the app does not send
continuous heartbeats. Acknowledgements indicate command handling, not processing by a target
application.

## Development checks

The host protocol and backend checks use fake transports:

```powershell
dotnet run --project src/MouseControl.App/Services/.checks/ServiceChecks.csproj -c Release
```

The pure `Protocol.h` state machine also has checks under `.checks`. AVR compilation and
physical USB operation require separate verification.

Arduino reference: [Mouse library](https://docs.arduino.cc/language-reference/en/functions/usb/Mouse/).

## Build-time USB fields

`scripts/build-firmware.ps1` compiles the sketch without uploading it. Its optional
`UsbVendorId`, `UsbProductId`, `UsbManufacturer`, and `UsbProductName` parameters override
the sketch's USB descriptors. Without overrides, the Arduino board package defaults apply.
`ArduinoCli`, `ConfigFile`, and `OutputDirectory` select the tool and output locations.
The script rebuilds the core in an isolated `.build` directory to apply descriptor changes
without reusing a previously compiled core.

From the repository root, with Arduino CLI and the dependencies above installed:

```powershell
.\scripts\build-firmware.ps1
```

These fields affect the operating system's reported device description. They do not change
the bootloader descriptors, serial protocol, or the visibility of USB and COM interfaces.
