using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Festival.Simulation.Content;

public sealed record ParsedContentDocument
{
    public int SchemaVersion { get; init; }
    public int CatalogueVersion { get; init; }
    public ParsedService[] Services { get; init; } = null!;
    public ParsedScenario[] Scenarios { get; init; } = null!;
}

public sealed record ParsedService
{
    public string Id { get; init; } = string.Empty;
    public int Version { get; init; }
    public int DurationTicks { get; init; }
    public int PricePennies { get; init; }
    public int Stock { get; init; }
}

public sealed record ParsedScenario
{
    public string Id { get; init; } = string.Empty;
    public int Version { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public ParsedCell[] BlockedCells { get; init; } = null!;
    public ParsedLocation[] Locations { get; init; } = null!;
    public ulong AgentSeed { get; init; }
    public int AgentCount { get; init; }
    public string ServiceId { get; init; } = string.Empty;
}

public sealed record ParsedCell(int X, int Y);

public sealed record ParsedLocation
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
    public bool Required { get; init; }
}

public sealed record ContentDiagnostic(string Code, string File, string Record, string Field, string Message)
{
    public override string ToString() => $"{File}: record '{Record}', field '{Field}': {Message} [{Code}]";
}

public sealed class ContentLoadResult
{
    private ContentLoadResult(ContentCatalogue? catalogue, IEnumerable<ContentDiagnostic> diagnostics)
    {
        Catalogue = catalogue;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public bool IsSuccess => Catalogue is not null;
    public ContentCatalogue? Catalogue { get; }
    public IReadOnlyList<ContentDiagnostic> Diagnostics { get; }

    internal static ContentLoadResult Success(ContentCatalogue catalogue) => new(catalogue, []);
    internal static ContentLoadResult Failure(IEnumerable<ContentDiagnostic> diagnostics) => new(null, diagnostics);
    public static ContentLoadResult FromAdapterFailure(ContentDiagnostic diagnostic) => new(null, [diagnostic]);
}

public sealed record Cell(int X, int Y);
public sealed record LocationDefinition(string Id, string Kind, Cell Cell, bool Required);
public sealed record ServiceDefinition(string Id, int Version, int DurationTicks, int PricePennies, int Stock);

public sealed class ScenarioDefinition
{
    internal ScenarioDefinition(string id, int version, int width, int height, IEnumerable<Cell> blockedCells,
        IEnumerable<LocationDefinition> locations, ulong agentSeed, int agentCount, ServiceDefinition service)
    {
        Id = id;
        Version = version;
        Width = width;
        Height = height;
        BlockedCells = Array.AsReadOnly(blockedCells.ToArray());
        Locations = Array.AsReadOnly(locations.ToArray());
        AgentSeed = agentSeed;
        AgentCount = agentCount;
        Service = service;
    }

    public string Id { get; }
    public int Version { get; }
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyCollection<Cell> BlockedCells { get; }
    public ReadOnlyCollection<LocationDefinition> Locations { get; }
    public ulong AgentSeed { get; }
    public int AgentCount { get; }
    public ServiceDefinition Service { get; }
}

public sealed class ContentCatalogue
{
    internal ContentCatalogue(int schemaVersion, int catalogueVersion, IEnumerable<ServiceDefinition> services, IEnumerable<ScenarioDefinition> scenarios)
    {
        SchemaVersion = schemaVersion;
        CatalogueVersion = catalogueVersion;
        Services = Array.AsReadOnly(services.ToArray());
        Scenarios = Array.AsReadOnly(scenarios.ToArray());
        ContentHash = ComputeHash(this);
    }

    public int SchemaVersion { get; }
    public int CatalogueVersion { get; }
    public ReadOnlyCollection<ServiceDefinition> Services { get; }
    public ReadOnlyCollection<ScenarioDefinition> Scenarios { get; }
    public string ContentHash { get; }

    private static string ComputeHash(ContentCatalogue catalogue)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            writer.Write(catalogue.SchemaVersion);
            writer.Write(catalogue.CatalogueVersion);
            writer.Write(catalogue.Services.Count);
            foreach (var service in catalogue.Services.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.Write(service.Id);
                writer.Write(service.Version);
                writer.Write(service.DurationTicks);
                writer.Write(service.PricePennies);
                writer.Write(service.Stock);
            }

