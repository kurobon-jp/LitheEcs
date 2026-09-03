#if !RELEASE && !DISABLE_LITHEECS_DIAGNOSTICS
#define _INTERNAL_DERIVED_USE_DIAGNOSTICS
#endif

#nullable enable

using System.Text;

namespace LitheEcs
{
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
    /// <summary>Allocation-prone cold paths observed since the last reset. Capturing this value does not allocate.</summary>
    public readonly struct AllocationDiagnosticsSnapshot
    {
        internal AllocationDiagnosticsSnapshot(AllocationDiagnostics c)
        {
            CommandBufferGrowths = c.CommandBufferGrowths; CommandPayloadGrowths = c.CommandPayloadGrowths;
            DeferredEntityBufferGrowths = c.DeferredEntityBufferGrowths; BatchEntityBufferGrowths = c.BatchEntityBufferGrowths;
            ComponentBufferRegistryGrowths = c.ComponentBufferRegistryGrowths; ComponentBufferCreations = c.ComponentBufferCreations;
            ArchetypeCreations = c.ArchetypeCreations; TransitionCreations = c.TransitionCreations;
            CopyPlanCreations = c.CopyPlanCreations; ChunkCreations = c.ChunkCreations; ChunkActivations = c.ChunkActivations;
            EntityPageAllocations = c.EntityPageAllocations; ComponentPageAllocations = c.ComponentPageAllocations;
            QueryPlanCreations = c.QueryPlanCreations; QueryMatchListGrowths = c.QueryMatchListGrowths;
            LastChunkArchetypeIndex = c.LastChunkArchetypeIndex;
            LastChunkEntityCount = c.LastChunkEntityCount;
            LastComponentPageTypeId = c.LastComponentPageTypeId;
            LastCommandPayloadTypeId = c.LastCommandPayloadTypeId;
        }
        public int CommandBufferGrowths { get; }
        public int CommandPayloadGrowths { get; }
        public int DeferredEntityBufferGrowths { get; }
        public int BatchEntityBufferGrowths { get; }
        public int ComponentBufferRegistryGrowths { get; }
        public int ComponentBufferCreations { get; }
        public int ArchetypeCreations { get; }
        public int TransitionCreations { get; }
        public int CopyPlanCreations { get; }
        public int ChunkCreations { get; }
        public int ChunkActivations { get; }
        public int EntityPageAllocations { get; }
        public int ComponentPageAllocations { get; }
        public int QueryPlanCreations { get; }
        public int QueryMatchListGrowths { get; }
        /// <summary>Archetype index for the most recent unreserved Chunk creation, or -1.</summary>
        public int LastChunkArchetypeIndex { get; }
        /// <summary>Entity count immediately before the most recent unreserved Chunk creation.</summary>
        public int LastChunkEntityCount { get; }
        /// <summary>Component type ID for the most recent Component Page allocation, or -1.</summary>
        public int LastComponentPageTypeId { get; }
        /// <summary>Component type ID for the most recent command payload list growth, or -1.</summary>
        public int LastCommandPayloadTypeId { get; }
        public int TotalEvents => CommandBufferGrowths + CommandPayloadGrowths + DeferredEntityBufferGrowths
            + BatchEntityBufferGrowths + ComponentBufferRegistryGrowths + ComponentBufferCreations + ArchetypeCreations
            + TransitionCreations + CopyPlanCreations + ChunkCreations + EntityPageAllocations
            + ComponentPageAllocations + QueryPlanCreations + QueryMatchListGrowths;

        public bool HasEvents => TotalEvents != 0;

        /// <summary>Formats only non-zero counters. Calling this method allocates the resulting string.</summary>
        public override string ToString()
        {
            if (!HasEvents) return "LitheEcs allocation diagnostics: none";
            var builder = new StringBuilder(256);
            builder.Append("LitheEcs allocation diagnostics:");
            Append(builder, nameof(CommandBufferGrowths), CommandBufferGrowths);
            Append(builder, nameof(CommandPayloadGrowths), CommandPayloadGrowths);
            Append(builder, nameof(DeferredEntityBufferGrowths), DeferredEntityBufferGrowths);
            Append(builder, nameof(BatchEntityBufferGrowths), BatchEntityBufferGrowths);
            Append(builder, nameof(ComponentBufferRegistryGrowths), ComponentBufferRegistryGrowths);
            Append(builder, nameof(ComponentBufferCreations), ComponentBufferCreations);
            Append(builder, nameof(ArchetypeCreations), ArchetypeCreations);
            Append(builder, nameof(TransitionCreations), TransitionCreations);
            Append(builder, nameof(CopyPlanCreations), CopyPlanCreations);
            Append(builder, nameof(ChunkCreations), ChunkCreations);
            Append(builder, nameof(ChunkActivations), ChunkActivations);
            Append(builder, nameof(EntityPageAllocations), EntityPageAllocations);
            Append(builder, nameof(ComponentPageAllocations), ComponentPageAllocations);
            Append(builder, nameof(QueryPlanCreations), QueryPlanCreations);
            Append(builder, nameof(QueryMatchListGrowths), QueryMatchListGrowths);
            if (LastCommandPayloadTypeId >= 0 && LastCommandPayloadTypeId < ComponentTypeRegistry.Count)
            {
                var type = ComponentTypeRegistry.GetType(LastCommandPayloadTypeId);
                builder.Append(" CommandPayloadLastType=").Append(type.FullName ?? type.Name);
            }
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string name, int value)
        {
            if (value != 0) builder.Append(' ').Append(name).Append('=').Append(value);
        }
    }

    internal sealed class AllocationDiagnostics
    {
        internal bool Enabled;
        internal int CommandBufferGrowths, CommandPayloadGrowths, DeferredEntityBufferGrowths, BatchEntityBufferGrowths;
        internal int ComponentBufferRegistryGrowths, ComponentBufferCreations, ArchetypeCreations, TransitionCreations;
        internal int CopyPlanCreations, ChunkCreations, ChunkActivations, EntityPageAllocations, ComponentPageAllocations;
        internal int QueryPlanCreations, QueryMatchListGrowths;
        internal int LastChunkArchetypeIndex = -1, LastChunkEntityCount, LastComponentPageTypeId = -1;
        internal int LastCommandPayloadTypeId = -1;
        internal void Reset()
        {
            CommandBufferGrowths = CommandPayloadGrowths = DeferredEntityBufferGrowths = BatchEntityBufferGrowths = 0;
            ComponentBufferRegistryGrowths = ComponentBufferCreations = ArchetypeCreations = TransitionCreations = 0;
            CopyPlanCreations = ChunkCreations = ChunkActivations = EntityPageAllocations = ComponentPageAllocations = 0;
            QueryPlanCreations = QueryMatchListGrowths = 0;
            LastChunkArchetypeIndex = LastComponentPageTypeId = LastCommandPayloadTypeId = -1;
            LastChunkEntityCount = 0;
        }
    }
#endif
}
