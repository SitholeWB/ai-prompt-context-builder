namespace TestApp;

public class OrderController
{
    private readonly IOrderRepository _repository;

    public OrderController(IOrderRepository repository)
    {
        _repository = repository;
    }

    public string Get(int id) => _repository.FindById(id);
}
