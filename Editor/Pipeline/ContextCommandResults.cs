using System.Collections.Generic;

namespace PILAR.Context.Pipeline
{
    // Response types for the context_* commands.
    //
    // Public fields rather than properties, matching ContextTargetInfo, which already round-trips
    // through the CLI serializer. Field names and their order are the CLI's wire contract - agents and
    // the shipped skills read these keys by name, so renaming or reordering one is a breaking change.
    // The 1.2 rename (unityPath -> scenePath, plcPath -> topologyPath, plcLinked and hierarchyRole
    // folded into metadata) was exactly that, which is why the skills ship in the same release.
    //
    // Where a command has two genuinely different responses (flat vs nested, wrote vs skipped) each
    // gets its own type rather than one union with half the fields left null: the commands already
    // returned different shapes, and modelling that honestly keeps the emitted JSON identical.

    /// <summary>context_tree with flat = true.</summary>
    public class ContextTreeFlatResult
    {
        public string root;
        public string scope;
        public int count;
        public List<ContextTargetInfo> targets;
    }

    /// <summary>context_tree with flat = false.</summary>
    public class ContextTreeNestedResult
    {
        public string root;
        public string scope;
        public int count;
        public ContextTargetInfo tree;
    }

    /// <summary>One context entry as returned by context_get.</summary>
    public class ContextEntryDto
    {
        public string key;
        public string value;
    }

    public class ContextGetResult
    {
        public string name;
        public string scenePath;
        public string topologyPath;
        public int tier;
        public string tierName;
        public ContextEntryDto[] metadata;
        public string[] components;
        public bool hasNode;
        public string prefabAsset;
        public ContextEntryDto[] entries;
    }

    /// <summary>Coverage counts for one tier.</summary>
    public class TierSummary
    {
        public int tier;
        public string tierName;
        public int total;
        public int withNode;
        public int nonEmpty;
    }

    /// <summary>
    /// Coverage only, and deliberately framework-neutral: how much of the twin is annotated and what
    /// is still missing. Facts that belong to a twin framework live in each target's metadata, which
    /// context_tree returns — this command does not aggregate over a vocabulary it does not own.
    /// </summary>
    public class ContextAuditResult
    {
        public string root;
        public string scope;
        public int total;
        public int withNode;
        public int nonEmpty;
        public List<TierSummary> byTier;
        public string[] missingNode;
        public string[] emptyNode;
    }

    public class ContextSetResult
    {
        public string target;
        public string topologyPath;
        public string tierName;
        public string wroteTo;
        public string[] applied;
        public int totalEntries;
    }

    public class ContextRemoveResult
    {
        public string target;
        public string wroteTo;
        public string key;
        public bool removed;
        public int totalEntries;
    }

    /// <summary>context_remove against a target that carries no ContextNode at all.</summary>
    public class ContextRemoveSkippedResult
    {
        public string target;
        public bool removed;
        public string reason;
    }

    /// <summary>Shared by context_set and context_remove when writing to the backing prefab asset.</summary>
    public class ContextPrefabWriteResult
    {
        public string target;
        public string topologyPath;
        public string tierName;
        public string wroteTo;
        public string prefabAsset;
        public string prefabChild;
        public string[] applied;
        public int totalEntries;
    }

    public class ContextEnsureResult
    {
        public string root;
        public string scope;
        public string mode;
        public bool dryRun;
        public int missingTotal;
        public int sceneCandidates;
        public int prefabSlots;
        public string[] addedScene;
        public string[] addedPrefab;
        public string[] skipped;
    }
}
