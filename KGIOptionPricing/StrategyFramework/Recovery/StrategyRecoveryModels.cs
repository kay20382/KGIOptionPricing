using System;

namespace KGIOptionPricing.StrategyFramework.Recovery;

public sealed record StrategyRecoveryCheckpoint(
    string StrategyType,
    string StrategyInstanceId,
    long StrategyRunGeneration,
    string Schema,
    DateTimeOffset CapturedAt,
    string PayloadJson);

public static class StrategyRecoveryCheckpointValidator
{
    public static void EnsureCompatible(
        StrategyRecoveryCheckpoint checkpoint,
        IStrategyRecoveryParticipant participant,
        long expectedRunGeneration)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(participant);
        StrategyRecoveryCheckpoint current = participant.CaptureRecoveryCheckpoint();
        if (!string.Equals(checkpoint.StrategyType, current.StrategyType, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.StrategyInstanceId, current.StrategyInstanceId, StringComparison.Ordinal) ||
            checkpoint.StrategyRunGeneration != expectedRunGeneration ||
            !string.Equals(checkpoint.Schema, participant.RecoverySchema, StringComparison.Ordinal))
            throw new InvalidOperationException("Strategy recovery checkpoint identity or schema is incompatible.");
    }
}
