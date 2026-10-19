using Newtonsoft.Json;

namespace LucentUI.Config;

/// <summary>Copies every setting of one settings object onto another of the same type (deep, lists replaced).</summary>
public static class SettingsCopy
{
    private static readonly JsonSerializerSettings Replace = new() { ObjectCreationHandling = ObjectCreationHandling.Replace };

    public static void Into<T>(T source, T target)
        where T : class
    {
        JsonConvert.PopulateObject(JsonConvert.SerializeObject(source), target, Replace);
    }
}
