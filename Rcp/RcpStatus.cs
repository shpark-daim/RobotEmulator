using Daim.Xms.General;
using Daim.Xms.Xcp;

namespace Rcp;
public record RcpStatus<TCustom>(
    string Id,
    long Sequence,
    long EventSeq,
    RcpMode Mode,
    RcpWorkingState WorkingState,
    IReadOnlyList<int> ErrorCodes,
    bool CarrierPresent
//long TaskSeq,
//string TaskResult
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
            && Util.EnumerableEquals(ErrorCodes, other.ErrorCodes)
            && EqualityComparer<bool>.Default.Equals(CarrierPresent, other.CarrierPresent)
            //&& EqualityComparer<long>.Default.Equals(TaskSeq, other.TaskSeq)
            //&& EqualityComparer<string>.Default.Equals(TaskResult, other.TaskResult)
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
    N,      // Initializing
    K,      // Picking
    L,      // Placing
    M,      // Moving
}