            writer.Write(catalogue.Scenarios.Count);
            foreach (var scenario in catalogue.Scenarios.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.Write(scenario.Id);
                writer.Write(scenario.Version);
                writer.Write(scenario.Width);
                writer.Write(scenario.Height);
                writer.Write(scenario.AgentSeed);
                writer.Write(scenario.AgentCount);
                writer.Write(scenario.Service.Id);
                writer.Write(scenario.BlockedCells.Count);
                foreach (var cell in scenario.BlockedCells.OrderBy(item => item.X).ThenBy(item => item.Y))
                {
                    writer.Write(cell.X);
                    writer.Write(cell.Y);
                }

                writer.Write(scenario.Locations.Count);
                foreach (var location in scenario.Locations.OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    writer.Write(location.Id);
                    writer.Write(location.Kind);
                    writer.Write(location.Cell.X);
                    writer.Write(location.Cell.Y);
                    writer.Write(location.Required);
                }
            }
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
}

public static class ContentCatalogueLoader
{
    public const int SupportedSchemaVersion = 1;

    public static ContentLoadResult Load(ParsedContentDocument document, string sourceFile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        var diagnostics = new List<ContentDiagnostic>();
        if (document.SchemaVersion != SupportedSchemaVersion)
            Add(diagnostics, "unknown_schema_version", sourceFile, "$document", "schemaVersion", $"Schema version {document.SchemaVersion} is not supported; expected {SupportedSchemaVersion}.");
        if (document.CatalogueVersion <= 0)
            Add(diagnostics, "invalid_version", sourceFile, "$document", "catalogueVersion", "Catalogue version must be positive.");

        var services = RequireCollection(document.Services, sourceFile, "$document", "services", diagnostics);
        var scenarios = RequireCollection(document.Scenarios, sourceFile, "$document", "scenarios", diagnostics);
        ReportDuplicateIds(services.Select(item => item.Id), "service", sourceFile, diagnostics);
        ReportDuplicateIds(scenarios.Select(item => item.Id), "scenario", sourceFile, diagnostics);

        foreach (var service in services)
        {
            var record = Record("service", service.Id);
            ValidateId(service.Id, sourceFile, record, diagnostics);
            ValidateVersion(service.Version, sourceFile, record, diagnostics);
            if (service.DurationTicks <= 0)
                Add(diagnostics, "nonpositive_service_duration", sourceFile, record, "durationTicks", "Service duration must be greater than zero ticks.");
            ValidateNonnegative(service.PricePennies, "pricePennies", sourceFile, record, diagnostics);
            ValidateNonnegative(service.Stock, "stock", sourceFile, record, diagnostics);
        }

        foreach (var scenario in scenarios)
            ValidateScenario(scenario, services, sourceFile, diagnostics);

        if (diagnostics.Count > 0)
            return ContentLoadResult.Failure(diagnostics);

        var resolvedServices = services.ToDictionary(item => item.Id!,
            item => new ServiceDefinition(item.Id!, item.Version, item.DurationTicks, item.PricePennies, item.Stock), StringComparer.Ordinal);
        var resolvedScenarios = scenarios.Select(item => new ScenarioDefinition(item.Id!, item.Version, item.Width, item.Height,
            item.BlockedCells!.Select(cell => new Cell(cell!.X, cell.Y)),
            item.Locations!.Select(location => new LocationDefinition(location!.Id!, location.Kind!, new Cell(location.X, location.Y), location.Required)),
            item.AgentSeed, item.AgentCount, resolvedServices[item.ServiceId!]));

        return ContentLoadResult.Success(new ContentCatalogue(document.SchemaVersion, document.CatalogueVersion,
            resolvedServices.Values, resolvedScenarios));
    }

