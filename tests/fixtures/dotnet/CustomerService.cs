namespace SampleApp.Services;

// Service implementation for managing customer records
public class CustomerService : ICustomerService
{
    private readonly CustomerRepository _repository;

    /* Primary constructor dependency */
    public CustomerService(CustomerRepository repository)
    {
        _repository = repository;
    }

    public Task<CustomerModel?> GetCustomerAsync(int id)
    {
        var model = _repository.FindById(id);
        return Task.FromResult<CustomerModel?>(model);
    }
}
