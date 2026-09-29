namespace TestApp;

public class OrderRepository : IOrderRepository
{
    public string FindById(int id) => "Order #123";
}
