namespace SampleApp.Services;

public interface ICustomerService
{
    Task<CustomerModel?> GetCustomerAsync(int id);
}
