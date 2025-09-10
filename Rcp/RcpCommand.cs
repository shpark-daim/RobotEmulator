using Daim.Xms.Xcp;
using System.Text.Json.Serialization;

namespace Rcp;
public abstract record RcpCommand() : IXcpCommand {

    public static string Identifier => Rcp.Identifier;
}

public record RcpTaskCommand([property: JsonPropertyOrder(-1)] long RefSeq)
    : RcpCommand();

public record RcpStatusCommand(string Id)
    : RcpCommand();

public record RcpSyncCommand(long Sequence)
    : RcpCommand();

public record RcpModeCommand(RcpMode Mode)
    : RcpCommand();

public record RcpPickCommand(string PickupId)
    : RcpCommand();

public record RcpPlaceCommand(string DropoffId)
    : RcpCommand();

public record RcpTransferCommand(string Id, long RefSeq, string Source, string Dest, string CarrierId, int Pickupslot = 0, int Dropoffslot = 0)
    : RcpTaskCommand(RefSeq);

