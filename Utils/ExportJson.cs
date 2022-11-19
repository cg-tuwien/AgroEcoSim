namespace Utils;

public static class Export
{
	public static string Json<T>(T input)
	{
		#if GODOT
		return Newtonsoft.Json.JsonConvert.SerializeObject(input);
		#else
		return System.Text.Json.JsonSerializer.Serialize(input);
		#endif
	}
}
