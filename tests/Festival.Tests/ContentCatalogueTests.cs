using Festival.ContentAdapter;
using Festival.Simulation.Content;

namespace Festival.Tests;

[TestClass]
public sealed class ContentCatalogueTests
{
    [TestMethod]
    public void ValidFixtureLoadsTwiceWithSameHashAndResolvedReferences()
    {
        var path = FixturePath("content", "scenarios", "m0-valid.json");
        var first = JsonContentAdapter.LoadFile(path);
        var second = JsonContentAdapter.LoadFile(path);

        Assert.IsTrue(first.IsSuccess, JoinDiagnostics(first));
        Assert.IsTrue(second.IsSuccess, JoinDiagnostics(second));
        Assert.AreEqual(first.Catalogue!.ContentHash, second.Catalogue!.ContentHash);
        Assert.AreEqual(3, first.Catalogue.Scenarios.Count);
        Assert.AreSame(first.Catalogue.Services.Single(), first.Catalogue.Scenarios.Single(item => item.Id == "scenario.single-agent").Service);
    }

    [TestMethod]
    public void ReorderingUnorderedRecordsDoesNotChangeHash()
    {
        var original = ValidDocument();
        var reordered = original with
        {
            Services = original.Services.Reverse().ToArray(),
            Scenarios = original.Scenarios.Reverse().Select(scenario => scenario with
            {
                BlockedCells = scenario.BlockedCells.Reverse().ToArray(),
                Locations = scenario.Locations.Reverse().ToArray()
            }).ToArray()
        };

        Assert.AreEqual(Load(original).Catalogue!.ContentHash, Load(reordered).Catalogue!.ContentHash);
    }

    [TestMethod]
    public void MeaningfulTuningChangeChangesHash()
    {
        var original = ValidDocument();
        var tuned = original with { Services = [original.Services[0] with { DurationTicks = original.Services[0].DurationTicks + 1 }] };
        Assert.AreNotEqual(Load(original).Catalogue!.ContentHash, Load(tuned).Catalogue!.ContentHash);
    }

