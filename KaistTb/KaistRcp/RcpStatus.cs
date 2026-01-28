using Daim.Xms.General;
using Daim.Xms.Xcp;

namespace KaistRcp; 
public record RcpStatus<TCustom>(
    string Id,
    long Sequence,
    long EventSeq,
    RcpMode Mode,
    RcpWorkingState WorkingState,
    string? CompletionReason,
    IReadOnlyList<int> ErrorCodes,
    string? JobId,
    string? RecipeId
    ) : IXcpStatus {

    public static string Identifier => Rcp.Identifier;

    public TCustom Custom { get; init; } = default!;

    public virtual bool Equals(RcpStatus<TCustom>? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualityComparer<string>.Default.Equals(Id, other.Id)
            && EqualityComparer<long>.Default.Equals(Sequence, other.Sequence)
            && EqualityComparer<long>.Default.Equals(EventSeq, other.EventSeq)
            && EqualityComparer<RcpMode>.Default.Equals(Mode, other.Mode)
            && EqualityComparer<RcpWorkingState>.Default.Equals(WorkingState, other.WorkingState)
            &&  EqualityComparer<string>.Default.Equals(CompletionReason, other.CompletionReason)
            && Util.EnumerableEquals(ErrorCodes, other.ErrorCodes)
            &&  EqualityComparer<string>.Default.Equals(JobId, other.JobId)
            &&  EqualityComparer<string>.Default.Equals(RecipeId, other.RecipeId)
            && CustomComparerMap.Equals(Custom, other.Custom);
    }

    public override int GetHashCode() => Id.GetHashCode();
}

public enum RcpMode {
    A,      // Auto
    M,      // Manual
    E,      // Error
}

public enum RcpWorkingState {
    I,      // Idle
    R,      //Running
    S,      //Stopping
    P,      //Pasuing
    M,      //Resuming
    A,      //Aborting
    C       //Completed
}