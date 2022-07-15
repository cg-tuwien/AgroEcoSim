namespace Utils;

public static class UID
{
    static ulong Counter = 0;
    public static ulong Next() => Counter++;
}