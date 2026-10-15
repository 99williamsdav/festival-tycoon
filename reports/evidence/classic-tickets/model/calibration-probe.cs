using Festival.Simulation;
using System.Text.Json;
var examples = new List<object>();
foreach (var seed in new ulong[] {1,2,3,4,5,6,12,14,20260922,20261015})
{
    var s = GameSession.CreateClassicCampaign(seed);
    var lineup = s.GetFestivalActs().Where(a => s.ActStandingOf(a) == ActStanding.Available).OrderBy(s.ActFee).ThenBy(a=>a.Id).Take(3).ToArray();
    var staff = s.GetStaffCandidates().GroupBy(c=>c.Role).Select(g=>g.OrderBy(c=>c.WagePennies).First()).ToArray();
    var facilities = new[] {BuildServiceKind.WaterTap,BuildServiceKind.Toilet,BuildServiceKind.FirstAid,BuildServiceKind.StewardPost};
    var minimumSite = staff.Sum(c=>c.WagePennies) + facilities.Sum(GameSession.BuildServiceFeePennies);
    var fees = lineup.Sum(s.ActFee);
    var input = new ClassicTicketDemandInput(25,0,1000,lineup.Select(a=>a.Popularity).ToArray());
    var estimate = ClassicTicketDemand.Estimate(input);
    var actual = ClassicTicketDemand.Resolve(seed,1,input);
    examples.Add(new {seed, acts=lineup.Select(a=>new {a.Id,a.Genre,a.Popularity,fee=s.ActFee(a),talent=PerformanceRules.Talent(a)}),
        estimate,actual,bandFeesPennies=fees,minimumMandatorySitePennies=minimumSite,
        starterCashPennies=20000,starterAfterMinimumPennies=20000+actual*1000-fees-minimumSite,
        lowestExistingCarryPennies=-10000,lowestCarryAfterMinimumPennies=-10000+actual*1000-fees-minimumSite,
        lowestCarryCanOpen=-10000+actual*1000-fees-minimumSite>=-10000});
}
Console.WriteLine(JsonSerializer.Serialize(examples,new JsonSerializerOptions{WriteIndented=true}));
