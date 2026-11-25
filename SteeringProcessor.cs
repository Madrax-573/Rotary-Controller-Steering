
using System;

namespace RotaryControllerSteering
{
    public class SteeringProcessor
    {
        // Steering configuration
        public double PhysicalRotationLimit { get; set; } = 170.0;

        public double VirtualSteeringRange { get; set; } = 900.0;

        public double SteeringSpeed { get; set; } = 900.0;

        public double SteeringDeadzone { get; set; } = 2.5;

        public double Linearity { get; set; } = 1.15;


        // Current actual steering position
        public double ActualSteeringPosition { get; private set; } = 0.0;


        // Convert physical stick rotation into requested steering position
        public double CalculateRequestedSteering(double physicalAngle)
        {
            // Limit the physical angle to the configured rotation range
            double limitedAngle = Math.Clamp(
                physicalAngle,
                -PhysicalRotationLimit,
                PhysicalRotationLimit);

            // Normalise physical angle to a range of -1 to +1
            double normalisedAngle =
                limitedAngle / PhysicalRotationLimit;

            // Apply the adjustable steering deadzone
            double deadzone = Math.Clamp(
                SteeringDeadzone / 100.0,
                0.0,
                0.99);

            double angleMagnitude = Math.Abs(normalisedAngle);

            if (angleMagnitude <= deadzone)
            {
                normalisedAngle = 0.0;
            }
            else if (deadzone > 0.0)
            {
                // Rescale the remaining input to preserve full steering lock
                normalisedAngle =
                    Math.Sign(normalisedAngle) *
                    ((angleMagnitude - deadzone) / (1.0 - deadzone));
            }

            // Apply the configurable response curve
            double curvedAngle =
                Math.Sign(normalisedAngle) *
                Math.Pow(Math.Abs(normalisedAngle), Linearity);

            // Convert physical rotation into virtual steering position
            double virtualLock = VirtualSteeringRange / 2.0;

            return curvedAngle * virtualLock;
        }


        // Move actual steering towards the requested position at a controlled rate
        public double UpdateSteering(double requestedSteering, double elapsedSeconds)
        {
            // Calculate the maximum movement allowed during this update
            double maximumMovement = SteeringSpeed * elapsedSeconds;

            // Calculate the difference between requested and actual steering
            double difference = requestedSteering - ActualSteeringPosition;

            // Limit the movement to the permitted steering speed
            if (difference > maximumMovement)
            {
                difference = maximumMovement;
            }
            else if (difference < -maximumMovement)
            {
                difference = -maximumMovement;
            }

            // Apply the permitted movement
            ActualSteeringPosition += difference;

            return ActualSteeringPosition;
        }


        // Reset steering position to centre
        public void Reset()
        {
            ActualSteeringPosition = 0.0;
        }
    }
}
