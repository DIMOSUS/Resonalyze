using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>
/// Turns the text of an assistant's reply into an <see cref="AgentProposal"/>, or
/// into one plain sentence saying why it could not. Everything on the clipboard is
/// untrusted: the reply is searched for exactly one marked block, the block is read
/// with a serializer that forgives nothing (no comments, no trailing commas, no
/// named floating-point literals, no unknown properties, no case games), and each
/// operation object is understood on its own so one bad object costs one row of
/// the review rather than the whole reply.
/// </summary>
internal static class AgentProposalParser
{
    // Deliberately NOT the options the session loader uses: those tolerate a
    // hand-edited file, and tolerance is the one thing a reply must not get.
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
        if (string.IsNullOrWhiteSpace(wire.Summary))
        {
            return AgentProposalParseResult.Fail("The proposal has no summary.");
        }
        // `required` only requires the member to be PRESENT; a JSON null passes it.
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
            wire.Summary,
            advice,
            sources,
            operations,
            rejected));
    }

    // Exactly one begin and one end, in that order, with something between them.
    // Two blocks are not "take the last one": an assistant that wrote two was
    // asked for one, and guessing which it meant is how the wrong tune gets applied.
    // The proposal is the one JSON object in the reply whose "kind" names it. A
    // chat pastes the object inside a Markdown fence and puts anything around it;
    // the earlier envelope of BEGIN/END markers is still read when a reply
    // carries it, since assistants keep copying what they saw work — but a chat
    // that set the markers OUTSIDE the block it offers to copy is why the object
    // now identifies itself.
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

    // A fenced block is what most chat UIs copy; the fence is not part of the JSON.
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

    // Every top-level JSON object in the text that names the proposal kind. A
    // brace scanner that knows JSON strings (so a brace inside a reason does not
    // end an object) walks each candidate from its opening brace; an object
    // that never closes, or does not name the kind, is prose and skipped, and
    // the walk resumes after the candidate so nested objects are not counted
    // twice.
    private static List<string> ProposalObjects(string text)
    {
        var found = new List<string>();
        string kindMarker = "\"" + AgentProtocol.ProposalKind + "\"";
        // No object opened after the last mention of the kind can contain it.
        int lastMarker = text.LastIndexOf(kindMarker, StringComparison.Ordinal);
        if (lastMarker < 0)
        {
            return found;
        }

        // Each brace that never closes is walked to the end of the text, and a
        // paste full of them (a minified script, say) would be quadratic. The
        // walk gets a budget generous for any reply and small for such a paste;
        // past it the reply reads as holding whatever was found by then.
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
        if (operation.Reason == null || !WithinLength(operation.Reason))
        {
            return "The reason is missing or too long.";
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
        : new SetGainOperation(wire.Id, wire.ChannelId, wire.Reason, wire.ExpectedCurrent, wire.Proposed);

    private static AgentOperation? Map(DelayWire? wire) => wire == null
        ? null
        : new SetDelayOperation(wire.Id, wire.ChannelId, wire.Reason, wire.ExpectedCurrent, wire.Proposed);

    private static AgentOperation? Map(PolarityWire? wire) => wire == null
        ? null
        : new SetPolarityOperation(wire.Id, wire.ChannelId, wire.Reason, wire.ExpectedCurrent, wire.Proposed);

    private static AgentOperation? Map(CrossoverWire? wire) => wire == null
        ? null
        : new SetCrossoverOperation(
            wire.Id, wire.ChannelId, wire.Reason,
            Map(NotNull(wire.ExpectedCurrent, "expectedCurrent")),
            Map(NotNull(wire.Proposed, "proposed")));

    private static AgentCrossover Map(CrossoverSpecWire wire) =>
        new(wire.Kind, Map(wire.HighPass), Map(wire.LowPass));

    // A JSON null in a required object: `required` does not catch it, so the
    // mapper does, with the same exception the strict reader would have thrown.
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
            wire.Id, wire.ChannelId, wire.Reason, wire.ExpectedCurrentHash,
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
            wire.Id, wire.Reason, wire.SceneOffsetMs, wire.RightHandDrive, wire.AdjustGains,
            wire.NearSideCutDb, wire.RearFillOffsetMs);

    private static AgentOperation? Map(AutoCrossoverWire? wire) => wire == null
        ? null
        : new RunAutoCrossoverOperation(wire.Id, wire.Reason);

    private static AgentOperation? Map(TuneJunctionWire? wire) => wire == null
        ? null
        : new TuneJunctionOperation(
            wire.Id, wire.Reason, wire.JunctionId, wire.MinHz, wire.MaxHz,
            wire.Families, wire.Slopes, wire.IndependentSlopes);

    // Every nested element goes through NotNull: `"variants": [null]` and
    // `"changes": [null]` are valid JSON that `required` does not catch, and
    // dereferencing one would leave the parser with a NullReferenceException —
    // which nothing above catches — instead of one rejected operation.
    private static AgentOperation? Map(ProbeWire? wire) => wire == null
        ? null
        : new ProbeOperation(
            wire.Id,
            wire.Reason,
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
            }).ToList());

    private static AgentPeqBand Map(PeqBandWire wire) =>
        new(wire.Type, wire.FrequencyHz, wire.Q, wire.GainDb);

    private static AgentOperation? Map(AutoTuneWire? wire) => wire == null
        ? null
        : new AutoTunePeqOperation(
            wire.Id, wire.ChannelId, wire.Reason, wire.TargetLevelDb, wire.MinHz, wire.MaxHz,
            wire.AllowShelves, wire.CutsOnly, wire.Source);

    private static AgentOperation? Map(SpatialAverageWire? wire) => wire == null
        ? null
        : new UseSpatialAverageOperation(wire.Id, wire.Reason, wire.Mode, wire.Hybrid);

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

    // Error text is shown in a message box, and a serializer message quotes the
    // offending token — which may be a whole paragraph of the reply.
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

    // The wire shapes: what the JSON is allowed to contain, no more. `required`
    // makes a missing member a JsonException; the strict options make an extra
    // one a JsonException too. `extensions` is the one open door, for a future
    // additive field, and its content is ignored.
    private sealed class ProposalWire
    {
        public required string Kind { get; init; }
        public required int ProtocolVersion { get; init; }
        public string? PackageId { get; init; }
        public required string Summary { get; init; }
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
        public required string Reason { get; init; }
        public JsonElement? Extensions { get; init; }
    }

    // Everything but the three requests aimed at the whole project: a channel id
    // is required, and a reply that leaves it out is refused rather than aimed
    // at a guess.
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

    // The engine requests. An optional input that is absent means "what the panel
    // would open with", so a `null` and a missing member read the same; what is
    // `required` here is what the request cannot be understood without.
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

    // The junction is what the request cannot be understood without; every
    // choice about the search is the tuner's own where the reply leaves it out.
    private sealed class TuneJunctionWire : OperationWire
    {
        public required string JunctionId { get; init; }
        public double? MinHz { get; init; }
        public double? MaxHz { get; init; }
        public List<string>? Families { get; init; }
        public List<int>? Slopes { get; init; }
        public bool? IndependentSlopes { get; init; }
    }

    // A probe carries what its own kind needs and nothing else; the review holds
    // each kind to its fields, so a missing one is a reason rather than a
    // silently different reading.
    private sealed class ProbeWire : OperationWire
    {
        public required string Probe { get; init; }
        public string? JunctionId { get; init; }
        public List<ProbeVariantWire>? Variants { get; init; }
    }

    private sealed class ProbeVariantWire
    {
        public string? Label { get; init; }
        public required List<ProbeChangeWire> Changes { get; init; }
    }

    // The same five parameters a settings operation writes, all optional: what
    // a variant leaves out, the channel keeps.
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
        public string? Source { get; init; }
    }

    private sealed class SpatialAverageWire : OperationWire
    {
        public required string Mode { get; init; }
        public required bool Hybrid { get; init; }
    }
}

/// <summary>Either a proposal or one sentence saying why there is none.</summary>
internal sealed record AgentProposalParseResult(AgentProposal? Proposal, string? Error)
{
    public bool Succeeded => Proposal != null;

    public static AgentProposalParseResult Success(AgentProposal proposal) => new(proposal, null);

    public static AgentProposalParseResult Fail(string error) => new(null, error);
}