    private static void ValidateScenario(ParsedScenario scenario, ParsedService[] services, string sourceFile, List<ContentDiagnostic> diagnostics)
    {
        var record = Record("scenario", scenario.Id);
        ValidateId(scenario.Id, sourceFile, record, diagnostics);
        ValidateVersion(scenario.Version, sourceFile, record, diagnostics);
        if (scenario.Width <= 0)
            Add(diagnostics, "invalid_dimensions", sourceFile, record, "width", "Scenario width must be greater than zero.");
        if (scenario.Height <= 0)
            Add(diagnostics, "invalid_dimensions", sourceFile, record, "height", "Scenario height must be greater than zero.");
        ValidateNonnegative(scenario.AgentCount, "agentCount", sourceFile, record, diagnostics);
        if (string.IsNullOrWhiteSpace(scenario.ServiceId) || !services.Any(item => item.Id == scenario.ServiceId))
            Add(diagnostics, "missing_reference", sourceFile, record, "serviceId", $"Service definition '{scenario.ServiceId}' was not found.");

        var blockedCells = RequireCollection(scenario.BlockedCells, sourceFile, record, "blockedCells", diagnostics);
        var blocked = new HashSet<(int X, int Y)>();
        foreach (var cell in blockedCells)
        {
            if (!InBounds(cell.X, cell.Y, scenario.Width, scenario.Height))
                Add(diagnostics, "cell_out_of_bounds", sourceFile, record, "blockedCells", $"Blocked cell ({cell.X}, {cell.Y}) is outside {scenario.Width}x{scenario.Height} bounds.");
            else if (!blocked.Add((cell.X, cell.Y)))
                Add(diagnostics, "duplicate_cell", sourceFile, record, "blockedCells", $"Blocked cell ({cell.X}, {cell.Y}) is duplicated.");
        }

        var locations = RequireCollection(scenario.Locations, sourceFile, record, "locations", diagnostics);
        ReportDuplicateIds(locations.Select(item => item.Id), $"location in {record}", sourceFile, diagnostics);
        foreach (var location in locations)
        {
            var locationRecord = $"{record}/location:{DisplayId(location.Id)}";
            ValidateId(location.Id, sourceFile, locationRecord, diagnostics);
            if (location.Kind is not ("spawn" or "target"))
                Add(diagnostics, "invalid_location_kind", sourceFile, locationRecord, "kind", "Location kind must be 'spawn' or 'target'.");
            var mustBeUsable = location.Kind == "spawn" || (location.Kind == "target" && location.Required);
            if (!InBounds(location.X, location.Y, scenario.Width, scenario.Height))
                Add(diagnostics, "location_out_of_bounds", sourceFile, locationRecord, "cell", $"{location.Kind} location ({location.X}, {location.Y}) is outside {scenario.Width}x{scenario.Height} bounds.");
            else if (mustBeUsable && blocked.Contains((location.X, location.Y)))
                Add(diagnostics, "location_blocked", sourceFile, locationRecord, "cell", $"{location.Kind} location ({location.X}, {location.Y}) is blocked.");
        }

        if (!locations.Any(location => location.Kind == "spawn"))
            Add(diagnostics, "missing_spawn", sourceFile, record, "locations", "At least one spawn location is required.");
        if (!locations.Any(location => location.Kind == "target" && location.Required))
            Add(diagnostics, "missing_required_target", sourceFile, record, "locations", "At least one required target location is required.");
    }

    private static T[] RequireCollection<T>(T?[]? values, string sourceFile, string record, string field, List<ContentDiagnostic> diagnostics)
        where T : class
    {
        if (values is null)
        {
            Add(diagnostics, "null_collection", sourceFile, record, field, $"'{field}' must be a JSON array; null or omission is not allowed.");
            return [];
        }

        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] is null)
                Add(diagnostics, "null_record", sourceFile, record, $"{field}[{index}]", $"'{field}' entry {index} cannot be null.");
        }

        return values.OfType<T>().ToArray();
    }

    private static void ReportDuplicateIds(IEnumerable<string?> ids, string recordType, string sourceFile, List<ContentDiagnostic> diagnostics)
    {
        foreach (var duplicate in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            Add(diagnostics, "duplicate_id", sourceFile, $"{recordType}:{duplicate.Key}", "id", $"Stable ID '{duplicate.Key}' is duplicated.");
    }

    private static void ValidateId(string? id, string sourceFile, string record, List<ContentDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(id)) Add(diagnostics, "missing_id", sourceFile, record, "id", "A non-empty stable ID is required.");
    }

    private static void ValidateVersion(int version, string sourceFile, string record, List<ContentDiagnostic> diagnostics)
    {
        if (version <= 0) Add(diagnostics, "invalid_version", sourceFile, record, "version", "Definition version must be positive.");
    }

    private static void ValidateNonnegative(int value, string field, string sourceFile, string record, List<ContentDiagnostic> diagnostics)
    {
        if (value < 0) Add(diagnostics, "negative_quantity", sourceFile, record, field, $"{field} cannot be negative.");
    }

    private static bool InBounds(int x, int y, int width, int height) => x >= 0 && y >= 0 && x < width && y < height;
    private static string Record(string kind, string? id) => $"{kind}:{DisplayId(id)}";
    private static string DisplayId(string? id) => string.IsNullOrWhiteSpace(id) ? "<missing>" : id;
    private static void Add(List<ContentDiagnostic> diagnostics, string code, string file, string record, string field, string message) => diagnostics.Add(new(code, file, record, field, message));
}
