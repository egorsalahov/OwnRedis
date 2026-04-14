namespace OwnRedis.Core;

public class RequestObject
{
    public string Key { get; set; }
    public object Value { get; set; }
    public TimeSpan SecondsTTL { get; set; } //TODO: TimeSpan
}