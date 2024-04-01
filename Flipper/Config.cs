public static class Config
{
    public static bool Initialized { get; set; } = false;
    public static string LeagueName { get; set; }
    public static List<string> SearchSuffix { get; set; } = new List<string>();
    public static string Cookie { get; set; }
    public static bool PlayNotificationSoundOnWhisper { get; set; } = true;
}

public class ConfigData
{
    public string LeagueName { get; set; }
    public List<string> SearchSuffix { get; set; } = new List<string>();
    public string Cookie { get; set; }
    public bool PlayNotificationSoundOnWhisper { get; set; } = true;

}
