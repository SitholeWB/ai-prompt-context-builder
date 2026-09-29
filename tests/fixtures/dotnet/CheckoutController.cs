namespace TestApp;

[ServiceFilter(typeof(AuditFilter))]
public class CheckoutController
{
    public void Index() { }
}
