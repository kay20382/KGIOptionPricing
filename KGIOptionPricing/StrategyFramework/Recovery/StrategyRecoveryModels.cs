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
    public static void EnsureCompatible(StrategyRecoveryCheckpoint checkpoint, string expectedStrategyType,
        string expectedInstanceId, long expectedRunGeneration, string expectedSchema)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!string.Equals(checkpoint.StrategyType, expectedStrategyType, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.StrategyInstanceId, expectedInstanceId, StringComparison.Ordinal) ||
            checkpoint.StrategyRunGeneration != expectedRunGeneration ||
            !string.Equals(checkpoint.Schema, expectedSchema, StringComparison.Ordinal))
            throw new InvalidOperationException("Strategy recovery checkpoint identity or schema is incompatible.");
    }

    public static void EnsureCompatible(
        StrategyRecoveryCheckpoint checkpoint,
        IStrategyRecoveryParticipant participant,
        long expectedRunGeneration)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(participant);
        StrategyRecoveryCheckpoint current = participant.CaptureRecoveryCheckpoint();
        EnsureCompatible(checkpoint, current.StrategyType, current.StrategyInstanceId,
            expectedRunGeneration, participant.RecoverySchema);
    }
}
