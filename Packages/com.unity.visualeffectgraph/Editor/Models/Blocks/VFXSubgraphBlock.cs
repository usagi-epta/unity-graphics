using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.VFX.Block;

namespace UnityEditor.VFX
{
    [VFXHelpURL("Subgraph")]
    [VFXInfo(name = "Empty Subgraph Block")]
    class VFXSubgraphBlock : VFXBlock
    {
        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField]
        protected VisualEffectSubgraphBlock m_Subgraph;

        [NonSerialized]
        VFXModel[] m_SubChildren;

        [NonSerialized]
        VFXBlock[] m_SubBlocks;
        VFXGraph m_UsedSubgraph;

        public VisualEffectSubgraphBlock subgraph => m_Subgraph;

        public override void GetImportDependentAssets(HashSet<int> dependencies)
        {
            base.GetImportDependentAssets(dependencies);
            if (!object.ReferenceEquals(m_Subgraph, null))
            {
                dependencies.Add(m_Subgraph.GetInstanceID());
            }
        }

        public override void CheckGraphBeforeImport()
        {
            base.CheckGraphBeforeImport();

            // If the graph is reimported it can be because one of its dependency such as the subgraphs, has been changed.
            if (!VFXGraph.explicitCompile)
            {
                ResyncSlots(true);
                ResyncCustomAttributes();
                Invalidate(InvalidationCause.kUIChangedTransient); // if a subgraph block has changed, we need to update it's visual valid state
            }
        }

        public sealed override string name => m_Subgraph != null ? ObjectNames.NicifyVariableName(m_Subgraph.name) : "Empty Subgraph Block";

        protected override IEnumerable<VFXPropertyWithValue> inputProperties
        {
            get
            {
                if (m_isInOnEnable) // Recreate copy cannot be called in OnEnable because the subgraph my not have been enabled itself so in OnEnable send back the previous input properties
                {
                    if (subgraph != null)
                    {
                        foreach (var inputSlot in inputSlots)
                            yield return new VFXPropertyWithValue(inputSlot.property, inputSlot.value);
                    }
                }
                else
                {
                    if (m_Subgraph == null && !object.ReferenceEquals(m_Subgraph, null))
                        m_Subgraph = EditorUtility.EntityIdToObject(m_Subgraph.GetEntityId()) as VisualEffectSubgraphBlock;
                    if (m_SubChildren == null && subgraph != null) // if the subasset exists but the subchildren has not been recreated yet, return the existing slots
                        RecreateCopy();

                    foreach (var param in GetParameters(InputPredicate).OrderBy(t => t.order))
                    {
                        yield return VFXSubgraphUtility.GetPropertyFromInputParameter(param);
                    }
                }
            }
        }

        static bool InputPredicate(VFXParameter param)
        {
            return param.exposed && !param.isOutput;
        }

        IEnumerable<VFXParameter> GetParameters(Func<VFXParameter, bool> predicate)
        {
            if (m_SubChildren == null) return Enumerable.Empty<VFXParameter>();
            return m_SubChildren.OfType<VFXParameter>().Where(predicate).OrderBy(t => t.order);
        }

        bool m_isInOnEnable;
        private new void OnEnable()
        {
            m_isInOnEnable = true;
            base.OnEnable();
            m_isInOnEnable = false;
        }

        void SubChildrenOnInvalidate(VFXModel model, InvalidationCause cause)
        {
            Invalidate(this, cause);
        }

        public override IEnumerable<VFXAttribute> usedAttributes => m_SubChildren?.OfType<IVFXAttributeUsage>().SelectMany(x => x.usedAttributes) ?? Array.Empty<VFXAttribute>();

        public override IEnumerable<VFXAttributeInfo> attributes
        {
            get
            {
                if (m_SubBlocks != null)
                {
                    foreach (var block in m_SubBlocks)
                    {
                        foreach (var attribute in block.attributes)
                            yield return attribute;
                    }
                }
            }
        }

        public void RecreateCopy()
        {
            if (m_SubChildren != null)
            {
                foreach (var child in m_SubChildren)
                {
                    if (child != null)
                    {
                        child.onInvalidateDelegate -= SubChildrenOnInvalidate;
                        ScriptableObject.DestroyImmediate(child, true);
                    }
                }
            }

            if (subgraph == null)
            {
                m_SubChildren = null;
                m_SubBlocks = null;
                m_UsedSubgraph = null;
                return;
            }

            var graph = m_Subgraph.GetResource().GetOrCreateGraph();
            HashSet<ScriptableObject> dependencies = new HashSet<ScriptableObject>();

            var context = graph.children.OfType<VFXBlockSubgraphContext>().FirstOrDefault();

            if (context == null)
            {
                m_SubChildren = null;
                m_SubBlocks = null;
                m_UsedSubgraph = null;
                return;
            }

            foreach (var child in graph.children.Where(t => t is VFXOperator || t is VFXParameter))
            {
                dependencies.Add(child);
                child.CollectDependencies(dependencies);
            }

            foreach (var block in context.children)
            {
                dependencies.Add(block);
                block.CollectDependencies(dependencies);
            }

            var copy = VFXMemorySerializer.DuplicateObjects(dependencies.ToArray());
            m_UsedSubgraph = graph;
            m_UsedSubgraph.SyncCustomAttributes();
            m_SubChildren = copy.OfType<VFXModel>().Where(t => t is VFXBlock || t is VFXOperator || t is VFXParameter).ToArray();
            m_SubBlocks = m_SubChildren.OfType<VFXBlock>().ToArray();
            foreach (var child in m_SubChildren)
            {
                child.CheckGraphBeforeImport();
                child.onInvalidateDelegate += SubChildrenOnInvalidate;
            }

            foreach (var child in copy)
            {
                child.hideFlags = HideFlags.HideAndDontSave;
            }

            foreach (var subgraphBlocks in m_SubBlocks.OfType<VFXSubgraphBlock>())
                subgraphBlocks.RecreateCopy();
            SyncSlots(VFXSlot.Direction.kInput, true);
            ResyncCustomAttributes();
            if (GetGraph() is { } mainGraph)
            {
                mainGraph.SyncCustomAttributes();
            }
        }

