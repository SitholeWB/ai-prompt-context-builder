namespace SampleApp.Services;

using SampleApp.Constants;

public class FanOption
{
    public string Name { get; set; } = string.Empty;
}

public class FanOptionHandler
{
    public bool CheckOption(FanOption opt)
    {
        if (opt.Name == CommonValues.Fan_Name)
        {
            return true;
        }
        return false;
    }
}
