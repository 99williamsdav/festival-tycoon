using System.Text.Json;
using Festival.Simulation.Content;

namespace Festival.ContentAdapter;

public static class JsonContentAdapter
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static ContentLoadResult LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var displayPath = Path.GetFileName(path);
        try
        {
            var document = JsonSerializer.Deserialize<ParsedContentDocument>(File.ReadAllText(path), Options);
            return document is null
                ? Failure(displayPath, "The JSON document is empty.")
                : ContentCatalogueLoader.Load(document, displayPath);
        }
        catch (JsonException exception)
        {
            return Failure(displayPath, $"Invalid JSON at path {exception.Path ?? "$"}: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(displayPath, $"Could not read content file: {exception.Message}");
        }
    }

    private static ContentLoadResult Failure(string file, string message) => ContentLoadResult.FromAdapterFailure(
        new ContentDiagnostic("content_read_failed", file, "$document", "$", message));
}