        public void PatchInputExpressions()
        {
            if (m_SubChildren == null) return;

            var inputExpressions = new List<VFXExpression>();

            foreach (var slot in inputSlots.SelectMany(t => t.GetExpressionSlots()))
            {
                inputExpressions.Add(slot.GetExpression());
            }

            VFXSubgraphUtility.TransferExpressionToParameters(inputExpressions, GetParameters(t => VFXSubgraphUtility.InputPredicate(t)).OrderBy(t => t.order));
        }

        public VFXModel[] subChildren => m_SubChildren;

        public IEnumerable<VFXBlock> recursiveSubBlocks
        {
            get
            {
                return m_SubBlocks == null || !isActive ? Enumerable.Empty<VFXBlock>() : (m_SubBlocks.SelectMany(t => t is VFXSubgraphBlock ? (t as VFXSubgraphBlock).recursiveSubBlocks : Enumerable.Repeat(t, 1)));
            }
        }
        public override bool isValid
        {
            get
            {
                if (m_Subgraph == null)
                    return true;

                VFXGraph subGraph = m_Subgraph.GetResource().GetOrCreateGraph();
                VFXBlockSubgraphContext blockContext = subGraph.children.OfType<VFXBlockSubgraphContext>().First();
                VFXContext parent = GetParent();
                if (parent == null)
                    return true;
                if (blockContext == null)
                    return false;

                return base.isValid;
            }
        }

        public override VFXContextType compatibleContexts { get { return (subgraph != null) ? subgraph.GetResource().GetOrCreateGraph().children.OfType<VFXBlockSubgraphContext>().First().compatibleContextType : VFXContextType.All; } }
        public override VFXDataType compatibleData { get { return (subgraph != null) ? subgraph.GetResource().GetOrCreateGraph().children.OfType<VFXBlockSubgraphContext>().First().ownedType : VFXDataType.Particle | VFXDataType.SpawnEvent; } }

        public override void CollectDependencies(HashSet<ScriptableObject> objs, bool ownedOnly = true)
        {
            base.CollectDependencies(objs, ownedOnly);

            if (m_SubChildren == null || ownedOnly)
                return;

            foreach (var child in m_SubChildren)
            {
                if (!(child is VFXParameter))
                {
                    objs.Add(child);

                    if (child is VFXModel)
                        (child as VFXModel).CollectDependencies(objs, false);
                }
            }
        }

        public void SetSubblocksFlattenedParent()
        {
            VFXContext parent = GetParent();
            foreach (var block in recursiveSubBlocks)
                block.flattenedParent = parent;
        }

        protected internal override void Invalidate(VFXModel model, InvalidationCause cause)
        {
            if (cause == InvalidationCause.kSettingChanged)
            {
                var graph = GetGraph();

                if (graph != null && subgraph != null && m_Subgraph.GetResource() != null)
                {
                    var otherGraph = m_Subgraph.GetResource().GetOrCreateGraph();
                    if (otherGraph == graph || otherGraph.subgraphDependencies.Contains(graph.GetResource().visualEffectObject))
                        m_Subgraph = null; // prevent cyclic dependencies.
                    if (otherGraph != m_UsedSubgraph)
                        RecreateCopy();
                    if (graph.GetResource().isSubgraph) // BuildSubgraphDependencies is called for vfx by recompilation, but in subgraph we must call it explicitely
                        graph.BuildSubgraphDependencies();
                }
                else if (m_UsedSubgraph != null)
                    RecreateCopy();
            }
            else if (cause == InvalidationCause.kParamChanged || cause == InvalidationCause.kExpressionValueInvalidated)
            {
                VFXSlot slot = model as VFXSlot;
                if (slot != null && slot.IsMasterSlot())
                {
                    int slotIndex = GetSlotIndex(slot);
                    if (slotIndex >= 0) // Not a toggle slot
                    {
                        var parameter = m_SubChildren.OfType<VFXParameter>().FirstOrDefault(m => m.order == slotIndex);
                        if (parameter != null)
                            parameter.GetOutputSlot(0).Invalidate(InvalidationCause.kExpressionValueInvalidated); // Propagate value change event to subblock parameter
                    }
                }
            }

            base.Invalidate(model, cause);
        }

        protected override void OnAdded()
        {
            base.OnAdded();
            ResyncCustomAttributes();
        }

        private void ResyncCustomAttributes()
        {
            var graph = GetGraph();
            if (graph == null || m_SubChildren == null)
            {
                return;
            }

            foreach (var customAttribute in usedAttributes)
            {
                if (!graph.attributesManager.Exist(customAttribute.name))
                {
                    graph.TryAddCustomAttribute(customAttribute.name, customAttribute.type, customAttribute.description, true, out _);
                }
                else
                {
                    graph.TryUpdateCustomAttribute(customAttribute.name, CustomAttributeUtility.GetSignature(customAttribute.type), customAttribute.description, true);
                }
                graph.SetCustomAttributeDirty();
            }
        }
    }
}
