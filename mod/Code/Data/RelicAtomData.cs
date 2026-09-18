using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AutoAnthonyRelics.Data;

/// <summary>
/// Data models for the relic atom pool and the hand-audited atom ledger.
///
/// The atom JSON is the extractor's output (research/relic_atoms.json). The
/// extractor has known systematic defects (first-number-as-value, default
/// self target, threshold-as-amount), so NOTHING from it is trusted on its
/// own: the ledger (research/relic_atom_ledger.json) is deny-by-default and
/// only atoms with a hand-verified "supported" entry reach the generator.
/// Ledger fixes carry the verified corrections (value/sign/target/condition).
/// </summary>
public sealed record RelicValueSlot
{
    public string Id { get; init; } = "";
    public int BaseValue { get; init; }
    public int Offset { get; init; }
    public bool Upgradable { get; init; }
}

public sealed record RelicConditionSpec
{
    public string Kind { get; init; } = "";
}

public sealed record RelicTriggerSpec
{
    public string Kind { get; init; } = "";
    public string? Lifetime { get; init; }
}

public sealed record RelicSpec
{
    public string Opcode { get; init; } = "";
    public string? Variant { get; init; }
    public string? Target { get; init; }
    public IReadOnlyList<RelicValueSlot> Values { get; init; } = Array.Empty<RelicValueSlot>();
    public RelicConditionSpec? Condition { get; init; }
    public RelicTriggerSpec? Trigger { get; init; }
}

public sealed record RelicAtom
{
    public string Id { get; init; } = "";
    public string Source { get; init; } = "";
    public string Rarity { get; init; } = "";
    public RelicSpec Spec { get; init; } = new();
}

public sealed record RelicAtomPool(IReadOnlyList<RelicAtom> Atoms)
{
    public static readonly RelicAtomPool Empty = new(Array.Empty<RelicAtom>());
}

public sealed record LedgerFix
{
    public string? Condition { get; init; }
    public string? Opcode { get; init; }
    public string? Variant { get; init; }
    public string? Target { get; init; }
    public string? Trigger { get; init; }
    public IReadOnlyDictionary<string, int>? Values { get; init; }
}

public sealed record LedgerEntry
{
    public string AtomId { get; init; } = "";
    public LedgerFix? Fix { get; init; }
    public string? Evidence { get; init; }
    public string? Note { get; init; }
}

public sealed record LedgerRejected
{
    public string AtomId { get; init; } = "";
    public string? Reason { get; init; }
    public string? Evidence { get; init; }
}

/// <summary>
/// The hand-audited ledger.
///
/// <paramref name="ExtraSupported"/> is the OPT-IN pool (user order 2026-09-18):
/// atoms ported from the sister mod QuriousCraftingRelics' extra catalog. They
/// are kept in their own list rather than merged into
/// <paramref name="Supported"/> so the two pools stay separable in code, in the
/// fingerprint and in the settings toggle - "which pool did this fragment come
/// from" must be answerable from data, not inferred from its opcode.
/// </summary>
public sealed record RelicLedger(
    IReadOnlyList<LedgerEntry> Supported,
    IReadOnlyList<LedgerRejected> Rejected,
    IReadOnlyList<LedgerEntry> ExtraSupported)
{
    public static readonly RelicLedger Empty = new(
        Array.Empty<LedgerEntry>(), Array.Empty<LedgerRejected>(), Array.Empty<LedgerEntry>());
}

/// <summary>
/// Loads the embedded atom pool + ledger. Embedded (not res://) so the
/// isolated probe can load them without Godot (touching Godot statics
/// outside the engine is a native access violation).
/// </summary>
public static class RelicAtomData
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static RelicAtomPool LoadAtoms()
    {
        string json = ReadEmbedded("relic_atoms.json");
        var parsed = System.Text.Json.JsonSerializer.Deserialize<RelicAtomFile>(json, JsonOptions);
        if (parsed?.Atoms is null)
        {
            throw new InvalidDataException("relic_atoms.json: missing atoms array");
        }
        return new RelicAtomPool(parsed.Atoms);
    }

    public static RelicLedger LoadLedger()
    {
        string json = ReadEmbedded("relic_atom_ledger.json");
        var parsed = System.Text.Json.JsonSerializer.Deserialize<RelicLedgerFile>(json, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidDataException("relic_atom_ledger.json: unparsable");
        }
        return new RelicLedger(
            parsed.Supported ?? Array.Empty<LedgerEntry>(),
            parsed.Rejected ?? Array.Empty<LedgerRejected>(),
            parsed.ExtraSupported ?? Array.Empty<LedgerEntry>());
    }

    private static string ReadEmbedded(string fileName)
    {
        Assembly assembly = typeof(RelicAtomData).Assembly;
        string fullName = assembly.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"embedded resource ending in {fileName} not found");
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

#pragma warning disable CS8632 // STJ parameter binding; records use nullable annotations on reference properties
    private sealed record RelicAtomFile
    {
        public string? Schema { get; init; }
        public int? AtomCount { get; init; }
        public IReadOnlyList<RelicAtom>? Atoms { get; init; }
    }

    private sealed record RelicLedgerFile
    {
        public string? Schema { get; init; }
        public IReadOnlyList<LedgerEntry>? Supported { get; init; }
        public IReadOnlyList<LedgerRejected>? Rejected { get; init; }
        public IReadOnlyList<LedgerEntry>? ExtraSupported { get; init; }
    }
#pragma warning restore CS8632
}
