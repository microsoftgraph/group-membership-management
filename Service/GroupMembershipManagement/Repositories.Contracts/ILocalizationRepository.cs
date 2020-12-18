namespace Repositories.Contracts
{
    public interface ILocalizationRepository
    {
        string TranslateSetting(string settingName, params string[] additionalParams);
    }
}
