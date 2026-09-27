namespace SecurityTests;

public class InsecureConfig
{
    // High-risk secrets that scanner should detect
    public const string ApiKey = "api_key = 'abcdef1234567890abcdef1234567890'";
    public const string ConnStr = "Server=myServerAddress;Database=myDataBase;Uid=myUsername;Pwd=mySecretPassword;";
}
