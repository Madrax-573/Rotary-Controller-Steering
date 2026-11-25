
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SDL3;
using HIDMaestro;

namespace RotaryControllerSteering
{
    public partial class MainWindow : Window
    {
        private bool _sdlInitialized;
        private IntPtr _gamepad = IntPtr.Zero;

        // HIDMaestro virtual controller
        private HMContext? _context;
        private HMController? _controller;

        // Steering processor
        private readonly SteeringProcessor _steeringProcessor =
            new SteeringProcessor();

        // Frame timing
        private readonly Stopwatch _frameStopwatch =
            new Stopwatch();

        // Diagnostics
        private const bool EnableDiagnostics = false;

        // Physical stick centre filter
        private const double PhysicalStickCentreDeadzone = 0.15;

        // Left stick readings
        private double _stickX;
        private double _stickY;
        private double _stickMagnitude;
        private double _stickAngle;

        private short _rawX;
        private short _rawY;

        private bool _stickInDeadzone;

        // Right stick readings
        private double _rightStickX;
        private double _rightStickY;
        private double _rightStickMagnitude;

        private short _rawRightX;
        private short _rawRightY;

        // Analogue trigger readings
        private short _rawLeftTrigger;
        private short _rawRightTrigger;

        private double _leftTrigger;
        private double _rightTrigger;

        // Steering values
        private double _requestedSteering;
        private double _actualSteering;


        public MainWindow()
        {
            InitializeComponent();

            InitialiseConfigurationControls();

            // Create the virtual steering wheel
            CreateVirtualWheel();

            // Initialise SDL
            if (!SDL.Init(SDL.InitFlags.Gamepad))
            {
                ControllerStatusText.Text =
                    "SDL error: " + SDL.GetError();

                return;
            }

            _sdlInitialized = true;

            // Find connected gamepads
            uint[] gamepads = SDL.GetGamepads(out int count);

            if (gamepads == null || count == 0)
            {
                ControllerStatusText.Text =
                    "No controller detected";

                return;
            }

            // Open the first detected gamepad
            _gamepad = SDL.OpenGamepad(gamepads[0]);

            if (_gamepad == IntPtr.Zero)
            {
                ControllerStatusText.Text =
                    "Could not open controller: " + SDL.GetError();

                return;
            }

            string name = SDL.GetGamepadName(_gamepad);

            ControllerStatusText.Text =
                "Connected: " + name +
                (_controller != null
                    ? " | Virtual wheel ready"
                    : " | Virtual wheel unavailable");

            // Start frame timing and input updates
            _frameStopwatch.Start();

            CompositionTarget.Rendering += UpdateDisplay;
        }


        // =====================================================
        // DIAGNOSTIC OUTPUT
        // =====================================================

        private static void DiagnosticWriteLine(string message)
        {
            if (EnableDiagnostics)
            {
                Debug.WriteLine(message);
            }
        }


        // =====================================================
        // CONFIGURATION
        // =====================================================

        private void InitialiseConfigurationControls()
        {
            PhysicalRotationSlider.Value =
                _steeringProcessor.PhysicalRotationLimit;

            VirtualRangeSlider.Value =
                _steeringProcessor.VirtualSteeringRange;

            SteeringSpeedSlider.Value =
                _steeringProcessor.SteeringSpeed;

            SteeringDeadzoneSlider.Value =
                _steeringProcessor.SteeringDeadzone;

            LinearitySlider.Value =
                _steeringProcessor.Linearity;

            PhysicalRotationSlider.ValueChanged +=
                PhysicalRotationSlider_ValueChanged;

            VirtualRangeSlider.ValueChanged +=
                VirtualRangeSlider_ValueChanged;

            SteeringSpeedSlider.ValueChanged +=
                SteeringSpeedSlider_ValueChanged;

            SteeringDeadzoneSlider.ValueChanged +=
                SteeringDeadzoneSlider_ValueChanged;

            LinearitySlider.ValueChanged +=
                LinearitySlider_ValueChanged;

            UpdateConfigurationDisplay();
        }


        // =====================================================
        // HIDMAESTRO VIRTUAL WHEEL
        // =====================================================