    [TestMethod]
    public void RequiredInvalidCategoriesAreActionableAndProduceNoCatalogue()
    {
        var invalid = ValidDocument() with
        {
            SchemaVersion = 42,
            Services =
            [
                new() { Id = "duplicate", Version = 1, DurationTicks = 0, PricePennies = -1, Stock = -1 },
                new() { Id = "duplicate", Version = 1, DurationTicks = 1, PricePennies = 0, Stock = 0 }
            ],
            Scenarios =
            [
                ValidDocument().Scenarios[0] with { Width = 0, AgentCount = -1, ServiceId = "missing" }
            ]
        };

        var result = Load(invalid, "all-invalid.json");
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Catalogue);
        foreach (var code in new[] { "unknown_schema_version", "duplicate_id", "invalid_dimensions", "nonpositive_service_duration", "negative_quantity", "missing_reference" })
        {
            var diagnostic = result.Diagnostics.FirstOrDefault(item => item.Code == code);
            Assert.IsNotNull(diagnostic, $"Missing diagnostic {code}");
            Assert.AreEqual("all-invalid.json", diagnostic.File);
            Assert.IsFalse(string.IsNullOrWhiteSpace(diagnostic.Record));
            Assert.IsFalse(string.IsNullOrWhiteSpace(diagnostic.Field));
        }
    }

    [TestMethod]
    public void OutOfBoundsSpawnAndRequiredTargetAreReported()
    {
        var scenario = ValidDocument().Scenarios[0] with
        {
            Locations =
            [
                new() { Id = "spawn.bad", Kind = "spawn", X = -1, Y = 0, Required = true },
                new() { Id = "target.bad", Kind = "target", X = 99, Y = 0, Required = true }
            ]
        };
        var result = Load(ValidDocument() with { Scenarios = [scenario] });
        Assert.AreEqual(2, result.Diagnostics.Count(item => item.Code == "location_out_of_bounds"));
        Assert.IsNull(result.Catalogue);
    }

    [TestMethod]
    public void BlockedSpawnAndRequiredTargetAreReported()
    {
        var scenario = ValidDocument().Scenarios[0] with
        {
            BlockedCells = [new(0, 1), new(3, 1)],
            Locations =
            [
                new() { Id = "spawn.bad", Kind = "spawn", X = 0, Y = 1, Required = true },
                new() { Id = "target.bad", Kind = "target", X = 3, Y = 1, Required = true }
            ]
        };
        var result = Load(ValidDocument() with { Scenarios = [scenario] });
        Assert.AreEqual(2, result.Diagnostics.Count(item => item.Code == "location_blocked"));
        Assert.IsNull(result.Catalogue);
    }

    [TestMethod]
    public void InvalidJsonReturnsContextWithoutThrowing()
    {
        var result = JsonContentAdapter.LoadFile(FixturePath("tests", "fixtures", "content", "m0-invalid-json.json"));
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Catalogue);
        Assert.AreEqual("content_read_failed", result.Diagnostics.Single().Code);
        Assert.AreEqual("$document", result.Diagnostics.Single().Record);
    }

    [TestMethod]
    public void NullCollectionsAndEntriesReturnDiagnosticsThroughJsonAdapter()
    {
        const string service = "{\"id\":\"service.water\",\"version\":1,\"durationTicks\":1,\"pricePennies\":0,\"stock\":1}";
        const string scenarioPrefix = "{\"id\":\"scenario.test\",\"version\":1,\"width\":2,\"height\":2,";
        const string scenarioSuffix = ",\"agentSeed\":1,\"agentCount\":1,\"serviceId\":\"service.water\"}";
        var cases = new (string Name, string Json, string Field, string Code)[]
        {
            ("services-null", "{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":null,\"scenarios\":[]}", "services", "null_collection"),
            ("scenarios-null", $"{{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":[{service}],\"scenarios\":null}}", "scenarios", "null_collection"),
            ("services-omitted", "{\"schemaVersion\":1,\"catalogueVersion\":1,\"scenarios\":[]}", "services", "null_collection"),
            ("scenarios-omitted", $"{{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":[{service}]}}", "scenarios", "null_collection"),
            ("service-entry-null", "{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":[null],\"scenarios\":[]}", "services[0]", "null_record"),
            ("scenario-entry-null", $"{{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":[{service}],\"scenarios\":[null]}}", "scenarios[0]", "null_record"),
            ("blocked-cells-null", $"{{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":[{service}],\"scenarios\":[{scenarioPrefix}\"blockedCells\":null,\"locations\":[]{scenarioSuffix}]}}", "blockedCells", "null_collection"),
            ("locations-null", $"{{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":[{service}],\"scenarios\":[{scenarioPrefix}\"blockedCells\":[],\"locations\":null{scenarioSuffix}]}}", "locations", "null_collection"),
            ("blocked-cells-omitted", $"{{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":[{service}],\"scenarios\":[{scenarioPrefix}\"locations\":[]{scenarioSuffix}]}}", "blockedCells", "null_collection"),
            ("locations-omitted", $"{{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":[{service}],\"scenarios\":[{scenarioPrefix}\"blockedCells\":[]{scenarioSuffix}]}}", "locations", "null_collection"),
            ("blocked-cell-entry-null", $"{{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":[{service}],\"scenarios\":[{scenarioPrefix}\"blockedCells\":[null],\"locations\":[]{scenarioSuffix}]}}", "blockedCells[0]", "null_record"),
            ("location-entry-null", $"{{\"schemaVersion\":1,\"catalogueVersion\":1,\"services\":[{service}],\"scenarios\":[{scenarioPrefix}\"blockedCells\":[],\"locations\":[null]{scenarioSuffix}]}}", "locations[0]", "null_record")
        };

        foreach (var item in cases)
        {
            var path = Path.Combine(Path.GetTempPath(), $"festival-{item.Name}-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, item.Json);
                var result = JsonContentAdapter.LoadFile(path);
                Assert.IsFalse(result.IsSuccess, item.Name);
                Assert.IsNull(result.Catalogue, item.Name);
                Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == item.Code && diagnostic.Field == item.Field),
                    $"{item.Name}: {JoinDiagnostics(result)}");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    private static ContentLoadResult Load(ParsedContentDocument document, string file = "test.json") => ContentCatalogueLoader.Load(document, file);

    private static ParsedContentDocument ValidDocument() => new()
    {
        SchemaVersion = 1,
        CatalogueVersion = 1,
        Services = [new() { Id = "service.water", Version = 1, DurationTicks = 120, PricePennies = 0, Stock = 100 }],
        Scenarios =
        [
            new()
            {
                Id = "scenario.test", Version = 1, Width = 5, Height = 4,
                BlockedCells = [new(2, 0), new(2, 2)],
                Locations =
                [
                    new() { Id = "spawn.gate", Kind = "spawn", X = 0, Y = 1, Required = true },
                    new() { Id = "target.water", Kind = "target", X = 3, Y = 1, Required = true }
                ],
                AgentSeed = 7, AgentCount = 1, ServiceId = "service.water"
            }
        ]
    };

    private static string FixturePath(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "FestivalTycoon.sln"))) directory = Directory.GetParent(directory)?.FullName;
        Assert.IsNotNull(directory, "Could not locate repository root.");
        return Path.Combine([directory, .. parts]);
    }

    private static string JoinDiagnostics(ContentLoadResult result) => string.Join(Environment.NewLine, result.Diagnostics);
}
