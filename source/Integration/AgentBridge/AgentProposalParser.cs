using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>Parses an untrusted reply into an <see cref="AgentProposal"/> or one sentence why not. Each operation is mapped on its own, so one bad object costs one review row. See docs/tech/agent-bridge.md#reply-parsing.</summary>
internal static class AgentProposalParser
{
    // Deliberately NOT the session loader's options: those tolerate hand edits, and a reply must get no tolerance.
    private static readonly JsonSerializerOptions Strict = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = AgentProtocol.MaxJsonDepth
    };

    public static AgentProposalParseResult Parse(string? clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            return AgentProposalParseResult.Fail("The clipboard holds no text.");
        }

        int bytes = Encoding.UTF8.GetByteCount(clipboardText);
        if (bytes > AgentProtocol.MaxProposalBytes)
        {
            return AgentProposalParseResult.Fail(
                $"The clipboard text is {bytes / 1024} KB; a proposal is at most " +
                $"{AgentProtocol.MaxProposalBytes / 1024} KB.");
        }

        if (!TryExtractBlock(clipboardText, out string json, out string? problem))
        {
            return AgentProposalParseResult.Fail(problem!);
        }

        ProposalWire? wire;
        try
        {
            wire = JsonSerializer.Deserialize<ProposalWire>(json, Strict);
        }
        catch (JsonException exception)
        {
            return AgentProposalParseResult.Fail(
                "The proposal block is not the JSON the protocol describes: " +
                Shorten(exception.Message));
        }

        if (wire == null)
        {
            return AgentProposalParseResult.Fail("The proposal block is empty.");
        }

        if (wire.Kind != AgentProtocol.ProposalKind)
        {
            return AgentProposalParseResult.Fail(
                $"The block is not a Resonalyze proposal (kind '{Shorten(wire.Kind)}').");
        }
        if (wire.ProtocolVersion != AgentProtocol.Version)
        {
            return AgentProposalParseResult.Fail(
                $"The proposal uses protocol version {wire.ProtocolVersion}; this build " +
                $"reads version {AgentProtocol.Version}.");
        }
        // `required` only requires PRESENCE; a JSON null passes it.
        if (wire.Operations == null)
        {
            return AgentProposalParseResult.Fail("The proposal's operations list is null.");
        }
        if (!WithinLength(wire.Summary) || !WithinLength(wire.PackageId))
        {
            return AgentProposalParseResult.Fail("The summary or package id is too long.");
        }

        List<string> advice = wire.Advice ?? [];
        if (advice.Count > AgentProtocol.MaxListItems ||
            advice.Any(line => line == null || !WithinLength(line)))
        {
            return AgentProposalParseResult.Fail(
                $"Advice is limited to {AgentProtocol.MaxListItems} lines of " +
                $"{AgentProtocol.MaxStringLength} characters.");
        }

        List<SourceWire> sourceWires = wire.Sources ?? [];
        if (sourceWires.Count > AgentProtocol.MaxListItems)
        {
            return AgentProposalParseResult.Fail(
                $"Sources are limited to {AgentProtocol.MaxListItems}.");
        }
        var sources = new List<AgentSource>(sourceWires.Count);
        foreach (SourceWire source in sourceWires)
        {
            if (source == null ||
                !IsWebUrl(source.Url) ||
                !WithinLength(source.Title) ||
                (source.FactsUsed?.Count ?? 0) > AgentProtocol.MaxListItems ||
                (source.FactsUsed?.Any(fact => fact == null || !WithinLength(fact)) ?? false))
            {
                return AgentProposalParseResult.Fail(
                    "A source is not an http(s) URL with a short title and fact list.");
            }

            sources.Add(new AgentSource(source.Url, source.Title, source.FactsUsed ?? []));
        }

        if (wire.Operations.Count > AgentProtocol.MaxOperations)
        {
            return AgentProposalParseResult.Fail(
                $"A proposal holds at most {AgentProtocol.MaxOperations} operations; " +
                $"this one has {wire.Operations.Count}.");
        }

        var operations = new List<AgentOperation>();
        var rejected = new List<AgentRejectedOperation>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement element in wire.Operations)
        {
            (AgentOperation? operation, AgentRejectedOperation? rejection) = ReadOperation(element);
            if (operation == null)
            {
                rejected.Add(rejection!);
            }
            else if (!ids.Add(operation.Id))
            {
                rejected.Add(new AgentRejectedOperation(
                    operation.Id, operation.Op, "Duplicate operation id."));
            }
            else
            {
                operations.Add(operation);
            }
        }

        return AgentProposalParseResult.Success(new AgentProposal(
            string.IsNullOrWhiteSpace(wire.PackageId) ? null : wire.PackageId,
            Prose(wire.Summary),
            advice,
            sources,
            operations,
            rejected));
    }

    // Exactly one proposal object (identified by its "kind"): two are refused, not guessed between. Legacy BEGIN/END markers are still read.
    private static bool TryExtractBlock(string text, out string json, out string? problem)
    {
        json = string.Empty;
        int begins = Count(text, AgentProtocol.ProposalBegin);
        int ends = Count(text, AgentProtocol.ProposalEnd);
        if (begins > 0 || ends > 0)
        {
            return TryExtractMarkedBlock(text, begins, ends, out json, out problem);
        }

        List<string> candidates = ProposalObjects(text);
        if (candidates.Count == 0)
        {
            problem = "No proposal found: the reply must contain one JSON object with " +
                $"\"kind\": \"{AgentProtocol.ProposalKind}\" (a fenced code block is fine).";
            return false;
        }
        if (candidates.Count > 1)
        {
            problem = $"The reply must contain exactly one proposal; found {candidates.Count} " +
                $"JSON objects with \"kind\": \"{AgentProtocol.ProposalKind}\".";
            return false;
        }

        json = candidates[0];
        problem = null;
        return true;
    }

    private static bool TryExtractMarkedBlock(
        string text, int begins, int ends, out string json, out string? problem)
    {
        json = string.Empty;
        if (begins != 1 || ends != 1)
        {
            problem = $"The reply must contain exactly one proposal block; found " +
                $"{begins} begin and {ends} end marker(s).";
            return false;
        }

        int begin = text.IndexOf(AgentProtocol.ProposalBegin, StringComparison.Ordinal);
        int end = text.IndexOf(AgentProtocol.ProposalEnd, StringComparison.Ordinal);
        int start = begin + AgentProtocol.ProposalBegin.Length;
        if (end < start)
        {
            problem = "The proposal's end marker comes before its begin marker.";
            return false;
        }

        json = Unfenced(text[start..end]);
        if (json.Length == 0)
        {
            problem = "The proposal block is empty.";
            return false;
        }

        problem = null;
        return true;
    }

    private static string Unfenced(string text)
    {
        string json = text.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            int newline = json.IndexOf('\n');
            json = newline < 0 ? string.Empty : json[(newline + 1)..];
            if (json.EndsWith("```", StringComparison.Ordinal))
            {
                json = json[..^3];
            }
            json = json.Trim();
        }

        return json;
    }

    // Top-level objects naming the proposal kind; the scanner knows JSON strings, skips unclosed or unnamed candidates, and resumes after each so nested objects are not counted twice.
    private static List<string> ProposalObjects(string text)
    {
        var found = new List<string>();
        string kindMarker = "\"" + AgentProtocol.ProposalKind + "\"";
        int lastMarker = text.LastIndexOf(kindMarker, StringComparison.Ordinal);
        if (lastMarker < 0)
        {
            return found;
        }

        // Unclosed braces each walk to the end, quadratic on a minified paste: past this budget the reply holds whatever was found.
        long budget = 8L * text.Length + (1L << 20);
        int index = 0;
        while (index <= lastMarker && (index = text.IndexOf('{', index)) >= 0 && index <= lastMarker)
        {
            int close = MatchingBrace(text, index, ref budget);
            if (budget <= 0)
            {
                break;
            }
            if (close < 0)
            {
                index++;
                continue;
            }

            string candidate = text[index..(close + 1)];
            if (candidate.Contains(kindMarker, StringComparison.Ordinal))
            {
                found.Add(candidate);
                index = close + 1;
            }
            else
            {
                index++;
            }
        }

        return found;
    }

    private static int MatchingBrace(string text, int open, ref long budget)
    {
        int depth = 0;
        bool inString = false;
        for (int index = open; index < text.Length && budget-- > 0; index++)
        {
            char c = text[index];
            if (inString)
            {
                if (c == '\\')
                {
                    index++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }
                    break;
            }
        }

        return -1;
    }

    private static (AgentOperation? Operation, AgentRejectedOperation? Rejection) ReadOperation(
        JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, new AgentRejectedOperation(null, null, "An operation must be an object."));
        }

        string? id = element.TryGetProperty("id", out JsonElement idElement) &&
            idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
        if (!element.TryGetProperty("op", out JsonElement opElement) ||
            opElement.ValueKind != JsonValueKind.String)
        {
            return (null, new AgentRejectedOperation(id, null, "The operation names no 'op'."));
        }

        string op = opElement.GetString()!;
        try
        {
            AgentOperation? operation = op switch
            {
                AgentProtocol.SetGainDb => Map(element.Deserialize<GainWire>(Strict)),
                AgentProtocol.SetDelayMs => Map(element.Deserialize<DelayWire>(Strict)),
                AgentProtocol.SetPolarity => Map(element.Deserialize<PolarityWire>(Strict)),
                AgentProtocol.SetCrossover => Map(element.Deserialize<CrossoverWire>(Strict)),
                AgentProtocol.ReplacePeqBank => Map(element.Deserialize<PeqWire>(Strict)),
                AgentProtocol.RunAutoDelay => Map(element.Deserialize<AutoDelayWire>(Strict)),
                AgentProtocol.RunAutoCrossover => Map(element.Deserialize<AutoCrossoverWire>(Strict)),
                AgentProtocol.TuneJunction => Map(element.Deserialize<TuneJunctionWire>(Strict)),
                AgentProtocol.Probe => Map(element.Deserialize<ProbeWire>(Strict)),
                AgentProtocol.AutoTunePeq => Map(element.Deserialize<AutoTuneWire>(Strict)),
                AgentProtocol.UseSpatialAverage => Map(element.Deserialize<SpatialAverageWire>(Strict)),
                _ => null
            };
            if (operation == null)
            {
                return (null, new AgentRejectedOperation(
                    id, Shorten(op), $"Unsupported operation '{Shorten(op)}'."));
            }

            string? problem = CheckStrings(operation);
            return problem == null
                ? (operation, null)
                : (null, new AgentRejectedOperation(id, op, problem));
        }
        catch (JsonException exception)
        {
            return (null, new AgentRejectedOperation(
                id, op, "Not the shape the protocol describes: " + Shorten(exception.Message)));
        }
    }

    // Blank is missing, as for the summary. Only within-limit blanks collapse, so CheckStrings still refuses an over-limit one.
    private static string? Prose(string? value) =>
        string.IsNullOrWhiteSpace(value) && WithinLength(value) ? null : value;

    private static string? CheckStrings(AgentOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.Id) || !WithinLength(operation.Id))
        {
            return "The operation id is missing or too long.";
        }
        if (operation is AgentChannelOperation channel &&
            (string.IsNullOrWhiteSpace(channel.ChannelId) || !WithinLength(channel.ChannelId)))
        {
            return "The channel id is missing or too long.";
        }
        if (!WithinLength(operation.Reason))
        {
            return "The reason is too long.";
        }

        return operation switch
        {
            SetCrossoverOperation crossover =>
                CheckCrossover(crossover.ExpectedCurrent) ?? CheckCrossover(crossover.Proposed),
            ReplacePeqBankOperation peq when
                string.IsNullOrWhiteSpace(peq.ExpectedCurrentHash) ||
                !WithinLength(peq.ExpectedCurrentHash) ||
                peq.Proposed?.Bands == null ||
                peq.Proposed.Bands.Any(band => band == null || band.Type == null) =>
                "The PEQ bank is incomplete.",
            AutoTunePeqOperation tune when !WithinLength(tune.Source) =>
                "The auto-tune source is too long.",
            TuneJunctionOperation junction when
                string.IsNullOrWhiteSpace(junction.JunctionId) || !WithinLength(junction.JunctionId) =>
                "The junction id is missing or too long.",
            TuneJunctionOperation junction when
                junction.Families != null &&
                (junction.Families.Count > AgentProtocol.MaxListItems ||
                    junction.Families.Any(family => string.IsNullOrWhiteSpace(family) || !WithinLength(family))) =>
                "The junction's family list is incomplete or too long.",
            TuneJunctionOperation junction when
                junction.Slopes != null && junction.Slopes.Count > AgentProtocol.MaxListItems =>
                "The junction's slope list is too long.",
            ProbeOperation probe when
                string.IsNullOrWhiteSpace(probe.Probe) || !WithinLength(probe.Probe) =>
                "The probe is missing or too long.",
            ProbeOperation probe when !WithinLength(probe.JunctionId) =>
                "The junction id is too long.",
            ProbeOperation probe when
                probe.Series != null &&
                (probe.Series.Count > AgentProtocol.MaxListItems ||
                    probe.Series.Any(name => string.IsNullOrWhiteSpace(name) || !WithinLength(name))) =>
                "The probe's series list is incomplete or too long.",
            ProbeOperation probe when
                probe.ChannelIds != null &&
                (probe.ChannelIds.Count > AgentProtocol.MaxListItems ||
                    probe.ChannelIds.Any(id => string.IsNullOrWhiteSpace(id) || !WithinLength(id))) =>
                "The probe's channel list is incomplete or too long.",
            ProbeOperation probe when
                probe.Variants != null &&
                probe.Variants.Any(variant =>
                    !WithinLength(variant.Label) ||
                    variant.Changes == null ||
                    variant.Changes.Any(change =>
                        string.IsNullOrWhiteSpace(change.ChannelId) || !WithinLength(change.ChannelId) ||
                        CheckCrossover(change.Crossover) != null && change.Crossover != null ||
                        change.Peq?.Bands == null && change.Peq != null ||
                        change.Peq?.Bands.Any(band => band == null || band.Type == null) == true)) =>
                "A probe variant's changes are incomplete.",
            UseSpatialAverageOperation spatial when
                string.IsNullOrWhiteSpace(spatial.Mode) || !WithinLength(spatial.Mode) =>
                "The spatial average mode is missing or too long.",
            _ => null
        };
    }

    private static string? CheckCrossover(AgentCrossover? crossover) =>
        crossover?.Kind == null ||
        crossover.HighPass is { Family: null } ||
        crossover.LowPass is { Family: null }
            ? "The crossover spec is incomplete."
            : null;

    private static AgentOperation? Map(GainWire? wire) => wire == null
        ? null
        : new SetGainOperation(wire.Id, wire.ChannelId, Prose(wire.Reason), wire.ExpectedCurrent, wire.Proposed);

    private static AgentOperation? Map(DelayWire? wire) => wire == null
        ? null
        : new SetDelayOperation(wire.Id, wire.ChannelId, Prose(wire.Reason), wire.ExpectedCurrent, wire.Proposed);

    private static AgentOperation? Map(PolarityWire? wire) => wire == null
        ? null
        : new SetPolarityOperation(wire.Id, wire.ChannelId, Prose(wire.Reason), wire.ExpectedCurrent, wire.Proposed);

    private static AgentOperation? Map(CrossoverWire? wire) => wire == null
        ? null
        : new SetCrossoverOperation(
            wire.Id, wire.ChannelId, Prose(wire.Reason),
            Map(NotNull(wire.ExpectedCurrent, "expectedCurrent")),
            Map(NotNull(wire.Proposed, "proposed")));

    private static AgentCrossover Map(CrossoverSpecWire wire) =>
        new(wire.Kind, Map(wire.HighPass), Map(wire.LowPass));

    // `required` does not catch a JSON null; throw the strict reader's exception.
    private static T NotNull<T>(T? value, string member) where T : class =>
        value ?? throw new JsonException($"'{member}' is null.");

    private static AgentCrossoverEdge? Map(EdgeWire? wire) => wire == null
        ? null
        : new AgentCrossoverEdge(wire.Family, wire.FrequencyHz, wire.SlopeDbPerOctave, wire.RippleDb);

    private static AgentOperation? Map(PeqWire? wire)
    {
        if (wire == null)
        {
            return null;
        }

        PeqBankWire bank = NotNull(wire.Proposed, "proposed");
        List<PeqBandWire> bands = NotNull(bank.Bands, "proposed.bands");
        return new ReplacePeqBankOperation(
            wire.Id, wire.ChannelId, Prose(wire.Reason), wire.ExpectedCurrentHash,
            new AgentPeqBank(
                bank.PreampDb,
                bands
                    .Select(band => NotNull(band, "proposed.bands[]"))
                    .Select(band => new AgentPeqBand(band.Type, band.FrequencyHz, band.Q, band.GainDb))
                    .ToList()));
    }

    private static AgentOperation? Map(AutoDelayWire? wire) => wire == null
        ? null
        : new RunAutoDelayOperation(
            wire.Id, Prose(wire.Reason), wire.SceneOffsetMs, wire.RightHandDrive, wire.AdjustGains,
            wire.NearSideCutDb, wire.RearFillOffsetMs);

    private static AgentOperation? Map(AutoCrossoverWire? wire) => wire == null
        ? null
        : new RunAutoCrossoverOperation(wire.Id, Prose(wire.Reason));

    private static AgentOperation? Map(TuneJunctionWire? wire) => wire == null
        ? null
        : new TuneJunctionOperation(
            wire.Id, Prose(wire.Reason), wire.JunctionId, wire.MinHz, wire.MaxHz,
            wire.Families, wire.Slopes, wire.IndependentSlopes);

    // `"variants": [null]` would otherwise escape as an uncaught NullReferenceException instead of one rejected operation.
    private static AgentOperation? Map(ProbeWire? wire) => wire == null
        ? null
        : new ProbeOperation(
            wire.Id,
            Prose(wire.Reason),
            wire.Probe,
            wire.JunctionId,
            wire.Variants?.Select(item =>
            {
                ProbeVariantWire variant = NotNull(item, "variants[]");
                return new AgentProbeVariant(
                    variant.Label,
                    (variant.Changes ?? []).Select(entry =>
                    {
                        ProbeChangeWire change = NotNull(entry, "changes[]");
                        return new AgentProbeChange(
                            change.ChannelId,
                            change.GainDb,
                            change.DelayMs,
                            change.InvertPolarity,
                            change.Crossover == null ? null : Map(change.Crossover),
                            change.Peq == null
                                ? null
                                : new AgentPeqBank(
                                    change.Peq.PreampDb,
                                    NotNull(change.Peq.Bands, "peq.bands")
                                        .Select(band => Map(NotNull(band, "peq.bands[]")))
                                        .ToList()));
                    }).ToList());
            }).ToList(),
            wire.Series,
            wire.ChannelIds,
            wire.PointsPerOctave,
            wire.Rows);

    private static AgentPeqBand Map(PeqBandWire wire) =>
        new(wire.Type, wire.FrequencyHz, wire.Q, wire.GainDb);

    private static AgentOperation? Map(AutoTuneWire? wire) => wire == null
        ? null
        : new AutoTunePeqOperation(
            wire.Id, wire.ChannelId, Prose(wire.Reason), wire.TargetLevelDb, wire.MinHz, wire.MaxHz,
            wire.AllowShelves, wire.CutsOnly, wire.Boosts, wire.Source);

    private static AgentOperation? Map(SpatialAverageWire? wire) => wire == null
        ? null
        : new UseSpatialAverageOperation(wire.Id, Prose(wire.Reason), wire.Mode, wire.Hybrid);

    private static bool WithinLength(string? value) =>
        value == null || value.Length <= AgentProtocol.MaxStringLength;

    private static bool IsWebUrl(string? url) =>
        url != null && WithinLength(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static int Count(string text, string marker)
    {
        int count = 0;
        for (int index = text.IndexOf(marker, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // Serializer messages can quote a whole paragraph into the message box.
    private static string Shorten(string? text)
    {
        const int limit = 160;
        if (text == null)
        {
            return string.Empty;
        }

        text = text.ReplaceLineEndings(" ");
        return text.Length <= limit ? text : text[..limit] + "…";
    }

    // Wire shapes: `required` rejects missing members, strict options reject extra ones; `extensions` is the one ignored open door.
    private sealed class ProposalWire
    {
        public required string Kind { get; init; }
        public required int ProtocolVersion { get; init; }
        public string? PackageId { get; init; }
        // Wanted, not required: refusing a reply over missing prose costs a chat round trip; the review says none was given.
        public string? Summary { get; init; }
        public List<string>? Advice { get; init; }
        public List<SourceWire>? Sources { get; init; }
        public required List<JsonElement> Operations { get; init; }
        public JsonElement? Extensions { get; init; }
    }

    private sealed class SourceWire
    {
        public required string Url { get; init; }
        public string? Title { get; init; }
        public List<string>? FactsUsed { get; init; }
    }

    private abstract class OperationWire
    {
        public required string Op { get; init; }
        public required string Id { get; init; }
        public string? Reason { get; init; }
        public JsonElement? Extensions { get; init; }
    }

    // A channel id is required, never guessed.
    private abstract class ChannelOperationWire : OperationWire
    {
        public required string ChannelId { get; init; }
    }

    private sealed class GainWire : ChannelOperationWire
    {
        public required double ExpectedCurrent { get; init; }
        public required double Proposed { get; init; }
    }

    private sealed class DelayWire : ChannelOperationWire
    {
        public required double ExpectedCurrent { get; init; }
        public required double Proposed { get; init; }
    }

    private sealed class PolarityWire : ChannelOperationWire
    {
        public required bool ExpectedCurrent { get; init; }
        public required bool Proposed { get; init; }
    }

    private sealed class CrossoverWire : ChannelOperationWire
    {
        public required CrossoverSpecWire ExpectedCurrent { get; init; }
        public required CrossoverSpecWire Proposed { get; init; }
    }

    private sealed class CrossoverSpecWire
    {
        public required string Kind { get; init; }
        public EdgeWire? HighPass { get; init; }
        public EdgeWire? LowPass { get; init; }
    }

    private sealed class EdgeWire
    {
        public required string Family { get; init; }
        public required double FrequencyHz { get; init; }
        public required int SlopeDbPerOctave { get; init; }
        public double? RippleDb { get; init; }
    }

    private sealed class PeqWire : ChannelOperationWire
    {
        public required string ExpectedCurrentHash { get; init; }
        public required PeqBankWire Proposed { get; init; }
    }

    private sealed class PeqBankWire
    {
        public required double PreampDb { get; init; }
        public required List<PeqBandWire> Bands { get; init; }
    }

    private sealed class PeqBandWire
    {
        public required string Type { get; init; }
        public required double FrequencyHz { get; init; }
        public required double Q { get; init; }
        public required double GainDb { get; init; }
    }

    // An absent optional input means the panel's default, so null and missing read the same.
    private sealed class AutoDelayWire : OperationWire
    {
        public double? SceneOffsetMs { get; init; }
        public bool? RightHandDrive { get; init; }
        public bool? AdjustGains { get; init; }
        public double? NearSideCutDb { get; init; }
        public double? RearFillOffsetMs { get; init; }
    }

    private sealed class AutoCrossoverWire : OperationWire
    {
    }

    private sealed class TuneJunctionWire : OperationWire
    {
        public required string JunctionId { get; init; }
        public double? MinHz { get; init; }
        public double? MaxHz { get; init; }
        public List<string>? Families { get; init; }
        public List<int>? Slopes { get; init; }
        public bool? IndependentSlopes { get; init; }
    }

    private sealed class ProbeWire : OperationWire
    {
        public required string Probe { get; init; }
        public string? JunctionId { get; init; }
        public List<ProbeVariantWire>? Variants { get; init; }
        public List<string>? Series { get; init; }
        public List<string>? ChannelIds { get; init; }
        public int? PointsPerOctave { get; init; }
        public int? Rows { get; init; }
    }

    private sealed class ProbeVariantWire
    {
        public string? Label { get; init; }
        public required List<ProbeChangeWire> Changes { get; init; }
    }

    // Optional: what a variant leaves out, the channel keeps.
    private sealed class ProbeChangeWire
    {
        public required string ChannelId { get; init; }
        public double? GainDb { get; init; }
        public double? DelayMs { get; init; }
        public bool? InvertPolarity { get; init; }
        public CrossoverSpecWire? Crossover { get; init; }
        public PeqBankWire? Peq { get; init; }
    }

    private sealed class AutoTuneWire : ChannelOperationWire
    {
        public double? TargetLevelDb { get; init; }
        public double? MinHz { get; init; }
        public double? MaxHz { get; init; }
        public bool? AllowShelves { get; init; }
        public bool? CutsOnly { get; init; }
        public string? Boosts { get; init; }
        public string? Source { get; init; }
    }

    private sealed class SpatialAverageWire : OperationWire
    {
        public required string Mode { get; init; }
        public required bool Hybrid { get; init; }
    }
}

internal sealed record AgentProposalParseResult(AgentProposal? Proposal, string? Error)
{
    public bool Succeeded => Proposal != null;

    public static AgentProposalParseResult Success(AgentProposal proposal) => new(proposal, null);

    public static AgentProposalParseResult Fail(string error) => new(null, error);
}
