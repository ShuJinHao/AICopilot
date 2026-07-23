using System.Text;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentNormalizedNodeCheckpoint(
    AgentEvidenceRecord Evidence,
    AgentRunUsageLedgerEntry Usage,
    string OutputDigest);

internal static class AgentEvidenceNormalizer
{
    public static Result<AgentNormalizedNodeCheckpoint> Normalize(
        DurableTaskClaim taskClaim,
        AgentNodeRunClaim nodeClaim,
        AgentPlanNodeDocument nodeContract,
        ToolRegistration tool,
        AgentStep step,
        AgentToolExecutionResult executionResult,
        AgentTaskRunState state,
        ArtifactWorkspace workspace,
        IReadOnlyCollection<AgentEvidenceRecord> parentEvidence,
        int toolCallCount,
        TimeSpan elapsed,
        DateTimeOffset nowUtc)
    {
        if (toolCallCount is < 1 or > 5 || toolCallCount > nodeContract.RetryPolicy.MaxAttempts)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentRunBudgetExceeded,
                "Node checkpoint tool-call usage is outside the frozen retry budget."));
        }

        AgentReasoningToolOutput? reasoningOutput = null;
        if (nodeContract.NodeKind == "AgentReasoningNode" &&
            !AgentReasoningOutputAuthority.TryRead(
                executionResult.ContractOutput.CanonicalJson,
                out reasoningOutput))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.ToolOutputSchemaInvalid,
                "AgentReasoningNode output is not a sealed Completed LlmInference result."));
        }

        AgentCloudHealthAssessmentOutput? healthOutput = null;
        if (string.Equals(tool.ToolCode, "assess_cloud_health", StringComparison.Ordinal) &&
            !AgentCloudHealthAssessmentOutputAuthority.TryRead(
                executionResult.ContractOutput.CanonicalJson,
                out healthOutput))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.ToolOutputSchemaInvalid,
                "Deterministic health output does not match the frozen algorithm contract."));
        }

        var actualParentNodeIds = parentEvidence.Select(evidence => evidence.NodeId).ToArray();
        var exactParents = actualParentNodeIds.SequenceEqual(
            nodeContract.EvidenceSelectors,
            StringComparer.Ordinal);
        var optionalBestEffortSubset = nodeContract.JoinPolicy == "OptionalBestEffort" &&
                                       actualParentNodeIds.SequenceEqual(
                                           nodeContract.EvidenceSelectors.Where(actualParentNodeIds.Contains),
                                           StringComparer.Ordinal);
        if (!exactParents && !optionalBestEffortSubset)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Node checkpoint inputs do not match the frozen Evidence selectors."));
        }

        var parentIds = parentEvidence
            .Select(evidence => evidence.Id.Value)
            .Distinct()
            .Order()
            .ToArray();
        if (!AgentEvidenceSetDigestAuthority.TryComputeEffective(
                parentEvidence,
                out var evidenceSetDigest) ||
            healthOutput is not null &&
            !string.Equals(
                healthOutput.EvidenceSetDigest,
                evidenceSetDigest,
                StringComparison.Ordinal))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Node input Evidence set digest is invalid or changed before checkpoint normalization."));
        }

        if (reasoningOutput is not null)
        {
            AgentReasoningEvidenceProfile expectedProfile;
            try
            {
                expectedProfile = AgentReasoningEvidenceProfileAuthority.Create(parentEvidence);
            }
            catch (AgentToolExecutionException)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentNodeRunStateConflict,
                    "AgentReasoningNode input Evidence could not be profiled safely."));
            }

            if (!reasoningOutput.CitationRefs.SequenceEqual(
                    expectedProfile.CitationRefs,
                    StringComparer.Ordinal) ||
                !reasoningOutput.EvidenceWarnings.SequenceEqual(
                    expectedProfile.EvidenceWarnings,
                    StringComparer.Ordinal) ||
                !string.Equals(
                    reasoningOutput.ConflictStatus,
                    expectedProfile.ConflictStatus,
                    StringComparison.Ordinal))
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentNodeRunStateConflict,
                    "AgentReasoningNode output is not bound to the exact input Evidence profile."));
            }
        }

        var evidenceKind = ResolveEvidenceKind(nodeContract.NodeKind, parentIds.Length);
        var truthClass = evidenceKind switch
        {
            AgentEvidenceKind.DataQuery or AgentEvidenceKind.RagCitation or AgentEvidenceKind.UploadedFile =>
                AgentEvidenceTruthClass.ObservedFact,
            AgentEvidenceKind.DerivedMetric => AgentEvidenceTruthClass.DerivedFact,
            AgentEvidenceKind.LlmInference => AgentEvidenceTruthClass.LlmInference,
            AgentEvidenceKind.ModelPrediction => AgentEvidenceTruthClass.ModelPrediction,
            _ => AgentEvidenceTruthClass.Recommendation
        };
        if (truthClass == AgentEvidenceTruthClass.DerivedFact && parentIds.Length == 0)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "DerivedFact node checkpoint requires parent Evidence."));
        }

        if (reasoningOutput is not null)
        {
            state.ReasoningOutcome = reasoningOutput;
        }

        if (healthOutput is not null)
        {
            state.CloudHealthAssessment = healthOutput;
        }

        var payloadJson = AgentTaskRunStateCheckpointCodec.CaptureEvidencePayload(
            state,
            executionResult.DurableOutput.CanonicalJson);
        var payloadBytes = Encoding.UTF8.GetByteCount(payloadJson);
        if (payloadBytes > AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.EvidencePayloadTooLarge,
                $"Inline Evidence canonical payload is {payloadBytes} UTF-8 bytes; maximum is {AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes}."));
        }

        var payloadDigest = CanonicalJson.ComputeSha256(payloadJson);

        var provider = healthOutput is not null
            ? "DeterministicHealthAlgorithm"
            : nodeContract.NodeKind == "AgentReasoningNode"
            ? "ConfiguredModel"
            : string.Equals(nodeContract.NodeKind, "CloudReadNode", StringComparison.Ordinal)
            ? "CloudAiRead"
            : nodeContract.NodeKind == "GovernedDataReadNode"
                ? state.CloudReadonlySourceMode is "CloudReadOnly"
                    ? "GovernedTextToSql"
                    : "GovernedDirectDb"
                : tool.ProviderType.ToString();
        var semanticIntent = nodeContract.Input?.SemanticIntent;
        var providerOperationCode = healthOutput is not null
            ? "assess_cloud_health"
            : nodeContract.NodeKind == "AgentReasoningNode"
            ? "agent_reasoning"
            : provider == "CloudAiRead" &&
                                    semanticIntent?.StartsWith("Analysis.", StringComparison.Ordinal) == true
            ? $"CloudAiRead.{semanticIntent["Analysis.".Length..]}"
            : tool.ToolCode;
        var evidenceId = AgentEvidenceRecordId.New();
        var producedArtifacts = workspace.Artifacts
            .Where(artifact => artifact.CreatedByStepId == step.Id)
            .ToArray();
        var artifactRefs = producedArtifacts
            .Select(artifact => artifact.Id.Value.ToString("D"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var parentDocuments = ReadParentEvidenceDocuments(parentEvidence);
        var presentation = BuildPresentation(
            nodeContract,
            tool.ToolCode,
            state,
            parentDocuments,
            producedArtifacts.Length,
            workspace.Artifacts.Count,
            reasoningOutput,
            healthOutput,
            nowUtc);
        var allowedConsumerScope = new[]
        {
            $"session:{taskClaim.Task.SessionId.Value:D}",
            $"task:{taskClaim.Task.Id.Value:D}",
            $"user:{taskClaim.Task.UserId:D}"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var document = new AgentEvidenceEnvelopeDocument(
            AgentPlanContractVersions.EvidenceV1,
            evidenceId.Value,
            TenantId: null,
            taskClaim.Task.UserId,
            taskClaim.Task.SessionId.Value,
            taskClaim.Task.Id.Value,
            taskClaim.RunAttempt.Id.Value,
            nodeContract.NodeId,
            evidenceKind.ToString(),
            truthClass.ToString(),
            new AgentEvidenceProducerDocument(
                nodeContract.NodeKind,
                healthOutput is not null
                    ? $"deterministic:{healthOutput.AlgorithmVersion}"
                    : reasoningOutput is null
                    ? $"built-in:{tool.ToolCode}"
                    : $"agent-child:{reasoningOutput.ChildRunId:D}",
                tool.ToolCode,
                CanonicalJson.ComputeSha256(CanonicalJson.Canonicalize(tool.OutputSchemaJson)),
                reasoningOutput is null ? null : nodeContract.ModelPolicy?.ModelId,
                ModelVersion: reasoningOutput is null
                    ? null
                    : nodeContract.ModelPolicy?.TemplateVersion,
                PromptVersion: reasoningOutput is null
                    ? null
                    : nodeContract.ModelPolicy?.TemplateVersion),
            new AgentEvidenceSourceDocument(
                nodeContract.NodeKind,
                $"opaque:{payloadDigest[..16]}",
                presentation.SourceMode,
                presentation.IsSimulation,
                presentation.ObservedAtUtc,
                presentation.AsOfUtc,
                nodeContract.Input?.TimeRange,
                nodeContract.Input?.RequestedScope.OrderBy(value => value, StringComparer.Ordinal).ToArray() ?? [],
                provider,
                providerOperationCode,
                semanticIntent,
                nodeContract.Input?.RequestedScope.OrderBy(value => value, StringComparer.Ordinal).ToArray() ?? []),
            new AgentEvidenceQualityDocument(
                presentation.RowCount,
                presentation.IsTruncated,
                presentation.Freshness,
                MissingRate: healthOutput?.MissingRate,
                Confidence: healthOutput?.Confidence ?? reasoningOutput?.Confidence,
                QualityFlags: presentation.QualityFlags),
            new AgentEvidencePayloadDocument(
                AgentPlanContractVersions.InlineEvidencePolicyV1,
                AgentEvidenceStorageMode.InlineCanonicalJson.ToString(),
                PayloadRef: null,
                "application/json",
                payloadBytes,
                payloadDigest,
                IsComplete: true,
                payloadJson),
            new AgentEvidenceContentDocument(
                presentation.SafeSummary,
                presentation.TypedMetrics,
                Findings: healthOutput?.Findings ?? (reasoningOutput is null
                    ? []
                    : reasoningOutput.Findings
                        .Concat(reasoningOutput.EvidenceWarnings.Select(
                            AgentReasoningEvidenceProfileAuthority.ToSafeWarning))
                        .ToArray()),
                CitationRefs: reasoningOutput?.CitationRefs ?? [],
                artifactRefs),
            new AgentEvidenceLineageDocument(
                parentIds,
                nodeClaim.NodeRun.InputDigest,
                payloadDigest,
                evidenceSetDigest),
            new AgentEvidenceGovernanceDocument(
                "Internal",
                "Redacted",
                allowedConsumerScope,
                "TaskLifetime"),
            Prediction: null,
            nowUtc,
            Digest: string.Empty);
        var sealedEnvelope = AgentEvidenceCanonicalizer.Seal(document);
        if (!sealedEnvelope.IsSuccess)
        {
            return Result.From(sealedEnvelope);
        }

        var canonical = sealedEnvelope.Value!;
        var evidence = new AgentEvidenceRecord(
            evidenceId,
            tenantId: null,
            taskClaim.Task.UserId,
            taskClaim.Task.SessionId,
            taskClaim.Task.Id,
            taskClaim.RunAttempt.Id,
            nodeClaim.NodeRun.Id,
            nodeContract.NodeId,
            evidenceKind,
            truthClass,
            AgentEvidenceStorageMode.InlineCanonicalJson,
            canonical.CanonicalJson,
            canonical.Digest,
            payloadDigest,
            payloadJson,
            payloadRef: null,
            "application/json",
            payloadBytes,
            payloadDigest,
            CanonicalJson.Serialize(allowedConsumerScope),
            taskClaim.TaskFencingToken,
            nodeClaim.NodeFencingToken,
            nowUtc: nowUtc);
        var usage = new AgentRunUsageLedgerEntry(
            taskClaim.Task.Id,
            taskClaim.RunAttempt.Id,
            nodeClaim.NodeRun.Id,
            taskClaim.TaskFencingToken,
            nodeClaim.NodeFencingToken,
            inputTokens: 0,
            outputTokens: 0,
            modelCalls: reasoningOutput?.ModelCalls ?? 0,
            toolCalls: toolCallCount,
            elapsedMilliseconds: Math.Min(
                Math.Max(0, (long)elapsed.TotalMilliseconds),
                nodeClaim.NodeRun.ReservedElapsedMilliseconds),
            costAmount: 0m,
            artifactCount: producedArtifacts.Length,
            artifactBytes: producedArtifacts.Sum(artifact => artifact.FileSize),
            costCurrency: taskClaim.RunAttempt.BudgetCostCurrency,
            correlationHash: CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
            {
                taskClaim.TaskFencingToken,
                nodeClaim.NodeFencingToken,
                nodeId = nodeContract.NodeId,
                payloadDigest
            })),
            nowUtc);
        return Result.Success(new AgentNormalizedNodeCheckpoint(evidence, usage, payloadDigest));
    }

    private static AgentEvidencePresentation BuildPresentation(
        AgentPlanNodeDocument node,
        string toolCode,
        AgentTaskRunState state,
        IReadOnlyCollection<AgentEvidenceEnvelopeDocument> parentDocuments,
        int producedArtifactCount,
        int workspaceArtifactCount,
        AgentReasoningToolOutput? reasoningOutput,
        AgentCloudHealthAssessmentOutput? healthOutput,
        DateTimeOffset nowUtc)
    {
        var parentIsSimulation = parentDocuments.Any(document => document.Source.IsSimulation);
        var parentIsTruncated = parentDocuments.Any(document => document.Quality.IsTruncated);
        var parentAsOfUtc = parentDocuments
            .Select(document => document.Source.AsOfUtc)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .DefaultIfEmpty(nowUtc)
            .Min();

        if (healthOutput is not null)
        {
            return new AgentEvidencePresentation(
                healthOutput.SourceMode,
                healthOutput.IsSimulation,
                ObservedAtUtc: null,
                healthOutput.SourceAsOfUtc,
                healthOutput.RowCount,
                healthOutput.IsTruncated,
                "source-as-of",
                AgentPlanCanonicalCollections.Strings(new[]
                {
                    "deterministic-health",
                    healthOutput.AlgorithmVersion,
                    healthOutput.IsTruncated ? "source-truncated" : null,
                    healthOutput.HealthLevel == "DataInsufficient" ? "data-insufficient" : null
                }.Where(flag => flag is not null).Select(flag => flag!)),
                healthOutput.SafeSummary,
                healthOutput.TypedMetrics);
        }

        if (reasoningOutput is not null)
        {
            return new AgentEvidencePresentation(
                AgentReasoningPolicyAuthority.ContextPolicy,
                parentIsSimulation,
                ObservedAtUtc: null,
                parentAsOfUtc,
                RowCount: null,
                parentIsTruncated,
                parentIsTruncated ? "parent-evidence-truncated" : "parent-evidence-current",
                AgentPlanCanonicalCollections.Strings(
                    new[]
                    {
                        "llm-inference",
                        reasoningOutput.RecoveryUsed ? "recovery-used" : null,
                        reasoningOutput.ConflictStatus == "PotentialConflict"
                            ? "potential-evidence-conflict"
                            : null
                    }
                    .Where(flag => flag is not null)
                    .Select(flag => flag!)
                    .Concat(reasoningOutput.EvidenceWarnings)),
                reasoningOutput.SafeSummary,
                new Dictionary<string, decimal>());
        }

        if (node.NodeKind is "CloudReadNode" or "GovernedDataReadNode")
        {
            var sourceMode = state.CloudReadonlySourceMode ??
                             (node.NodeKind == "CloudReadNode" ? "CloudReadOnly" : "GovernedReadOnly");
            return new AgentEvidencePresentation(
                sourceMode,
                state.CloudReadonlyIsSimulation,
                state.CloudReadonlyQueriedAtUtc ?? nowUtc,
                state.CloudReadonlyQueriedAtUtc ?? nowUtc,
                state.CloudReadonlyRowCount,
                state.CloudReadonlyIsTruncated,
                "source-as-of",
                AgentPlanCanonicalCollections.Strings(new[]
                {
                    state.CloudReadonlyIsSimulation ? "simulation-source" : null,
                    state.CloudReadonlyIsTruncated ? "source-truncated" : null
                }.Where(flag => flag is not null).Select(flag => flag!)),
                node.NodeKind == "CloudReadNode"
                    ? $"Cloud 只读查询返回 {state.CloudReadonlyRowCount} 条已授权记录。"
                    : $"受控只读查询返回 {state.CloudReadonlyRowCount} 条已授权记录。",
                new Dictionary<string, decimal>
                {
                    ["rowCount"] = state.CloudReadonlyRowCount
                });
        }

        if (node.NodeKind == "KnowledgeRetrievalNode")
        {
            var lowConfidence = state.RagResults.Count == 0 || state.RagResults.Any(item => item.IsLowConfidence);
            return new AgentEvidencePresentation(
                "KnowledgeBaseRead",
                IsSimulation: false,
                nowUtc,
                nowUtc,
                RowCount: null,
                IsTruncated: false,
                "checkpoint-current",
                AgentPlanCanonicalCollections.Strings(new[]
                {
                    "knowledge-retrieval",
                    state.RagResults.Count == 0 ? "no-results" : null,
                    lowConfidence ? "low-confidence" : null
                }.Where(flag => flag is not null).Select(flag => flag!)),
                $"知识检索返回 {state.RagResults.Count} 条已授权候选证据。",
                new Dictionary<string, decimal>
                {
                    ["itemCount"] = state.RagResults.Count
                });
        }

        if (node.NodeKind == "FileAnalysisNode")
        {
            var isParse = toolCode is "parse_csv_json" or "parse_table_file";
            var fileCount = isParse ? state.ParsedData.Count : state.Uploads.Count;
            var rowCount = isParse ? state.Tables.Sum(table => table.Rows.Count) : (int?)null;
            var metrics = new Dictionary<string, decimal>
            {
                ["fileCount"] = fileCount
            };
            if (rowCount is not null)
            {
                metrics["rowCount"] = rowCount.Value;
            }

            return new AgentEvidencePresentation(
                isParse ? "UploadedFileParse" : "UploadedFileRead",
                IsSimulation: false,
                nowUtc,
                nowUtc,
                rowCount,
                IsTruncated: false,
                "checkpoint-current",
                AgentPlanCanonicalCollections.Strings(new[]
                {
                    isParse ? "parsed-file-data" : "uploaded-file-metadata",
                    fileCount == 0 ? "no-results" : null
                }.Where(flag => flag is not null).Select(flag => flag!)),
                isParse
                    ? $"已解析 {fileCount} 个授权上传文件。"
                    : $"已读取 {fileCount} 个授权上传文件。",
                metrics);
        }

        var dependentSourceMode = node.NodeKind switch
        {
            "JoinNode" => "EvidenceJoin",
            "DeterministicComputeNode" => "DeterministicCompute",
            "ArtifactGenerationNode" => "EvidenceBoundArtifact",
            "ApprovalCheckpointNode" => "ApprovalCheckpoint",
            "PolicyValidationNode" => "PolicyValidation",
            _ => "BuiltInExecution"
        };
        var dependentMetric = node.NodeKind switch
        {
            "ArtifactGenerationNode" => new Dictionary<string, decimal>
            {
                ["artifactCount"] = producedArtifactCount
            },
            "ApprovalCheckpointNode" => new Dictionary<string, decimal>
            {
                ["artifactCount"] = workspaceArtifactCount
            },
            _ => new Dictionary<string, decimal>
            {
                ["itemCount"] = parentDocuments.Count
            }
        };
        var dependentSummary = node.NodeKind switch
        {
            "JoinNode" => $"已汇合 {parentDocuments.Count} 条授权父证据。",
            "DeterministicComputeNode" => $"已基于 {parentDocuments.Count} 条父证据完成固定计算。",
            "ArtifactGenerationNode" => $"已生成 {producedArtifactCount} 个受控产物。",
            "ApprovalCheckpointNode" => $"已完成 {workspaceArtifactCount} 个产物的最终检查点。",
            "PolicyValidationNode" => "已完成只读策略校验。",
            _ => $"已完成节点 {node.NodeId} 的受控执行。"
        };
        var dependentFlags = new[]
        {
            node.NodeKind switch
            {
                "JoinNode" => "evidence-join",
                "DeterministicComputeNode" => "deterministic-compute",
                "ArtifactGenerationNode" => "artifact-generated",
                "ApprovalCheckpointNode" => "artifact-finalized",
                "PolicyValidationNode" => "policy-validation",
                _ => "built-in-execution"
            },
            parentIsTruncated ? "parent-evidence-truncated" : null,
            parentIsSimulation ? "simulation-evidence" : null,
            node.JoinPolicy == "OptionalBestEffort" &&
            parentDocuments.Count < node.EvidenceSelectors.Count
                ? "optional-evidence-missing"
                : null
        };
        return new AgentEvidencePresentation(
            dependentSourceMode,
            parentIsSimulation,
            ObservedAtUtc: null,
            parentAsOfUtc,
            RowCount: null,
            parentIsTruncated,
            parentIsTruncated ? "parent-evidence-truncated" : "derived-from-parent",
            AgentPlanCanonicalCollections.Strings(
                dependentFlags.Where(flag => flag is not null).Select(flag => flag!)),
            dependentSummary,
            dependentMetric);
    }

    private static IReadOnlyCollection<AgentEvidenceEnvelopeDocument> ReadParentEvidenceDocuments(
        IReadOnlyCollection<AgentEvidenceRecord> parentEvidence)
    {
        try
        {
            return parentEvidence
                .OrderBy(evidence => evidence.NodeId, StringComparer.Ordinal)
                .Select(evidence =>
                    System.Text.Json.JsonSerializer.Deserialize<AgentEvidenceEnvelopeDocument>(
                        evidence.CanonicalEnvelopeJson,
                        CanonicalJson.SerializerOptions)
                    ?? throw new System.Text.Json.JsonException("Parent Evidence envelope is missing."))
                .ToArray();
        }
        catch (Exception exception) when (exception is
            System.Text.Json.JsonException or NotSupportedException)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Authorized parent Evidence could not be decoded for checkpoint presentation.");
        }
    }

    private static AgentEvidenceKind ResolveEvidenceKind(string nodeKind, int parentCount)
    {
        return nodeKind switch
        {
            "CloudReadNode" or "GovernedDataReadNode" => AgentEvidenceKind.DataQuery,
            "KnowledgeRetrievalNode" => AgentEvidenceKind.RagCitation,
            "FileAnalysisNode" => AgentEvidenceKind.UploadedFile,
            "DeterministicComputeNode" when parentCount > 0 => AgentEvidenceKind.DerivedMetric,
            "PolicyValidationNode" => AgentEvidenceKind.PolicyDecision,
            "AgentReasoningNode" => AgentEvidenceKind.LlmInference,
            _ => AgentEvidenceKind.DerivedMetric
        };
    }

    private sealed record AgentEvidencePresentation(
        string SourceMode,
        bool IsSimulation,
        DateTimeOffset? ObservedAtUtc,
        DateTimeOffset? AsOfUtc,
        int? RowCount,
        bool IsTruncated,
        string Freshness,
        IReadOnlyCollection<string> QualityFlags,
        string SafeSummary,
        IReadOnlyDictionary<string, decimal> TypedMetrics);
}
