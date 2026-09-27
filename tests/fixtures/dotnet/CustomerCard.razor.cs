using Microsoft.AspNetCore.Components;
using SampleApp.Services;

namespace SampleApp.Components;

public partial class CustomerCard : ComponentBase
{
    [Parameter]
    public CustomerModel? Customer { get; set; }
}
