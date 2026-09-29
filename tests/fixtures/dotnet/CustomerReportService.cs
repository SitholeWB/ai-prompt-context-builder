namespace TestApp.Services;

using TestApp.Models;
using TestApp.Extensions;

public class CustomerReportService
{
    public string GenerateReport(Customer customer)
    {
        var summary = customer.ToSummary();
        var vipStatus = customer.IsVip() ? "VIP" : "Standard";
        return $"Report: {summary} - Status: {vipStatus}";
    }
}
