namespace Festival.Simulation;

public enum StaffRole { Sound, Medic, Steward }

/// <summary>Character quirks a candidate brings to any role. All show on the staff page; the alcoholic only as a hint.</summary>
public enum StaffTrait { Slacker, SneakyAlcoholic, WeakBladder, IronBladder, DodgyKnee, Charismatic, Abrasive, Tardy }

/// <summary>
/// One person on the staff market. Grade -2..2 sets their abilities against the slot's baseline;
/// the wage follows the grade with some haggling noise, so a bargain or a dud is possible.
/// Traits shift the wage too; a tardy candidate turns up <see cref="LateTicks"/> after the gates open.
/// </summary>
public sealed record StaffCandidate(string Id, StaffRole Role, string Name, string Blurb, int WagePennies, int Grade,
    int WalkingSpeedPermille, int TreatmentTicks, int CalmingSkill, int ConfrontationSkill, int MixingBonus, StaffTrait[] Traits, int LateTicks = 0)
{
    public bool Has(StaffTrait trait) => Traits.Contains(trait);
    public int Number => Id[^1] - '0';
    public string ExtraOfferId => $"staff.extra-{StaffCatalogue.Key(Role)}.{Number}";
    public StaffProfile Profile(ulong agentId) => new(agentId, Name, Role == StaffRole.Medic ? ResponseRole.Medic : ResponseRole.Steward,
        WalkingSpeedPermille, TreatmentTicks, CalmingSkill, ConfrontationSkill);
}

/// <summary>The standard abilities of each slot, which the middle-grade candidate matches exactly.</summary>
public sealed record StaffBaseline(int MedicSpeedPermille, int StewardSpeedPermille, int CalmingSkill, int ConfrontationSkill);

public static class StaffCatalogue
{
    public const int PerRole = 6;
    public const int UnhiredMedicTreatmentTicks = 480;

    public static string Key(StaffRole role) => role switch { StaffRole.Sound => "sound", StaffRole.Medic => "medic", _ => "steward" };
    public static string RoleName(StaffRole role) => role switch { StaffRole.Sound => "sound engineer", StaffRole.Medic => "medic", _ => "steward" };
    /// <summary>The offer category for hiring into a role's main slot or its perk-granted second slot.</summary>
    public static string Category(StaffRole role, bool extra) => extra ? "extra-" + Key(role) : role == StaffRole.Sound ? "staff" : Key(role);
    public static bool IsWorkCategory(string category) => category is "staff" or "medic" or "steward" or "maintenance" or "extra-medic" or "extra-steward";
    public static bool IsVacancy(string name) => Enum.GetValues<StaffRole>().Any(role => Vacancy(role) == name);
    public static string Vacancy(StaffRole role) => role switch { StaffRole.Sound => "Sound engineer (unhired)", StaffRole.Medic => "Medic (unhired)", _ => "Steward (unhired)" };

    /// <summary>The candidate an offer id hires, whether into the main slot or the extra one.</summary>
    public static StaffCandidate? ForOffer(IEnumerable<StaffCandidate> candidates, string offerId)
    {
        var id = offerId.Replace("staff.extra-", "staff.", StringComparison.Ordinal);
        return candidates.SingleOrDefault(candidate => candidate.Id == id);
    }

