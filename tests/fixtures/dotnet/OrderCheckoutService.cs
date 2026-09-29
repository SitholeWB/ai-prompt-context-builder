namespace TestApp;

public class OrderCheckoutService
{
    public void Checkout(string customerId)
    {
        var cmd = new CreateOrderCommand(customerId);
    }
}
