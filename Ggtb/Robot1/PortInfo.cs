using System.Windows.Controls;

namespace Robot1;

public record PortInfo
{
    public required string PortId { get; init; }

    public Canvas? Carrier { get; set; }

    public string? CarrierId { get; set; }
}
