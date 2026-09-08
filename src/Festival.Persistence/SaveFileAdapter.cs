using System.IO.Compression;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Festival.Simulation;

namespace Festival.Persistence;

public static partial class SaveFileAdapter
{
    public const string FormatId = "festival-tycoon-save";
    public const string FileExtension = ".ftsave";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string ResolveSlotPath(string directory, string slotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (string.IsNullOrWhiteSpace(slotId) || !SlotPattern().IsMatch(slotId))
            throw new ArgumentException("Slot ID must be 1-64 ASCII letters, digits, underscores or hyphens and start with a letter or digit.", nameof(slotId));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var path = Path.GetFullPath(Path.Combine(root, slotId + FileExtension));
        if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Resolved slot path escaped the intended save directory.", nameof(slotId));
        return path;
    }

    public static SaveOperationResult SaveSlot(
        string directory,
        string slotId,
        SaveWriteRequest request,
        Action<SaveFailurePoint>? failureInjector = null) =>
        SaveFile(ResolveSlotPath(directory, slotId), request, failureInjector);

    public static SaveLoadResult LoadSlot(string directory, string slotId, SaveCompatibility expected) =>
        LoadFile(ResolveSlotPath(directory, slotId), expected);

    public static SaveOperationResult SaveFile(
        string path,
        SaveWriteRequest request,
        Action<SaveFailurePoint>? failureInjector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null) return SaveOperationResult.Failure("Save path must have a parent directory.", fullPath);
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + ".tmp";
        var backupPath = fullPath + ".bak";

