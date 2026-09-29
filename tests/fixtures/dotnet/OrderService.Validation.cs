namespace TestApp;

public partial class OrderService
{
    public bool ValidateOrder(int id) => id > 0;
}
