namespace Festival.Simulation.Fixtures;

/// <summary>
/// Scale diagnostic only: an ordinary Build campaign whose opening roster has a chosen number
/// of guests, so per-tick cost can be measured at the sizes the design targets. The session is
/// not saveable (persistence validation expects the real tier roster) and is not player content.
/// The festival is established, so any act will play.
/// </summary>
public static class BuildScaleFixture
{
    public static GameSession Create(ulong seed, int guests) => GameSession.CreateBuildCampaign(seed, guests, FestivalStanding.Established);
}
