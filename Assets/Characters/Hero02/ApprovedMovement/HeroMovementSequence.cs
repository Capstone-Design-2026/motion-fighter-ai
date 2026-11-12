using UnityEngine;

/// <summary>Gesture-level movement; UDP packet frequency never counts as repeated gestures.</summary>
public sealed class HeroMovementSequence
{
    public enum Phase { Idle, ForwardDash, ForwardContinue, ForwardStep, BackwardDash }
    public const float DashSeconds = .6f, StepSeconds = 20f / 30f;
    public const float HoldForContinuation = .22f;
    public Phase Current { get; private set; }
    public float Age { get; private set; }
    public int Direction { get; private set; }
    public int Revision { get; private set; }
    public float Duration => Current == Phase.ForwardStep ? StepSeconds : DashSeconds;
    public string StateName => Current == Phase.Idle ? "IDLE" : Current == Phase.ForwardDash ? "HERO_DASH_FORWARD"
        : Current == Phase.ForwardContinue ? "HERO_FORWARD_CONTINUE" : Current == Phase.ForwardStep ? "HERO_FORWARD_STEP" : "HERO_DASH_BACKWARD";
    int input, pendingDirection, pendingCount;
    float held, forwardGrace;
    int lastForwardDirection;
    bool repeat;

    public void SetInput(float axis)
    {
        float deadZone = input == 0 ? .12f : .06f;
        int next = Mathf.Abs(axis) < deadZone ? 0 : (axis > 0 ? 1 : -1);
        if (next != 0 && next != input) {
            if (pendingDirection == next) pendingCount++;
            else { pendingDirection = next; pendingCount = 1; }
        }
        if (next != input) held = 0;
        input = next;
    }
    public void Cancel()
    {
        if (Current != Phase.Idle) Revision++;
        Current = Phase.Idle; Age = held = forwardGrace = 0; input = pendingDirection = pendingCount = Direction = lastForwardDirection = 0; repeat = false;
    }
    void Begin(int direction, int facing)
    {
        bool resumeForward = forwardGrace > 0 && direction == lastForwardDirection && direction * facing > 0;
        Direction = direction; Age = held = forwardGrace = 0; repeat = pendingDirection == direction && pendingCount >= 2;
        Current = resumeForward ? Phase.ForwardStep : direction * facing > 0 ? Phase.ForwardDash : Phase.BackwardDash;
        pendingDirection = pendingCount = 0; Revision++;
    }
    public float Advance(float dt, int facing, bool allowed = true)
    {
        if (!allowed) { Cancel(); return 0; }
        if (dt <= 0) return 0;
        forwardGrace = Mathf.Max(0, forwardGrace-dt);
        if (Current == Phase.Idle) {
            int requested = input != 0 ? input : pendingDirection;
            if (requested == 0) return 0;
            Begin(requested, facing);
        } else if (input != 0 && input != Direction) Begin(input, facing);
        else if (pendingCount > 0) {
            if (pendingDirection == Direction) repeat = true;
            pendingDirection = pendingCount = 0;
        }
        float travel = 0;
        while (dt > .000001f && Current != Phase.Idle) {
            if (Current == Phase.ForwardDash && Mathf.Abs(Age-.3f) < .000001f && repeat) {
                Current = Phase.ForwardContinue; Revision++;
            }
            float boundary = Current == Phase.ForwardDash && Age < .3f - .000001f ? .3f : Duration;
            float slice = Mathf.Min(dt, boundary - Age);
            if (input == Direction) { held += slice; if (held >= HoldForContinuation && Current == Phase.ForwardDash) repeat = true; }
            float before = Distance(Current, Age / Duration);
            Age += slice; dt -= slice;
            travel += Direction * (Distance(Current, Age / Duration) - before);
            if (Age < Duration - .000001f) continue;
            if (Current == Phase.ForwardContinue || (Current == Phase.ForwardDash && repeat)) {
                Current = Phase.ForwardStep; Age = 0; repeat = false; Revision++;
            } else if (Current == Phase.ForwardStep && input == Direction) Age = 0;
            else if (Current == Phase.BackwardDash && (input == Direction || repeat)) Begin(Direction, facing);
            else {
                if (Current == Phase.ForwardDash) { forwardGrace=.35f;lastForwardDirection=Direction; }
                Current = Phase.Idle; Age = 0; repeat = false; Revision++;
            }
        }
        return travel;
    }
    static float Smooth(float t) { t = Mathf.Clamp01(t); return t*t*(3-2*t); }
    static float Dash(float t)
    {
        if (t <= .12f) return 0;
        if (t < .28f) return .32f * .24f * Smooth((t-.12f)/.16f);
        if (t < .47f) return .32f * (.24f + .6f*Smooth((t-.28f)/.19f));
        if (t < .63f) return .32f * (.84f + .16f*Smooth((t-.47f)/.16f));
        return .32f;
    }
    public static float Distance(Phase phase, float t)
    {
        t = Mathf.Clamp01(t);
        if (phase == Phase.Idle) return 0;
        if (phase == Phase.ForwardStep) return .14f*t;
        if (phase != Phase.ForwardContinue || t <= .5f) return Dash(t);
        float u = (t-.5f)*2, u2 = u*u, u3 = u2*u;
        float q = (.5f-.47f)/.16f;
        float velocity = .32f*.16f*6*q*(1-q)/(.16f*DashSeconds);
        return (2*u3-3*u2+1)*Dash(.5f)+(u3-2*u2+u)*velocity*.3f
            +(-2*u3+3*u2)*.4f+(u3-u2)*(.14f/.8f)*.3f;
    }
}
