namespace Fastdown.Core;

public sealed class UpdateCheckSchedule(DateTimeOffset started)
{
    private int _failures;
    public DateTimeOffset NextCheck { get; private set; } = started.AddSeconds(4);
    public void Complete(bool succeeded, DateTimeOffset now)
    {
        _failures = succeeded ? 0 : Math.Min(5, _failures + 1);
        NextCheck = succeeded ? now.AddHours(12) : now.AddMinutes(Math.Min(60, 5 * Math.Pow(2, _failures - 1)));
    }
}
