using Daim.Xms.Xcp;
using System.Text.Json.Serialization;

namespace Ggtb.Rcp;
public abstract record RcpCommand() : IXcpCommand {

    public static string Identifier => Rcp.Identifier;
}

public record RcpTaskCommand([property: JsonPropertyOrder(-1)] long RefSeq)
    : RcpCommand();

public record RcpStatusCommand(string Id)
    : RcpCommand();

public record RcpSyncCommand(long Sequence)
    : RcpCommand();

public record RcpAutoCommand()
    : RcpCommand();

public record RcpManualCommand()
    : RcpCommand();

public record RcpPickCommand(string PickupId, long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpPlaceCommand(string DropoffId, long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