    /// <summary>The candidate hired into a role's main or extra slot among accepted offers.</summary>
    public static StaffCandidate? Hired(IEnumerable<StaffCandidate> candidates, IEnumerable<string> offerIds, StaffRole role, bool extra)
    {
        var prefix = (extra ? "staff.extra-" : "staff.") + Key(role) + ".";
        return offerIds.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).Select(id => ForOffer(candidates, id)).FirstOrDefault(item => item is not null);
    }

    /// <summary>A dodgy knee walks at this share of normal pace, on top of any other slowing.</summary>
    public const int DodgyKneePacePermille = 800;

    /// <summary>The label the staff page shows. The alcoholic is only ever hinted at.</summary>
    public static string TraitLabel(StaffTrait trait) => trait switch
    {
        StaffTrait.Slacker => "Slacker",
        StaffTrait.SneakyAlcoholic => "Acts suspicious",
        StaffTrait.WeakBladder => "Weak bladder",
        StaffTrait.IronBladder => "Iron bladder",
        StaffTrait.DodgyKnee => "Dodgy knee",
        StaffTrait.Charismatic => "Charismatic",
        StaffTrait.Abrasive => "Abrasive",
        _ => "Tardy",
    };

    public static string TraitDescription(StaffTrait trait) => trait switch
    {
        StaffTrait.Slacker => "Easily distracted by food and drink.",
        StaffTrait.SneakyAlcoholic => "Acts suspicious and occasionally slurs words.",
        StaffTrait.WeakBladder => "Needs the toilet a lot.",
        StaffTrait.IronBladder => "Rarely needs the toilet.",
        StaffTrait.DodgyKnee => "Hobbles; walks slowly.",
        StaffTrait.Charismatic => "Guests near them enjoy themselves more.",
        StaffTrait.Abrasive => "Guests near them enjoy themselves less.",
        _ => "Turns up late.",
    };

    public static bool IsPositive(StaffTrait trait) => trait is StaffTrait.IronBladder or StaffTrait.Charismatic;

    /// <summary>How a trait moves the wage, in percent; two traits multiply.</summary>
    public static int TraitWagePercent(StaffTrait trait) => trait switch
    {
        StaffTrait.Charismatic => 25, StaffTrait.IronBladder => 10, StaffTrait.WeakBladder => -10, StaffTrait.SneakyAlcoholic => -20, _ => -15,
    };

    // 1-5 ratings for the staff page; each spans the generator's full range. A dodgy knee counts against pace.
    public static int PaceRating(StaffCandidate c) => Math.Clamp(((c.Has(StaffTrait.DodgyKnee) ? c.WalkingSpeedPermille * DodgyKneePacePermille / 1_000 : c.WalkingSpeedPermille) - 850) / 75 + 1, 1, 5);
    public static int TreatmentRating(StaffCandidate c) => Math.Clamp((600 - c.TreatmentTicks) / 60 + 1, 1, 5);
    public static int SkillRating(int skill) => Math.Clamp((skill - 3_500) * 4 / 4_500 + 1, 1, 5);
    public static int MixingRating(StaffCandidate c) => c.MixingBonus switch { <= -2 => 1, -1 => 2, 0 => 3, <= 2 => 4, _ => 5 };

    private static readonly string[] FirstNames = ["Bev", "Clive", "Dot", "Fran", "Gaz", "Hattie", "Ian", "Jackie", "Kev", "Lorraine", "Mo", "Nigel",
        "Pam", "Reg", "Shaz", "Terry", "Una", "Vince", "Wendy", "Del", "Trish", "Barry", "Gwen", "Lol"];
    private static readonly string[] LastNames = ["Pickering", "Thistlethwaite", "Bunce", "Cobb", "Dimmock", "Fairweather", "Grubb", "Hobday",
        "Kettle", "Lumb", "Mottram", "Nuttall", "Oddie", "Pugh", "Ramsbottom", "Scattergood", "Tuffin", "Wragg", "Bellchamber", "Crump"];

    // Two lines per grade, -2 first.
    private static readonly string[][] SoundBlurbs = [
        ["Owns a mixing desk. Has read some of the manual.", "Does sound for a nephew's ska band. Mostly turns it up."],
        ["Ran the PA for the village panto. Very loud panto.", "Brings their own gaffer tape. Sometimes uses it."],
        ["Reliable pair of ears. Will tell you if it's the cable.", "Did the Rotary Club disco circuit for a decade."],
        ["Toured with a covers band you've half heard of.", "Can hear feedback coming before it happens."],
        ["Mixed a stage at a real festival once. Mentions it often.", "Ex-studio engineer. Brings a spare everything."]];
    private static readonly string[][] MedicBlurbs = [
        ["First aid certificate expires next Tuesday.", "Very calm. Possibly too calm."],
        ["Volunteers at the cattle show. Mostly sheep, but still.", "Knows the recovery position and several variants."],
        ["Veteran of the county show first aid tent.", "Carries a bum bag of plasters and quiet authority."],
        ["Retired practice nurse. Brisk, kind, unshockable.", "Former lifeguard. Gets there fast and says 'right then'."],
        ["A&E nurse on a weekend off. Unflappable.", "Mountain rescue. Considers festivals a rest."]];
    private static readonly string[][] StewardBlurbs = [
        ["The hi-vis is doing most of the work.", "Tends to apologise to people who are shouting."],
        ["Ran the car park at the cricket club. Mostly pointed.", "Friendly. Slightly scared of teenagers."],
        ["Seasoned on the door at the Red Lion.", "Can settle a row about a parking space."],
        ["Ex-bouncer with a soft spot for folk music.", "Former PE teacher. One look and the queue behaves."],
        ["Calmed a riot at a darts final. Allegedly.", "Large, polite and somehow everywhere at once."]];

    /// <summary>
    /// Six candidates per role for this edition's seed. Candidate 1 has standard abilities (exactly the
    /// slot's baseline abilities), candidates 2 and 4 are below it, 3 and 5 above it, and 6 anywhere from a
    /// little below to a little above; the staff page sorts by wage, so the order is not on show.
    /// </summary>
    public static StaffCandidate[] Candidates(ulong seed, StaffBaseline baseline)
    {
        var names = RandomStreamFactory.Create(seed ^ 0x5354414646UL, RandomStreamId.IndividualBehaviour);
        var firsts = new HashSet<string>(); var lasts = new HashSet<string>();
        string NextName()
        {
            while (true)
            {
                var first = FirstNames[names.NextUInt32() % (uint)FirstNames.Length];
                var last = LastNames[names.NextUInt32() % (uint)LastNames.Length];
                if (firsts.Contains(first) || lasts.Contains(last)) continue;
                firsts.Add(first); lasts.Add(last);
                return $"{first} {last}";
            }
        }
        var result = new List<StaffCandidate>(PerRole * 3);
        foreach (var role in new[] { StaffRole.Sound, StaffRole.Medic, StaffRole.Steward })
        {
            var random = RandomStreamFactory.Create(seed ^ (0x4352455700UL + (ulong)role), RandomStreamId.IndividualBehaviour);
            // Traits draw from their own stream so abilities and wages stay as they were without them.
            var quirks = RandomStreamFactory.Create(seed ^ (0x5452414954UL + (ulong)role), RandomStreamId.IndividualBehaviour);
            int Next(int count) => (int)(random.NextUInt32() % (uint)count);
            for (var number = 1; number <= PerRole; number++)
            {
                var grade = number switch { 1 => 0, 2 or 4 => -1 - Next(2), 6 => Next(3) - 1, _ => 1 + Next(2) };
                var noise = grade == 0 ? 0 : (Next(7) - 3) * 100;
                var blurbs = (role switch { StaffRole.Sound => SoundBlurbs, StaffRole.Medic => MedicBlurbs, _ => StewardBlurbs })[grade + 2];
                var blurb = blurbs[Next(2)];
                var name = NextName();
                var id = $"staff.{Key(role)}.{number}";
                StaffCandidate candidate = role switch
                {
                    StaffRole.Sound => new(id, role, name, blurb, Math.Max(800, 2_000 + grade * 900 + noise), grade,
                        1_000, 0, 0, 0, grade switch { -2 => -2, -1 => -1, 0 => 0, 1 => 2, _ => 3 }, []),
                    StaffRole.Medic => new(id, role, name, blurb, Math.Max(800, 1_500 + grade * 600 + noise), grade,
                        Speed(baseline.MedicSpeedPermille), UnhiredMedicTreatmentTicks - grade * 60, 0, 0, 0, []),
                    _ => Steward(id, name, blurb, Math.Max(800, 1_200 + grade * 500 + noise), grade)
                };
                result.Add(WithTraits(candidate, quirks));

                int Speed(int standard) => grade == 0 ? standard : Math.Clamp(standard + grade * 75 + (Next(3) - 1) * 50, 850, 1_150);
                StaffCandidate Steward(string stewardId, string stewardName, string stewardBlurb, int wage, int stewardGrade)
                {
                    // A personality lean: some talk people down, others are better in a scuffle.
                    var lean = stewardGrade == 0 ? 0 : (Next(5) - 2) * 300;
                    return new(stewardId, StaffRole.Steward, stewardName, stewardBlurb, wage, stewardGrade, Speed(baseline.StewardSpeedPermille), 0,
                        Math.Clamp(baseline.CalmingSkill + stewardGrade * 900 + lean, 3_500, 8_000),
                        Math.Clamp(baseline.ConfrontationSkill + stewardGrade * 900 - lean, 3_500, 8_000), 0, []);
                }
            }
        }
        return result.ToArray();
    }

    /// <summary>
    /// Any candidate has none (4 in 8), one (3 in 8) or two (1 in 8) traits, so about half the
    /// market has a quirk. Opposites never pair up.
    /// </summary>
    private static StaffCandidate WithTraits(StaffCandidate candidate, Pcg32Random random)
    {
        var roll = random.NextUInt32() % 8;
        var count = roll < 4 ? 0 : roll == 7 ? 2 : 1;
        var all = Enum.GetValues<StaffTrait>();
        var traits = new List<StaffTrait>(2);
        while (traits.Count < count)
        {
            var trait = all[random.NextUInt32() % (uint)all.Length];
            if (traits.Contains(trait) || traits.Contains(Opposite(trait))) continue;
            traits.Add(trait);
        }
        traits.Sort();
        var late = traits.Contains(StaffTrait.Tardy) ? (30 + (int)(random.NextUInt32() % 61)) * 80 : 0;
        return candidate with
        {
            Traits = traits.ToArray(), LateTicks = late,
            WagePennies = Math.Max(500, traits.Aggregate(candidate.WagePennies, (wage, trait) => wage * (100 + TraitWagePercent(trait)) / 100) / 100 * 100),
        };
    }

    private static StaffTrait Opposite(StaffTrait trait) => trait switch
    {
        StaffTrait.WeakBladder => StaffTrait.IronBladder, StaffTrait.IronBladder => StaffTrait.WeakBladder,
        StaffTrait.Charismatic => StaffTrait.Abrasive, StaffTrait.Abrasive => StaffTrait.Charismatic,
        _ => trait,
    };
}
