namespace Utils;

public static class Export
{
	public static string Json<T>(T input)
	{
		return System.Text.Json.JsonSerializer.Serialize(input);
	}
}
