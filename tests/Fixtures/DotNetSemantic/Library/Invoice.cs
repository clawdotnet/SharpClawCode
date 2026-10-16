namespace Billing;

/// <summary>Creates invoices.</summary>
public interface IInvoiceService
{
    /// <summary>Creates an invoice.</summary>
    string Create();
}

/// <summary>Base invoice service.</summary>
public abstract class InvoiceBase
{
    /// <summary>Creates an invoice.</summary>
    public abstract string Create();
}

/// <summary>Concrete invoice service.</summary>
public sealed class InvoiceService : InvoiceBase, IInvoiceService
{
    /// <inheritdoc />
    public override string Create() => "invoice";

    /// <summary>Processes an invoice by number.</summary>
    public void Process(int number) { }

    /// <summary>Processes an invoice by label.</summary>
    public void Process(string label) { }
}
