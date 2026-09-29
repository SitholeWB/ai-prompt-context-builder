namespace TestApp.Extensions;

using TestApp.Models;

public static class CustomerExtensions
{
    public static string ToSummary(this Customer customer)
    {
        return $"{customer.Name} (ID: {customer.Id})";
    }

    public static bool IsVip(this Customer customer)
    {
        return customer.TotalPurchases > 1000;
    }
}
