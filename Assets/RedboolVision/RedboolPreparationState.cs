using System;

/// <summary>Only accepted, fresh STATE packets may prepare a battle. No Unity clock dependency.</summary>
public sealed class RedboolPreparationState
{
    public const double TimeoutSeconds = .5;
    public const double ReadyHoldSeconds = 2;
    public bool Running { get; private set; }
    public bool Rehearsal { get; private set; }
    public bool Calibrated { get; private set; }
    public bool Connected { get; private set; }
    public double Countdown(double now) => readySince < 0 ? ReadyHoldSeconds : Math.Max(0, ReadyHoldSeconds - (now - readySince));
    public bool CountingDown => readySince >= 0 && !Running;
    private double lastState = -1, lastValid = -1, readySince = -1;

    public void Reset()
    {
        Running = Rehearsal = Calibrated = Connected = false;
        lastState = lastValid = readySince = -1;
    }

    public void Observe(double now, bool calibrated, string tracking, string phase, bool block, string movement)
    {
        Tick(now); // A gap must never be bridged by a single newly arrived packet.
        lastState = now;
        Connected = true;
        Calibrated = calibrated;
        if (!calibrated || tracking == "STOPPED")
        {
            Running = Rehearsal = false;
            readySince = lastValid = -1;
            return;
        }
        bool synthetic = tracking == "SYNTHETIC";
        bool valid = tracking == "VALID" || synthetic;
        if (!valid) { readySince = -1; return; }
        lastValid = now;
        bool neutral = phase == "READY" && !block && movement == "NEUTRAL";
        if (synthetic)
        {
            Running = false;
            readySince = -1;
            // A probe may exercise the player, while AI and the round clock stay held.
            if (neutral) Rehearsal = true;
            return;
        }
        if (Rehearsal) { Rehearsal = false; readySince = -1; }
        if (Running) return; // Normal attacks/guard/movement do not restart preparation.
        if (!neutral) { readySince = -1; return; }
        if (readySince < 0) readySince = now;
        if (now - readySince >= ReadyHoldSeconds) Running = true;
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
            readySince = -1;
        }
    }
}
