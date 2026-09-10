using System.Collections.Immutable;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;

namespace SeqDoc.Analysis.Behavior;
/// <summary>
/// Normalizes one extracted method body into a method flow graph with nodes, edges, regions, loops,
/// and reconciled structural outcomes.
/// </summary>
public static class MethodFlowBuilder
{
    public static MethodFlowBuildResult Build(ExtractedMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var nodes = ImmutableArray.CreateBuilder<FlowNode>();
        var edges = ImmutableArray.CreateBuilder<FlowEdge>();
        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        var edgeOrdinal = 0;

        var operationsById = body.Operations.ToDictionary(operation => operation.Id);
        var blocksByOrdinal = body.Blocks.ToDictionary(block => block.Ordinal);
        var exitBlock = body.Blocks.FirstOrDefault(block => block.Terminal == ExtractedBlockTerminalKind.Exit);
        var preserveWorkerTerminalBlocks = HasSemaphoreCandidate(body)
            || HasCancellationCandidate(body)
            || HasNaturalLoopCatchShape(body);

        var entryId = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
            body.Method, "Entry", 0, 0, "entry"));
        var exitId = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
            body.Method, "Exit", int.MaxValue, int.MaxValue, "exit"));
        nodes.Add(new EntryFlowNode(entryId, body.Method, body.Evidence, CertaintyLevel.Exact));
        nodes.Add(new ExitFlowNode(exitId, body.Method, body.Evidence, CertaintyLevel.Exact));

        var blockHeads = new Dictionary<int, FlowNodeId>();
        var blockTails = new Dictionary<int, FlowNodeId>();

        foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
        {
            if (exitBlock is not null && block.Ordinal == exitBlock.Ordinal)
            {
                blockHeads[block.Ordinal] = exitId;
                blockTails[block.Ordinal] = exitId;
                continue;
            }

            FlowNodeId? firstNodeId = null;
            FlowNodeId? lastNodeId = null;
            var blockOperationIds = CollectBlockOperations(block, operationsById);
            foreach (var operationId in blockOperationIds)
            {
                if (!operationsById.TryGetValue(operationId, out var operation))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "BD2001",
                        "A block references an operation outside its own body.",
                        body.Method.Value,
                        block.Ordinal));
                    continue;
                }

                var node = CreateOperationNode(body.Method, operation, block.Ordinal, operationsById,
                    preserveWorkerTerminalBlocks);
                nodes.Add(node);
                firstNodeId ??= node.Id;
                if (lastNodeId is { } previous)
                {
                    edges.Add(CreateEdge(body.Method, previous, node.Id, FlowEdgeKind.Normal, null, ref edgeOrdinal));
                }

                lastNodeId = node.Id;
            }

            if (block.BranchCondition is { } conditionId)
            {
                var decisionId = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                    body.Method, "Decision", block.Ordinal, 0, "decision"));
                nodes.Add(new DecisionFlowNode(
                    decisionId,
                    body.Method,
                    conditionId,
                    block.Evidence,
                    CertaintyLevel.Exact));
                if (lastNodeId is { } prior)
                {
                    edges.Add(CreateEdge(body.Method, prior, decisionId, FlowEdgeKind.Normal, null, ref edgeOrdinal));
                }

                firstNodeId ??= decisionId;
                lastNodeId = decisionId;
            }

            var terminalNode = CreateTerminalNode(body.Method, block, operationsById, preserveWorkerTerminalBlocks);
            if (terminalNode is not null)
            {
                nodes.Add(terminalNode);
                if (lastNodeId is { } prior)
                {
                    var kind = terminalNode.Kind == FlowNodeKind.Return
                        ? FlowEdgeKind.Return
                        : FlowEdgeKind.Throw;
                    edges.Add(CreateEdge(body.Method, prior, terminalNode.Id, kind, null, ref edgeOrdinal));
                }

                firstNodeId ??= terminalNode.Id;
                lastNodeId = terminalNode.Id;
            }

            if (block.Ordinal == 0 && firstNodeId is null)
            {
                firstNodeId = entryId;
            }

            blockHeads[block.Ordinal] = firstNodeId ?? lastNodeId ?? entryId;
            blockTails[block.Ordinal] = lastNodeId ?? firstNodeId ?? entryId;
        }

        var dominators = ComputeDominators(body, blocksByOrdinal);
        AddBlockEdges(
            body,
            blocksByOrdinal,
            operationsById,
            blockHeads,
            blockTails,
            entryId,
            exitId,
            dominators,
            edges,
            ref edgeOrdinal,
            diagnostics);

        var regions = ImmutableArray.CreateBuilder<FlowRegion>();
        foreach (var region in body.Regions.OrderBy(region => region.Ordinal))
        {
            AddFlowRegion(body, region, blocksByOrdinal, blockHeads, blockTails, regions,
                HasSemaphoreCandidate(body) || HasCancellationCandidate(body) || HasNaturalLoopCatchShape(body));
        }

        var loopRegions = DetectLoops(
            body,
            blocksByOrdinal,
            operationsById,
            blockHeads,
            blockTails,
            nodes,
            regions,
            body.Evidence,
            ref edgeOrdinal,
            diagnostics);
        if (exitBlock is null)
        {
            diagnostics.Add(CreateDiagnostic(
                "BD2004",
                "The method flow has no exit block.",
                body.Method.Value,
                -1));
        }

        var catchContinuations = BuildCatchContinuations(body, nodes, diagnostics);
        var outcomes = ReconcileTerminals(body, blocksByOrdinal, blockTails, exitId, edges, regions, diagnostics,
            !catchContinuations.IsDefaultOrEmpty);
        var ordinaryBranches = HasSemaphoreCandidate(body) ? body.OrdinaryBranches : default;

        var preliminary = new MethodFlowSnapshot(
            body.Method,
            body.BodyFingerprint,
            nodes.OrderBy(node => node.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            edges.OrderBy(edge => edge.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            regions.OrderBy(region => region.Ordinal).ToImmutableArray(),
            outcomes.OrderBy(outcome => outcome.BlockOrdinal ?? -1).ToImmutableArray(),
            new LocalValueGraph([], []),
            [],
            null,
            diagnostics.ToImmutable(),
            string.Empty,
            catchContinuations.IsDefaultOrEmpty ? default : catchContinuations,
            ordinaryBranches);
        var (graph, dependences, summary) = LocalFlowAnalyzer.Analyze(body, preliminary);
        var snapshot = preliminary with
        {
            ValueGraph = graph,
            ControlDependences = dependences,
            Summary = summary,
        };
        return new MethodFlowBuildResult(
            snapshot with { FlowFingerprint = MethodFlowFingerprint.Compute(snapshot) },
            snapshot.Diagnostics);
    }

    private static bool HasSemaphoreCandidate(ExtractedMethodBody body)
        => body.Operations.Any(operation => operation.Invocation is { TargetIdentity: { } identity }
            && identity.AssemblyIdentity == "System.Threading"
            && identity.AssemblyVersion == "10.0.0.0"
            && identity.ContainingMetadataType == "System.Threading.SemaphoreSlim"
            && identity.GenericArity == 0
            && identity.MethodMetadataName is "WaitAsync" or "Release"
             && operation.Invocation.IsPlatformTarget
             && operation.Invocation.TargetAssemblyFullIdentity == "System.Threading, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
             && operation.Invocation.ReceiverOriginalTypeIdentity == new FrameworkTypeIdentity("System.Threading", "10.0.0.0", "System.Threading.SemaphoreSlim")
             && operation.Invocation.ReceiverOriginalTypeFullAssemblyIdentity == "System.Threading, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");

    private static bool HasNaturalLoopCatchShape(ExtractedMethodBody body)
        => !body.OrdinaryBranches.IsDefaultOrEmpty && body.OrdinaryBranches.Any(branch =>
            branch.DestinationBlockOrdinal >= 0
            && branch.LeavingRegions.Any(regionId => body.Regions.Any(region =>
                region.Id == regionId && region.Kind == ExtractedRegionKind.Catch)));

    private static bool HasCancellationCandidate(ExtractedMethodBody body)
        => body.Operations.Any(operation => operation.Invocation is { TargetIdentity: { } identity }
            && identity.AssemblyIdentity == "System.Runtime"
            && identity.AssemblyVersion == "10.0.0.0"
            && identity.ContainingMetadataType == "System.Threading.CancellationToken"
            && identity.GenericArity == 0
            && identity.MethodMetadataName == "ThrowIfCancellationRequested"
            && identity.Parameters.IsEmpty
            && identity.ReturnType == "System.Void"
             && operation.Invocation.IsPlatformTarget
             && operation.Invocation.TargetAssemblyFullIdentity == "System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
             && operation.Invocation.ReceiverOriginalTypeIdentity == new FrameworkTypeIdentity("System.Runtime", "10.0.0.0", "System.Threading.CancellationToken")
             && operation.Invocation.ReceiverOriginalTypeFullAssemblyIdentity == "System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
             && operation.Invocation.ReceiverParameterOrdinal is not null
             && operation.Invocation.ReceiverIdentity == $"{body.Method.Value}:parameter:{operation.Invocation.ReceiverParameterOrdinal.Value}");

    private static ImmutableArray<CatchContinuation> BuildCatchContinuations(
        ExtractedMethodBody body, ImmutableArray<FlowNode>.Builder flowNodes,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics)
    {
        var result = new List<CatchContinuation>();
        var ordinaryBranches = body.OrdinaryBranches.IsDefault ? [] : body.OrdinaryBranches;
        foreach (var branch in ordinaryBranches.Where(item => item.DestinationBlockOrdinal >= 0))
        {
            var catches = branch.LeavingRegions.Select(id => body.Regions.FirstOrDefault(region => region.Id == id))
                .Where(region => region?.Kind == ExtractedRegionKind.Catch).Cast<ExtractedExceptionRegion>().ToArray();
            var loops = flowNodes.OfType<LoopNode>()
                .Where(loop => loop.HeaderBlockOrdinal == branch.DestinationBlockOrdinal).ToArray();
            if (catches.Length == 0 || loops.Length == 0) { continue; }
            if (catches.Length != 1 || loops.Length != 1)
            {
                diagnostics.Add(CreateAmbiguousCatchContinuationDiagnostic(body, branch, catches, loops));
                continue;
            }
            var loopMembers = loops[0].BodyBlockOrdinals.Append(loops[0].HeaderBlockOrdinal).ToHashSet();
            var candidates = catches
                .SelectMany(catchRegion => body.Regions
                    .Where(region => region.Kind == ExtractedRegionKind.Try && region.Parent == catchRegion.Parent
                        && Enumerable.Range(region.StartBlockOrdinal, region.EndBlockOrdinal - region.StartBlockOrdinal + 1)
                            .All(loopMembers.Contains))
                    .Select(tryRegion => (Catch: catchRegion, Try: tryRegion, Loop: loops[0])))
                .ToArray();
            if (candidates.Length == 1)
            {
                var candidate = candidates[0];
                var evidence = branch.Evidence.Concat(candidate.Catch.Evidence).Concat(candidate.Try.Evidence).Concat(candidate.Loop.Evidence)
                    .DistinctBy(item => item.Id).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
                result.Add(new CatchContinuation(candidate.Catch.Id, candidate.Try.Id, candidate.Loop.Region,
                    branch.SourceBlockOrdinal, branch.DestinationBlockOrdinal, evidence,
                    new[] { branch.Certainty, candidate.Catch.Certainty, candidate.Try.Certainty, candidate.Loop.Certainty }.Max()));
            }
            else if (candidates.Length > 1)
            {
                diagnostics.Add(CreateAmbiguousCatchContinuationDiagnostic(
                    body,
                    branch,
                    candidates.SelectMany(candidate => new[] { candidate.Catch, candidate.Try }),
                    candidates.Select(candidate => candidate.Loop)));
            }
        }
        return result.Count == 0
            ? default
            : result.OrderBy(item => item.LoopRegion.Value, StringComparer.Ordinal)
                .ThenBy(item => item.SourceBlockOrdinal).ToImmutableArray();
    }

    private static AnalysisDiagnostic CreateAmbiguousCatchContinuationDiagnostic(
        ExtractedMethodBody body,
        ExtractedOrdinaryBranch branch,
        IEnumerable<ExtractedExceptionRegion> regions,
        IEnumerable<LoopNode> loops)
    {
        var contributingRegions = regions
            .DistinctBy(region => region.Id)
            .ToArray();
        var contributingLoops = loops
            .DistinctBy(loop => loop.Id)
            .ToArray();
        var evidence = branch.Evidence
            .Concat(contributingRegions.SelectMany(region => region.Evidence))
            .Concat(contributingLoops.SelectMany(loop => loop.Evidence))
            .DistinctBy(item => item.Id)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var certainty = new[] { branch.Certainty }
            .Concat(contributingRegions.Select(region => region.Certainty))
            .Concat(contributingLoops.Select(loop => loop.Certainty))
            .Concat(evidence.Select(item => item.Certainty))
            .Max();
        return CreateDiagnostic(
            "BD2020",
            "A catch continuation mapping is ambiguous and was withheld.",
            body.Method.Value,
            branch.SourceBlockOrdinal,
            evidence,
            certainty);
    }

    private static FlowNode CreateOperationNode(MethodId method, ExtractedOperation operation, int blockOrdinal, Dictionary<OperationId, ExtractedOperation> operationsById, bool preserveWorkerTerminalBlocks)
    {
        var id = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
            method,
            operation.Kind.ToString(),
            blockOrdinal,
            operation.EvaluationOrdinal,
            "operation"));
        return operation.Kind switch
        {
            ExtractedOperationKind.Invocation or ExtractedOperationKind.DynamicInvocation or ExtractedOperationKind.EventAssignment => new InvocationFlowNode(
                id,
                method,
                operation.Id,
                operation.Invocation?.Target,
                operation.Invocation?.IsDispatchable ?? false,
                operation.Invocation?.IsDelegateOrEventInvoke ?? false,
                 operation.Invocation?.IsStatic ?? false,
                 operation.Invocation?.IsConstructor ?? false,
                 operation.Invocation?.IsDynamic ?? false,
                 operation.Evidence,
                 operation.Certainty,
                 operation.Invocation?.TargetContainingTypeName,
                  operation.Invocation?.TargetMethodName,
                  operation.Invocation?.IsInsideNestedFunction ?? false,
                   operation.IsSourceBacked,
                   operation.Invocation?.IsLoadedProjectTarget ?? false,
                   blockOrdinal,
                   operation.EvaluationOrdinal,
                   operation.Invocation?.TargetAssemblyName,
                   operation.Invocation?.IsPlatformTarget ?? false,
                    ConstantArguments: ProjectConstantArguments(operation, operationsById),
                    TargetIdentity: operation.Invocation?.TargetIdentity,
                    ReceiverParameterOrdinal: operation.Invocation?.ReceiverParameterOrdinal,
                    ReceiverIdentity: operation.Invocation?.ReceiverIdentity,
                     TargetAssemblyFullIdentity: operation.Invocation?.TargetAssemblyFullIdentity,
                     ReceiverOriginalTypeIdentity: operation.Invocation?.ReceiverOriginalTypeIdentity,
                     ReceiverOriginalTypeFullAssemblyIdentity: operation.Invocation?.ReceiverOriginalTypeFullAssemblyIdentity),
            ExtractedOperationKind.ObjectCreation => new InvocationFlowNode(
                id,
                method,
                operation.Id,
                operation.ReferencedMethods.FirstOrDefault(),
                IsDispatchable: false,
                IsDelegateOrEventInvoke: false,
                IsStatic: false,
                 IsConstructor: true,
                 IsDynamic: false,
                 operation.Evidence,
                 operation.Certainty,
                 IsSourceBacked: operation.IsSourceBacked),
            ExtractedOperationKind.Await => new AwaitFlowNode(
                id,
                method,
                operation.Await?.Operand ?? operation.Id,
                operation.Evidence,
                operation.Certainty),
            ExtractedOperationKind.Return => new ReturnFlowNode(
                id,
                method,
                operation.Return?.Value,
                operation.Evidence,
                operation.Certainty)
            {
                BlockOrdinal = preserveWorkerTerminalBlocks ? blockOrdinal : null,
            },
            ExtractedOperationKind.Throw => new ThrowFlowNode(
                id,
                method,
                operation.Throw?.Exception,
                operation.Throw?.IsRethrow ?? false,
                operation.Evidence,
                operation.Certainty),
            ExtractedOperationKind.Unknown => new UnknownOperationFlowNode(
                id,
                method,
                operation.Id,
                operation.Evidence,
                operation.Certainty),
            _ => new OperationFlowNode(
                id,
                method,
                operation.Id,
                operation.Kind,
                operation.Evidence,
                operation.Certainty),
        };
    }

    /// <summary>
    /// Projects compiler-proven constant arguments from an invocation's argument operations.
    /// Only literal and constant-valued arguments are included; parameter references,
    /// enum members, and non-constant expressions are excluded so downstream presentation
    /// never infers unsupported argument meaning.
    /// </summary>
    private static ImmutableArray<CompilerProvenArgument> ProjectConstantArguments(ExtractedOperation operation, Dictionary<OperationId, ExtractedOperation> operationsById)
    {
        if (operation.Invocation is not { Arguments: { Length: > 0 } arguments })
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<CompilerProvenArgument>();
        var mappings = operation.Invocation.ArgumentMappings;
        if (!mappings.IsDefaultOrEmpty)
        {
            if (mappings.Any(mapping => !mapping.IsMappingComplete || mapping.ParameterOrdinal is null))
            {
                return [];
            }

            foreach (var mapping in mappings.OrderBy(mapping => mapping.ParameterOrdinal))
            {
                if (!operationsById.TryGetValue(mapping.Operation, out var argOperation)
                    || !argOperation.HasConstantValue
                    || string.IsNullOrWhiteSpace(argOperation.TypeDescriptor))
                {
                    return [];
                }
                builder.Add(new CompilerProvenArgument(mapping.ParameterOrdinal!.Value,
                    argOperation.TypeDescriptor, argOperation.ConstantValue,
                    isNull: argOperation.ConstantValue is null));
            }
            return builder.ToImmutable();
        }

        for (int ordinal = 0; ordinal < arguments.Length; ordinal++)
        {
            if (!operationsById.TryGetValue(arguments[ordinal], out var argOperation)
                || argOperation.ParameterOrdinal is not { } parameterOrdinal
                || !argOperation.HasConstantValue
                || string.IsNullOrWhiteSpace(argOperation.TypeDescriptor))
            {
                return [];
            }
            builder.Add(new CompilerProvenArgument(parameterOrdinal, argOperation.TypeDescriptor,
                argOperation.ConstantValue, isNull: argOperation.ConstantValue is null));
        }

        return builder.OrderBy(argument => argument.Ordinal).ToImmutableArray();
    }

    private static OperationId[] CollectBlockOperations(
        ExtractedBasicBlock block,
        Dictionary<OperationId, ExtractedOperation> operationsById)
    {
        var collected = new List<OperationId>();
        var visited = new HashSet<OperationId>();
        var pending = new Stack<OperationId>();
        foreach (var operationId in block.Operations.Reverse())
        {
            pending.Push(operationId);
        }

        while (pending.TryPop(out var operationId))
        {
            if (!visited.Add(operationId))
            {
                continue;
            }

            collected.Add(operationId);
            if (operationsById.TryGetValue(operationId, out var operation))
            {
                foreach (var operand in operation.Operands)
                {
                    pending.Push(operand);
                }
            }
        }

        return collected
            .OrderBy(operationId => operationsById[operationId].EvaluationOrdinal)
            .ToArray();
    }

    private static FlowNode? CreateTerminalNode(
        MethodId method,
        ExtractedBasicBlock block,
        Dictionary<OperationId, ExtractedOperation> operationsById,
        bool preserveWorkerTerminalBlocks)
    {
        return block.Terminal switch
        {
            ExtractedBlockTerminalKind.Return => new ReturnFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                    method, "Return", block.Ordinal, 0, "terminal")),
                method,
                FindTerminalValue(block, operationsById),
                block.Evidence,
                CertaintyLevel.Exact)
            {
                BlockOrdinal = preserveWorkerTerminalBlocks ? block.Ordinal : null,
            },
            ExtractedBlockTerminalKind.Throw => new ThrowFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                    method, "Throw", block.Ordinal, 0, "terminal")),
                method,
                FindTerminalValue(block, operationsById),
                IsRethrow: false,
                block.Evidence,
                CertaintyLevel.Exact),
            ExtractedBlockTerminalKind.Rethrow => new ThrowFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                    method, "Rethrow", block.Ordinal, 0, "terminal")),
                method,
                null,
                IsRethrow: true,
                block.Evidence,
                CertaintyLevel.Exact),
            _ => null,
        };
    }

    private static OperationId? FindTerminalValue(
        ExtractedBasicBlock block,
        Dictionary<OperationId, ExtractedOperation> operationsById)
    {
        foreach (var operationId in block.Operations)
        {
            if (operationsById.TryGetValue(operationId, out var operation)
                && operation.Kind is not (ExtractedOperationKind.Return or ExtractedOperationKind.Throw))
            {
                return operationId;
            }
        }

        return null;
    }

    private static void AddBlockEdges(
        ExtractedMethodBody body,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        Dictionary<OperationId, ExtractedOperation> operationsById,
        Dictionary<int, FlowNodeId> blockHeads,
        Dictionary<int, FlowNodeId> blockTails,
        FlowNodeId entryId,
        FlowNodeId exitId,
        Dictionary<int, HashSet<int>> dominators,
        ImmutableArray<FlowEdge>.Builder edges,
        ref int edgeOrdinal,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics)
    {
        if (blockHeads.TryGetValue(0, out var entryHead) && entryHead != entryId)
        {
            edges.Add(CreateEdge(body.Method, entryId, entryHead, FlowEdgeKind.Normal, null, ref edgeOrdinal));
        }

        foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
        {
            var tail = blockTails[block.Ordinal];
            var targets = new List<(FlowNodeId Target, FlowEdgeKind Kind, OperationId? Guard)>();
            if (block.Terminal == ExtractedBlockTerminalKind.Conditional)
            {
                foreach (var successor in block.ConditionalSuccessors)
                {
                    if (TryResolveTarget(successor, blocksByOrdinal, blockHeads, exitId, out var target))
                    {
                        targets.Add((target, IsBackEdge(block.Ordinal, successor, dominators) ? FlowEdgeKind.LoopBack : FlowEdgeKind.True, block.BranchCondition));
                    }
                    else
                    {
                        diagnostics.Add(CreateDiagnostic(
                            "BD2002",
                            "A conditional successor references an unknown block.",
                            body.Method.Value,
                            block.Ordinal));
                    }
                }

                if (block.FallThroughSuccessor is { } fallThrough
                    && TryResolveTarget(fallThrough, blocksByOrdinal, blockHeads, exitId, out var fallTarget))
                {
                    targets.Add((fallTarget, IsBackEdge(block.Ordinal, fallThrough, dominators) ? FlowEdgeKind.LoopBack : FlowEdgeKind.False, block.BranchCondition));
                }
            }
            else if (block.FallThroughSuccessor is { } fallThrough)
            {
                if (TryResolveTarget(fallThrough, blocksByOrdinal, blockHeads, exitId, out var fallTarget))
                {
                    var kind = block.Terminal switch
                    {
                        ExtractedBlockTerminalKind.Return => FlowEdgeKind.Return,
                        ExtractedBlockTerminalKind.Throw => FlowEdgeKind.Throw,
                        ExtractedBlockTerminalKind.Rethrow => FlowEdgeKind.Rethrow,
                        _ => IsBackEdge(block.Ordinal, fallThrough, dominators) ? FlowEdgeKind.LoopBack : FlowEdgeKind.Normal,
                    };
                    targets.Add((fallTarget, kind, null));
                }
                else
                {
                    diagnostics.Add(CreateDiagnostic(
                        "BD2003",
                        "A fall-through successor references an unknown block.",
                        body.Method.Value,
                        block.Ordinal));
                }
            }
            else if (block.Terminal is ExtractedBlockTerminalKind.Throw or ExtractedBlockTerminalKind.Rethrow)
            {
                var kind = block.Terminal == ExtractedBlockTerminalKind.Rethrow ? FlowEdgeKind.Rethrow : FlowEdgeKind.Throw;
                targets.Add((exitId, kind, null));
            }

            foreach (var target in targets)
            {
                edges.Add(CreateEdge(body.Method, tail, target.Target, target.Kind, target.Guard, ref edgeOrdinal));
            }
        }
    }

    private static bool IsBackEdge(
        int sourceOrdinal,
        int targetOrdinal,
        Dictionary<int, HashSet<int>> dominators)
    {
        if (targetOrdinal > sourceOrdinal
            || !dominators.TryGetValue(sourceOrdinal, out var sourceDominators))
        {
            return false;
        }

        return sourceDominators.Contains(targetOrdinal);
    }

    private static bool TryResolveTarget(
        int successor,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        Dictionary<int, FlowNodeId> blockHeads,
        FlowNodeId exitId,
        out FlowNodeId target)
    {
        if (blocksByOrdinal.ContainsKey(successor))
        {
            target = blockHeads[successor];
            return true;
        }

        target = default;
        return false;
    }

    private static void AddFlowRegion(
        ExtractedMethodBody body,
        ExtractedExceptionRegion region,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        Dictionary<int, FlowNodeId> blockHeads,
        Dictionary<int, FlowNodeId> blockTails,
        ImmutableArray<FlowRegion>.Builder regions,
        bool preserveWorkerMetadata)
    {
        var kind = ToFlowRegionKind(region.Kind, preserveWorkerMetadata);
        if (kind == FlowRegionKind.Unknown)
        {
            return;
        }

        var regionNodes = new List<FlowNodeId>();
        foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
        {
            if (block.Ordinal < region.StartBlockOrdinal || block.Ordinal > region.EndBlockOrdinal)
            {
                continue;
            }

            if (blockHeads.TryGetValue(block.Ordinal, out var head) && !regionNodes.Contains(head))
            {
                regionNodes.Add(head);
            }

            if (blockTails.TryGetValue(block.Ordinal, out var tail) && !regionNodes.Contains(tail))
            {
                regionNodes.Add(tail);
            }
        }

        regions.Add(new FlowRegion(
            region.Id,
            body.Method,
            kind,
            region.Parent,
            region.Ordinal,
            regionNodes.OrderBy(id => id.Value, StringComparer.Ordinal).ToImmutableArray(),
            region.ExceptionType,
            region.Evidence,
            region.Certainty)
        {
            StartBlockOrdinal = preserveWorkerMetadata ? region.StartBlockOrdinal : null,
            EndBlockOrdinal = preserveWorkerMetadata ? region.EndBlockOrdinal : null,
        });
    }

    private static FlowRegionKind ToFlowRegionKind(ExtractedRegionKind kind, bool preserveWorkerMetadata) => kind switch
    {
        ExtractedRegionKind.Root => FlowRegionKind.Root,
        ExtractedRegionKind.Try => FlowRegionKind.Try,
        ExtractedRegionKind.Catch => FlowRegionKind.Catch,
        ExtractedRegionKind.Filter => FlowRegionKind.Filter,
        ExtractedRegionKind.Finally => FlowRegionKind.Finally,
        ExtractedRegionKind.TryAndFinally when preserveWorkerMetadata => FlowRegionKind.TryAndFinally,
        ExtractedRegionKind.TryAndCatch when preserveWorkerMetadata => FlowRegionKind.TryAndCatch,
        _ => FlowRegionKind.Unknown,
    };

    private static ImmutableArray<FlowRegion> DetectLoops(
        ExtractedMethodBody body,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        Dictionary<OperationId, ExtractedOperation> operationsById,
        Dictionary<int, FlowNodeId> blockHeads,
        Dictionary<int, FlowNodeId> blockTails,
        ImmutableArray<FlowNode>.Builder nodes,
        ImmutableArray<FlowRegion>.Builder regions,
        ImmutableArray<EvidenceRef> evidence,
        ref int edgeOrdinal,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics)
    {
        var loopOrdinal = regions.Count;
        var anchors = body.LoopAnchors.IsDefault ? [] : body.LoopAnchors;
        var ordinaryBranches = body.OrdinaryBranches.IsDefault ? [] : body.OrdinaryBranches;
        if (anchors.Select(anchor => anchor.Operation).Distinct().Count() != anchors.Length
            || anchors.Select(anchor => anchor.Operation.Value ?? string.Empty).Distinct(StringComparer.Ordinal).Count() != anchors.Length
            || anchors.Any(anchor => anchor.Evidence.IsDefaultOrEmpty))
        {
            diagnostics.Add(CreateDiagnostic("BD2012", "The compiler loop-anchor collection is invalid.", body.Method.Value, -1));
            return regions.ToImmutable();
        }

        foreach (var loop in (body.NaturalLoops.IsDefault ? [] : body.NaturalLoops)
                     .OrderBy(loop => loop.HeaderBlockOrdinal)
                     .ThenBy(loop => loop.LoopOperation.Value, StringComparer.Ordinal))
        {
            var anchorMatches = anchors.Where(candidate => candidate.Operation == loop.LoopOperation).ToArray();
            var anchor = anchorMatches.Length == 1 ? anchorMatches[0] : null;
            if (!blocksByOrdinal.ContainsKey(loop.HeaderBlockOrdinal)
                || loop.BodyBlockOrdinals.Any(ordinal => !blocksByOrdinal.ContainsKey(ordinal))
                || loop.ExitBlockOrdinals.Any(ordinal => !blocksByOrdinal.ContainsKey(ordinal))
                || loop.LatchBlockOrdinals.Any(ordinal => !blocksByOrdinal.ContainsKey(ordinal)))
            {
                diagnostics.Add(CreateDiagnostic("BD2010", "A compiler-derived natural loop references an unknown block.", body.Method.Value, loop.HeaderBlockOrdinal));
                continue;
            }
            if (anchor is null
                || anchor.Operation.Value is null
                || anchor.Kind != loop.Kind
                || anchor.Evidence.Any(evidence => !loop.Evidence.Any(candidate => candidate.Id == evidence.Id))
                || loop.Certainty < anchor.Certainty
                || loop.LoopOperation.Value is null
                || !loop.LatchBlockOrdinals.Any()
                || loop.BackEdges.IsDefaultOrEmpty
                || loop.BackEdges.Any(edge => edge.DestinationBlockOrdinal != loop.HeaderBlockOrdinal
                    || !loop.LatchBlockOrdinals.Contains(edge.SourceBlockOrdinal))
                || loop.HeaderBlockOrdinal < 0
                || loop.BodyBlockOrdinals.Contains(loop.HeaderBlockOrdinal)
                || loop.ExitBlockOrdinals.Contains(loop.HeaderBlockOrdinal)
                || (loop.Kind != ExtractedLoopKind.DoWhileLoop && loop.LatchBlockOrdinals.Any(ordinal => ordinal == loop.HeaderBlockOrdinal))
                || loop.BodyBlockOrdinals.Intersect(loop.ExitBlockOrdinals).Any()
                || loop.LatchBlockOrdinals.Intersect(loop.ExitBlockOrdinals).Any()
                || loop.LatchBlockOrdinals.Any(ordinal => ordinal != loop.HeaderBlockOrdinal && !loop.BodyBlockOrdinals.Contains(ordinal))
                || loop.BodyBlockOrdinals.Length != loop.BodyBlockOrdinals.Distinct().Count()
                || loop.ExitBlockOrdinals.Length != loop.ExitBlockOrdinals.Distinct().Count()
                || loop.LatchBlockOrdinals.Length != loop.LatchBlockOrdinals.Distinct().Count()
                || loop.BackEdges.Select(edge => (edge.SourceBlockOrdinal, edge.DestinationBlockOrdinal)).Distinct().Count() != loop.BackEdges.Length
                || !loop.LatchBlockOrdinals.SequenceEqual(loop.BackEdges.Select(edge => edge.SourceBlockOrdinal).Distinct().Order())
                || !loop.Evidence.Any()
                || !loop.Evidence.Select(evidence => evidence.Id).Distinct().SequenceEqual(loop.Evidence.Select(evidence => evidence.Id))
                 || loop.BackEdges.Any(edge => edge.Evidence.IsDefaultOrEmpty
                     || ordinaryBranches.Count(admitted => admitted.SourceBlockOrdinal == edge.SourceBlockOrdinal
                         && admitted.DestinationBlockOrdinal == edge.DestinationBlockOrdinal
                         && admitted.EnteringRegions.SequenceEqual(edge.EnteringRegions)
                         && admitted.LeavingRegions.SequenceEqual(edge.LeavingRegions)) != 1
                     || edge.EnteringRegions.Any(region => !body.Regions.Any(candidate => candidate.Id == region))
                     || edge.LeavingRegions.Any(region => !body.Regions.Any(candidate => candidate.Id == region))
                     || edge.Evidence.Any(evidence => !loop.Evidence.Any(candidate => candidate.Id == evidence.Id)))
                 || !HasValidTopology(body, loop, blocksByOrdinal, ordinaryBranches))
            {
                diagnostics.Add(CreateDiagnostic("BD2011", "A compiler-derived natural loop descriptor is invalid and was withheld.", body.Method.Value, loop.HeaderBlockOrdinal));
                continue;
            }

            var loopId = StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(
                body.Method, "NaturalLoop", loopOrdinal));
            var loopNodeId = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                body.Method, "Loop", loop.HeaderBlockOrdinal, loopOrdinal, loop.LoopOperation.Value ?? "loop"));
            var projectedBodyOrdinals = loop.BodyBlockOrdinals.IsEmpty && loop.Kind == ExtractedLoopKind.DoWhileLoop
                ? loop.LatchBlockOrdinals
                : loop.BodyBlockOrdinals;
            var bodyNodes = projectedBodyOrdinals
                    .Order()
                    .Select(ordinal => blockTails.TryGetValue(ordinal, out var tail) ? tail : blockHeads[ordinal])
                    .OrderBy(id => id.Value, StringComparer.Ordinal)
                    .ToImmutableArray();
            var exits = loop.ExitBlockOrdinals
                    .Select(ordinal => blockHeads.TryGetValue(ordinal, out var head) ? head : blockTails[ordinal])
                    .OrderBy(id => id.Value, StringComparer.Ordinal)
                    .ToImmutableArray();

            nodes.Add(new LoopNode(
                loopNodeId,
                body.Method,
                loopId,
                blockHeads[loop.HeaderBlockOrdinal],
                bodyNodes,
                exits,
                loop.Evidence,
                loop.Certainty,
                loop.BodyBlockOrdinals)
            {
                LoopKind = loop.Kind,
                HeaderBlockOrdinal = loop.HeaderBlockOrdinal,
                LatchBlockOrdinals = loop.LatchBlockOrdinals,
                BackEdges = loop.BackEdges,
            });
            regions.Add(new FlowRegion(
                    loopId,
                    body.Method,
                    FlowRegionKind.NaturalLoop,
                    null,
                    loopOrdinal,
                    bodyNodes,
                    null,
                    loop.Evidence,
                    loop.Certainty)
            {
                StartBlockOrdinal = HasSemaphoreCandidate(body) || HasCancellationCandidate(body) || HasNaturalLoopCatchShape(body)
                    ? loop.HeaderBlockOrdinal
                    : null,
                EndBlockOrdinal = HasSemaphoreCandidate(body) || HasCancellationCandidate(body) || HasNaturalLoopCatchShape(body)
                    ? loop.BodyBlockOrdinals.Concat(loop.ExitBlockOrdinals).DefaultIfEmpty(loop.HeaderBlockOrdinal).Max()
                    : null,
            });
            loopOrdinal++;
        }

        return regions.ToImmutable();
    }

    private static Dictionary<int, HashSet<int>> ComputeDominators(
        ExtractedMethodBody body,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal)
    {
        var ordinals = body.Blocks.Select(block => block.Ordinal).Order().ToArray();
        var all = new HashSet<int>(ordinals);
        var dominators = ordinals.ToDictionary(
            ordinal => ordinal,
            ordinal => ordinal == 0 ? new HashSet<int> { 0 } : new HashSet<int>(all));

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var ordinal in ordinals.Where(ordinal => ordinal != 0))
            {
                if (!blocksByOrdinal.TryGetValue(ordinal, out var block))
                {
                    continue;
                }

                var predecessors = block.Predecessors
                    .Where(predecessor => dominators.ContainsKey(predecessor))
                    .ToArray();
                if (predecessors.Length == 0)
                {
                    continue;
                }

                var intersection = new HashSet<int>(dominators[predecessors[0]]);
                foreach (var predecessor in predecessors.Skip(1))
                {
                    intersection.IntersectWith(dominators[predecessor]);
                }

                intersection.Add(ordinal);
                if (!intersection.SetEquals(dominators[ordinal]))
                {
                    dominators[ordinal] = intersection;
                    changed = true;
                }
            }
        }

        return dominators;
    }

    private static bool HasValidTopology(
        ExtractedMethodBody body,
        ExtractedNaturalLoop loop,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        ImmutableArray<ExtractedOrdinaryBranch> ordinaryBranches)
    {
        if (!blocksByOrdinal.ContainsKey(loop.HeaderBlockOrdinal)
            || loop.BodyBlockOrdinals.Any(ordinal => !blocksByOrdinal.ContainsKey(ordinal))
            || loop.LatchBlockOrdinals.Any(ordinal => !blocksByOrdinal.ContainsKey(ordinal))
            || loop.ExitBlockOrdinals.Any(ordinal => !blocksByOrdinal.ContainsKey(ordinal)))
        {
            return false;
        }

        var members = loop.BodyBlockOrdinals.Append(loop.HeaderBlockOrdinal).ToHashSet();
        static bool HasSuccessor(ExtractedBasicBlock block, int destination) =>
            block.FallThroughSuccessor == destination || block.ConditionalSuccessors.Contains(destination);

        var verified = ordinaryBranches
            .Where(branch => blocksByOrdinal.TryGetValue(branch.SourceBlockOrdinal, out var source)
                && blocksByOrdinal.TryGetValue(branch.DestinationBlockOrdinal, out var destination)
                && HasSuccessor(source, branch.DestinationBlockOrdinal)
                && destination.Predecessors.Contains(branch.SourceBlockOrdinal))
            .ToArray();
        if (verified.Length != ordinaryBranches.Length)
        {
            return false;
        }

        if (verified.GroupBy(branch => (branch.SourceBlockOrdinal, branch.DestinationBlockOrdinal)).Any(group => group.Count() != 1))
        {
            return false;
        }

        if (!blocksByOrdinal.ContainsKey(0))
        {
            return false;
        }

        var entryReachable = new HashSet<int> { 0 };
        var pendingFromEntry = new Stack<int>();
        pendingFromEntry.Push(0);
        while (pendingFromEntry.TryPop(out var current))
        {
            foreach (var successor in verified
                         .Where(branch => branch.SourceBlockOrdinal == current)
                         .Select(branch => branch.DestinationBlockOrdinal))
            {
                if (entryReachable.Add(successor))
                {
                    pendingFromEntry.Push(successor);
                }
            }
        }

        if (!entryReachable.Contains(loop.HeaderBlockOrdinal)
            || loop.BodyBlockOrdinals.Any(ordinal => !entryReachable.Contains(ordinal)))
        {
            return false;
        }

        var verifiedByPair = verified.ToDictionary(branch => (branch.SourceBlockOrdinal, branch.DestinationBlockOrdinal));
        var actualMemberPairs = members
            .SelectMany(source => GetSuccessors(blocksByOrdinal[source]).Select(destination => (Source: source, Destination: destination)))
            .ToHashSet();
        var suppliedMemberBranches = verified.Where(branch => members.Contains(branch.SourceBlockOrdinal)).ToArray();
        if (suppliedMemberBranches.GroupBy(branch => (branch.SourceBlockOrdinal, branch.DestinationBlockOrdinal)).Any(group => group.Count() != 1)
            || suppliedMemberBranches.Any(branch => !actualMemberPairs.Contains((branch.SourceBlockOrdinal, branch.DestinationBlockOrdinal)))
            || actualMemberPairs.Any(pair => !verifiedByPair.ContainsKey((pair.Source, pair.Destination))))
        {
            return false;
        }
        var completeMemberEdges = suppliedMemberBranches;
        if (verified.Any(edge => !members.Contains(edge.SourceBlockOrdinal)
                && members.Contains(edge.DestinationBlockOrdinal)
                && edge.DestinationBlockOrdinal != loop.HeaderBlockOrdinal))
        {
            return false;
        }

        foreach (var edge in loop.BackEdges)
        {
            if (!members.Contains(edge.SourceBlockOrdinal)
                || edge.DestinationBlockOrdinal != loop.HeaderBlockOrdinal
                || !verifiedByPair.ContainsKey((edge.SourceBlockOrdinal, edge.DestinationBlockOrdinal)))
            {
                return false;
            }
        }

        var reverse = new HashSet<int> { loop.HeaderBlockOrdinal };
        var pending = new Stack<int>(loop.LatchBlockOrdinals);
        while (pending.TryPop(out var current))
        {
            if (!reverse.Add(current))
            {
                continue;
            }

            foreach (var predecessor in completeMemberEdges.Where(edge => edge.DestinationBlockOrdinal == current).Select(edge => edge.SourceBlockOrdinal))
            {
                if (predecessor != loop.HeaderBlockOrdinal && members.Contains(predecessor))
                {
                    pending.Push(predecessor);
                }
            }
        }

        if (!reverse.SetEquals(members))
        {
            return false;
        }

        var reachable = new HashSet<int> { loop.HeaderBlockOrdinal };
        pending = new Stack<int>([loop.HeaderBlockOrdinal]);
        while (pending.TryPop(out var current))
        {
            foreach (var destination in completeMemberEdges.Where(edge => edge.SourceBlockOrdinal == current)
                         .Select(edge => edge.DestinationBlockOrdinal).Where(members.Contains))
            {
                if (reachable.Add(destination))
                {
                    pending.Push(destination);
                }
            }
        }

        if (!reachable.SetEquals(members))
        {
            return false;
        }

        if (completeMemberEdges.Any(edge => members.Contains(edge.DestinationBlockOrdinal)
                && edge.DestinationBlockOrdinal != loop.HeaderBlockOrdinal
                && !members.Contains(edge.SourceBlockOrdinal)))
        {
            return false;
        }

        var actualExits = completeMemberEdges
            .Where(branch => members.Contains(branch.SourceBlockOrdinal) && !members.Contains(branch.DestinationBlockOrdinal))
            .Select(branch => branch.DestinationBlockOrdinal)
            .ToHashSet();
        if (!actualExits.SetEquals(loop.ExitBlockOrdinals))
        {
            return false;
        }

        if (body.Regions.Any(region => region.Parent is { } parent
                && (!body.Regions.Any(candidate => candidate.Id == parent)
                    || !body.Regions.Any(candidate => candidate.Id == parent
                        && candidate.StartBlockOrdinal <= region.StartBlockOrdinal
                        && candidate.EndBlockOrdinal >= region.EndBlockOrdinal))))
        {
            return false;
        }

        foreach (var branch in completeMemberEdges.Where(branch =>
                     members.Contains(branch.SourceBlockOrdinal)
                     && (members.Contains(branch.DestinationBlockOrdinal) || loop.ExitBlockOrdinals.Contains(branch.DestinationBlockOrdinal))))
        {
            var sourceRegions = body.Regions.Where(region => region.StartBlockOrdinal <= branch.SourceBlockOrdinal && region.EndBlockOrdinal >= branch.SourceBlockOrdinal).Select(region => region.Id).ToHashSet();
            var destinationRegions = body.Regions.Where(region => region.StartBlockOrdinal <= branch.DestinationBlockOrdinal && region.EndBlockOrdinal >= branch.DestinationBlockOrdinal).Select(region => region.Id).ToHashSet();
            var expectedEntering = destinationRegions.Except(sourceRegions).ToHashSet();
            var expectedLeaving = sourceRegions.Except(destinationRegions).ToHashSet();
            if (!branch.EnteringRegions.ToHashSet().SetEquals(expectedEntering)
                || !branch.LeavingRegions.ToHashSet().SetEquals(expectedLeaving))
            {
                return false;
            }
        }

        return true;

        static IEnumerable<int> GetSuccessors(ExtractedBasicBlock block)
        {
            if (block.FallThroughSuccessor is { } fallThrough)
            {
                yield return fallThrough;
            }

            foreach (var conditional in block.ConditionalSuccessors)
            {
                yield return conditional;
            }
        }
    }

    private static HashSet<int> ComputeNaturalLoop(
        int header,
        int latch,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal)
    {
        if (header == latch)
        {
            return new HashSet<int> { header };
        }

        var loop = new HashSet<int> { header, latch };
        var pending = new Stack<int>();
        pending.Push(latch);
        while (pending.TryPop(out var ordinal))
        {
            if (!blocksByOrdinal.TryGetValue(ordinal, out var block))
            {
                continue;
            }

            foreach (var predecessor in block.Predecessors)
            {
                if (predecessor != header && loop.Add(predecessor))
                {
                    pending.Push(predecessor);
                }
            }
        }

        return loop;
    }

    private static ImmutableArray<FlowOutcome> ReconcileTerminals(
        ExtractedMethodBody body,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        Dictionary<int, FlowNodeId> blockTails,
        FlowNodeId exitId,
        ImmutableArray<FlowEdge>.Builder edges,
        ImmutableArray<FlowRegion>.Builder regions,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics,
        bool hasCatchContinuation)
    {
        var outcomes = ImmutableArray.CreateBuilder<FlowOutcome>();
        var normalExitReachable = false;
        var preserveTerminalAnchors = hasCatchContinuation
            || HasSemaphoreCandidate(body)
            || HasCancellationCandidate(body);
        foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
        {
            if (block.Terminal == ExtractedBlockTerminalKind.Exit)
            {
                continue;
            }

            switch (block.Terminal)
            {
                case ExtractedBlockTerminalKind.Return:
                    outcomes.Add(new FlowOutcome(
                        FlowOutcomeKind.ExplicitReturn,
                        block.Ordinal,
                        block.BranchCondition,
                        block.Evidence,
                        CertaintyLevel.Exact,
                        null));
                    break;
                case ExtractedBlockTerminalKind.Throw:
                    if (!block.EscapingThrow)
                    {
                        continue;
                    }

                    outcomes.Add(new FlowOutcome(
                        FlowOutcomeKind.EscapingThrow,
                        block.Ordinal,
                        block.BranchCondition,
                        block.Evidence,
                        CertaintyLevel.Exact,
                        preserveTerminalAnchors ? blockTails.GetValueOrDefault(block.Ordinal) : null));
                    break;
                case ExtractedBlockTerminalKind.Rethrow:
                    if (!block.EscapingThrow)
                    {
                        continue;
                    }

                    outcomes.Add(new FlowOutcome(
                        FlowOutcomeKind.EscapingThrow,
                        block.Ordinal,
                        block.BranchCondition,
                        block.Evidence,
                        CertaintyLevel.Exact,
                        preserveTerminalAnchors ? blockTails.GetValueOrDefault(block.Ordinal) : null));
                    break;
                case ExtractedBlockTerminalKind.Unknown:
                    outcomes.Add(new FlowOutcome(
                        FlowOutcomeKind.Unknown,
                        block.Ordinal,
                        block.BranchCondition,
                        block.Evidence,
                        CertaintyLevel.Unknown,
                        preserveTerminalAnchors ? blockTails.GetValueOrDefault(block.Ordinal) : null));
                    break;
            }

            if (block.FallThroughSuccessor is { } successor
                && blocksByOrdinal.TryGetValue(successor, out var successorBlock)
                && successorBlock.Terminal == ExtractedBlockTerminalKind.Exit)
            {
                normalExitReachable = true;
            }
        }

        if (edges.Any(edge => edge.Target == exitId && edge.Kind == FlowEdgeKind.Normal))
        {
            normalExitReachable = true;
        }

        if (normalExitReachable)
        {
            outcomes.Add(new FlowOutcome(
                FlowOutcomeKind.NormalCompletion,
                null,
                null,
                [],
                CertaintyLevel.Exact));
        }

        if (outcomes.All(outcome => outcome.Kind
                is not (FlowOutcomeKind.NormalCompletion or FlowOutcomeKind.ExplicitReturn)))
        {
            outcomes.Add(new FlowOutcome(
                FlowOutcomeKind.NoNormalExit,
                null,
                null,
                [],
                CertaintyLevel.Conservative));
        }

        return outcomes.ToImmutable();
    }

    private static FlowEdge CreateEdge(
        MethodId method,
        FlowNodeId source,
        FlowNodeId target,
        FlowEdgeKind kind,
        OperationId? guard,
        ref int edgeOrdinal)
    {
        var id = StableIdentity.CreateFlowEdgeId(new FlowEdgeIdentityDescriptor(
            method,
            source.Value,
            target.Value,
            kind.ToString(),
            edgeOrdinal));
        edgeOrdinal++;
        return new FlowEdge(id, method, source, target, kind, guard, [], CertaintyLevel.Exact);
    }

    private static AnalysisDiagnostic CreateDiagnostic(
        string code,
        string summary,
        string subjectId,
        int ordinal,
        ImmutableArray<EvidenceRef> evidence = default,
        CertaintyLevel certainty = CertaintyLevel.Exact)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.BaselineIndex,
            null,
            subjectId,
            Math.Max(0, ordinal)));
        return new AnalysisDiagnostic(
            id,
            code,
            DiagnosticSeverity.Warning,
            AnalysisStage.BaselineIndex,
            summary,
            new DiagnosticLocation("method flow", symbol: new SymbolId(subjectId)),
            $"The method flow violates invariant '{code}'.",
            "The method flow is not trustworthy for this method.",
            "Reanalyze the target; if the problem persists, report the affected method identity.",
            certainty,
            evidence,
            internalDetail: $"{code} at ordinal {ordinal}");
    }
}

/// <summary>Carries one normalized method flow and its build diagnostics.</summary>
public sealed record MethodFlowBuildResult(MethodFlowSnapshot Snapshot, ImmutableArray<AnalysisDiagnostic> Diagnostics);
