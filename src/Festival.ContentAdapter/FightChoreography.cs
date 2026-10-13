namespace Festival.ContentAdapter;

public enum FightPose { Enter, Guard, SwingLeft, SwingRight, Flinch, StandDown }
public readonly record struct FightPoseSample(FightPose Pose, double Seconds);
public readonly record struct FightClipLengths(double Enter, double Guard, double Swing, double Flinch, double StandDown);

/// <summary>Unsaved, pair-owned acting. It never draws simulation RNG or produces combat events.</summary>
public sealed class FightChoreography
{
    private enum Beat { Enter, Hold, Swing, StandDown, Attending }
    private Beat _beat;
    private double _seconds, _hold;
    private ulong _variation;
    private int _turn;
    private bool _secondSwings;
    private bool _handling;
    public FightPoseSample First { get; private set; }
    public FightPoseSample Second { get; private set; }
    public double Seconds => _seconds;
    public int Turn => _turn;
    public bool Attending => _beat is Beat.StandDown or Beat.Attending;

    public FightChoreography(ulong first, ulong second, long incidentTick, bool restored = false)
    {
        _variation = unchecked(first * 11400714819323198485UL ^ second * 14029467366897019727UL ^ (ulong)incidentTick);
        _secondSwings = (_variation & 1) != 0;
        _beat = restored ? Beat.Hold : Beat.Enter;
        _hold = NextHold();
        First = Second = new(restored ? FightPose.Guard : FightPose.Enter, 0);
    }

    private double NextHold()
    {
        _variation = unchecked(_variation * 6364136223846793005UL + 1442695040888963407UL);
        return .55 + (_variation >> 32) % 551 / 1000.0;
    }

    public void Advance(double delta, bool frozen, bool handling, bool swingsSafe, FightClipLengths lengths)
    {
        if (frozen) return;
        if (handling != _handling)
        {
            _handling = handling; _seconds = 0;
            _beat = handling ? Beat.StandDown : Beat.Hold;
            _hold = NextHold();
        }
        // One visible step, never a backlog of missed punches after a long frame or fast-forward.
        _seconds += Math.Clamp(delta, 0, .1);
        var duration = _beat switch
        {
            Beat.Enter => lengths.Enter,
            Beat.Hold => _hold,
            Beat.Swing => Math.Max(lengths.Swing, .16 + lengths.Flinch),
            Beat.StandDown => lengths.StandDown,
            _ => double.PositiveInfinity
        };
        if (_seconds >= duration)
        {
            _seconds = 0;
            switch (_beat)
            {
                case Beat.Enter: _beat = Beat.Hold; _hold = NextHold(); break;
                case Beat.Hold:
                    // An occasional omitted beat and bounded varied holds prevent metronomic flailing.
                    if (swingsSafe && (_variation >> 40) % 5 != 0) _beat = Beat.Swing;
                    else _hold = NextHold();
                    break;
                case Beat.Swing: _beat = Beat.Hold; _secondSwings = !_secondSwings; _turn++; _hold = NextHold(); break;
                case Beat.StandDown: _beat = Beat.Attending; break;
            }
        }
        // Moving, too-close or wide pairs immediately withdraw a swing; never fake contact.
        if (!swingsSafe && _beat == Beat.Swing) { _beat = Beat.Hold; _seconds = 0; _hold = NextHold(); }
        var guard = new FightPoseSample(FightPose.Guard, _seconds % Math.Max(.001, lengths.Guard));
        var both = _beat switch
        {
            Beat.Enter => new FightPoseSample(FightPose.Enter, _seconds),
            Beat.StandDown => new(FightPose.StandDown, _seconds),
            Beat.Attending => new(FightPose.StandDown, lengths.StandDown),
            _ => guard
        };
        First = Second = both;
        if (_beat != Beat.Swing) return;
        var swing = _seconds < lengths.Swing ? new FightPoseSample((_turn & 1) == 0 ? FightPose.SwingLeft : FightPose.SwingRight, _seconds) : guard;
        var flinch = _seconds >= .16 && _seconds < .16 + lengths.Flinch ? new FightPoseSample(FightPose.Flinch, _seconds - .16) : guard;
        First = _secondSwings ? flinch : swing; Second = _secondSwings ? swing : flinch;
    }
}