        private void CreateVirtualWheel()
        {
            try
            {
                DiagnosticWriteLine("Starting HIDMaestro...");

                _context = new HMContext();

                // Inspect HIDMaestro button values and flags
                if (EnableDiagnostics)
                {
                    Type buttonType = typeof(HMGamepadState)
                        .GetField("Buttons")!
                        .FieldType;

                    DiagnosticWriteLine("========== HMButton DETAILS ==========");
                    DiagnosticWriteLine($"Type: {buttonType.FullName}");
                    DiagnosticWriteLine($"Is flags enum: {buttonType.IsDefined(typeof(FlagsAttribute), false)}");

                    foreach (var value in Enum.GetValues(buttonType))
                    {
                        DiagnosticWriteLine($"{value} = {Convert.ToInt64(value)}");
                    }

                    DiagnosticWriteLine("======================================");
                }

                int loadedProfiles = _context.LoadDefaultProfiles();

                DiagnosticWriteLine(
                    $"Loaded HIDMaestro profiles: {loadedProfiles}");

                _context.InstallDriver();

                DiagnosticWriteLine(
                    "HIDMaestro driver check completed.");

                // Load the source profiles
                HMProfile? original =
                    _context.GetProfile("thrustmaster-t300rs");

                HMProfile? xbox =
                    _context.GetProfile("xbox-360-wired");

                if (original == null)
                {
                    throw new InvalidOperationException(
                        "Could not find the T300RS profile.");
                }

                if (xbox == null)
                {
                    throw new InvalidOperationException(
                        "Could not find the Xbox 360 profile.");
                }

                // Compare the source profiles
                DiagnosticWriteLine("========== PROFILE COMPARISON ==========");

                DiagnosticWriteLine("T300RS:");
                DiagnosticWriteLine($"  Type: {original.Type}");
                DiagnosticWriteLine($"  Trigger mode: {original.TriggerMode}");
                DiagnosticWriteLine($"  Sticks: {original.Sticks.Count}");
                DiagnosticWriteLine($"  Triggers: {original.Triggers.Count}");

                DiagnosticWriteLine("Xbox 360:");
                DiagnosticWriteLine($"  Type: {xbox.Type}");
                DiagnosticWriteLine($"  Trigger mode: {xbox.TriggerMode}");
                DiagnosticWriteLine($"  Sticks: {xbox.Sticks.Count}");
                DiagnosticWriteLine($"  Triggers: {xbox.Triggers.Count}");

                DiagnosticWriteLine("========================================");

                // Inspect the original T300RS configuration
                DiagnosticWriteLine("========== ORIGINAL PROFILE ==========");

                InspectObject("T300RS Layout", original.Layout);

                if (original.Layout is HMWheelLayout originalWheel)
                {
                    InspectCollection(
                        "T300RS Pedals",
                        originalWheel.Pedals);
                }

                InspectCollection(
                    "T300RS Triggers",
                    original.Triggers);

                DiagnosticWriteLine($"T300RS Descriptor: {original.DescriptorHex}");
                DiagnosticWriteLine($"T300RS Axis map: {original.AxisMap}");

                DiagnosticWriteLine("======================================");

                // Build the virtual HID descriptor
                var virtualDescriptor = new HidDescriptorBuilder()
                    .Joystick()
                    .AddAxis(HMAxis.X, 16, -32768, 32767)
                    .AddAxis(HMAxis.Y, 16, 0, 65535)
                    .AddAxis(HMAxis.Z, 16, -32768, 32767)
                    .AddAxis(HMAxis.Rx, 16, -32768, 32767)
                    .AddAxis(HMAxis.Ry, 16, -32768, 32767)
                    .AddAxis(HMAxis.Rz, 16, -32768, 32767)
                    .AddAxis(HMAxis.Slider, 16, 0, 65535)
                    .AddButtons(14)
                    .AddHat(8)
                    .Build();

                DiagnosticWriteLine(
                    "GENERATED HID DESCRIPTOR: " +
                    Convert.ToHexString(virtualDescriptor));

                // Set the descriptor report ID
                virtualDescriptor[3] = 0x04;

                // Define our custom wheel and pedal layout
                var wheelLayout = new HMWheelLayout
                {
                    Wheel = new HMWheelSpec
                    {
                        Axis = HMAxis.X,
                        RotationDegrees = 900,
                        ForceFeedback = false
                    },

                    Pedals =
                    [
                        new HMPedal
                        {
                            Axis = HMAxis.Y,
                            Role = HMAxisRole.Clutch,
                            Type = HMPedalType.Potentiometer
                        },

                        new HMPedal
                        {
                            Axis = HMAxis.Slider,
                            Role = HMAxisRole.HandbrakeAxis,
                            Type = HMPedalType.Potentiometer
                        }
                    ]
                };

                InspectCollection(
                    "Custom Pedals",
                    wheelLayout.Pedals);

                // Clone the T300RS profile and customise it
                var builder = new HMProfileBuilder()
                    .FromProfile(original)
                    .Id("rotary-controller-wheel")
                    .Name("Rotary Controller Steering Wheel")
                    .Vid(0x1234)
                    .Pid(0x5678)
                    .ProductString("Rotary Controller Steering Wheel")
                    .ManufacturerString("Rotary Controller")
                    .Descriptor(virtualDescriptor);

                // The builder's internal profile contains the layout.
                // Replace that layout with our custom wheel layout.
                // This reflection is required for normal operation.
                FieldInfo? copyField = typeof(HMProfileBuilder)
                    .GetField(
                        "_copy",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                if (copyField == null)
                {
                    throw new InvalidOperationException(
                        "Could not locate the internal profile.");
                }

                object? internalProfile =
                    copyField.GetValue(builder);

                if (internalProfile == null)
                {
                    throw new InvalidOperationException(
                        "Could not access the internal profile.");
                }

                InspectObject(
                    "Builder Internal Profile",
                    internalProfile);

                PropertyInfo? layoutProperty =
                    internalProfile.GetType().GetProperty(
                        "Layout",
                        BindingFlags.Instance | BindingFlags.Public);

                if (layoutProperty == null ||
                    !layoutProperty.CanWrite)
                {
                    throw new InvalidOperationException(
                        "Could not access the profile Layout property.");
                }

                layoutProperty.SetValue(
                    internalProfile,
                    wheelLayout);

                // Build the final profile
                HMProfile customProfile = builder.Build();

                // Inspect how the profile obtains its trigger definitions
                InspectTriggerGetterImplementation();

                // Inspect the resulting profile
                DiagnosticWriteLine("========== FINAL PROFILE ==========");

                DiagnosticWriteLine($"Type: {customProfile.Type}");
                DiagnosticWriteLine($"Trigger mode: {customProfile.TriggerMode}");
                DiagnosticWriteLine($"Trigger count: {customProfile.Triggers.Count}");

                InspectObject(
                    "Final Custom Layout",
                    customProfile.Layout);

                if (customProfile.Layout is HMWheelLayout finalWheel)
                {
                    InspectCollection(
                        "Final Custom Pedals",
                        finalWheel.Pedals);
                }

                InspectCollection(
                    "Final Custom Triggers",
                    customProfile.Triggers);

                DiagnosticWriteLine($"Descriptor: {customProfile.DescriptorHex}");
                DiagnosticWriteLine($"Axis map: {customProfile.AxisMap}");

                DiagnosticWriteLine("===================================");

                // Create the virtual controller
                _controller = _context.CreateController(customProfile);

                DiagnosticWriteLine(
                    $"HIDMaestro driver version: {_controller.DriverVersion}");

                DiagnosticWriteLine(
                    $"Last driver install: {_context.LastDriverInstall}");

                DiagnosticWriteLine(
                    "Virtual steering wheel created successfully.");

                // Submit an initial state so HIDMaestro builds its report plan.
                SubmitSteeringState(0.0);

                // Inspect the internal report builder and compiled axis writes.
                // This is diagnostic-only and is skipped during normal operation.
                if (EnableDiagnostics)
                {
                    DiagnosticWriteLine("========== AXIS WRITE DIAGNOSTIC ==========");

                    FieldInfo? reportBuilderField = _controller.GetType().GetField(
                        "_reportBuilder",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    if (reportBuilderField == null)
                    {
                        DiagnosticWriteLine("Could not locate _reportBuilder.");
                    }
                    else
                    {
                        object? reportBuilder =
                            reportBuilderField.GetValue(_controller);

                        if (reportBuilder == null)
                        {
                            DiagnosticWriteLine("Report builder is NULL.");
                        }
                        else
                        {
                            FieldInfo? planField = reportBuilder.GetType().GetField(
                                "_plan",
                                BindingFlags.Instance | BindingFlags.NonPublic);

                            if (planField == null)
                            {
                                DiagnosticWriteLine("Could not locate _plan.");
                            }
                            else
                            {
                                object? plan = planField.GetValue(reportBuilder);

                                if (plan == null)
                                {
                                    DiagnosticWriteLine("Compiled report plan is NULL.");
                                }
                                else
                                {
                                    PropertyInfo? writesProperty = plan.GetType().GetProperty(
                                        "Writes",
                                        BindingFlags.Instance | BindingFlags.Public);

                                    if (writesProperty == null)
                                    {
                                        DiagnosticWriteLine("Could not locate Writes.");
                                    }
                                    else
                                    {
                                        object? writes = writesProperty.GetValue(plan);

                                        if (writes is System.Collections.IEnumerable entries)
                                        {
                                            foreach (object? entry in entries)
                                            {
                                                if (entry == null)
                                                    continue;

                                                Type entryType = entry.GetType();

                                                object? GetValue(string propertyName)
                                                {
                                                    return entryType.GetProperty(
                                                        propertyName,
                                                        BindingFlags.Instance | BindingFlags.Public)
                                                        ?.GetValue(entry);
                                                }

                                                object? field = GetValue("Field");

                                                object? bitOffset = field?.GetType()
                                                    .GetProperty("BitOffset")
                                                    ?.GetValue(field);

                                                object? bitSize = field?.GetType()
                                                    .GetProperty("BitSize")
                                                    ?.GetValue(field);

                                                DiagnosticWriteLine(
                                                    $"Axis: {GetValue("Key")}, " +
                                                    $"BitOffset: {bitOffset}, " +
                                                    $"BitSize: {bitSize}, " +
                                                    $"Rest: {GetValue("Rest")}, " +
                                                    $"Min: {GetValue("Min")}, " +
                                                    $"Span: {GetValue("Span")}, " +
                                                    $"IsCentered: {GetValue("IsCentered")}, " +
                                                    $"Center: {GetValue("Center")}, " +
                                                    $"Skip: {GetValue("Skip")}");
                                            }
                                        }
                                        else
                                        {
                                            DiagnosticWriteLine(
                                                "Writes is NULL or not enumerable.");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    DiagnosticWriteLine("============================================");
                }
            }
            catch (Exception ex)
            {
                // Always report genuine errors, even with diagnostics disabled.
                Debug.WriteLine("HIDMaestro error: " + ex);

                _controller?.Dispose();
                _controller = null;

                _context?.Dispose();
                _context = null;
            }
        }


        // =====================================================
        // HIDMAESTRO TRIGGER DIAGNOSTICS
        // =====================================================

        private static void InspectTriggerGetterImplementation()
        {
            if (!EnableDiagnostics)
                return;

            DiagnosticWriteLine("========== TRIGGER GETTER INVESTIGATION ==========");

            PropertyInfo? triggersProperty =
                typeof(HMProfile).GetProperty(
                    "Triggers",
                    BindingFlags.Instance | BindingFlags.Public);

            MethodInfo? triggerGetter = triggersProperty?.GetMethod;

            if (triggerGetter == null)
            {
                DiagnosticWriteLine("Could not find the Triggers getter.");
                return;
            }

            InspectMethodIL("HMProfile.Triggers getter", triggerGetter);

            const int calledMethodToken = 0x060003ED;

            try
            {
                MethodBase? calledMethod =
                    typeof(HMProfile).Module.ResolveMethod(calledMethodToken);

                if (calledMethod == null)
                {
                    DiagnosticWriteLine("Could not resolve GetSimpleTriggers.");
                    return;
                }

                InspectMethodIL("GetSimpleTriggers", calledMethod);

                // Metadata tokens extracted from GetSimpleTriggers IL.
                int[] tokens =
                [
                    0x060003EF,
                    0x06000863,
                    0x06001AB7,
                    0x06001AA5,
                    0x06001AA7,
                    0x060001D7,
                    0x060001D9,
                    0x060001DB,
                    0x0A00021B,
                    0x06000865
                ];

                DiagnosticWriteLine("--- Resolved method references ---");

                foreach (int token in tokens)
                {
                    try
                    {
                        MemberInfo? member =
                            typeof(HMProfile).Module.ResolveMember(token);

                        DiagnosticWriteLine(
                            $"0x{token:X8} -> {member}");
                    }
                    catch (Exception ex)
                    {
                        DiagnosticWriteLine(
                            $"0x{token:X8} -> Resolution failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticWriteLine(
                    "Trigger investigation failed: " + ex);
            }

            DiagnosticWriteLine("===================================================");
        }


        private static void InspectMethodIL(
            string description,
            MethodBase method)
        {
            if (!EnableDiagnostics)
                return;

            DiagnosticWriteLine($"--- {description} ---");

            MethodBody? body = method.GetMethodBody();

            if (body == null)
            {
                DiagnosticWriteLine("No method body available.");
                return;
            }

            byte[]? il = body.GetILAsByteArray();

            DiagnosticWriteLine($"IL length: {il?.Length ?? 0}");

            DiagnosticWriteLine(
                il == null
                    ? "IL unavailable."
                    : Convert.ToHexString(il));

            if (body.LocalVariables.Count > 0)
            {
                DiagnosticWriteLine("Local variables:");

                foreach (LocalVariableInfo local in body.LocalVariables)
                {
                    DiagnosticWriteLine(
                        $"  {local.LocalIndex}: {local.LocalType.FullName}");
                }
            }
        }


        // =====================================================
        // GENERAL DIAGNOSTICS
        // =====================================================

        private static void InspectObject(
            string name,
            object? obj)
        {
            if (!EnableDiagnostics)
                return;

            DiagnosticWriteLine($"--- {name} ---");

            if (obj == null)
            {
                DiagnosticWriteLine("NULL");
                return;
            }

            Type type = obj.GetType();

            DiagnosticWriteLine($"Runtime type: {type.FullName}");

            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public))
            {
                try
                {
                    object? value = property.GetValue(obj);

                    DiagnosticWriteLine(
                        $"{property.Name} ({property.PropertyType.Name}) = " +
                        $"{value ?? "NULL"}");
                }
                catch (Exception ex)
                {
                    DiagnosticWriteLine(
                        $"{property.Name} = <could not read: {ex.Message}>");
                }
            }
        }


        private static void InspectCollection(
            string name,
            System.Collections.IEnumerable? items)
        {
            if (!EnableDiagnostics)
                return;

            DiagnosticWriteLine($"--- {name} ---");

            if (items == null)
            {
                DiagnosticWriteLine("NULL");
                return;
            }

            int index = 0;

            foreach (object? item in items)
            {
                DiagnosticWriteLine($"Item {index}:");

                if (item == null)
                {
                    DiagnosticWriteLine("NULL");
                }
                else
                {
                    InspectObject($"Item {index}", item);
                }

                index++;
            }

            if (index == 0)
            {
                DiagnosticWriteLine("EMPTY");
            }
        }


        // =====================================================
        // VIRTUAL CONTROLLER OUTPUT
        // =====================================================

        private void SubmitSteeringState(double steeringAngle)
        {
            if (_controller == null)
                return;

            if (_gamepad == IntPtr.Zero)
                return;

            double virtualLock =
                _steeringProcessor.VirtualSteeringRange / 2.0;

            if (virtualLock <= 0)
                return;

            // Convert steering angle into a 0.0 to 1.0 axis value
            double normalisedSteering =
                0.5 + (steeringAngle / (virtualLock * 2.0));

            normalisedSteering = Math.Clamp(
                normalisedSteering,
                0.0,
                1.0);

            try
            {
                // -----------------------------------------
                // Read physical buttons
                // -----------------------------------------

                HMButton buttons = HMButton.None;

                // Face buttons
                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.South))
                    buttons |= HMButton.A;

                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.East))
                    buttons |= HMButton.B;

                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.West))
                    buttons |= HMButton.X;

                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.North))
                    buttons |= HMButton.Y;

                // Shoulder buttons
                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.LeftShoulder))
                    buttons |= HMButton.LeftBumper;

                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.RightShoulder))
                    buttons |= HMButton.RightBumper;

                // Stick clicks
                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.LeftStick))
                    buttons |= HMButton.LeftStick;

                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.RightStick))
                    buttons |= HMButton.RightStick;

                // Other buttons
                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.Back))
                    buttons |= HMButton.Back;

                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.Start))
                    buttons |= HMButton.Start;

                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.Guide))
                    buttons |= HMButton.Guide;

                if (SDL.GetGamepadButton(_gamepad, SDL.GamepadButton.Touchpad))
                    buttons |= HMButton.Touchpad;


                // -----------------------------------------
                // D-pad to virtual hat
                // -----------------------------------------

                bool up = SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.DPadUp);

                bool down = SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.DPadDown);

                bool left = SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.DPadLeft);

                bool right = SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.DPadRight);

                HMHat hat = HMHat.None;

                if (up && right)
                    hat = HMHat.NorthEast;
                else if (down && right)
                    hat = HMHat.SouthEast;
                else if (down && left)
                    hat = HMHat.SouthWest;
                else if (up && left)
                    hat = HMHat.NorthWest;
                else if (up)
                    hat = HMHat.North;
                else if (right)
                    hat = HMHat.East;
                else if (down)
                    hat = HMHat.South;
                else if (left)
                    hat = HMHat.West;


                // -----------------------------------------
                // Build virtual controller state
                // -----------------------------------------

                var state = new HMGamepadState
                {
                    Axes = new Dictionary<HMAxis, float>
                    {
                        // Steering
                        [HMAxis.X] = (float)normalisedSteering,

                        // Clutch - unused
                        [HMAxis.Y] = 0.0f,

                        // Right stick X
                        [HMAxis.Z] = (float)((_rightStickX + 1.0) / 2.0),

                        // Accelerator - R2
                        [HMAxis.Rx] = (float)_rightTrigger,

                        // Brake - L2
                        [HMAxis.Ry] = (float)_leftTrigger,

                        // Right stick Y
                        [HMAxis.Rz] = (float)((_rightStickY + 1.0) / 2.0),

                        // Handbrake - unused
                        [HMAxis.Slider] = 0.0f
                    },

                    Buttons = buttons,
                    Hat = hat
                };

                DiagnosticWriteLine(
                    $"Right stick output: Z={_rightStickX:F3}, Rz={_rightStickY:F3}");

                _controller.SubmitState(state);
            }
            catch (Exception ex)
            {
                // Always report genuine errors, even with diagnostics disabled.
                Debug.WriteLine(
                    "Virtual steering output error: " + ex);

                _controller.Dispose();
                _controller = null;
            }
        }


        // =====================================================
        // CONTROLLER INPUT AND DISPLAY
        // =====================================================

        private void UpdateButtonIndicator(
            FrameworkElement indicator,
            bool pressed)
        {
            indicator.Opacity = pressed ? 1.0 : 0.3;
        }


        private void UpdateDisplay(object sender, EventArgs e)
        {
            if (_gamepad == IntPtr.Zero)
                return;

            // Calculate elapsed time since previous frame
            double elapsedSeconds =
                _frameStopwatch.Elapsed.TotalSeconds;

            _frameStopwatch.Restart();

            SDL.PumpEvents();


            // -----------------------------------------
            // Face buttons
            // -----------------------------------------

            UpdateButtonIndicator(
                ButtonSouth,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.South));

            UpdateButtonIndicator(
                ButtonEast,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.East));

            UpdateButtonIndicator(
                ButtonWest,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.West));

            UpdateButtonIndicator(
                ButtonNorth,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.North));


            // -----------------------------------------
            // Shoulder buttons
            // -----------------------------------------

            UpdateButtonIndicator(
                ButtonL1,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.LeftShoulder));

            UpdateButtonIndicator(
                ButtonR1,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.RightShoulder));


            // -----------------------------------------
            // Other buttons
            // -----------------------------------------

            UpdateButtonIndicator(
                ButtonBack,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.Back));

            UpdateButtonIndicator(
                ButtonStart,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.Start));

            UpdateButtonIndicator(
                ButtonGuide,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.Guide));

            UpdateButtonIndicator(
                ButtonL3,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.LeftStick));

            UpdateButtonIndicator(
                ButtonR3,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.RightStick));

            UpdateButtonIndicator(
                ButtonTouchpad,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.Touchpad));


            // -----------------------------------------
            // D-pad
            // -----------------------------------------

            UpdateButtonIndicator(
                DPadUp,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.DPadUp));

            UpdateButtonIndicator(
                DPadDown,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.DPadDown));

            UpdateButtonIndicator(
                DPadLeft,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.DPadLeft));

            UpdateButtonIndicator(
                DPadRight,
                SDL.GetGamepadButton(
                    _gamepad, SDL.GamepadButton.DPadRight));


            // -----------------------------------------
            // Left stick - steering
            // -----------------------------------------

            _rawX = SDL.GetGamepadAxis(
                _gamepad, SDL.GamepadAxis.LeftX);

            _rawY = SDL.GetGamepadAxis(
                _gamepad, SDL.GamepadAxis.LeftY);

            _stickX = _rawX / 32768.0;
            _stickY = _rawY / 32768.0;

            _stickMagnitude = Math.Sqrt(
                _stickX * _stickX +
                _stickY * _stickY);

            // Ignore physical stick movement within the centre filter.
            // This prevents drift and unstable angle readings.
            _stickInDeadzone =
                _stickMagnitude <= PhysicalStickCentreDeadzone;

            if (_stickInDeadzone)
            {
                _requestedSteering = 0.0;
            }
            else
            {
                _stickAngle =
                    Math.Atan2(_stickX, -_stickY)
                    * 180.0 / Math.PI;

                _requestedSteering =
                    _steeringProcessor.CalculateRequestedSteering(
                        _stickAngle);
            }

            // Move actual steering towards requested position
            _actualSteering =
                _steeringProcessor.UpdateSteering(
                    _requestedSteering,
                    elapsedSeconds);


            // -----------------------------------------
            // Right stick - monitoring only
            // -----------------------------------------

            _rawRightX = SDL.GetGamepadAxis(
                _gamepad, SDL.GamepadAxis.RightX);

            _rawRightY = SDL.GetGamepadAxis(
                _gamepad, SDL.GamepadAxis.RightY);

            _rightStickX = _rawRightX / 32768.0;
            _rightStickY = _rawRightY / 32768.0;

            _rightStickMagnitude = Math.Sqrt(
                _rightStickX * _rightStickX +
                _rightStickY * _rightStickY);

            const double rightStickCentre = 140.0;
            const double rightStickRadius = 110.0;

            Canvas.SetLeft(
                RightStickDot,
                rightStickCentre +
                (_rightStickX * rightStickRadius) - 7);

            Canvas.SetTop(
                RightStickDot,
                rightStickCentre +
                (_rightStickY * rightStickRadius) - 7);

            if (_rightStickMagnitude <= 0.05)
            {
                RightStickPositionText.Text = "Centre";
            }
            else
            {
                RightStickPositionText.Text =
                    $"X: {_rightStickX:+0.00;-0.00;0.00}  " +
                    $"Y: {_rightStickY:+0.00;-0.00;0.00}";
            }

            RightStickDisplacementText.Text =
                $"Displacement: {_rightStickMagnitude * 100:F1}%";

            RightRawAxisText.Text =
                $"Raw X: {_rawRightX}    Raw Y: {_rawRightY}";


            // -----------------------------------------
            // Analogue triggers
            // -----------------------------------------

            _rawLeftTrigger = SDL.GetGamepadAxis(
                _gamepad, SDL.GamepadAxis.LeftTrigger);

            _rawRightTrigger = SDL.GetGamepadAxis(
                _gamepad, SDL.GamepadAxis.RightTrigger);

            // SDL3 trigger axes range from 0 to 32767
            _leftTrigger = Math.Clamp(
                _rawLeftTrigger / 32767.0,
                0.0,
                1.0);

            _rightTrigger = Math.Clamp(
                _rawRightTrigger / 32767.0,
                0.0,
                1.0);

            LeftTriggerBar.Value =
                _leftTrigger * 100.0;

            RightTriggerBar.Value =
                _rightTrigger * 100.0;

            LeftTriggerValue.Text =
                $"{_leftTrigger * 100.0:F0}%";

            RightTriggerValue.Text =
                $"{_rightTrigger * 100.0:F0}%";


            // -----------------------------------------
            // Submit analogue inputs to virtual wheel
            // -----------------------------------------

            SubmitSteeringState(_actualSteering);


            // -----------------------------------------
            // Physical left stick display
            // -----------------------------------------

            AngleText.Text = _stickInDeadzone
                ? "Centre"
                : _stickAngle.ToString("F1") + "°";

            const double stickCentre = 140.0;
            const double stickRadius = 110.0;

            Canvas.SetLeft(
                StickDot,
                stickCentre + (_stickX * stickRadius) - 7);

            Canvas.SetTop(
                StickDot,
                stickCentre + (_stickY * stickRadius) - 7);

            StickDisplacementText.Text =
                $"Displacement: {_stickMagnitude * 100:F1}%";

            RawAxisText.Text =
                $"Raw X: {_rawX}    Raw Y: {_rawY}";


            // -----------------------------------------
            // Steering processor display
            // -----------------------------------------

            RequestedSteeringText.Text =
                $"{_requestedSteering:F1}°";

            ActualSteeringText.Text =
                $"{_actualSteering:F1}°";

            SteeringSpeedText.Text =
                $"{_steeringProcessor.SteeringSpeed:F0}° / sec";


            // Calculate indicator position from available width
            double indicatorWidth = SteeringIndicatorCanvas.ActualWidth;

            double virtualLock =
                _steeringProcessor.VirtualSteeringRange / 2.0;

            if (indicatorWidth > 0 && virtualLock > 0)
            {
                double indicatorCentre = indicatorWidth / 2.0;

                // Keep 15 pixels of clearance at either end
                double indicatorTravel =
                    Math.Max(0, (indicatorWidth - 30.0) / 2.0);

                double indicatorPosition =
                    indicatorCentre +
                    (_actualSteering / virtualLock * indicatorTravel);

                // Position the dot within the responsive Grid
                SteeringIndicatorDot.Margin =
                    new Thickness(indicatorPosition - 7, 0, 0, 0);
            }
        }


        // =====================================================
        // CONFIGURATION SLIDER EVENTS
        // =====================================================

        private void PhysicalRotationSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            _steeringProcessor.PhysicalRotationLimit =
                PhysicalRotationSlider.Value;

            UpdateConfigurationDisplay();
        }


        private void VirtualRangeSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            _steeringProcessor.VirtualSteeringRange =
                VirtualRangeSlider.Value;

            UpdateConfigurationDisplay();
        }


        private void SteeringSpeedSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            _steeringProcessor.SteeringSpeed =
                SteeringSpeedSlider.Value;

            UpdateConfigurationDisplay();
        }


        private void SteeringDeadzoneSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            _steeringProcessor.SteeringDeadzone =
                SteeringDeadzoneSlider.Value;

            UpdateConfigurationDisplay();
        }


        private void LinearitySlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            _steeringProcessor.Linearity =
                LinearitySlider.Value;

            UpdateConfigurationDisplay();
        }


        // Refresh configuration values on screen
        private void UpdateConfigurationDisplay()
        {
            PhysicalRotationValue.Text =
                $"{_steeringProcessor.PhysicalRotationLimit:F0}°";

            VirtualRangeValue.Text =
                $"{_steeringProcessor.VirtualSteeringRange:F0}°";

            SteeringSpeedValue.Text =
                $"{_steeringProcessor.SteeringSpeed:F0}°/sec";

            SteeringDeadzoneValue.Text =
                $"{_steeringProcessor.SteeringDeadzone:F1}%";

            LinearityValue.Text =
                $"{_steeringProcessor.Linearity:F2}";

            double virtualLock =
                _steeringProcessor.VirtualSteeringRange / 2.0;

            LeftSteeringLimitText.Text =
                $"-{virtualLock:F0}°";

            RightSteeringLimitText.Text =
                $"+{virtualLock:F0}°";
        }


        // =====================================================
        // APPLICATION CLEAN-UP
        // =====================================================

        protected override void OnClosed(EventArgs e)
        {
            CompositionTarget.Rendering -= UpdateDisplay;

            _frameStopwatch.Stop();

            if (_gamepad != IntPtr.Zero)
            {
                SDL.CloseGamepad(_gamepad);
                _gamepad = IntPtr.Zero;
            }

            if (_sdlInitialized)
            {
                SDL.Quit();
                _sdlInitialized = false;
            }

            _controller?.Dispose();
            _controller = null;

            _context?.Dispose();
            _context = null;

            base.OnClosed(e);
        }
    }
}
