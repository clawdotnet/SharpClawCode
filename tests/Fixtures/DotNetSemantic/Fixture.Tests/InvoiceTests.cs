using Xunit;
namespace Billing.Tests;
public sealed class InvoiceTests
{
    [Fact]
    public void Invoice_has_expected_value() => Assert.Equal("invoice", new InvoiceService().Create());
}
