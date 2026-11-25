# Rotary Controller Steering

**Gamepad-to-wheel steering emulation for driving simulators.**

Rotary Controller Steering is a Windows application that transforms analogue gamepad stick input into virtual steering wheel input, allowing you to use a gamepad as a more natural and predictable steering interface in driving simulators.

The project started as an experiment to overcome the limitations of conventional gamepad steering in driving games. The aim is simple: provide consistent, configurable steering input that lets the game handle the car, rather than the gamepad handling the steering.

## Current status

**Early Beta – Public Testing**

The application is functional and has been tested across multiple driving simulators and physical controllers. I'm now looking for additional testers to help identify compatibility issues, improve usability and gather feedback.

This is an experimental project and is still under development.

## Features

- Converts analogue gamepad stick movement into virtual steering wheel input.
- Emulates a Thrustmaster T300RS virtual steering device.
- Adjustable physical steering rotation.
- Adjustable virtual steering range.
- Configurable steering speed.
- Adjustable steering deadzone.
- Configurable steering linearity.
- Separate physical stick centre deadzone.
- Gamepad button and trigger mapping.
- D-pad and right-stick input support.
- Compatible with multiple physical gamepad types through the SDL3 input system.

## Tested Controllers

The following controllers have been tested successfully:

- Sony DualSense (USB)
- 8BitDo Ultimate 2C

Additional controller compatibility testing is welcome.

## Tested Games

The following titles have been tested successfully:

- Assetto Corsa
- Assetto Corsa Competizione
- Assetto Corsa Rally
- Automobilista 2
- BeamNG.drive
- Euro Truck Simulator 2
- Forza Horizon 6
- Project Motor Racing
- RaceRoom

This is not an exhaustive compatibility list. Feedback from other driving simulators is welcome.

## Requirements

- Windows 10/11 (64-bit)
- A compatible physical gamepad
- HIDMaestro
- HidHide
- Administrator privileges when running the application

The application uses HIDMaestro to create the virtual steering wheel and HidHide to manage physical controller visibility to games.

## Installation and Setup

1. Download the latest release ZIP.
2. Extract the application folder to a suitable location.
3. Install and configure HIDMaestro and HidHide.
4. Add `RotaryControllerSteering.exe` to the HidHide application whitelist.
5. Configure HidHide to hide the physical controller from games where required.
6. Run Rotary Controller Steering as Administrator.
7. Select your preferred steering settings and launch your driving simulator.

**Important:** The application executable must be whitelisted in HidHide so it can continue accessing the physical controller when that device is hidden from games.

## Feedback and Contributions

I'm particularly interested in feedback relating to:

- Compatibility with other controllers.
- Compatibility with additional driving simulators.
- Steering response, consistency and feel.
- Configuration and usability.
- Bugs, unexpected behaviour and suggestions for improvement.

If you try the application, please include your controller model, game title and any relevant configuration details when reporting issues.

Feedback and bug reports can be submitted through the repository's Issues section.

## Development

The application is developed in C# using WPF and .NET 10, with SDL3 for controller input.

The project is currently in active development, and features and compatibility may change as testing progresses.

---

**Rotary Controller Steering – Consistent, predictable steering input. Let the game handle the car, not the gamepad.**
