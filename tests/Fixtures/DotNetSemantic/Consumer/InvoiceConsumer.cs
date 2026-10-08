using Billing;

namespace Consumer;

/// <summary>Consumes the cross-project invoice contract.</summary>
public sealed class InvoiceConsumer
{
    /// <summary>Calls a compiler-resolved project reference.</summary>
    public string Run(IInvoiceService service) => service.Create();
}