        try
        {
            var envelope = CreateEnvelope(request);
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            WriteEnvelope(temporaryPath, envelope);
            var temporaryValidation = LoadFile(temporaryPath, request.Compatibility);
            if (!temporaryValidation.IsSuccess)
                throw new InvalidDataException($"Temporary save validation failed: {temporaryValidation.Error}");

            failureInjector?.Invoke(SaveFailurePoint.AfterTemporaryValidationBeforeReplace);
            if (File.Exists(fullPath))
            {
                var priorValidation = LoadFile(fullPath, request.Compatibility);
                File.Replace(
                    temporaryPath,
                    fullPath,
                    priorValidation.IsSuccess ? backupPath : null,
                    ignoreMetadataErrors: true);
            }
            else
                File.Move(temporaryPath, fullPath);
            return SaveOperationResult.Success(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                return SaveOperationResult.Failure($"Save failed: {exception.Message} Temporary cleanup also failed: {cleanupException.Message}", fullPath);
            }

            return SaveOperationResult.Failure($"Save failed before replacement; the previous slot was preserved. {exception.Message}", fullPath);
        }
    }

    public static SaveLoadResult LoadFile(string path, SaveCompatibility expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            var envelope = ReadEnvelope(Path.GetFullPath(path));
            if (envelope.Header is null || envelope.Payload is null)
                return SaveLoadResult.Failure("Save envelope must contain non-null header and payload records.");
            var migration = SaveMigrationPipeline.Migrate(envelope);
            if (!migration.IsSuccess) return SaveLoadResult.Failure(migration.Error!);
            envelope = migration.Envelope!;

            if (!string.Equals(envelope.Header.Format, FormatId, StringComparison.Ordinal))
                return SaveLoadResult.Failure($"Unknown save format '{envelope.Header.Format}'.");
            if (string.IsNullOrWhiteSpace(envelope.Header.BuildId) || string.IsNullOrWhiteSpace(envelope.Header.Purpose) ||
                !DateTimeOffset.TryParse(envelope.Header.TimestampUtc, out _))
                return SaveLoadResult.Failure("Save header requires a build ID, purpose and valid UTC timestamp.");
            if (!string.Equals(envelope.Header.ContentHash, expected.ContentHash, StringComparison.Ordinal))
                return SaveLoadResult.Failure($"Content hash mismatch: save '{envelope.Header.ContentHash}', expected '{expected.ContentHash}'. Explicit migration or matching content is required.");
            if (!string.Equals(envelope.Header.RulesetHash, expected.RulesetHash, StringComparison.Ordinal))
                return SaveLoadResult.Failure($"Ruleset hash mismatch: save '{envelope.Header.RulesetHash}', expected '{expected.RulesetHash}'. Explicit migration or matching ruleset is required.");
            if (envelope.Header.CampaignId != envelope.Payload.CampaignId || envelope.Header.Phase != envelope.Payload.Phase)
                return SaveLoadResult.Failure("Save header campaign/phase does not match its authoritative payload.");
            var checksum = ComputePayloadChecksum(envelope.Payload);
            if (!string.Equals(checksum, envelope.Header.PayloadChecksum, StringComparison.Ordinal))
                return SaveLoadResult.Failure($"Payload checksum mismatch: expected {envelope.Header.PayloadChecksum}, calculated {checksum}. The save may be corrupted or truncated.");

            var restored = GameSession.Restore(envelope.Payload);
            if (!restored.IsSuccess) return SaveLoadResult.Failure($"Authoritative payload validation failed: {restored.Error}");
            var buildMismatch = !string.Equals(envelope.Header.BuildId, expected.BuildId, StringComparison.Ordinal);
            return SaveLoadResult.Success(restored.Session!, envelope.Header, buildMismatch);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException)
        {
            return SaveLoadResult.Failure($"Could not load save '{Path.GetFileName(path)}': {exception.Message}");
        }
    }

    public static string ComputePayloadChecksum(SessionPersistenceSnapshot payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static SaveEnvelopeV1 CreateEnvelope(SaveWriteRequest request)
    {
        ValidateCompatibility(request.Compatibility);
        if (string.IsNullOrWhiteSpace(request.Purpose)) throw new InvalidDataException("Save purpose is required.");
        var payload = request.Session.CapturePersistenceSnapshot();
        var header = new SaveHeaderV1(
            FormatId,
            SaveMigrationPipeline.CurrentSchemaVersion,
            request.Compatibility.BuildId,
            request.Compatibility.ContentHash,
            request.Compatibility.RulesetHash,
            payload.CampaignId,
            request.TimestampUtc.ToUniversalTime().ToString("O"),
            payload.Phase,
            request.Purpose,
            ComputePayloadChecksum(payload));
        return new SaveEnvelopeV1(header, payload);
    }

    private static void ValidateCompatibility(SaveCompatibility compatibility)
    {
        if (string.IsNullOrWhiteSpace(compatibility.BuildId) || string.IsNullOrWhiteSpace(compatibility.ContentHash) || string.IsNullOrWhiteSpace(compatibility.RulesetHash))
            throw new InvalidDataException("Build ID, content hash and ruleset hash are required.");
    }

    private static void WriteEnvelope(string path, SaveEnvelopeV1 envelope)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize, leaveOpen: true))
            JsonSerializer.Serialize(gzip, envelope, JsonOptions);
        file.Flush(flushToDisk: true);
    }

    private static SaveEnvelopeV1 ReadEnvelope(string path)
    {
        var compressed = File.ReadAllBytes(path);
        if (compressed.Length < 18 || compressed[0] != 0x1f || compressed[1] != 0x8b || compressed[2] != 8)
            throw new InvalidDataException("Save is not a complete gzip stream with a valid header and footer.");
        using var input = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        try
        {
            gzip.CopyTo(output);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException("Save gzip stream is corrupted or truncated.", exception);
        }
        var payloadBytes = output.ToArray();
        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(compressed.AsSpan(compressed.Length - 8, 4));
        var expectedSize = BinaryPrimitives.ReadUInt32LittleEndian(compressed.AsSpan(compressed.Length - 4, 4));
        if (expectedCrc != ComputeCrc32(payloadBytes) || expectedSize != unchecked((uint)payloadBytes.Length))
            throw new InvalidDataException("Save gzip footer CRC or uncompressed size is invalid; the file may be truncated.");
        return JsonSerializer.Deserialize<SaveEnvelopeV1>(payloadBytes, JsonOptions)
            ?? throw new InvalidDataException("Save envelope is empty.");
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xedb88320U ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlotPattern();
}
