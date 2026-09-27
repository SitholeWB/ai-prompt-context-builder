namespace SampleApp.Services;

public class CustomerRepository
{
    public CustomerModel FindById(int id) => new CustomerModel(id, "John Doe", "john@example.com");
}
