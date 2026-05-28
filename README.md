# ForzaG29Leds

Windows tray app that drives the rev-limiter LEDs on a Logitech G29 or G923 steering wheel using live UDP telemetry from Forza Horizon 6 (compatible with FH5).

## How it works

- Runs in the system tray — right-click for Settings or Exit
- Opens a UDP socket on the configured port and parses the FH6 "Car Dash" 324-byte telemetry packet
- Sends raw HID output reports directly to the wheel (no Logitech SDK dependency)
- Three LED stages:
  - **Progressive fill** from idle RPM upward
  - **All solid** approaching the shift point
  - **All flashing** at the configured flash threshold (shift now)

## Supported wheels

| Wheel | PID |
|---|---|
| Logitech G29 | `0xC24F` |
| Logitech G923 PS/PC | `0xC267` |
| Logitech G923 Xbox | `0xC26D` / `0xC26E` |

## Setup

### 1. Enable Data Out in Forza Horizon 6

Go to **Settings › HUD & Gameplay**, scroll down to the **Telemetry** section and set:

| Setting | Value |
|---|---|
| Data Out | **On** |
| Data Out IP Address | `127.0.0.1` |
| Data Out IP Port | `9999` |

### 2. Run

```
ForzaG29Leds.exe
```

The app starts in the system tray. Double-click the icon or right-click › **Settings** to configure.

The wheel must be plugged in before launching. No G HUB or additional DLLs required.

If no telemetry is arriving (grey status dot), confirm Data Out is enabled in Forza and the port matches.

## Settings

| Setting | Default | Description |
|---|---|---|
| UDP Port | `9999` | Must match the Forza Data Out port |
| Solid from | `77` % | All LEDs on solid above this RPM fraction |
| Flash from | `82` % | All LEDs flash above this RPM fraction |
| Flash interval | `80` ms | On/off interval when flashing (lower = faster) |

Settings are saved to `%AppData%\ForzaG29Leds\settings.json`.

## LED behaviour

| RPM | LEDs |
|---|---|
| < idle | ░░░░░ off |
| idle → solid% | Progressive fill █░░░░ → █████ |
| solid% → flash% | █████ solid |
| ≥ flash% | █████ flashing |

Percentages are of `EngineMaxRpm` as reported by the telemetry, so they adapt automatically to every car.

## Building from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download) and Windows.

```
git clone https://github.com/mathewpalmer17/ForzaG29Leds
cd ForzaG29Leds
dotnet build
dotnet test
```

To produce a self-contained single-file exe:

```
dotnet publish ForzaG29Leds -c Release -o publish
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for more detail.

## Technical notes

- LED control uses raw USB HID output reports — the Logitech Steering Wheel SDK reports `LedCaps=False` for the G29/G923 in G HUB 2024+ and cannot drive the rev LEDs.
- LED command (7 data bytes from Linux `hid-lg4ff.c`, identical on G29 and G923): `[0xF8, 0x12, ledMask, 0x00, 0x00, 0x00, 0x01]`. On Windows, `WriteFile` to a HID device requires a `0x00` Report ID prefix and the buffer padded to `OutputReportByteLength`.
- Device matched by VID `0x046D` (Logitech) + supported PID + HID Usage Page 1 (Generic Desktop).
- Telemetry packet offsets verified against [TheBanHammer/fh6-tel](https://github.com/TheBanHammer/fh6-tel). Tire temperatures are transmitted in Fahrenheit.
