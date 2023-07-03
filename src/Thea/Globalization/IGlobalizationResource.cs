using System.Threading.Tasks;

namespace Thea.Globalization;

public interface IGlobalizationResource
{
    string GetGlossary(string tagName, string cultureName = null, int lifetimeMinutes = 5);
    Task<string> GetGlossaryAsync(string tagName, string cultureName = null, int lifetimeMinutes = 5);
    string GetCulture();
}
