using ClaudeTimer.Models;

namespace ClaudeTimer.Services;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
