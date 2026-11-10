using System;

/// <summary>Only accepted, fresh STATE packets may prepare a battle. No Unity clock dependency.</summary>
public sealed class RedboolPreparationState
{
    public const double TimeoutSeconds = .5;
    public bool Running { get; private set; }
    public bool Rehearsal { get; private set; }
    public bool Calibrated { get; private set; }
    public bool Connected { get; private set; }
    private double lastState = -1, lastValid = -1;

    public void Reset()
    {
        Running = Rehearsal = Calibrated = Connected = false;
        lastState = lastValid = -1;
    }

    public void Observe(double now, bool calibrated, string tracking, string phase, bool block, string movement)
    {
        Tick(now); // Discard expired state before accepting this fresh observation.
        lastState = now;
        Connected = true;
        Calibrated = calibrated;
        if (!calibrated || tracking == "STOPPED")
        {
            Running = Rehearsal = false;
            lastValid = -1;
            return;
        }
        bool synthetic = tracking == "SYNTHETIC";
        bool valid = tracking == "VALID" || synthetic;
        if (!valid) return;
        lastValid = now;
        // Calibration belongs to the Python stream, not the Unity scene. A new
        // scene or reconnect needs only a fresh, valid calibrated STATE; no pose
        // hold or C key is required again. SYNC alone cannot release the gate.
        Rehearsal = synthetic;
        Running = !synthetic;
    }

    public void Tick(double now)
    {
        if (lastState < 0 || now - lastState >= TimeoutSeconds)
        {
            Reset();
            return;
        }
        if (lastValid < 0 || now - lastValid >= TimeoutSeconds)
        {
            Running = Rehearsal = false;
        }
    }
}
