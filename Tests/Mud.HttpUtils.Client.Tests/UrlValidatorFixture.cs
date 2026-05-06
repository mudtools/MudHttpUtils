namespace Mud.HttpUtils.Tests;

public class UrlValidatorFixture : IDisposable
{
    private readonly string[] _domains;

    public UrlValidatorFixture()
    {
        _domains = ["api.example.com", "auth.example.com"];
        foreach (var domain in _domains)
        {
            UrlValidator.AddAllowedDomain(domain);
        }
    }

    public void RestoreDomains()
    {
        UrlValidator.ConfigureAllowedDomains(_domains);
    }

    public void Dispose()
    {
    }
}
